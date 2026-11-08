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
    private readonly int[] _relaxed;
    private readonly double[] _relaxedValue;

    public override int Width { get; }
    public override int Height { get; }

    public SafetyMap(IGridView<bool> walkable)
    {
        Width = walkable.Width;
        Height = walkable.Height;
        _value = new double[Width * Height];
        _reachable = new bool[Width * Height];
        _relaxed = new int[Width * Height];
        _relaxedValue = new double[Width * Height];
    }

    public override double? this[Point pos]
    {
        get
        {
            int i = pos.Y * Width + pos.X;
            return _reachable[i] ? _value[i] : null;
        }
    }

    /// <summary>
    /// A Dijkstra pass without a heap. The seeds (distance × factor) come sorted from the
    /// danger map's search order, read backwards; relaxed cells come out in nondecreasing
    /// order on their own, since each is one more than a value already settled. Merging the
    /// two sorted streams visits cells in the same order a priority queue would, and each
    /// cell enters the relaxed stream at most once (a later candidate is never lower).
    /// </summary>
    public void Update(DistanceMap danger)
    {
        var distance = danger._distance;
        for (int i = 0; i < distance.Length; i++)
        {
            int d = distance[i];
            _reachable[i] = d >= 0;
            _value[i] = d * FleeFactor;
        }

        var seeds = danger._queue;
        int seed = danger._reached - 1; // farthest from danger first: lowest seed value
        int head = 0, tail = 0;
        while (seed >= 0 || head < tail)
        {
            int i;
            double value;
            if (head < tail && (seed < 0 || _relaxedValue[head] <= distance[seeds[seed]] * FleeFactor))
            {
                i = _relaxed[head];
                value = _relaxedValue[head++];
            }
            else
            {
                i = seeds[seed--];
                value = distance[i] * FleeFactor;
            }
            if (value > _value[i])
                continue; // improved since

            int x = i % Width, y = i / Width;
            double next = value + 1;
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
                _relaxed[tail] = j;
                _relaxedValue[tail++] = next;
            }
        }
    }
}
