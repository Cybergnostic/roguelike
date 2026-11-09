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
    /// <summary>The way back up to the level above.</summary>
    StairsUp,
    Grass,
    Road,
    /// <summary>Thick undergrowth: you can push through it, but nobody can see through it.</summary>
    Brush,
    Tree,
    /// <summary>A wall of sharpened logs around a camp.</summary>
    Palisade,
    GateOpen,
    /// <summary>Blocks movement and sight; walking into it opens it.</summary>
    GateClosed,
    /// <summary>The surface's way down into the dungeon.</summary>
    CaveMouth,
    /// <summary>The old marker where the road begins; what the valley's errand is brought back to.</summary>
    Waystone,
    /// <summary>A stone carved with a piece of the site's past. Stepping on it reads it.</summary>
    Inscription,
    /// <summary>Fallen rock: blocks movement and sight, like a pillar with a story.</summary>
    Rubble,
    /// <summary>A gate smashed open: anyone can pass and see through, and it can't be shut.</summary>
    GateBroken,
    /// <summary>Too deep to wade: blocks movement, but you can see across it.</summary>
    Water,
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
    private readonly CellGrid _walkable;
    private readonly CellGrid _transparent;
    private readonly CellGrid _passable;

    /// <summary>Cells the player has ever seen. Pure game state, so it lives in the core.</summary>
    public ArrayView<bool> Explored { get; }

    public IGridView<bool> Walkable { get; }
    public IGridView<bool> Transparent { get; }

    /// <summary>Where actors can plan a route: walkable cells and closed gates, which they open on arrival.</summary>
    public IGridView<bool> Passable { get; }

    /// <summary>The player's field of view. Recalculated by <see cref="World"/> after each turn.</summary>
    public IFOV PlayerFov { get; }

    /// <summary>Rooms produced by the generator, used for spawning.</summary>
    public IReadOnlyList<Rectangle> Rooms { get; }

    /// <summary>Named areas (lairs). Filled in while the level is populated.</summary>
    public List<Zone> Zones { get; } = [];

    public Zone? ZoneAt(Point p) => Zones.FirstOrDefault(z => z.Area.Contains(p));

    public static bool IsWalkable(Tile tile) => tile is Tile.Floor or Tile.LairFloor or Tile.StairsDown or Tile.StairsUp
        or Tile.Grass or Tile.Road or Tile.Brush or Tile.GateOpen or Tile.CaveMouth or Tile.Waystone or Tile.Inscription or Tile.GateBroken;

    public static bool IsTransparent(Tile tile) => (IsWalkable(tile) && tile != Tile.Brush) || tile == Tile.Water;

    public static bool IsPassable(Tile tile) => IsWalkable(tile) || tile == Tile.GateClosed;

    /// <summary>Where the stairs down are (every level has one).</summary>
    public Point? StairsDown { get; set; }

    /// <summary>Where the stairs up are, on levels that have a level above to return to.</summary>
    public Point? StairsUp { get; set; }

    public GameMap(ArrayView<Tile> tiles, IReadOnlyList<Rectangle> rooms)
    {
        Width = tiles.Width;
        Height = tiles.Height;
        _tiles = tiles;
        Rooms = rooms;
        Explored = new ArrayView<bool>(Width, Height);
        _walkable = new CellGrid(Width, Height);
        _transparent = new CellGrid(Width, Height);
        _passable = new CellGrid(Width, Height);
        for (int i = 0; i < Width * Height; i++)
        {
            _walkable.Cells[i] = IsWalkable(tiles[i]);
            _transparent.Cells[i] = IsTransparent(tiles[i]);
            _passable.Cells[i] = IsPassable(tiles[i]);
        }
        Walkable = _walkable;
        Transparent = _transparent;
        Passable = _passable;
        PlayerFov = new RecursiveShadowcastingFOV(Transparent);
    }

    public void SetTile(Point p, Tile tile)
    {
        _tiles[p] = tile;
        int i = p.Y * Width + p.X;
        _walkable.Cells[i] = IsWalkable(tile);
        _transparent.Cells[i] = IsTransparent(tile);
        _passable.Cells[i] = IsPassable(tile);
    }

    public bool InBounds(Point p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
}
