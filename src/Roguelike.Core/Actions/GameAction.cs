using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using SadRogue.Primitives;

namespace Roguelike.Core.Actions;

/// <summary>
/// Everything an actor can do is an action. Input, AI and (later) replays and bots
/// all produce the same action objects, which keeps the rules in one place.
/// </summary>
public abstract record GameAction
{
    /// <summary>Applies the action. Returns false if it did not use up the actor's turn.</summary>
    public abstract bool Perform(World world, Actor actor);
}

public sealed record WaitAction : GameAction
{
    public override bool Perform(World world, Actor actor) => true;
}

/// <summary>Step in a direction; bumping a hostile actor attacks it.</summary>
public sealed record MoveAction(Direction Direction) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        var target = actor.Position + Direction;

        if (!world.Map.InBounds(target) || !world.Map.Walkable[target])
        {
            world.Emit(new BumpedWall(actor.Id, target));
            return false;
        }

        var other = world.ActorAt(target);
        if (other is not null)
        {
            if (!world.AreHostile(actor, other))
                return false;
            return new AttackAction(other.Id).Perform(world, actor);
        }

        var from = actor.Position;
        actor.Position = target;
        world.Emit(new ActorMoved(actor.Id, from, target));
        return true;
    }
}

public sealed record AttackAction(int TargetId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        var target = world.GetActor(TargetId);
        if (target is null || target.IsDead || world.IsUntouchable(target))
            return false;

        int roll = world.Rng.NextInt(1, actor.Attack + 1);
        int damage = Math.Max(0, roll - target.Defense);
        target.Hp -= damage;
        world.Emit(new ActorAttacked(actor.Id, target.Id, damage));

        if (target.IsDead)
            world.Emit(new ActorDied(target.Id, target.Kind, target.Faction.Id));
        return true;
    }
}
