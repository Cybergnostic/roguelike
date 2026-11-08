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

/// <summary>Takes the stairs down (the player only). On the last level, this wins the game.</summary>
public sealed record DescendAction : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (actor != world.Player || world.Map.StairsDown != actor.Position)
        {
            world.Emit(new ActionFailed(actor.Id, FailureReason.NotOnStairs));
            return false;
        }
        world.Descend();
        return true;
    }
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
            if (world.AreHostile(actor, other))
                return new AttackAction(other.Id).Perform(world, actor);
            if (!world.CanSwap(actor, other))
                return false;

            // Allies trade places, so two of them can't block each other in a corridor.
            other.Position = actor.Position;
            world.Emit(new ActorMoved(other.Id, target, actor.Position));
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

        Strike(world, actor, target);

        // Cleave: a killing blow carries on into another adjacent foe (once per attack).
        if (target.IsDead && actor.Character?.Has("cleave") == true
            && world.AdjacentHostile(actor) is { } next && !world.IsUntouchable(next))
        {
            world.Emit(new SpecialUsed(actor.Id, "cleave", next.Id));
            Strike(world, actor, next);
        }
        return true;
    }

    private static void Strike(World world, Actor actor, Actor target)
    {
        world.ResolveAttack(actor, target, ranged: false);
    }
}
