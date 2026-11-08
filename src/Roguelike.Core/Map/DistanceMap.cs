using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Map;

/// <summary>
/// Dijkstra map with uniform 8-way step cost: distance in steps from the nearest goal,
/// null where unreachable. Following it downhill approaches the goals.
/// A plain breadth-first search, much cheaper than GoRogue's general GoalMap (~0.09 ms
/// on 80x24); built for per-faction "toward enemies" maps, recalculated every turn.
/// </summary>
public sealed class DistanceMap : GridViewBase<double?>
{
    private readonly IGridView<bool> _walkable;
    private readonly bool[]? _cells; // the map's own walkability array, when it has one (fast path)
    internal readonly int[] _distance;
    /// <summary>Reachable cells in the order the search reached them: nondecreasing distance.</summary>
    internal readonly int[] _queue;
    internal int _reached;

    public override int Width { get; }
    public override int Height { get; }

    public DistanceMap(IGridView<bool> walkable)
    {
        _walkable = walkable;
        _cells = (walkable as CellGrid)?.Cells;
        Width = walkable.Width;
        Height = walkable.Height;
        _distance = new int[Width * Height];
        _queue = new int[Width * Height];
        Array.Fill(_distance, -1);
    }

    public override double? this[Point pos]
    {
        get
        {
            int d = _distance[pos.Y * Width + pos.X];
            return d < 0 ? null : d;
        }
    }

    /// <summary>Recalculates distances from the given goal cells.</summary>
    public void Update(IEnumerable<Point> goals)
    {
        Array.Fill(_distance, -1);
        int head = 0, tail = 0;
        foreach (var g in goals)
        {
            int i = g.Y * Width + g.X;
            if (_distance[i] >= 0 || !_walkable[g])
                continue;
            _distance[i] = 0;
            _queue[tail++] = i;
        }

        var cells = _cells;
        while (head < tail)
        {
            int i = _queue[head++];
            int x = i % Width, y = i / Width, next = _distance[i] + 1;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((dx | dy) == 0 || nx < 0 || ny < 0 || nx >= Width || ny >= Height)
                    continue;
                int j = ny * Width + nx;
                if (_distance[j] >= 0 || !(cells is null ? _walkable[nx, ny] : cells[j]))
                    continue;
                _distance[j] = next;
                _queue[tail++] = j;
            }
        }
        _reached = tail;
    }
}
