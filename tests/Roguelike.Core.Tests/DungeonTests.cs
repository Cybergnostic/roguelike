using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.Events;
using Roguelike.Core.Map;
using Roguelike.Core.Simulation;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class DungeonTests
{
    private static IReadOnlyList<GameEvent> TakeStairs(World world)
    {
        world.Player.Position = world.Map.StairsDown!.Value;
        return world.Step(new DescendAction());
    }

    [Fact]
    public void EveryLevelHasStairsYouCanReach()
    {
        for (ulong seed = 0; seed < 15; seed++)
        {
            var world = World.Create(seed);
            for (int level = 0; level < 3; level++)
            {
                var stairs = world.Map.StairsDown;
                Assert.NotNull(stairs);
                Assert.Equal(Tile.StairsDown, world.Map.Tiles[stairs.Value]);
                var fromStart = new DistanceMap(world.Map.Walkable);
                fromStart.Update([world.Player.Position]);
                Assert.NotNull(fromStart[stairs.Value]);
                TakeStairs(world);
            }
        }
    }

    [Fact]
    public void DescendingKeepsTheCharacterAndBuildsANewLevel()
    {
        var world = World.Create(4);
        var player = world.Player;
        player.Character!.Gain(500);
        world.Step(new LearnSkillAction("toughness"));
        int items = player.Inventory!.Items.Count, hp = player.Hp, turn = world.Turn;
        var oldMonsters = world.Actors.Where(a => a != player).ToList();

        var events = TakeStairs(world);

        Assert.Contains(events, e => e is LevelEntered { Depth: 2 });
        Assert.Equal(2, world.Depth);
        Assert.Same(player, world.Player);
        Assert.Equal(items, player.Inventory.Items.Count);
        Assert.Equal(1, player.Character.Rank("toughness"));
        Assert.Equal(hp, player.Hp);
        Assert.Equal(turn + 1, world.Turn);
        Assert.DoesNotContain(world.Actors, oldMonsters.Contains);
        Assert.Contains(world.Actors, a => a != player);
    }

    [Fact]
    public void MonstersGrowWithDepth()
    {
        var content = Roguelike.Core.Content.ContentDatabase.Default;
        var deep = World.Create(1, depth: 7);
        var warrior = content.Monsters["goblin_warrior"];
        var spawned = deep.SpawnMonster("goblin_warrior", deep.Map.Tiles.Positions().First(deep.IsFree));
        Assert.Equal(content.Dungeon.HpFor(warrior.MaxHp, 7), spawned.MaxHp);
        Assert.True(spawned.MaxHp > warrior.MaxHp);
        Assert.Equal(warrior.Attack + content.Dungeon.AttackBonus(7), spawned.Attack);
    }

    [Fact]
    public void BigWarPartiesOnlyLiveDeepDown()
    {
        var party = Roguelike.Core.Content.ContentDatabase.Default.SquadTemplates.Single(t => t.Name == "goblin war party");
        Assert.False(party.AppearsAt(1));
        Assert.True(party.AppearsAt(party.MinDepth));
    }

    [Fact]
    public void TheStairsOnTheLastLevelLeadOutAndWin()
    {
        var world = World.Create(2, depth: Roguelike.Core.Content.ContentDatabase.Default.Dungeon.Levels);
        var events = TakeStairs(world);
        Assert.True(world.IsWon);
        Assert.True(world.IsOver);
        Assert.Contains(events, e => e is GameWon);
        Assert.Empty(world.Step(new WaitAction())); // nothing happens after the end
    }

    [Fact]
    public void YouCantDescendWithoutStairs()
    {
        var world = World.Create(2);
        var events = world.Step(new DescendAction());
        Assert.Equal(1, world.Depth);
        Assert.Contains(events, e => e is ActionFailed { Reason: FailureReason.NotOnStairs });
    }

    [Fact]
    public void TheTacticalBotGetsDownstairs()
    {
        // A smoke test: in a few games the bot must make progress and never hang the simulation.
        var results = Enumerable.Range(0, 4)
            .Select(seed => Simulator.Run((ulong)seed, new TacticalBot(), maxTurns: 4000))
            .ToList();
        Assert.Contains(results, r => r.DepthReached >= 2);
    }
}
