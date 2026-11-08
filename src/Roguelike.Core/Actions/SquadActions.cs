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

        world.ResolveAttack(actor, target, ranged: true);
        return true;
    }
}

/// <summary>
/// Raise a corpse within reach and sight. The new undead joins the caster's squad and faction,
/// whatever it was in life, and is bound to the caster.
/// </summary>
public sealed record RaiseDeadAction(int CorpseId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        var corpse = world.Corpses.FirstOrDefault(c => c.Id == CorpseId);
        var raise = world.Content.Monsters.GetValueOrDefault(actor.Kind)?.Raise;
        if (corpse is null || raise is null
            || world.Content.Monsters.GetValueOrDefault(corpse.Kind)?.RisesAs is not { } risesAs
            || World.Distance(actor.Position, corpse.Position) > raise.Range
            || !world.HasLineOfSight(actor.Position, corpse.Position)
            || !world.IsFree(corpse.Position)
            || world.MinionsOf(actor).Count() >= raise.MaxMinions)
            return false;

        world.RemoveCorpse(corpse);
        var def = world.Content.Monsters[risesAs];
        var raised = world.SpawnMonster(def.Id, corpse.Position, world.SquadOf(actor), actor.Faction);
        raised.MasterId = actor.Id;
        world.Emit(new CorpseRaised(actor.Id, corpse.Kind, raised.Id, raised.Kind, corpse.Position));
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
