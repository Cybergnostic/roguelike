using Roguelike.Core.AI;
using Roguelike.Core.Items;
using Roguelike.Core.Progression;
using SadRogue.Primitives;

namespace Roguelike.Core.Entities;

/// <summary>
/// Anything that takes turns. <see cref="Kind"/> is a data id ("goblin"); the front end
/// decides what a kind looks like, so the core never mentions glyphs or colours.
/// </summary>
public enum Rank
{
    Normal,
    /// <summary>Stronger than its kind, with a skill of its own.</summary>
    Elite,
    Boss,
}

public sealed class Actor
{
    public int Id { get; }
    public string Kind { get; }
    public Faction Faction { get; }
    public Point Position { get; set; }

    /// <summary>Natural hit points, before attributes and skills.</summary>
    public int BaseMaxHp { get; }
    public int MaxHp => BaseMaxHp + Bonus(Stat.MaxHp);
    public int Hp { get; set; }

    /// <summary>Attributes, skills and XP. Only the player has one so far.</summary>
    public Character? Character { get; init; }

    /// <summary>Bonus to a derived stat from attributes, skills and the properties of worn items.</summary>
    public int Bonus(Stat stat) => (Character?.Bonus(stat) ?? 0) + (Equipment?.Bonus(stat) ?? 0);

    /// <summary>A named ability (ContentDatabase.Specials) from a skill or a worn legendary.</summary>
    public bool HasSpecial(string special) =>
        Character?.Has(special) == true || Equipment?.HasSpecial(special) == true;

    /// <summary>Natural stats, before equipment.</summary>
    public int BaseAttack { get; }
    public int BaseDefense { get; }

    /// <summary>Natural ranged distance (monster archers); a bow overrides it.</summary>
    public int BaseRange { get; init; }

    /// <summary>Natural accuracy and evasion (monsters.json, player.json), before attributes and skills.</summary>
    public int BaseAccuracy { get; init; }
    public int BaseEvasion { get; init; }

    public int Accuracy => BaseAccuracy + Bonus(Stat.Accuracy);
    public int Evasion => BaseEvasion + Bonus(Stat.Evasion);

    /// <summary>Ignores morale: never routs.</summary>
    public bool Fearless { get; init; }

    /// <summary>Energy gained per turn (100 = one action per turn). See World.RunNonPlayerTurns.</summary>
    public int Speed { get; init; } = 100;

    /// <summary>Accumulated energy; an actor acts while it has at least 100.</summary>
    public int Energy { get; set; }

    /// <summary>The necromancer bound to this actor, if any. It crumbles when the master dies.</summary>
    public int? MasterId { get; set; }

    /// <summary>Loot table rolled on death (items.json), if any.</summary>
    public string? LootTable { get; init; }

    /// <summary>Carried and worn items. Only the player has them so far.</summary>
    public Inventory? Inventory { get; init; }
    public Equipment? Equipment { get; init; }

    /// <summary>Money in the purse. Only the player has any so far.</summary>
    public int Gold { get; set; }

    /// <summary>Null for the player, whose actions come from input.</summary>
    public IBrain? Brain { get; set; }

    public int BaseStagger { get; init; }

    /// <summary>Percent chance a melee hit knocks the target off its stride. Needs a weapon (or body) built for it.</summary>
    public int StaggerChance
    {
        get
        {
            int weapon = Equipment is { } gear ? gear.Weapon?.Def.Stagger ?? 0 : BaseStagger;
            return weapon > 0 ? weapon + Bonus(Stat.Stagger) : 0;
        }
    }

    /// <summary>It didn't step last turn, so it has its feet set.</summary>
    public bool StoodStill { get; set; }

    /// <summary>Extra armour it gets right now from having its feet set in heavy gear.</summary>
    public int BraceBonus => StoodStill ? (Equipment?.Armor?.Def.Brace ?? 0) + (HasSpecial("hold_line") ? 1 : 0) : 0;

