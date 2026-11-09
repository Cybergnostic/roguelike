using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Map;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class WiderValleyTests
{
    private static World Valley(ulong seed = 1)
    {
        var world = HistoryTests.ValleyHeldBy("tollkeepers", seed);
        world.Player.Hp = 100_000;
        return world;
    }

    private static Actor One(World w, string kind) => w.Actors.First(a => a.Kind == kind);

    private static Point NextTo(World w, Actor a) => World.EightWay.Select(d => a.Position + d).First(w.IsFree);

    [Fact]
    public void EverythingInTheValleyCanBeReached()
    {
        var world = Valley();
        var walk = new DistanceMap(world.Map.Passable);
        walk.Update([world.Player.Position]);

        Assert.All(world.Map.Tiles.Positions().Where(p => world.Map.Walkable[p]), p => Assert.True(walk[p] is not null, $"{p} is cut off"));
        Assert.All(world.Actors, a => Assert.True(world.Map.Walkable[a.Position], $"{a.Kind} is in a wall at {a.Position}"));
        Assert.All(world.Actors, a => Assert.NotNull(walk[a.Position]));
        Assert.All(world.FloorItems, f => Assert.NotNull(walk[f.Position]));
    }

    [Theory]
    [InlineData("wolf"), InlineData("boar"), InlineData("giant_spider"), InlineData("bear"), InlineData("bandit"), InlineData("bandit_chief")]
    public void TheWildsAreHostile(string kind)
    {
        var world = Valley();
        Assert.True(world.AreHostile(One(world, kind), world.Player));
    }

    [Fact]
    public void TheWildsLeaveTheGoblinsAlone()
    {
        var world = Valley();
        var goblin = world.Actors.First(a => a.Faction.Id == "goblins");
        Assert.False(world.AreHostile(One(world, "wolf"), goblin));
        Assert.False(world.AreHostile(One(world, "bandit"), goblin));
    }

    [Fact]
    public void TheStartOfTheRoadIsQuiet()
    {
        var world = Valley();
        var events = new List<GameEvent>();
        for (int t = 0; t < 150; t++)
            events.AddRange(world.Step(new WaitAction()));
        Assert.DoesNotContain(events, e => e is ActorAttacked a && a.TargetId == world.Player.Id);
    }

    [Fact]
    public void WolvesRunTheirRouteThroughTheWood()
    {
        var world = Valley();
        var wolf = One(world, "wolf");
        var start = wolf.Position;
        for (int t = 0; t < 30; t++)
            world.Step(new WaitAction());
        Assert.NotEqual(start, wolf.Position);
    }

    [Fact]
    public void WalkingIntoAVillagerTalksAndTakesNoTime()
    {
        var world = Valley();
        var villager = One(world, "villager");
        world.Player.Position = NextTo(world, villager);
        var dir = Direction.GetDirection(world.Player.Position, villager.Position);
        int turn = world.Turn;

        var first = world.Step(new MoveAction(dir));
        var second = world.Step(new MoveAction(dir));

        Assert.Equal(turn, world.Turn);
        var said = first.OfType<Talked>().Single();
        Assert.False(said.HasGoods);
        Assert.NotEqual(said.Line, second.OfType<Talked>().Single().Line);
    }

    [Fact]
    public void ThePeddlerSellsForGold()
    {
        var world = Valley();
        var peddler = One(world, "peddler");
        world.Player.Position = NextTo(world, peddler);
        world.Player.Gold = 20;

        var talk = world.Step(new TalkAction(peddler.Id));
        Assert.True(talk.OfType<Talked>().Single().HasGoods);

        var bought = world.Step(new BuyAction(peddler.Id, "healing_potion"));
        Assert.Contains(bought, e => e is ItemBought { Price: 15 });
        Assert.Equal(5, world.Player.Gold);
        Assert.Equal(4, world.Player.Inventory!.Items.Single(i => i.Def.Id == "healing_potion").Count);

        var broke = world.Step(new BuyAction(peddler.Id, "healing_potion"));
        Assert.Contains(broke, e => e is ActionFailed { Reason: FailureReason.NotEnoughGold });
        Assert.Equal(5, world.Player.Gold);
    }

    [Fact]
    public void YouCantBuyFromAcrossTheValley()
    {
        var world = Valley();
        world.Player.Gold = 500;
        var peddler = One(world, "peddler");
        world.Step(new BuyAction(peddler.Id, "healing_potion"));
        Assert.Equal(500, world.Player.Gold);
    }

    [Fact]
    public void TheHamletNeverJoinsAFight()
    {
        var world = Valley();
        var villager = One(world, "villager");
        Assert.False(world.CouldFight(world.Player, villager));
        Assert.True(world.AreHostile(One(world, "wolf"), villager));
    }

    [Fact]
    public void TheBanditDenHoldsAChiefAndAGate()
    {
        var world = Valley();
        var chief = One(world, "bandit_chief");
        Assert.Equal("the Red Road den", world.Map.ZoneAt(chief.Position)?.Name);
        Assert.Equal(Rank.Elite, chief.Rank);
        Assert.Equal(Tile.GateOpen, world.Map.Tiles[new Point(123, 32)]);
    }

    [Fact]
    public void YouSeeFartherInTheOpenThanUnderground()
    {
        var world = Valley();
        var fov = world.Map.PlayerFov.BooleanResultView;
        Assert.Contains(world.Map.Tiles.Positions(), p => fov[p] && World.Distance(p, world.Player.Position) > World.FovRadius);
    }
}
