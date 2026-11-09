using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Map;
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

    /// <summary>How long it takes, asked before it happens. 100 is one ordinary turn.</summary>
    public virtual int TimeCost(World world, Actor actor) => 100;

    /// <summary>How many cells away the player's doing this can be heard. 0: silent.</summary>
    public virtual int NoiseMade(World world, Actor actor) => 0;
}

/// <summary>Starts or stops moving carefully. Takes no time.</summary>
public sealed record ToggleSneakAction : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        actor.Sneaking = !actor.Sneaking;
        world.Emit(new SneakChanged(actor.Sneaking));
        return false;
    }
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

/// <summary>Takes the stairs back up (the player only).</summary>
public sealed record AscendAction : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (actor != world.Player || world.Map.StairsUp != actor.Position)
        {
            world.Emit(new ActionFailed(actor.Id, FailureReason.NotOnStairs));
            return false;
        }
        world.Ascend();
        return true;
    }
}

/// <summary>Closes an open gate next to the actor. A gate with someone standing in it won't close.</summary>
public sealed record CloseGateAction : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        foreach (var dir in World.EightWay)
        {
            var p = actor.Position + dir;
            if (world.Map.InBounds(p) && world.Map.Tiles[p] == Tile.GateOpen && world.ActorAt(p) is null)
            {
                world.SetGate(p, open: false, actor);
                return true;
            }
        }
        world.Emit(new ActionFailed(actor.Id, FailureReason.NoGateToClose));
        return false;
    }
}

/// <summary>The player uses a skill they know. Aimed ones need a target in reach; each has its own cooldown.</summary>
public sealed record UseAbilityAction(string AbilityId, int? TargetId = null) : GameAction
{
    public override int NoiseMade(World world, Actor actor) => world.Content.Dungeon.FightNoise;

    public override bool Perform(World world, Actor actor)
    {
        if (!world.Content.Abilities.TryGetValue(AbilityId, out var ability))
            return false;
        var target = TargetId is { } id ? world.GetActor(id) : null;
        if (world.AbilityProblem(actor, ability, target) is { } problem)
        {
            world.Emit(new ActionFailed(actor.Id, problem));
            return false;
        }
        new AbilityAction(ability, TargetId, world.AbilityLanding(actor, ability, target)).Perform(world, actor);
        world.StartCooldown(ability);
        return true;
    }
}

/// <summary>Uses a skill from abilities.json. At: where it lands, for skills that move the user.</summary>
public sealed record AbilityAction(AbilityDef Ability, int? TargetId, Point? At = null) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        var target = TargetId is { } id ? world.GetActor(id) : null;
        world.Emit(new AbilityUsed(actor.Id, Ability.Id, TargetId));
        switch (Ability.Kind)
        {
            case AbilityKind.Charge:
                actor.Position = At!.Value;
                if (target is { IsDead: false })
                    world.ResolveAttack(actor, target, ranged: false);
                break;
            case AbilityKind.Warcry:
                if (world.SquadOf(actor) is { } squad)
                    squad.Morale = Math.Min(AI.Squads.Squad.MaxMorale, squad.Morale + Ability.Amount);
                break;
            case AbilityKind.Volley:
                for (int i = 0; i < Ability.Count && target is { IsDead: false }; i++)
                    world.ResolveAttack(actor, target, ranged: true);
                break;
            case AbilityKind.Blink:
                actor.Position = At!.Value;
                break;
            case AbilityKind.Summon:
                for (int i = 0; i < Ability.Count; i++)
                    if (world.FreeCellNear(actor.Position, 3) is { } spot)
                        world.SpawnMonster(Ability.Monster!, spot, world.SquadOf(actor)).MasterId = actor.Id;
                break;
            case AbilityKind.Drain:
                if (target is { IsDead: false })
                {
                    int before = target.Hp;
                    world.ResolveAttack(actor, target, ranged: true, power: Ability.Power > 0 ? Ability.Power + actor.Bonus(Progression.Stat.Magic) : null);
                    if (target.Hp < before)
                        actor.Hp = Math.Min(actor.MaxHp, actor.Hp + Ability.Amount);
                }
                break;
            case AbilityKind.Frighten:
                world.Frighten(actor, Ability);
                break;
            case AbilityKind.Bash:
                if (target is { IsDead: false })
                {
                    world.ResolveAttack(actor, target, ranged: false, power: actor.Attack + Ability.Amount);
                    world.StaggerIfAlive(target, actor);
                }
                break;
            case AbilityKind.Pin:
                if (target is { IsDead: false })
                {
                    world.ResolveAttack(actor, target, ranged: true);
                    world.StaggerIfAlive(target, actor);
                }
                break;
            case AbilityKind.Vanish:
                actor.Position = At!.Value;
                actor.HiddenUntil = world.Turn + Ability.Amount;
                world.ForgetWhereThePlayerIs();
                break;
            case AbilityKind.Bolt:
                if (target is { IsDead: false })
                    world.ResolveAttack(actor, target, ranged: true, power: Ability.Power + actor.Bonus(Progression.Stat.Magic), accuracyBonus: 3);
                break;
            case AbilityKind.Heal:
                int healed = Math.Min(actor.MaxHp - actor.Hp, Ability.Power + actor.Bonus(Progression.Stat.Magic));
                actor.Hp += healed;
                world.Emit(new ActorHealed(actor.Id, actor.Id, healed));
                break;
        }
        return true;
    }
}

