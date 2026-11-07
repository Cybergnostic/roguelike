using Roguelike.Core.AI;
using SadRogue.Primitives;

namespace Roguelike.Core.Entities;

public enum Faction
{
    Player,
    Goblins,
}

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
    public int Attack { get; }
    public int Defense { get; }

    /// <summary>Null for the player, whose actions come from input.</summary>
    public IBrain? Brain { get; }

    public bool IsDead => Hp <= 0;

    public Actor(int id, string kind, Faction faction, Point position,
        int maxHp, int attack, int defense, IBrain? brain)
    {
        Id = id;
        Kind = kind;
        Faction = faction;
        Position = position;
        MaxHp = Hp = maxHp;
        Attack = attack;
        Defense = defense;
        Brain = brain;
    }

    public bool IsHostileTo(Actor other) => Faction != other.Faction;
}
