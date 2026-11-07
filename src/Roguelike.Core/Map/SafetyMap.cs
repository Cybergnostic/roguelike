using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Map;

/// <summary>
/// Brogue-style flee map. Start from distance-to-danger multiplied by a negative factor,
/// then smooth it (each cell is at most one step worse than its best neighbour).
/// Following it downhill moves away from danger, but prefers routes that keep
/// going (toward exits and loops) over running into dead-end corners.
/// </summary>
public sealed class SafetyMap : GridViewBase<double?>
{
    public const double FleeFactor = -1.2;

    private readonly double[] _value;
    private readonly bool[] _reachable;
    private readonly PriorityQueue<int, double> _queue = new();

    public override int Width { get; }
    public override int Height { get; }

    public SafetyMap(IGridView<bool> walkable)
    {
        Width = walkable.Width;
        Height = walkable.Height;
        _value = new double[Width * Height];
        _reachable = new bool[Width * Height];
    }

    public override double? this[Point pos]
    {
        get
        {
            int i = pos.Y * Width + pos.X;
            return _reachable[i] ? _value[i] : null;
        }
    }

    public void Update(DistanceMap danger)
    {
        _queue.Clear();
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int i = y * Width + x;
            if (danger[x, y] is { } d)
            {
                _reachable[i] = true;
                _value[i] = d * FleeFactor;
                _queue.Enqueue(i, _value[i]);
            }
            else
                _reachable[i] = false;
        }

        // Dijkstra relaxation: a cell can be no more than one step worse than a neighbour.
        while (_queue.TryDequeue(out int i, out double priority))
        {
            if (priority > _value[i])
                continue; // stale entry
            int x = i % Width, y = i / Width;
            double next = _value[i] + 1;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((dx | dy) == 0 || nx < 0 || ny < 0 || nx >= Width || ny >= Height)
                    continue;
                int j = ny * Width + nx;
                if (!_reachable[j] || _value[j] <= next)
                    continue;
                _value[j] = next;
                _queue.Enqueue(j, next);
            }
        }
    }
}
