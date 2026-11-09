using Roguelike.Core.Content;
using Roguelike.Core.Events;

namespace Roguelike.Core.Progression;

/// <summary>
/// The player's growth: six attribute scores, skill ranks and an XP pool to spend on both.
/// What attributes and skills do comes from data (attributes.json, skills.json); this class
/// only keeps the numbers and sums their effects into derived <see cref="Stat"/> bonuses.
/// </summary>
public sealed class Character
{
    private readonly ContentDatabase _content;
    private readonly Dictionary<string, int> _scores = [];
    private readonly Dictionary<string, int> _ranks = [];
    private readonly HashSet<string> _discovered = [];
    private int[]? _bonuses;

    /// <summary>The class this character started as; null for a classless wanderer.</summary>
    public ClassDef? Class { get; }

    public Character(ContentDatabase content, IReadOnlyDictionary<string, int>? scores = null, ClassDef? characterClass = null)
    {
        _content = content;
        Class = characterClass;
        foreach (var skill in characterClass?.Starts ?? [])
            _ranks[skill] = 1;
        foreach (var a in content.Attributes)
            _scores[a.Id] = scores?.GetValueOrDefault(a.Id, BaseScore) ?? BaseScore;
    }

    /// <summary>An unremarkable score: modifier 0.</summary>
    public const int BaseScore = 10;

    /// <summary>XP available to spend.</summary>
    public int Xp { get; private set; }

    /// <summary>All XP ever earned.</summary>
    public int TotalXp { get; private set; }

    /// <summary>Attribute points bought so far; each one makes the next dearer.</summary>
    public int AttributeRaises { get; private set; }

    public int Score(string attribute) => _scores[attribute];

    /// <summary>D&amp;D-style: 10–11 is 0, 12–13 is +1, 8–9 is −1.</summary>
    public static int Modifier(int score) => (int)Math.Floor((score - BaseScore) / 2.0);

    public int ModifierOf(string attribute) => Modifier(Score(attribute));

    public int Rank(string skill) => _ranks.GetValueOrDefault(skill);

    /// <summary>Whether any learned skill grants this special ability ("cleave").</summary>
    public bool Has(string special) =>
        _ranks.Keys.Any(id => _content.Skills[id].Special == special);

    /// <summary>
    /// Total bonus to a stat from attributes (per modifier point) and skills (per rank).
    /// Fractions add up before rounding down, so "0.5 defense per Dexterity modifier" works.
    /// </summary>
    public int Bonus(Stat stat)
    {
        if (_bonuses is null)
        {
            var sums = new double[Enum.GetValues<Stat>().Length];
            foreach (var a in _content.Attributes)
                foreach (var (s, amount) in a.PerModifier)
                    sums[(int)s] += amount * ModifierOf(a.Id);
            foreach (var (id, rank) in _ranks)
                foreach (var (s, amount) in _content.Skills[id].PerRank)
                    sums[(int)s] += amount * rank;
            _bonuses = [.. sums.Select(v => (int)Math.Floor(v + 1e-9))];
        }
        return _bonuses[(int)stat];
    }

    /// <summary>The part of a stat's bonus that comes from one attribute, rounded down.</summary>
    public int BonusFrom(string attribute, Stat stat) =>
        _content.Attributes.First(a => a.Id == attribute).PerModifier.TryGetValue(stat, out double amount)
            ? (int)Math.Floor(amount * ModifierOf(attribute) + 1e-9) : 0;

    // ---- Spending ----

    /// <summary>XP for the next +1 to any attribute.</summary>
    public int RaiseCost => _content.AttributeRaise.BaseCost + _content.AttributeRaise.CostPerRaise * AttributeRaises;

    public FailureReason? CheckRaise(string attribute)
    {
        if (!_scores.ContainsKey(attribute))
            throw new ArgumentException($"unknown attribute '{attribute}'");
        if (Score(attribute) >= _content.AttributeRaise.MaxScore)
            return FailureReason.AttributeAtMax;
        if (Xp < RaiseCost)
            return FailureReason.NotEnoughXp;
        return null;
    }

    /// <summary>XP for the skill's next rank; null once it's mastered.</summary>
    public int? NextRankCost(string skill)
    {
        var def = _content.Skills[skill];
        int rank = Rank(skill);
        return rank < def.Ranks ? def.Cost[rank] : null;
    }

    /// <summary>Whether this character's class teaches this tree. The classless get only the general trees.</summary>
    public bool CanLearnFrom(string tree) =>
        Class is { } own ? own.Trees.Contains(tree) : !_content.Classes.Any(c => c.Starts.Any(s => _content.Skills[s].Tree == tree));

    /// <summary>The active skills this character knows, in the order they were defined.</summary>
    public IEnumerable<SkillDef> ActiveSkills =>
        _content.SkillTrees.SelectMany(t => t.Skills).Where(s => s.Ability is not null && Rank(s.Id) > 0);

    public bool KnowsAbility(string abilityId) => ActiveSkills.Any(s => s.Ability == abilityId);

    public FailureReason? CheckLearn(string skill)
    {
        var def = _content.Skills.TryGetValue(skill, out var d) ? d : throw new ArgumentException($"unknown skill '{skill}'");
        if (!CanLearnFrom(def.Tree))
            return FailureReason.NotYourClass;
        if (NextRankCost(skill) is not { } cost)
            return FailureReason.SkillMastered;
        if (def.Requires.Any(r => Rank(r) == 0))
            return FailureReason.MissingPrerequisite;
        if (Xp < cost)
            return FailureReason.NotEnoughXp;
        return null;
    }

    internal void Raise(string attribute)
    {
        Xp -= RaiseCost;
        AttributeRaises++;
        _scores[attribute]++;
        _bonuses = null;
    }

    internal void Learn(string skill)
    {
        Xp -= NextRankCost(skill)!.Value;
        _ranks[skill] = Rank(skill) + 1;
        _bonuses = null;
    }

    internal void Gain(int amount)
    {
        Xp += amount;
        TotalXp += amount;
    }

    /// <summary>Marks a first encounter ("kind:goblin_archer", "zone:the tower"). True the first time only.</summary>
    internal bool Discover(string key) => _discovered.Add(key);

    public bool HasDiscovered(string key) => _discovered.Contains(key);
}
