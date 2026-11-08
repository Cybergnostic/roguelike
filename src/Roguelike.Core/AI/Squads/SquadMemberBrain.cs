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
    private int _nextRaiseTurn;

    public Squad Squad { get; } = squad;

    public GameAction Decide(World world, Actor self)
    {
        var action = DecideFreely(world, self);
        return Squad.IsLeashed ? KeepToTerritory(world, self, action) : action;
    }

    /// <summary>
    /// Lair squads defend their territory but don't leave it: a step past the leash becomes a
    /// wait (at the border, where archers and casters can still shoot). Steps back home are fine.
    /// </summary>
    private GameAction KeepToTerritory(World world, Actor self, GameAction action)
    {
        if (action is not MoveAction move)
            return action;
        var to = self.Position + move.Direction;
        if (world.ActorAt(to) is { } other && world.AreHostile(self, other))
            return action; // a bump attack, not a step
        return Squad.WithinLeash(to) || Squad.DistanceFromHome(to) < Squad.DistanceFromHome(self.Position)
            ? action
            : new WaitAction();
    }

    private GameAction DecideFreely(World world, Actor self)
    {
        var role = Squad.RoleOf(self);

        // A runner on a mission keeps running whatever the squad is doing: fetching help
        // matters most when the squad is losing.
        if (role == SquadRole.Runner && Squad.HelpTargetSquadId is not null && Squad.State != SquadState.Idle)
            return Runner(world, self);

        // A roused necromancer raises whatever dead it can reach. At rest he broods, so the
        // bones in his lair are still lying there when someone comes calling.
        if (role == SquadRole.Necromancer && Squad.State is SquadState.Engage or SquadState.Alert
            && TryRaise(world, self) is { } raise)
            return raise;

        return Squad.State switch
        {
            SquadState.Flee => Flee(world, self),
            SquadState.Engage => role switch
            {
                SquadRole.Archer or SquadRole.Necromancer => Archer(world, self),
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

        // Close in, but never into melee reach: a shooter that can't shoot waits instead.
        var step = world.StepDownhill(self, world.TowardEnemiesOf(self.Faction));
        if (step is not { } s || NextToHostile(world, self, self.Position + s))
            return new WaitAction();
        return new MoveAction(s);
    }

    private static bool NextToHostile(World world, Actor self, Point p) =>
        World.EightWay.Any(d => world.ActorAt(p + d) is { } other && other != self && world.AreHostile(self, other));

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

    /// <summary>Nearest raisable corpse in reach and sight, if the cooldown and minion cap allow.</summary>
    private GameAction? TryRaise(World world, Actor self)
    {
        var raise = world.Content.Monsters.GetValueOrDefault(self.Kind)?.Raise;
        if (raise is null || world.Turn < _nextRaiseTurn || world.MinionsOf(self).Count() >= raise.MaxMinions)
            return null;

        var corpse = world.Corpses
            .Where(c => world.Content.Monsters.GetValueOrDefault(c.Kind)?.RisesAs is not null
                        && World.Distance(self.Position, c.Position) <= raise.Range
                        && world.IsFree(c.Position)
                        && world.HasLineOfSight(self.Position, c.Position))
            .OrderBy(c => World.Distance(self.Position, c.Position))
            .FirstOrDefault();
        if (corpse is null)
            return null;

        _nextRaiseTurn = world.Turn + raise.Cooldown;
        return new RaiseDeadAction(corpse.Id);
    }

    private GameAction Runner(World world, Actor self)
    {
        var target = world.Squads.FirstOrDefault(s => s.Id == Squad.HelpTargetSquadId);
        var targetLeader = target?.Leader;
        if (target is null || targetLeader is null)
        {
            // The band it ran for is gone: try the next nearest, or stay and fight if there's none.
            Squad.HelpTargetSquadId = null;
            SquadDirector.DispatchRunner(world, Squad);
            target = world.Squads.FirstOrDefault(s => s.Id == Squad.HelpTargetSquadId);
            targetLeader = target?.Leader;
            if (target is null || targetLeader is null)
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
            // Amble, so the group can keep up; raiders march, pausing only for stragglers.
            if (Squad.Destination is not { } goal)
                return new WaitAction();
            if (Squad.IsRaiding)
            {
                bool stragglers = Squad.Members.Any(m => World.Distance(m.Position, self.Position) > 4);
                Squad.StragglerWait = stragglers ? Squad.StragglerWait + 1 : 0;
                if (stragglers && Squad.StragglerWait <= Squad.MaxStragglerWait)
                    return new WaitAction();
            }
            else if (world.Rng.NextInt(4) == 0)
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
