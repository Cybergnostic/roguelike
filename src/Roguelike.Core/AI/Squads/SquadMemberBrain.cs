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
    private const int ShoutRange = 6;

    private int _nextHealTurn;

    public Squad Squad { get; } = squad;

    public GameAction Decide(World world, Actor self)
    {
        var role = Squad.RoleOf(self);

        // A runner on a mission keeps running whatever the squad is doing: fetching help
        // matters most when the squad is losing.
        if (role == SquadRole.Runner && Squad.HelpTargetSquadId is not null && Squad.State != SquadState.Idle)
            return Runner(world, self);

        return Squad.State switch
        {
            SquadState.Flee => Flee(world, self),
            SquadState.Engage => role switch
            {
                SquadRole.Archer => Archer(world, self),
                SquadRole.Shaman => Shaman(world, self),
                _ => Warrior(world, self),
            },
            SquadState.Alert => Investigate(world, self),
            _ => Patrol(world, self),
        };
    }

    // ---- Roles while engaged ----
    // "Enemy" is anyone hostile to this actor's faction (the player, or another faction).

    private GameAction Warrior(World world, Actor self)
    {
        if (world.AdjacentHostile(self) is { } adjacent)
            return new AttackAction(adjacent.Id);

        var step = world.StepDownhill(self, world.TowardEnemiesOf(self.Faction));
        if (step is null)
            return new WaitAction();

        // Don't file into the enemy's corridor one at a time; wait at the mouth for a while,
        // as long as someone can shoot at them in the meantime.
        bool wouldEnterChokepoint = world.IsChokepoint(self.Position + step.Value)
                                    && !world.IsChokepoint(self.Position);
        if (wouldEnterChokepoint && Squad.IsHoldingPassage)
            return new WaitAction();

        return new MoveAction(step.Value);
    }

    private GameAction Archer(World world, Actor self)
    {
        if (world.AdjacentHostile(self) is { } adjacent)
        {
            var away = world.StepDownhill(self, world.AwayFromEnemiesOf(self.Faction));
            return away is { } a ? new MoveAction(a) : new AttackAction(adjacent.Id);
        }

        var target = world.NearestVisibleHostile(self);
        if (target is not null && World.Distance(self.Position, target.Position) <= self.Range)
        {
            if (world.HasClearShot(self.Position, target.Position))
                return new RangedAttackAction(target.Id);

            // An ally is in the way: shuffle sideways to a spot with a clear shot.
            foreach (var dir in World.EightWay)
            {
                var p = self.Position + dir;
                if (world.IsFree(p) && World.Distance(p, target.Position) > 1
                    && world.HasClearShot(p, target.Position))
                    return new MoveAction(dir);
            }
            return new WaitAction();
        }

        var step = world.StepDownhill(self, world.TowardEnemiesOf(self.Faction));
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
        var enemy = world.NearestVisibleHostile(self);
        int distance = enemy is null ? int.MaxValue : World.Distance(self.Position, enemy.Position);
        if (distance <= 3)
        {
            var away = world.StepDownhill(self, world.AwayFromEnemiesOf(self.Faction));
            if (away is { } a)
                return new MoveAction(a);
            if (world.AdjacentHostile(self) is { } adjacent)
                return new AttackAction(adjacent.Id);
        }
        else if (distance > 5)
        {
            var step = world.StepDownhill(self, world.TowardEnemiesOf(self.Faction));
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

        // Shouting distance is enough: the other squad doesn't have to stand still to be reached.
        if (World.Distance(self.Position, targetLeader.Position) <= ShoutRange
            && world.HasLineOfSight(self.Position, targetLeader.Position))
            return new CallForHelpAction(Squad.Id, target.Id);

        var step = world.StepToward(self, targetLeader.Position);
        return step is { } s ? new MoveAction(s) : new WaitAction();
    }

    // ---- Other states ----

    private GameAction Flee(World world, Actor self)
    {
        var step = world.StepDownhill(self, world.AwayFromEnemiesOf(self.Faction));
        if (step is { } s)
            return new MoveAction(s);
        // Cornered: fight back.
        return world.AdjacentHostile(self) is { } adjacent
            ? new AttackAction(adjacent.Id)
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
