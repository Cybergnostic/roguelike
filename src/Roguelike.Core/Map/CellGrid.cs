using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Map;

/// <summary>
/// A read-only bool grid backed by a flat array (index y * Width + x). The map keeps walkability
/// in one so the per-turn pathfinding (Dijkstra and flee maps, A*) reads an array instead of
/// looking up and classifying a tile on every probe.
/// </summary>
public sealed class CellGrid : GridViewBase<bool>
{
    internal readonly bool[] Cells;

    public override int Width { get; }
    public override int Height { get; }

    public CellGrid(int width, int height)
    {
        Width = width;
        Height = height;
        Cells = new bool[width * height];
    }

    public override bool this[Point pos] => Cells[pos.Y * Width + pos.X];
}