/// <summary>Rushes at someone in heavy armour and strikes them, closing the distance a slow fighter can't otherwise.</summary>
public sealed record ChargeAction(int TargetId) : GameAction
{
    public override int NoiseMade(World world, Actor actor) => world.Content.Dungeon.FightNoise;

    public override bool Perform(World world, Actor actor)
    {
        if (world.GetActor(TargetId) is not { IsDead: false } target)
            return false;
        if (world.ChargeProblem(actor, target) is { } problem)
        {
            world.Emit(new ActionFailed(actor.Id, problem));
            return false;
        }
        world.Charge(actor, target);
        return true;
    }
}

/// <summary>Tells the guards at the gate what you think of their toll. They don't take it well.</summary>
public sealed record ThreatenAction : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (actor != world.Player || world.CampInTalk() is not { } camp)
            return false;
        world.Threaten(camp);
        return true;
    }
}

/// <summary>Lets someone begging for mercy live, in return for what they know.</summary>
public sealed record SpareAction(int ActorId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (world.GetActor(ActorId) is not { IsDead: false } beggar || world.SquadOf(beggar) is not { Surrendered: true } squad)
            return false;
        squad.Surrendered = false;
        squad.Spared = true;
        switch (squad.Offer)
        {
            case AI.Squads.Ransom.Secret:
                world.TellSecret(beggar);
                break;
            case AI.Squads.Ransom.Gold:
                actor.Gold += squad.OfferedGold;
                world.Emit(new RansomPaid(beggar.Id, squad.OfferedGold));
                break;
            default:
                world.Emit(new BeggarSpared(beggar.Id));
                break;
        }
        return true;
    }
}

/// <summary>Speaks to someone next to the player. Takes no time.</summary>
public sealed record TalkAction(int NpcId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (world.GetActor(NpcId) is not { IsDead: false } npc || world.NpcOf(npc) is not { } def || !world.IsAdjacent(actor.Position, npc.Position))
            return false;
        var line = def.Lines is { Count: > 0 } lines ? lines[npc.TimesTalkedTo % lines.Count] : null;
        npc.TimesTalkedTo++;
        world.Emit(new Talked(npc.Id, line, def.Goods is { Count: > 0 }));
        return false;
    }
}

