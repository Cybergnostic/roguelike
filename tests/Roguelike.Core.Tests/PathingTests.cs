using Roguelike.Core.Actions;
using Roguelike.Core.Map;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class PathingTests
{
    [Fact]
    public void FleeMapMatchesAPlainRelaxation()
    {
        for (ulong seed = 0; seed < 5; seed++)
        {
            var world = World.Create(seed);
            var walkable = world.Map.Walkable;
            var danger = new DistanceMap(walkable);
            danger.Update(world.Actors.Where(a => a != world.Player).Select(a => a.Position));
            var flee = new SafetyMap(walkable);
            flee.Update(danger);

            // Reference: start at distance × factor, then lower each cell to one more than its
            // best neighbour until nothing changes.
            var expected = new Dictionary<Point, double>();
            foreach (var p in walkable.Positions())
                if (danger[p] is { } d)
                    expected[p] = d * SafetyMap.FleeFactor;
            for (bool changed = true; changed;)
            {
                changed = false;
                foreach (var p in expected.Keys.ToList())
                foreach (var dir in World.EightWay)
                    if (expected.TryGetValue(p + dir, out var nb) && nb + 1 < expected[p])
                    {
                        expected[p] = nb + 1;
                        changed = true;
                    }
            }

            foreach (var p in walkable.Positions())
                Assert.Equal(expected.TryGetValue(p, out var v) ? v : null, flee[p]);
        }
    }

    [Fact]
    public void CachedPathsStillLeadToTheGoal()
    {
        var world = World.Create(4);
        foreach (var monster in world.Actors.Where(a => a != world.Player).ToList())
            world.Damage(world.Player, monster, 1000, ranged: false);
        world.Step(new WaitAction());
        var fromStart = new DistanceMap(world.Map.Walkable);
        fromStart.Update([world.Player.Position]);
        var goal = world.Map.Walkable.Positions().Where(p => world.IsFree(p))
            .MaxBy(p => fromStart[p] ?? -1);

        for (int t = 0; t < 300 && world.Player.Position != goal; t++)
        {
            var step = world.StepToward(world.Player, goal);
            Assert.True(step is not null, $"turn {t}: stuck at {world.Player.Position} heading for {goal}");
            world.Step(new MoveAction(step.Value));
        }
        Assert.Equal(goal, world.Player.Position);
    }
}
