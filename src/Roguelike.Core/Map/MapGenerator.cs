using GoRogue.MapGeneration;
using GoRogue.MapGeneration.ContextComponents;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;
using ShaiRandom.Generators;

namespace Roguelike.Core.Map;

/// <summary>
/// Builds a level using GoRogue's generators. This is the place the history-driven
/// generator will plug in later; for now it is GoRogue's rooms-and-mazes algorithm.
/// </summary>
public static class MapGenerator
{
    public static GameMap RoomsAndMazes(int width, int height, IEnhancedRandom rng)
    {
        var generator = new Generator(width, height)
            .ConfigAndGenerateSafe(gen => gen.AddSteps(
                DefaultAlgorithms.DungeonMazeMapSteps(rng: rng, minRooms: 6, maxRooms: 12)));

        var wallFloor = generator.Context.GetFirst<ISettableGridView<bool>>("WallFloor");
        var tiles = new ArrayView<Tile>(width, height);
        tiles.ApplyOverlay(p => wallFloor[p] ? Tile.Floor : Tile.Wall);

        var rooms = generator.Context.GetFirstOrDefault<ItemList<Rectangle>>("Rooms");
        var roomList = rooms?.Items.ToList() ?? [];

        return new GameMap(tiles, roomList);
    }
}
