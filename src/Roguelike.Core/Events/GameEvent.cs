using Roguelike.Core.Items;
using SadRogue.Primitives;

namespace Roguelike.Core.Events;

/// <summary>
/// Structured record of something that happened during a turn. Front ends turn these
/// into messages, animations and sounds; tests and bots can inspect them directly.
/// </summary>
public abstract record GameEvent;

public sealed record ActorMoved(int ActorId, Point From, Point To) : GameEvent;

public sealed record ActorAttacked(int AttackerId, int TargetId, int Damage, bool Ranged = false) : GameEvent;

public sealed record ActorHealed(int HealerId, int TargetId, int Amount) : GameEvent;

/// <summary>KillerId is whoever dealt the final blow.</summary>
public sealed record ActorDied(int ActorId, string Kind, string FactionId, int KillerId) : GameEvent;

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

public enum FailureReason
{
    NothingToPickUp,
    InventoryFull,
    NoBow,
    OutOfRange,
    NoLineOfFire,
    CannotEquip,
    NotDrinkable,
}

/// <summary>An action that didn't happen, and why (the turn isn't used).</summary>
public sealed record ActionFailed(int ActorId, FailureReason Reason) : GameEvent;