    /// <summary>Percent chance of hitting back at the first attacker each turn while braced.</summary>
    public int CounterChance => (Equipment?.Armor?.Def.Counter ?? 0) + (HasSpecial("hold_line") ? 15 : 0);

    /// <summary>Unseen from a distance until this turn.</summary>
    public int HiddenUntil { get; set; } = -1;

    /// <summary>How long a step takes in what it's wearing. 100 is an ordinary step.</summary>
    public int MoveTime => Equipment?.Armor?.Def.MoveTime ?? 100;

    /// <summary>How much more dangerous its gear makes it look, in percent.</summary>
    public int Menace => Equipment?.All.Sum(i => i.Def.Menace) ?? 0;

    /// <summary>Moving carefully: slower, quieter and harder to spot.</summary>
    public bool Sneaking { get; set; }

    /// <summary>Melee attack: natural attack, wielded weapon (including its +X), attributes and skills.</summary>
    public int Attack => BaseAttack + (Equipment?.Weapon?.AttackBonus ?? 0) + Bonus(Stat.MeleeAttack);

    /// <summary>How long a blow takes. 100 is one ordinary turn.</summary>
    public int BaseAttackTime { get; init; } = 100;
    public int MeleeAttackTime => Equipment?.Weapon?.Def.AttackTime ?? BaseAttackTime;
    public int RangedAttackTime => Equipment?.Bow?.Def.AttackTime ?? BaseAttackTime;

    /// <summary>The off-hand weapon's strength, before the off-hand penalty. It gets only a share of a Strength bonus.</summary>
    public int OffHandAttack(int strengthPercent)
    {
        int strength = Math.Max(0, Character?.BonusFrom("str", Stat.MeleeAttack) ?? 0);
        return BaseAttack + (Equipment?.OffHand?.AttackBonus ?? 0) + Bonus(Stat.MeleeAttack) - (strength - strength * strengthPercent / 100);
    }

    public int Defense => BaseDefense + (Equipment?.Armor?.DefenseBonus ?? 0) + Bonus(Stat.Defense);

    /// <summary>Ranged attack: natural plus the bow if one is equipped (monster archers have none), attributes and skills.</summary>
    public int RangedAttack => BaseAttack + (Equipment?.Bow?.AttackBonus ?? 0) + Bonus(Stat.RangedAttack);

    /// <summary>Maximum ranged attack distance; 0 means melee only. Range bonuses only extend a bow.</summary>
    public int Range => Equipment?.Bow is { } bow ? bow.Def.Range + Bonus(Stat.Range) : BaseRange;

    /// <summary>Human-readable name derived from the kind id ("goblin_archer" -> "goblin archer").</summary>
    public string Name => Title is null ? Kind.Replace('_', ' ') : $"{Title} {Kind.Replace('_', ' ')}";

    public Rank Rank { get; init; }

    /// <summary>How it thinks without a commander (minds.json).</summary>
    public Content.MindDef? Mind { get; init; }

    /// <summary>The mind it gives its whole squad while it lives; null if it commands nobody.</summary>
    public Content.MindDef? Commands { get; init; }

    /// <summary>How many times the player has spoken to it, so it can say the next thing.</summary>
    public int TimesTalkedTo { get; set; }

    /// <summary>The word an elite goes by ("brutal"), if it has one.</summary>
    public string? Title { get; init; }

    /// <summary>What it can do beyond hitting things. Each is on its own cooldown.</summary>
    public IReadOnlyList<Content.AbilityDef> Abilities { get; init; } = [];

    /// <summary>Multiplies the XP for killing it (elites are worth more).</summary>
    public double XpMultiplier { get; init; } = 1;

    public bool IsDead => Hp <= 0;

    public Actor(int id, string kind, Faction faction, Point position,
        int maxHp, int attack, int defense, IBrain? brain = null)
    {
        Id = id;
        Kind = kind;
        Faction = faction;
        Position = position;
        BaseMaxHp = Hp = maxHp;
        BaseAttack = attack;
        BaseDefense = defense;
        Brain = brain;
    }
}
