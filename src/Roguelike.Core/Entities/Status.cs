namespace Roguelike.Core.Entities;

/// <summary>
/// A passing condition on an actor (COMBAT_AND_MAGIC.md §5 step 2). Each kind's Amount means:
/// Slowed: percent slower; Held: unused (can't step); Hexed: percent more damage taken and less dealt;
/// Shielded: points of damage still to absorb; Marked: percent more damage from whoever marked it;
/// Emboldened: percent more damage dealt.
/// </summary>
public enum StatusKind { Slowed, Held, Hexed, Shielded, Marked, Emboldened }

/// <summary>One status: until the end of turn <see cref="Until"/>, from <see cref="SourceId"/> (who caused it).</summary>
public sealed class Status(StatusKind kind, int until, int amount, int sourceId)
{
    public StatusKind Kind { get; } = kind;
    public int Until { get; set; } = until;
    public int Amount { get; set; } = amount;
    public int SourceId { get; } = sourceId;
}

/// <summary>How a fighter stands: a skill lets the player choose, and changing takes a moment.</summary>
public enum Stance
{
    /// <summary>Neither: the ordinary way.</summary>
    None,
    /// <summary>Everything into the blows: harder hits, less guard.</summary>
    Aggressive,
    /// <summary>Behind the guard: more armour, and the first one to come at you each turn is struck back.</summary>
    Guarded,
}

/// <summary>A big blow announced a turn before it lands (a charge, a heavy swing). Stagger or step away to spoil it.</summary>
public sealed record WindUp(string AbilityId, int TargetId, int Turn);
