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

    public override int TimeCost(World world, Actor actor) =>
        world.Content.Abilities.TryGetValue(AbilityId, out var ability) ? ability.Time : 100;

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
                    var behind = actor.HasSpecial("pin_through") ? world.BehindInLine(actor, target) : null;
                    foreach (var pinned in new[] { target, behind }.OfType<Actor>())
                    {
                        world.ResolveAttack(actor, pinned, ranged: true);
                        if (pinned.IsDead)
                            continue;
                        world.StaggerIfAlive(pinned, actor);
                        world.AddStatus(pinned, StatusKind.Slowed, Math.Max(1, Ability.Turns), Math.Max(1, Ability.Amount), actor);
                        if (actor.HasSpecial("pin_hold"))
                            world.AddStatus(pinned, StatusKind.Held, 1, 0, actor);
                    }
                }
                break;
            case AbilityKind.Shove:
                if (target is { IsDead: false })
                    world.Shove(actor, target);
                break;
            case AbilityKind.AimedShot:
                if (target is { IsDead: false })
                    world.ResolveAttack(actor, target, ranged: true, power: actor.RangedAttack * (100 + Ability.Amount) / 100, accuracyBonus: Ability.Power);
                break;
            case AbilityKind.Suppress:
                if (target is { IsDead: false })
                    world.Suppress(actor, target, Ability);
                break;
            case AbilityKind.Disengage:
                if (target is { IsDead: false } && At is { } back)
                {
                    var from = actor.Position;
                    actor.Position = back;
                    world.Emit(new ActorMoved(actor.Id, from, back));
                    world.ResolveAttack(actor, target, ranged: true);
                }
                break;
            case AbilityKind.MarkPrey:
                if (target is { IsDead: false })
                    world.AddStatus(target, StatusKind.Marked, Math.Max(1, Ability.Turns), actor.HasSpecial("keen_hunter") ? Ability.Power : Ability.Amount, actor);
                break;
            case AbilityKind.Tumble:
                if (At is { } past)
                {
                    var from = actor.Position;
                    actor.Position = past;
                    world.Emit(new ActorMoved(actor.Id, from, past));
                }
                break;
            case AbilityKind.Distract:
                if (At is { } landing)
                    world.Distract(actor, landing);
                break;
            case AbilityKind.Word:
                if (target is { IsDead: false })
                    world.SpeakWord(actor, target, Ability);
                break;
            case AbilityKind.Hex:
                if (target is { IsDead: false })
                    world.Hex(actor, target, Ability);
                break;
            case AbilityKind.SpiritBlade:
                world.AddStatus(actor, StatusKind.SpiritBlade, Math.Max(1, Ability.Turns), Ability.Amount, actor);
                break;
            case AbilityKind.Aegis:
                world.AddStatus(actor, StatusKind.Shielded, Math.Max(1, Ability.Turns), Ability.Power + actor.Bonus(Progression.Stat.Magic), actor);
                break;
            case AbilityKind.Smoke:
                world.MakeSmoke(actor.Position, Math.Max(1, Ability.Range), Math.Max(1, Ability.Turns));
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
        : (actor.Sneaking ? SneakStepTime(world, actor) * actor.MoveTime / 100 : actor.MoveTime) * TimePercent / 100;

    /// <summary>How slow sneaking is: Shadowcraft makes it quicker, then full speed.</summary>
    private static int SneakStepTime(World world, Actor actor) =>
        actor.HasSpecial("shadow_runner") ? 100 : actor.HasSpecial("light_step") ? 115 : world.Content.Dungeon.SneakStepTime;

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
        if (other is null && actor.Has(Entities.StatusKind.Held))
        {
            world.Emit(new ActionFailed(actor.Id, FailureReason.Held));
            return false;
        }
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
        if (actor != world.Player)
            world.AfterStep(actor, from);
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

/// <summary>The player takes up a stance (or drops it). It takes a moment; a skill must teach it.</summary>
public sealed record StanceAction(Entities.Stance Stance) : GameAction
{
    public override int TimeCost(World world, Actor actor) => actor.HasSpecial("stance_free") ? 0 : 100;

    public override bool Perform(World world, Actor actor)
    {
        if (actor.Stance == Stance)
            return false;
        if (world.StanceProblem(actor, Stance) is { } problem)
        {
            world.Emit(new ActionFailed(actor.Id, problem));
            return false;
        }
        actor.Stance = Stance;
        world.Emit(new StanceChanged(actor.Id, Stance));
        return true;
    }
}

/// <summary>A monster announces a big blow at someone: it lands on its next action, if it still can.</summary>
public sealed record WindUpAction(AbilityDef Ability, int TargetId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (world.GetActor(TargetId) is not { IsDead: false } target)
            return false;
        world.WindUpBlow(actor, Ability, target);
        return true;
    }
}

/// <summary>Carves (or paints) a rune the player knows: on the ground underfoot, on the skin, or into gear.</summary>
public sealed record CarveAction(string RuneId, RuneSurface Surface) : GameAction
{
    public override int TimeCost(World world, Actor actor) => World.CarveTime(actor, Surface);

    public override int NoiseMade(World world, Actor actor) => Surface == RuneSurface.Body ? 0 : CarvingNoise;

    /// <summary>Chisel on stone: about as loud as a scuffle.</summary>
    private const int CarvingNoise = 5;

    public override bool Perform(World world, Actor actor)
    {
        if (world.CarveProblem(actor, RuneId, Surface) is { } problem)
        {
            world.Emit(new ActionFailed(actor.Id, problem));
            return false;
        }
        world.Carve(actor, RuneId, Surface);
        return true;
    }
}

/// <summary>Unmakes a mark at or next to the actor: their own, a seal (anyone, slowly), or any with Rune Lore 3.</summary>
public sealed record BreakMarkAction : GameAction
{
    public override int TimeCost(World world, Actor actor) => world.BreakTime(actor);

    public override bool Perform(World world, Actor actor)
    {
        if (world.BreakProblem(actor, out _) is { } problem)
        {
            world.Emit(new ActionFailed(actor.Id, problem));
            return false;
        }
        world.BreakMark(actor);
        return true;
    }
}

