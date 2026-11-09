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

    /// <summary>Everything a level starts with, in a form two games can compare.</summary>
    private static List<string> Layout(World world) =>
    [
        .. world.Map.Tiles.Positions().Select(p => world.Map.Tiles[p].ToString()),
        .. world.Actors.Where(a => a != world.Player).Select(a => $"{a.Kind} {a.Position} {a.MaxHp}"),
        .. world.FloorItems.Select(f => $"{f.Item.Def.Id} {f.Item.Enchantment} {f.Item.Rarity} {f.Position}"),
        .. world.Corpses.Select(c => $"{c.Kind} {c.Position}"),
    ];

    [Fact]
    public void ASeedGivesTheSameLevelsHoweverYouPlay()
    {
        for (ulong seed = 0; seed < 5; seed++)
        {
            var hasty = World.Create(seed);
            var slow = World.Create(seed);
            slow.Player.Hp = 100_000; // this test is about levels, not survival

            // Time passing on the first level uses up random numbers in one game only.
            for (int turn = 0; turn < 150; turn++)
                slow.Step(new WaitAction());

            for (int level = 0; level < 3; level++)
            {
                TakeStairs(hasty);
                TakeStairs(slow);
                Assert.Equal(Layout(hasty), Layout(slow));
            }
        }
    }

    private static void ClimbBack(World world)
    {
        world.Player.Position = world.Map.StairsUp!.Value;
        world.Step(new AscendAction());
    }

    [Fact]
    public void ALevelYouLeaveIsStillThereWhenYouComeBack()
    {
        var world = World.Create(4);
        var player = world.Player;
        var map = world.Map;
        var victim = world.Actors.First(a => a != player);
        victim.Hp = 0;
        world.Step(new WaitAction()); // the dead are cleared away
        var survivors = world.Actors.Where(a => a != player).Select(a => (a, a.Position, a.Hp)).ToList();
        var exploredCell = map.Tiles.Positions().First(p => map.Explored[p]);
        int items = world.FloorItems.Count;

        TakeStairs(world);
        var below = world.Map;
        TakeStairs(world);
        ClimbBack(world);
        Assert.Same(below, world.Map);
        var events = world.Step(new WaitAction()); // a turn on the middle level, then all the way back
        Assert.DoesNotContain(events, e => e is LevelEntered);
        ClimbBack(world);

        Assert.Equal(1, world.Depth);
        Assert.Same(map, world.Map);
        Assert.Equal(map.StairsDown, player.Position);
        Assert.Equal(survivors, world.Actors.Where(a => a != player).Select(a => (a, a.Position, a.Hp)).ToList());
        Assert.DoesNotContain(victim, world.Actors);
        Assert.True(map.Explored[exploredCell]);
        Assert.Equal(items, world.FloorItems.Count);
    }

    [Fact]
    public void GoingBackDownReturnsToTheSameLevelAtItsStairsUp()
    {
        var world = World.Create(4);
        TakeStairs(world);
        var level2 = world.Map;
        ClimbBack(world);

        var events = TakeStairs(world);

        Assert.Same(level2, world.Map);
        Assert.Equal(level2.StairsUp, world.Player.Position);
        Assert.Contains(events, e => e is LevelEntered { Depth: 2, GoingUp: false });
    }

    [Fact]
    public void ClimbingUpIsAnnouncedAndUsesATurn()
    {
        var world = World.Create(4);
        TakeStairs(world);
        int turn = world.Turn;
        world.Player.Position = world.Map.StairsUp!.Value;

        var events = world.Step(new AscendAction());

        Assert.Contains(events, e => e is LevelEntered { Depth: 1, GoingUp: true });
        Assert.Equal(turn + 1, world.Turn);
    }

    [Fact]
    public void TheFirstLevelHasNoStairsUpAndYouCantClimbWithoutThem()
    {
        var world = World.Create(4);
        Assert.Null(world.Map.StairsUp);
        var events = world.Step(new AscendAction());
        Assert.Equal(1, world.Depth);
        Assert.Contains(events, e => e is ActionFailed { Reason: FailureReason.NotOnStairs });
    }

    [Fact]
    public void WhoeverStandsOnTheStairsMakesRoomWhenYouReturn()
    {
        var world = World.Create(4);
        var stairs = world.Map.StairsDown!.Value;
        var monster = world.Actors.First(a => a != world.Player);
        world.Player.Position = stairs;
        world.Step(new DescendAction());
        // While the player is below, something ended up on the stairs of the level above.
        monster.Position = stairs;

        ClimbBack(world);

        Assert.Equal(stairs, world.Player.Position);
        Assert.NotEqual(stairs, monster.Position);
        Assert.True(world.Map.Walkable[monster.Position]);
    }

    [Fact]
    public void ReplayingTheSameInputsAcrossLevelsGivesTheSameGame()
    {
        static List<string> Play()
        {
            var world = World.Create(9);
            world.Player.Hp = 100_000;
            var log = new List<string>();
            void Record(IEnumerable<GameEvent> events) => log.AddRange(events.Select(e => e.ToString()!));
            for (int i = 0; i < 40; i++) Record(world.Step(new WaitAction()));
            world.Player.Position = world.Map.StairsDown!.Value;
            Record(world.Step(new DescendAction()));
            for (int i = 0; i < 40; i++) Record(world.Step(new WaitAction()));
            world.Player.Position = world.Map.StairsUp!.Value;
            Record(world.Step(new AscendAction()));
            for (int i = 0; i < 40; i++) Record(world.Step(new WaitAction()));
            return log;
        }

        Assert.Equal(Play(), Play());
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
