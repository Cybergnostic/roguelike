using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.Map;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class WorldTests
{
    private static readonly Direction[] Dirs =
        [Direction.Up, Direction.Down, Direction.Left, Direction.Right,
         Direction.UpLeft, Direction.UpRight, Direction.DownLeft, Direction.DownRight];

    /// <summary>Plays a fixed pseudo-random input sequence, independent of the world's RNG.</summary>
    private static string Simulate(ulong seed, int turns)
    {
        var world = World.Create(seed);
        var input = new Random(1234);
        for (int i = 0; i < turns && !world.Player.IsDead; i++)
            world.Step(new MoveAction(Dirs[input.Next(Dirs.Length)]));

        var actors = string.Join(";", world.Actors.Select(a => $"{a.Id}:{a.Kind}@{a.Position}:{a.Hp}"));
        return $"turn={world.Turn} {actors}";
    }

    [Fact]
    public void SameSeedProducesSameMap()
    {
        var a = World.Create(42);
        var b = World.Create(42);
        Assert.True(a.Map.Tiles.Positions().All(p => a.Map.Tiles[p] == b.Map.Tiles[p]));
    }

    [Fact]
    public void SameSeedAndInputsProduceSameGame()
    {
        Assert.Equal(Simulate(7, 300), Simulate(7, 300));
    }

    [Fact]
    public void DifferentSeedsProduceDifferentMaps()
    {
        var a = World.Create(1);
        var b = World.Create(2);
        Assert.Contains(a.Map.Tiles.Positions(), p => a.Map.Tiles[p] != b.Map.Tiles[p]);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(99UL)]
    [InlineData(123456789UL)]
    public void PlayerStartsOnFloorAndSeesSurroundings(ulong seed)
    {
        var world = World.Create(seed);
        Assert.Equal(Tile.Floor, world.Map.Tiles[world.Player.Position]);
        Assert.True(world.Map.PlayerFov.BooleanResultView[world.Player.Position]);
        Assert.True(world.Map.Explored[world.Player.Position]);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(5UL)]
    [InlineData(2026UL)]
    public void EveryFloorCellIsReachableFromThePlayer(ulong seed)
    {
        var world = World.Create(seed);
        foreach (var p in world.Map.Tiles.Positions().Where(p => world.Map.Walkable[p]))
            Assert.NotNull(world.TowardPlayer[p]);
    }

    [Fact]
    public void LongHeadlessRunDoesNotCrash()
    {
        for (ulong seed = 0; seed < 20; seed++)
            Simulate(seed, 1000);
    }

    [Fact]
    public void NoMonsterSpawnsNearTheStart()
    {
        for (ulong seed = 0; seed < 30; seed++)
        {
            var world = World.Create(seed);
            var fromStart = new Roguelike.Core.Map.DistanceMap(world.Map.Walkable);
            fromStart.Update([world.Player.Position]);
            foreach (var monster in world.Actors.Where(a => a != world.Player))
                Assert.True(fromStart[monster.Position] >= world.Content.SafeStartDistance,
                    $"seed {seed}: {monster.Kind} spawned {fromStart[monster.Position]} steps from the start");
        }
    }
}
