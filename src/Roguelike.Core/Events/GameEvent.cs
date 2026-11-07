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

public sealed record ActorDied(int ActorId, string Kind) : GameEvent;

public sealed record BumpedWall(int ActorId, Point At) : GameEvent;

/// <summary>A squad noticed the player; <paramref name="SpotterId"/> raised the alarm.</summary>
public sealed record SquadSpotted(int SquadId, int SpotterId) : GameEvent;

/// <summary>A squad's morale broke and it is fleeing.</summary>
public sealed record SquadRouted(int SquadId, IReadOnlyList<int> MemberIds) : GameEvent;

/// <summary>A runner reached another squad and brought it into the fight.</summary>
public sealed record ReinforcementsCalled(int RunnerId, int FromSquadId, int ToSquadId) : GameEvent;
