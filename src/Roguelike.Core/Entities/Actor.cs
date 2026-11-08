using Roguelike.Core.AI;
using Roguelike.Core.Items;
using Roguelike.Core.Progression;
using SadRogue.Primitives;

namespace Roguelike.Core.Entities;

/// <summary>
/// Anything that takes turns. <see cref="Kind"/> is a data id ("goblin"); the front end
/// decides what a kind looks like, so the core never mentions glyphs or colours.
/// </summary>
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

    /// <summary>Bonus to a derived stat from attributes and skills (0 without a character).</summary>
    public int Bonus(Stat stat) => Character?.Bonus(stat) ?? 0;

    /// <summary>Natural stats, before equipment.</summary>
    public int BaseAttack { get; }
    public int BaseDefense { get; }

    /// <summary>Natural ranged distance (monster archers); a bow overrides it.</summary>
    public int BaseRange { get; init; }

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

    /// <summary>Null for the player, whose actions come from input.</summary>
    public IBrain? Brain { get; set; }

    /// <summary>Melee attack: natural attack, wielded weapon (including its +X), attributes and skills.</summary>
    public int Attack => BaseAttack + (Equipment?.Weapon?.AttackBonus ?? 0) + Bonus(Stat.MeleeAttack);

    public int Defense => BaseDefense + (Equipment?.Armor?.DefenseBonus ?? 0) + Bonus(Stat.Defense);

    /// <summary>Ranged attack: natural plus the bow if one is equipped (monster archers have none), attributes and skills.</summary>
    public int RangedAttack => BaseAttack + (Equipment?.Bow?.AttackBonus ?? 0) + Bonus(Stat.RangedAttack);

    /// <summary>Maximum ranged attack distance; 0 means melee only. Range bonuses only extend a bow.</summary>
    public int Range => Equipment?.Bow is { } bow ? bow.Def.Range + Bonus(Stat.Range) : BaseRange;

    /// <summary>Human-readable name derived from the kind id ("goblin_archer" -> "goblin archer").</summary>
    public string Name => Kind.Replace('_', ' ');

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
