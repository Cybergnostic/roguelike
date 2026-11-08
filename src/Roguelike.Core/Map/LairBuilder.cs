using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Map;

/// <summary>
/// Turns an ordinary room into a lair with its own architecture. Styles are named in
/// spawns.json ("architecture"); every style must keep the room fully connected.
/// </summary>
public static class LairBuilder
{
    public static readonly IReadOnlySet<string> Styles = new HashSet<string> { "tower" };

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
