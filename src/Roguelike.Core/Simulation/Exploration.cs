using SadRogue.Primitives;

namespace Roguelike.Core.Simulation;

/// <summary>Auto-explore, as in most roguelikes: head for the nearest known floor next to unexplored space.</summary>
public static class Exploration
{
    /// <summary>The nearest known walkable cell that borders unexplored space, or null when the level is mapped.</summary>
    public static Point? Frontier(World world)
    {
        var map = world.Map;
        int w = map.Width, h = map.Height;
        var start = world.Player.Position;
        var visited = new bool[w * h];
        var queue = new int[w * h];
        int head = 0, tail = 0;
        visited[start.Y * w + start.X] = true;
        queue[tail++] = start.Y * w + start.X;
        while (head < tail)
        {
            int i = queue[head++];
            int x = i % w, y = i / w;
            bool frontier = false;
            foreach (var d in World.EightWay) // same order as always, so the same frontier wins ties
            {
                int nx = x + d.DeltaX, ny = y + d.DeltaY;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    continue;
                if (!map.Explored[nx, ny])
                {
                    frontier = true;
                    continue;
                }
                int j = ny * w + nx;
                if (!visited[j] && map.Walkable[nx, ny])
                {
                    visited[j] = true;
                    queue[tail++] = j;
                }
            }
            if (frontier && i != queue[0])
                return new Point(x, y);
        }
        return null;
    }

    public static Direction? NextStep(World world) =>
        Frontier(world) is { } goal ? world.StepToward(world.Player, goal) : null;
}
