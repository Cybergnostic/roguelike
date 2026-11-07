using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;

namespace Roguelike.Core.Actions;

public sealed record RangedAttackAction(int TargetId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        var target = world.GetActor(TargetId);
        if (target is null || target.IsDead || world.IsUntouchable(target) || actor.Range <= 0
            || World.Distance(actor.Position, target.Position) > actor.Range
            || !world.HasClearShot(actor.Position, target.Position))
            return false;

        int roll = world.Rng.NextInt(1, Math.Max(1, actor.RangedAttack) + 1);
        world.Damage(actor, target, Math.Max(0, roll - target.Defense), ranged: true);
        return true;
    }
}

public sealed record HealAction(int TargetId, int Amount) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        var target = world.GetActor(TargetId);
        if (target is null || target.IsDead)
            return false;

        int healed = Math.Min(Amount, target.MaxHp - target.Hp);
        target.Hp += healed;
        world.Emit(new ActorHealed(actor.Id, target.Id, healed));
        return true;
    }
}

/// <summary>A runner brings another squad into the fight, pointing it at the player's last known position.</summary>
public sealed record CallForHelpAction(int FromSquadId, int ToSquadId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        var from = world.Squads.FirstOrDefault(s => s.Id == FromSquadId);
        var to = world.Squads.FirstOrDefault(s => s.Id == ToSquadId);
        if (from is null || to is null || to.IsWipedOut)
            return false;

        from.HelpTargetSquadId = null;
        from.HelpCalled = true;

        if (to.State is SquadState.Idle or SquadState.Alert)
        {
            to.State = SquadState.Alert;
            to.TargetId = from.TargetId;
            to.LastKnownTarget = from.LastKnownTarget;
            to.LastSeenTurn = world.Turn;
            to.Destination = from.LastKnownTarget;
        }
        world.Emit(new ReinforcementsCalled(actor.Id, from.Id, to.Id));
        return true;
    }
}
