using Roguelike.Core.Map;
using SadRogue.Primitives;

namespace Roguelike.Core.AI.Squads;

/// <summary>
/// What a camp asks at its gate: Price buys Turns of safe passage. Guards call out the terms to
/// anyone within Range of the Gate, and remind a visitor WarnBefore turns before the time is up.
/// </summary>
public sealed record TollTerms(Point Gate, int Price, int Turns, int Range, int WarnBefore);

/// <summary>
/// A place its squads defend. They leave a passing traveller alone: only someone inside the zone,
/// or anyone once the camp has been attacked (it remembers), is treated as an enemy. Standing in
/// the gate isn't trespassing yet, and paying the toll makes the inside safe for a while.
/// </summary>
public sealed class Camp(Zone zone, TollTerms? toll = null)
{
    public Zone Zone { get; } = zone;

    /// <summary>What it costs to get in peacefully. Null: nobody gets in peacefully.</summary>
    public TollTerms? Toll { get; } = toll;

    /// <summary>Someone has attacked the camp. From now on its squads fight the player on sight.</summary>
    public bool Provoked { get; set; }

    /// <summary>Its defenders were wiped out by someone else: nobody here deals with travellers any more.</summary>
    public bool Overrun { get; internal set; }

    /// <summary>The turn the player's paid time runs out, if they have any.</summary>
    public int? PassUntil { get; internal set; }

    /// <summary>The player has heard the terms at least once, so they can be shown to them.</summary>
    public bool TermsKnown { get; internal set; }

    // What the guards have already said, so they don't repeat themselves every turn.
    internal bool Challenged { get; set; }
    internal bool WarnedAtGate { get; set; }
    internal bool PassWarned { get; set; }

    public bool HasPass(int turn) => !Provoked && PassUntil > turn;

    public int PassTurnsLeft(int turn) => HasPass(turn) ? PassUntil!.Value - turn : 0;

    /// <summary>Standing in the gate itself: the last place to turn back from.</summary>
    public bool IsThreshold(Point p) => Toll?.Gate == p;

    public bool IsInside(Point p) => Zone.Area.Contains(p) && !IsThreshold(p);

    public bool Tolerates(Point playerAt, int turn) => !Provoked && (!IsInside(playerAt) || HasPass(turn));
}
