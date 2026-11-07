using Roguelike.Core.AI;
using Roguelike.Core.Items;
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

    public int MaxHp { get; }
    public int Hp { get; set; }

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

    /// <summary>Melee attack: natural attack plus wielded weapon (including its +X).</summary>
    public int Attack => BaseAttack + (Equipment?.Weapon?.AttackBonus ?? 0);

    public int Defense => BaseDefense + (Equipment?.Armor?.DefenseBonus ?? 0);

    /// <summary>Ranged attack: the bow's if one is equipped, otherwise natural (monster archers).</summary>
    public int RangedAttack => Equipment?.Bow is { } bow ? BaseAttack + bow.AttackBonus : BaseAttack;

    /// <summary>Maximum ranged attack distance; 0 means melee only.</summary>
    public int Range => Equipment?.Bow?.Def.Range ?? BaseRange;

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
        MaxHp = Hp = maxHp;
        BaseAttack = attack;
        BaseDefense = defense;
        Brain = brain;
    }
}
