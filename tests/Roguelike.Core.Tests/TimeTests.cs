using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Map;
using SadRogue.Primitives;

namespace Roguelike.Core.Tests;

public class TimeTests
{
    private static List<GameEvent> Run(World world, int turns)
    {
        var events = new List<GameEvent>();
        for (int t = 0; t < turns; t++)
            events.AddRange(world.Step(new WaitAction()));
        return events;
    }

    /// <summary>Goes down the cave, spends these turns below unseen and unharmed, and comes back; returns what the return brought.</summary>
    private static IReadOnlyList<GameEvent> AwayFor(World world, int turns)
    {
        world.Player.Position = world.Map.StairsDown!.Value;
        world.Step(new DescendAction());
        world.ObserverMode = true;
        Run(world, turns);
        world.ObserverMode = false;
        world.Player.Position = world.Map.StairsUp!.Value;
        return world.Step(new AscendAction());
    }

    private static World QuietLevel(ulong seed = 3)
    {
        var world = World.Create(seed);
        world.Player.Hp = 100_000;
        return world;
    }

    private static Actor FirstGoblin(World world) => world.Actors.First(a => a.Faction.Id == "goblins");

    [Fact]
    public void CorpsesWitherAndThenRotAway()
    {
        var world = QuietLevel();
        var goblin = FirstGoblin(world);
        foreach (var other in world.Actors.Where(a => a != world.Player && a != goblin))
            other.Hp = 0;
        int before = world.Corpses.Count;
        world.Damage(world.Player, goblin, 999, ranged: false);
        var corpse = world.Corpses.Single(c => c.Kind == goblin.Kind && !c.Ancient);
        Assert.False(world.IsWithered(corpse));
        Assert.Equal(before + 1, world.Corpses.Count);

        Run(world, world.Content.Dungeon.CorpseWithersAfter + 1);
        Assert.Contains(corpse, world.Corpses);
        Assert.True(world.IsWithered(corpse));

        Run(world, world.Content.Dungeon.CorpseGoneAfter - world.Content.Dungeon.CorpseWithersAfter + 10);
        Assert.DoesNotContain(corpse, world.Corpses);
    }

    [Fact]
    public void TheAncientDeadDoNotRotAway()
    {
        var world = QuietLevel();
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        int ancient = world.Corpses.Count(c => c.Ancient);
        Assert.True(ancient > 0);

        Run(world, world.Content.Dungeon.CorpseGoneAfter + 20);

        Assert.Equal(ancient, world.Corpses.Count(c => c.Ancient));
        Assert.All(world.Corpses.Where(c => c.Ancient), c => Assert.True(world.IsWithered(c)));
    }

    [Fact]
    public void CorpsesKeepRottingWhileYouAreGone()
    {
        var world = HistoryTests.ValleyHeldBy("tollkeepers");
        world.Player.Hp = 100_000;
        var goblin = FirstGoblin(world);
        world.Damage(world.Player, goblin, 999, ranged: false);
        Assert.NotEmpty(world.Corpses);
        foreach (var guard in world.Squads.Where(s => s.Camp is not null).SelectMany(s => s.Members))
            guard.Hp = 1000; // the camp can fall to a raid meanwhile, leaving fresh corpses in the same spots

        AwayFor(world, world.Content.Dungeon.CorpseGoneAfter + 20);

        Assert.DoesNotContain(world.Corpses, c => c.Kind == goblin.Kind && c.Position == goblin.Position && !c.Ancient);
    }

    [Fact]
    public void ALongAbsenceHealsAndCalmsThePlace()
    {
        var world = HistoryTests.ValleyHeldBy("tollkeepers");
        world.Player.Hp = 100_000;
        var sentries = world.Squads[0];
        var guard = sentries.Members[0];
        var post = guard.Position;
        guard.Hp = 1;
        guard.Position = post + Direction.Up + Direction.Up;
        sentries.State = SquadState.Engage;
        sentries.Morale = 40;
        sentries.LastKnownTarget = new Point(43, 14);
        sentries.LastSeenTurn = world.Turn;

        AwayFor(world, 200);

        Assert.Equal(SquadState.Idle, sentries.State);
        Assert.Null(sentries.LastKnownTarget);
        Assert.Equal(post, guard.Position);
        Assert.True(guard.Hp > 1);
        Assert.True(sentries.Morale > 40);
    }

