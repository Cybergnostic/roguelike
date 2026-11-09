using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;
using ShaiRandom.Generators;

namespace Roguelike.Core.Map;

/// <summary>
/// Turns an ordinary room into a lair with its own architecture. Styles are named in
/// spawns.json ("architecture"); every style must keep the room fully connected.
/// </summary>
public static class LairBuilder
{
    public static readonly IReadOnlySet<string> Styles = new HashSet<string> { "tower" };

    /// <summary>
    /// Fallen rock in the middle of a room: single blocks that never touch each other or the
    /// room's edge, so the room stays connected and every door stays clear.
    /// </summary>
    public static void Rubble(GameMap map, Rectangle room, IEnhancedRandom rng)
    {
        const double Chance = 0.4;
        foreach (var p in room.Positions())
        {
            bool interior = p.X > room.MinExtentX && p.X < room.MaxExtentX && p.Y > room.MinExtentY && p.Y < room.MaxExtentY;
            if (!interior || map.Tiles[p] != Tile.Floor || rng.NextDouble() >= Chance)
                continue;
            if (World.EightWay.All(d => map.Tiles[p + d] != Tile.Rubble))
                map.SetTile(p, Tile.Rubble);
        }
    }

    /// <summary>The timber props of an old mine working: free-standing, like a tower's pillars.</summary>
    public static void Workings(GameMap map, Rectangle room)
    {
        foreach (var p in room.Positions())
        {
            int dx = p.X - room.MinExtentX, dy = p.Y - room.MinExtentY;
            if (map.Tiles[p] == Tile.Floor && dx % 3 == 2 && dy % 3 == 2 && p.X <= room.MaxExtentX - 2 && p.Y <= room.MaxExtentY - 2)
                map.SetTile(p, Tile.Pillar);
        }
    }

    public static void Build(GameMap map, Rectangle room, string style)
    {
        switch (style)
        {
            case "tower":
                Tower(map, room);
                break;
            default:
                throw new ArgumentException($"unknown lair architecture '{style}'", nameof(style));
        }
    }

    /// <summary>
    /// The ground floor of a tower: dark flagstones and a grid of free-standing pillars.
    /// Pillars are 3 cells apart and never touch the room's edge, so they can't cut it in two;
    /// they block sight, giving cover from whoever lives here.
    /// </summary>
    private static void Tower(GameMap map, Rectangle room)
    {
        foreach (var p in room.Positions())
        {
            if (map.Tiles[p] != Tile.Floor)
                continue;
            int dx = p.X - room.MinExtentX, dy = p.Y - room.MinExtentY;
            bool pillar = dx % 3 == 2 && dy % 3 == 2 && p.X <= room.MaxExtentX - 2 && p.Y <= room.MaxExtentY - 2;
            map.SetTile(p, pillar ? Tile.Pillar : Tile.LairFloor);
        }
    }
}
