using Roguelike.Core.Content;
using ShaiRandom.Generators;

namespace Roguelike.Core.History;

public enum EraKind
{
    /// <summary>The dwarves dug the mine.</summary>
    Founding,
    /// <summary>The mine fell, in the way <see cref="SiteHistory.Fall"/> says.</summary>
    Fall,
    GoblinsCame,
    NecromancerCame,
}

public enum FallKind
{
    /// <summary>A battle: the level is strewn with the dead, and some still walk.</summary>
    War,
    /// <summary>A cave-in: fallen rock, and what the miners left behind.</summary>
    Collapse,
    /// <summary>A sickness: the dead got up again, slow and hard to put down.</summary>
    Plague,
}

/// <summary>One chapter of the site's past. Depth: the level whose inscribed stone tells it.</summary>
public sealed record Era(EraKind Kind, int Depth);

/// <summary>
/// What happened here before the player came. Rolled once per seed, before any map is built and
/// separately from everything else, so it can't depend on how the game is played. Facts only:
/// the front end words them. The maps, the dead and the founder's heirloom follow from these.
/// </summary>
public sealed record SiteHistory(
    string Founder,
    string Clan,
    string Necromancer,
    string Tribe,
    TribeDef Holders,
    int MineDepth,
    FallKind Fall,
    int FallDepth,
    string HeirloomItem,
    IReadOnlyList<string> HeirloomWords,
    IReadOnlyList<Era> Eras)
{
    public static SiteHistory Generate(HistoryDef rules, IEnhancedRandom rng)
    {
        string Pick(IReadOnlyList<string> words) => words[rng.NextInt(words.Count)];
        var names = rules.Names;

        var founder = Pick(names.FounderFirst) + Pick(names.FounderLast);
        var clan = Pick(names.ClanFirst) + Pick(names.ClanLast);
        var necromancer = $"{Pick(names.NecromancerFirst)} {Pick(names.NecromancerLast)}";

        int mineDepth = rng.NextInt(rules.MineDepth[0], rules.MineDepth[1] + 1);
        int fallRoll = rng.NextInt(rules.Falls.Sum(f => f.Weight));
        var fall = rules.Falls.First(f => (fallRoll -= f.Weight) < 0).Kind;
        int fallDepth = rng.NextInt(2, mineDepth + 1);

        var tribe = Pick(names.TribeFirst) + " " + Pick(names.TribeLast);
        int tribeRoll = rng.NextInt(rules.Tribes.Sum(t => t.Weights.GetValueOrDefault(fall)));
        var holders = rules.Tribes.First(t => (tribeRoll -= t.Weights.GetValueOrDefault(fall)) < 0);

        int itemRoll = rng.NextInt(rules.Heirloom.Items.Sum(i => i.Weight));
        var item = rules.Heirloom.Items.First(i => (itemRoll -= i.Weight) < 0).Item;
        IReadOnlyList<string> words = [Pick(names.HeirloomFirst), Pick(names.HeirloomLast)];

        return new SiteHistory(founder, clan, necromancer, tribe, holders, mineDepth, fall, fallDepth, item, words,
        [
            new Era(EraKind.GoblinsCame, World.SurfaceDepth),
            new Era(EraKind.Founding, 1),
            new Era(EraKind.Fall, fallDepth),
            new Era(EraKind.NecromancerCame, mineDepth + 1),
        ]);
    }

    /// <summary>Oldest first, the order a chronicle tells them in.</summary>
    public IEnumerable<Era> InOrder => Eras.OrderBy(e => e.Kind);
}
