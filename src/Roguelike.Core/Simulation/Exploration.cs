using SadRogue.Primitives;

namespace Roguelike.Core.Simulation;

/// <summary>Auto-explore, as in most roguelikes: head for the nearest known floor next to unexplored space.</summary>
public static class Exploration
{
    /// <summary>The nearest known walkable cell that borders unexplored space, or null when the level is mapped.</summary>
    public static Point? Frontier(World world)
    {
        var map = world.Map;
        var start = world.Player.Position;
        var visited = new HashSet<Point> { start };
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur != start && World.EightWay.Any(d => map.InBounds(cur + d) && !map.Explored[cur + d]))
                return cur;
            foreach (var d in World.EightWay)
            {
                var nb = cur + d;
                if (map.InBounds(nb) && map.Walkable[nb] && map.Explored[nb] && visited.Add(nb))
                    queue.Enqueue(nb);
            }
        }
        return null;
    }

    public static Direction? NextStep(World world) =>
        Frontier(world) is { } goal ? world.StepToward(world.Player, goal) : null;
}
