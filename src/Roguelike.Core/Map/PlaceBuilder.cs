using Roguelike.Core.Content;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Map;

/// <summary>Turns a hand-drawn place from the content files into a map.</summary>
public static class PlaceBuilder
{
    public static GameMap Build(PlaceDef place)
    {
        var tiles = new ArrayView<Tile>(place.Width, place.Height);
        foreach (var p in tiles.Positions())
            tiles[p] = place.TileAt(p.X, p.Y);

        var areas = place.Zones.Select(z => new Rectangle(z.Area[0], z.Area[1], z.Area[2], z.Area[3])).ToList();
        var map = new GameMap(tiles, areas);
        map.StairsDown = tiles.Positions().First(p => tiles[p] == Tile.CaveMouth);

        for (int i = 0; i < areas.Count; i++)
        {
            var center = areas[i].Center;
            var heart = areas[i].Positions().Where(p => map.Walkable[p])
                .OrderBy(p => Distance.Chebyshev.Calculate(p, center)).First();
            map.Zones.Add(new Zone(place.Zones[i].Name, areas[i], place.Zones[i].Faction, heart));
        }
        return map;
    }
}
