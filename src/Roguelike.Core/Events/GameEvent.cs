using SadRogue.Primitives;

namespace Roguelike.Core.Events;

/// <summary>
/// Structured record of something that happened during a turn. Front ends turn these
/// into messages, animations and sounds; tests and bots can inspect them directly.
/// </summary>
public abstract record GameEvent;

public sealed record ActorMoved(int ActorId, Point From, Point To) : GameEvent;

public sealed record ActorAttacked(int AttackerId, int TargetId, int Damage) : GameEvent;

public sealed record ActorDied(int ActorId, string Kind) : GameEvent;

public sealed record BumpedWall(int ActorId, Point At) : GameEvent;
