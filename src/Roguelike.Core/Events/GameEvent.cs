using Roguelike.Core.Items;
using SadRogue.Primitives;

namespace Roguelike.Core.Events;

/// <summary>
/// Structured record of something that happened during a turn. Front ends turn these
/// into messages, animations and sounds; tests and bots can inspect them directly.
/// </summary>
public abstract record GameEvent;

public sealed record ActorMoved(int ActorId, Point From, Point To) : GameEvent;

/// <summary>An attack. Missed: it didn't connect (Damage is then 0).</summary>
public sealed record ActorAttacked(int AttackerId, int TargetId, int Damage, bool Ranged = false, bool Missed = false) : GameEvent;

public sealed record ActorHealed(int HealerId, int TargetId, int Amount) : GameEvent;

/// <summary>KillerId is whoever dealt the final blow.</summary>
public sealed record ActorDied(int ActorId, string Kind, string FactionId, int KillerId) : GameEvent;

/// <summary>A necromancer turned a corpse into a new undead actor.</summary>
public sealed record CorpseRaised(int CasterId, string CorpseKind, int RaisedId, string RaisedKind, Point At) : GameEvent;

/// <summary>A lair sent out a fresh squad.</summary>
public sealed record SquadMustered(int SquadId, string FactionId, string ZoneName) : GameEvent;

/// <summary>A faction sent a squad to raid an enemy's zone.</summary>
public sealed record RaidLaunched(int SquadId, string FactionId, string ZoneName) : GameEvent;

/// <summary>The player took the stairs and arrived on a new level.</summary>
public sealed record LevelEntered(int Depth) : GameEvent;

/// <summary>The player took the stairs down from the last level: the run is won.</summary>
public sealed record GameWon(int Turn) : GameEvent;

/// <summary>The player walked into a named place (a lair).</summary>
public sealed record ZoneEntered(string ZoneName) : GameEvent;

/// <summary>A bound servant fell apart because its master died.</summary>
public sealed record MinionCrumbled(int ActorId, string Kind, int MasterId) : GameEvent;

public sealed record BumpedWall(int ActorId, Point At) : GameEvent;

/// <summary>A squad noticed an enemy (<paramref name="TargetId"/>); <paramref name="SpotterId"/> raised the alarm.</summary>
public sealed record SquadSpotted(int SquadId, int SpotterId, int TargetId) : GameEvent;

/// <summary>A squad's morale broke and it is fleeing.</summary>
public sealed record SquadRouted(int SquadId, IReadOnlyList<int> MemberIds) : GameEvent;

/// <summary>A runner reached another squad and brought it into the fight.</summary>
public sealed record ReinforcementsCalled(int RunnerId, int FromSquadId, int ToSquadId) : GameEvent;

// ---- Items ----

public sealed record ItemPickedUp(int ActorId, Item Item) : GameEvent;

public sealed record ItemDropped(int ActorId, Item Item) : GameEvent;

/// <summary>A dying monster dropped loot where it fell.</summary>
public sealed record LootDropped(int ActorId, Item Item, Point At) : GameEvent;

public sealed record ItemEquipped(int ActorId, Item Item, Item? Replaced) : GameEvent;

public sealed record ItemUnequipped(int ActorId, Item Item) : GameEvent;

public sealed record PotionDrunk(int ActorId, Item Item, int Healed) : GameEvent;

// ---- Character ----

public enum XpSource
{
    Kill,
    /// <summary>First sight of a kind of monster; Subject is its kind.</summary>
    Sighting,
    /// <summary>First visit to a named place; Subject is its name.</summary>
    Zone,
}

/// <summary>Subject: the monster kind (kills, sightings) or the zone name.</summary>
public sealed record XpGained(int ActorId, int Amount, XpSource Source, string Subject) : GameEvent;

public sealed record AttributeRaised(int ActorId, string AttributeId, int NewScore) : GameEvent;

public sealed record SkillLearned(int ActorId, string SkillId, int NewRank) : GameEvent;

/// <summary>A skill's special ability went off ("cleave"), aimed at TargetId.</summary>
public sealed record SpecialUsed(int ActorId, string Special, int TargetId) : GameEvent;

public enum FailureReason
{
    NotEnoughXp,
    AttributeAtMax,
    SkillMastered,
    MissingPrerequisite,
    NothingToPickUp,
    InventoryFull,
    NoBow,
    OutOfRange,
    NoLineOfFire,
    CannotEquip,
    NotDrinkable,
    NotOnStairs,
}

/// <summary>An action that didn't happen, and why (the turn isn't used).</summary>
public sealed record ActionFailed(int ActorId, FailureReason Reason) : GameEvent;
