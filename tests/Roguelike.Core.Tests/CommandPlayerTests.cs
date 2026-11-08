using Roguelike.Core;
using Roguelike.Core.Simulation;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class CommandPlayerTests
{
    private static World Play(ulong seed, params string[] lines)
    {
        var world = World.Create(seed);
        var commands = new CommandPlayer(world);
        foreach (var line in lines)
            foreach (var action in commands.Run(line))
                world.Step(action);
        return world;
    }

    [Fact]
    public void ExploringUncoversTheMapAndStopsForTrouble()
    {
        var world = World.Create(3);
        int explored() => world.Map.Tiles.Positions().Count(p => world.Map.Explored[p]);
        int before = explored();
        var commands = new CommandPlayer(world);
        foreach (var action in commands.Run("explore 300"))
            world.Step(action);

        Assert.True(explored() > before);
        Assert.True(world.Turn > 0);
        Assert.NotNull(commands.StopReason); // something always turns up within 300 turns
    }

    [Fact]
    public void TheSameCommandsReplayTheSameGame()
    {
        string[] script = ["explore", "explore", "learn toughness", "wait 5", "explore"];
        var a = Play(7, script);
        var b = Play(7, script);
        Assert.Equal(a.Turn, b.Turn);
        Assert.Equal(a.Player.Position, b.Player.Position);
        Assert.Equal(a.Player.Hp, b.Player.Hp);
    }

    [Fact]
    public void UnknownCommandsSayWhatIsAvailable()
    {
        var commands = new CommandPlayer(World.Create(1));
        Assert.Empty(commands.Run("dance"));
        Assert.Contains("explore", commands.StopReason);
    }
}
