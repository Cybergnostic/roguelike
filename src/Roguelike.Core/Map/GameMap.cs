using GoRogue.FOV;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Map;

public enum Tile : byte
{
    Wall,
    Floor,
}

/// <summary>Terrain plus derived views (walkability, transparency) and the player's view of it.</summary>
public sealed class GameMap
{
    public int Width { get; }
    public int Height { get; }
    public ArrayView<Tile> Tiles { get; }

    /// <summary>Cells the player has ever seen. Pure game state, so it lives in the core.</summary>
    public ArrayView<bool> Explored { get; }

    public IGridView<bool> Walkable { get; }
    public IGridView<bool> Transparent { get; }

    /// <summary>The player's field of view. Recalculated by <see cref="World"/> after each turn.</summary>
    public IFOV PlayerFov { get; }

    /// <summary>Rooms produced by the generator, used for spawning.</summary>
    public IReadOnlyList<Rectangle> Rooms { get; }

    public GameMap(ArrayView<Tile> tiles, IReadOnlyList<Rectangle> rooms)
    {
        Width = tiles.Width;
        Height = tiles.Height;
        Tiles = tiles;
        Rooms = rooms;
        Explored = new ArrayView<bool>(Width, Height);
        Walkable = new LambdaGridView<bool>(Width, Height, p => Tiles[p] == Tile.Floor);
        Transparent = new LambdaGridView<bool>(Width, Height, p => Tiles[p] == Tile.Floor);
        PlayerFov = new RecursiveShadowcastingFOV(Transparent);
    }

    public bool InBounds(Point p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
}
