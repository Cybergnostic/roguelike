using GoRogue.FOV;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Map;

public enum Tile : byte
{
    Wall,
    Floor,
    /// <summary>Floor of a special place (a lair); walkable and see-through like floor.</summary>
    LairFloor,
    /// <summary>A free-standing column: blocks movement and sight, so it gives cover.</summary>
    Pillar,
    /// <summary>The way down to the next level (or, on the last level, out).</summary>
    StairsDown,
}

/// <summary>
/// A named area of the map, such as a lair, held by a faction. Heart is the walkable cell at
/// its centre: where its owners wait, and where raiders head. Entering a zone is announced.
/// </summary>
public sealed record Zone(string Name, Rectangle Area, string FactionId, Point Heart);

/// <summary>Terrain plus derived views (walkability, transparency) and the player's view of it.</summary>
public sealed class GameMap
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>The terrain. Read-only so walkability stays in sync: change it with <see cref="SetTile"/>.</summary>
    public IGridView<Tile> Tiles => _tiles;
    private readonly ArrayView<Tile> _tiles;
    private readonly CellGrid _open;

    /// <summary>Cells the player has ever seen. Pure game state, so it lives in the core.</summary>
    public ArrayView<bool> Explored { get; }

    public IGridView<bool> Walkable { get; }
    public IGridView<bool> Transparent { get; }

    /// <summary>The player's field of view. Recalculated by <see cref="World"/> after each turn.</summary>
    public IFOV PlayerFov { get; }

    /// <summary>Rooms produced by the generator, used for spawning.</summary>
    public IReadOnlyList<Rectangle> Rooms { get; }

    /// <summary>Named areas (lairs). Filled in while the level is populated.</summary>
    public List<Zone> Zones { get; } = [];

    public Zone? ZoneAt(Point p) => Zones.FirstOrDefault(z => z.Area.Contains(p));

    public static bool IsOpen(Tile tile) => tile is Tile.Floor or Tile.LairFloor or Tile.StairsDown;

    /// <summary>Where the stairs down are (every level has one).</summary>
    public Point? StairsDown { get; set; }

    public GameMap(ArrayView<Tile> tiles, IReadOnlyList<Rectangle> rooms)
    {
        Width = tiles.Width;
        Height = tiles.Height;
        _tiles = tiles;
        Rooms = rooms;
        Explored = new ArrayView<bool>(Width, Height);
        // Every open tile is also see-through (and the reverse), so one grid serves both.
        _open = new CellGrid(Width, Height);
        for (int i = 0; i < Width * Height; i++)
            _open.Cells[i] = IsOpen(tiles[i]);
        Walkable = _open;
        Transparent = _open;
        PlayerFov = new RecursiveShadowcastingFOV(Transparent);
    }

    public void SetTile(Point p, Tile tile)
    {
        _tiles[p] = tile;
        _open.Cells[p.Y * Width + p.X] = IsOpen(tile);
    }

    public bool InBounds(Point p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
}
