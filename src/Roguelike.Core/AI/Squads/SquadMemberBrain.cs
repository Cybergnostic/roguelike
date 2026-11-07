using Roguelike.Core.Actions;
using Roguelike.Core.Entities;
using SadRogue.Primitives;

namespace Roguelike.Core.AI.Squads;

/// <summary>
/// One squad member's decisions: reads the squad's shared state and acts according to its role.
/// </summary>
public sealed class SquadMemberBrain(Squad squad) : IBrain
{
    private const int ShamanHealCooldown = 5;
    private const int ShamanHealRange = 6;
    private const int MaxHoldTurns = 8;

    private int _nextHealTurn;

    public Squad Squad { get; } = squad;

    public GameAction Decide(World world, Actor self)
    {
        var role = Squad.RoleOf(self);
        return Squad.State switch
        {
            SquadState.Flee => Flee(world, self),
            SquadState.Engage => role switch
            {
                SquadRole.Archer => Archer(world, self),
                SquadRole.Shaman => Shaman(world, self),
                SquadRole.Runner when Squad.HelpTargetSquadId is not null => Runner(world, self),
                _ => Warrior(world, self),
            },
            SquadState.Alert => Investigate(world, self),
            _ => Patrol(world, self),
        };
    }

    // ---- Roles while engaged ----

    private GameAction Warrior(World world, Actor self)
    {
        var player = world.Player;
        if (world.IsAdjacent(self.Position, player.Position))
            return new AttackAction(player.Id);

        var step = world.StepDownhill(self, world.TowardPlayer);
        if (step is null)
            return new WaitAction();

        // Don't file into the player's corridor one at a time; wait at the mouth for a while,
        // as long as someone can shoot at them in the meantime.
        bool wouldEnterChokepoint = world.IsChokepoint(self.Position + step.Value)
                                    && !world.IsChokepoint(self.Position);
        if (wouldEnterChokepoint && Squad.HoldTurns > 0 && Squad.HoldTurns <= MaxHoldTurns
            && Squad.WithRole(SquadRole.Archer).Any())
            return new WaitAction();

        return new MoveAction(step.Value);
    }

    private GameAction Archer(World world, Actor self)
    {
        var player = world.Player;
        int distance = World.Distance(self.Position, player.Position);

        if (distance <= 1)
        {
            var away = world.StepDownhill(self, world.AwayFromPlayer);
            return away is { } a ? new MoveAction(a) : new AttackAction(player.Id);
        }

        if (world.PlayerCanSee(self) && distance <= self.Range)
        {
            if (world.HasClearShot(self.Position, player.Position))
                return new RangedAttackAction(player.Id);

            // An ally is in the way: shuffle sideways to a spot with a clear shot.
            foreach (var dir in World.EightWay)
            {
                var p = self.Position + dir;
                if (world.IsFree(p) && World.Distance(p, player.Position) > 1
                    && world.HasClearShot(p, player.Position))
                    return new MoveAction(dir);
            }
            return new WaitAction();
        }

        var step = world.StepDownhill(self, world.TowardPlayer);
        return step is { } s ? new MoveAction(s) : new WaitAction();
    }

    private GameAction Shaman(World world, Actor self)
    {
        if (world.Turn >= _nextHealTurn)
        {
            var patient = Squad.Members
                .Where(m => m != self && m.Hp < m.MaxHp * 0.6
                            && World.Distance(m.Position, self.Position) <= ShamanHealRange
                            && world.HasLineOfSight(self.Position, m.Position))
                .OrderBy(m => (double)m.Hp / m.MaxHp)
                .FirstOrDefault();
            if (patient is not null)
            {
                _nextHealTurn = world.Turn + ShamanHealCooldown;
                return new HealAction(patient.Id, Amount: 6);
            }
        }

        // Stay behind the line: back off if close, creep up if far.
        int distance = World.Distance(self.Position, world.Player.Position);
        if (distance <= 3)
        {
            var away = world.StepDownhill(self, world.AwayFromPlayer);
            if (away is { } a)
                return new MoveAction(a);
            if (distance <= 1)
                return new AttackAction(world.Player.Id);
        }
        else if (distance > 5)
        {
            var step = world.StepDownhill(self, world.TowardPlayer);
            if (step is { } s)
                return new MoveAction(s);
        }
        return new WaitAction();
    }

    private GameAction Runner(World world, Actor self)
    {
        var target = world.Squads.FirstOrDefault(s => s.Id == Squad.HelpTargetSquadId);
        var targetLeader = target?.Leader;
        if (target is null || targetLeader is null)
        {
            Squad.HelpTargetSquadId = null; // nobody left to fetch; fight instead
            return Warrior(world, self);
        }

        if (World.Distance(self.Position, targetLeader.Position) <= 2)
            return new CallForHelpAction(Squad.Id, target.Id);

        var step = world.StepToward(self, targetLeader.Position);
        return step is { } s ? new MoveAction(s) : new WaitAction();
    }

    // ---- Other states ----

    private GameAction Flee(World world, Actor self)
    {
        var step = world.StepDownhill(self, world.AwayFromPlayer);
        if (step is { } s)
            return new MoveAction(s);
        // Cornered: fight back.
        return world.IsAdjacent(self.Position, world.Player.Position)
            ? new AttackAction(world.Player.Id)
            : new WaitAction();
    }

    private GameAction Investigate(World world, Actor self)
    {
        if (Squad.Destination is not { } goal)
            return new WaitAction();
        var step = world.StepToward(self, goal);
        return step is { } s ? new MoveAction(s) : new WaitAction();
    }

    private GameAction Patrol(World world, Actor self)
    {
        var leader = Squad.Leader;
        if (leader == self)
        {
            // Amble, so the group can keep up.
            if (Squad.Destination is not { } goal || world.Rng.NextInt(4) == 0)
                return new WaitAction();
            var step = world.StepToward(self, goal);
            return step is { } s ? new MoveAction(s) : new WaitAction();
        }

        if (leader is not null && World.Distance(self.Position, leader.Position) > 2)
        {
            var step = world.StepToward(self, leader.Position);
            return step is { } s ? new MoveAction(s) : new WaitAction();
        }
        return new WaitAction();
    }
}