    [Fact]
    public void AShortTripChangesNothing()
    {
        var world = HistoryTests.ValleyHeldBy("tollkeepers");
        world.Player.Hp = 100_000;
        var guard = world.Squads[0].Members[0];
        guard.Hp = 1;

        var events = AwayFor(world, 5);

        Assert.Equal(1, guard.Hp);
        Assert.DoesNotContain(events, e => e is CampReinforced or CampAttackedWhileAway);
    }

    [Fact]
    public void ACampReplacesItsDeadGivenTime()
    {
        var world = HistoryTests.ValleyHeldBy("tollkeepers");
        world.Player.Hp = 100_000;
        var sentries = world.Squads[0];
        int newestId = world.Actors.Max(a => a.Id);
        world.Damage(world.Player, sentries.Members[0], 999, ranged: false);
        world.Step(new WaitAction());
        foreach (var guard in world.Squads.Where(s => s.Camp is not null).SelectMany(s => s.Members))
            guard.Hp = 1000; // the undead attack on this schedule too, and this isn't about them

        var events = AwayFor(world, world.Content.Dungeon.TurnsPerReplacement + 5);

        Assert.Contains(events, e => e is CampReinforced);
        var fresh = Assert.Single(world.Actors, a => a.Id > newestId && a.Faction.Id == "goblins");
        Assert.Contains(world.Squads, s => s.Posts.TryGetValue(fresh.Id, out var post) && post == fresh.Position);
    }

    [Fact]
    public void AMissedAttackIsFoughtAndLeavesItsMark()
    {
        bool sawHeld = false, sawFallen = false;
        for (ulong seed = 0; seed < 30 && !(sawHeld && sawFallen); seed++)
        {
            var world = World.Create(seed, depth: World.SurfaceDepth);
            world.Player.Hp = 100_000;
            int goblins = world.Actors.Count(a => a.Faction.Id == "goblins");

            var report = AwayFor(world, 320).OfType<CampAttackedWhileAway>().Single();

            Assert.Equal(Tile.GateBroken, world.Map.Tiles[new Point(43, 10)]);
            Assert.False(world.Camps[0].Provoked);
            Assert.True(report.DefendersLost > 0);
            Assert.Equal(goblins - report.DefendersLost, world.Actors.Count(a => a.Faction.Id == "goblins")); // too soon for replacements
            Assert.NotEmpty(world.Corpses);
            if (report.Held)
            {
                sawHeld = true;
                Assert.False(world.Camps[0].Overrun);
                Assert.Contains(world.Actors, a => a.Faction.Id == "goblins" && !a.IsDead);
                Assert.DoesNotContain(world.Actors, a => a.Faction.Id == "undead");
            }
            else
            {
                sawFallen = true;
                Assert.True(world.Camps[0].Overrun);
                Assert.DoesNotContain(world.Actors, a => a.Faction.Id == "goblins");
                Assert.Contains(world.Actors, a => a.Faction.Id == "undead");
                Assert.DoesNotContain(world.Step(new WaitAction()), e => e is GateChallenged);
            }
        }
        Assert.True(sawHeld, "no seed where the camp held");
        Assert.True(sawFallen, "no seed where the camp fell");
    }

    [Fact]
    public void ABrokenGateCantBeShut()
    {
        var world = HistoryTests.ValleyHeldBy("tollkeepers");
        world.Player.Hp = 100_000;
        AwayFor(world, 320);
        world.Player.Position = new Point(43, 11);

        Assert.Contains(world.Step(new CloseGateAction()), e => e is ActionFailed { Reason: FailureReason.NoGateToClose });
        Assert.True(world.Map.Walkable[new Point(43, 10)]);
        Assert.True(world.Map.Transparent[new Point(43, 10)]);
    }

    [Fact]
    public void ComingBackPlaysTheSameEveryTime()
    {
        static List<string> Play()
        {
            var world = World.Create(9, depth: World.SurfaceDepth);
            world.Player.Hp = 100_000;
            var log = AwayFor(world, 450).Select(e => e.ToString()!).ToList();
            log.AddRange(world.Actors.Select(a => $"{a.Id} {a.Kind} {a.Position} {a.Hp}"));
            log.AddRange(world.Corpses.Select(c => $"{c.Kind} {c.Position}"));
            return log;
        }

        Assert.Equal(Play(), Play());
    }
}