/// <summary>Buys one of what an adjacent person sells.</summary>
public sealed record BuyAction(int NpcId, string ItemId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (world.GetActor(NpcId) is not { IsDead: false } npc || !world.IsAdjacent(actor.Position, npc.Position)
            || world.NpcOf(npc)?.Goods?.FirstOrDefault(w => w.Item == ItemId) is not { } ware || actor.Inventory is not { } pack)
            return false;
        if (actor.Gold < ware.Price)
            return PickUpAction.Fail(world, actor, FailureReason.NotEnoughGold);
        var item = world.CreateItem(world.Content.Items[ware.Item], enchantment: 0);
        if (!pack.CanAdd(item))
            return PickUpAction.Fail(world, actor, FailureReason.InventoryFull);
        actor.Gold -= ware.Price;
        pack.Add(item);
        world.Emit(new ItemBought(actor.Id, item, ware.Price));
        return true;
    }
}

/// <summary>Pays the toll at a nearby camp gate (the player only).</summary>
public sealed record PayTollAction : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (actor != world.Player)
            return false;
        if (world.TollProblem() is { } problem)
        {
            world.Emit(new ActionFailed(actor.Id, problem));
            return false;
        }
        world.PayToll();
        return true;
    }
}

/// <summary>Step in a direction; opening a closed gate or bumping a hostile actor uses the step. TimePercent shortens a step taken at a run.</summary>
public sealed record MoveAction(Direction Direction, int TimePercent = 100) : GameAction
{
    public override int TimeCost(World world, Actor actor) =>
        world.ActorAt(actor.Position + Direction) is { } other && world.CouldFight(actor, other) ? actor.MeleeAttackTime
        : (actor.Sneaking ? world.Content.Dungeon.SneakStepTime * actor.MoveTime / 100 : actor.MoveTime) * TimePercent / 100;

    public override int NoiseMade(World world, Actor actor) =>
        world.ActorAt(actor.Position + Direction) is { } other && world.CouldFight(actor, other) ? world.Content.Dungeon.FightNoise : world.FootstepNoise(actor);

    public override bool Perform(World world, Actor actor)
    {
        var target = actor.Position + Direction;

        if (world.Map.InBounds(target) && world.Map.Tiles[target] == Tile.GateClosed)
        {
            world.SetGate(target, open: true, actor);
            return true;
        }

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
            if (actor == world.Player && world.NpcOf(other) is not null)
                return new TalkAction(other.Id).Perform(world, actor);
            if (actor == world.Player && world.SquadOf(other) is { Surrendered: true })
            {
                world.Emit(new MercyAsked(other.Id));
                return false;
            }
            // Someone leaving you alone: the first bump is a warning, the second means it.
            if (actor == world.Player && world.CouldFight(actor, other))
            {
                if (world.AlreadyWarnedAbout(other))
                    return new AttackAction(other.Id).Perform(world, actor);
                world.Emit(new ActionFailed(actor.Id, FailureReason.NotHostileYet));
                return false;
            }
            if (!world.CanSwap(actor, other))
                return false;

            // Allies trade places, so two of them can't block each other in a corridor.
            other.Position = actor.Position;
            world.Emit(new ActorMoved(other.Id, target, actor.Position));
        }

        var from = actor.Position;
        actor.Position = target;
        world.Emit(new ActorMoved(actor.Id, from, target));
        if (actor != world.Player && world.CouldFight(world.Player, actor))
            world.Noise(target, Events.SoundKind.Footsteps, actor);
        return true;
    }
}

public sealed record AttackAction(int TargetId) : GameAction
{
    public override int TimeCost(World world, Actor actor) => actor.MeleeAttackTime;

    public override int NoiseMade(World world, Actor actor) => world.Content.Dungeon.FightNoise;

    public override bool Perform(World world, Actor actor)
    {
        var target = world.GetActor(TargetId);
        if (target is null || target.IsDead || world.IsUntouchable(target))
            return false;

        Strike(world, actor, target);

        // Cleave: a killing blow carries on into another adjacent foe (once per attack).
        if (target.IsDead && actor.HasSpecial("cleave")
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
        if (actor.Equipment?.OffHand is not null && !target.IsDead)
        {
            var rules = world.Content.Combat;
            world.ResolveAttack(actor, target, ranged: false, power: actor.OffHandAttack(rules.OffHandStrength) + rules.OffHandAttack, accuracyBonus: rules.OffHandAccuracy);
        }
    }
}
