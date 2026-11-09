using Roguelike.Core.Map;
using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class NecromancerTests
{
    private static Actor NecromancerOf(World world) => world.Actors.Single(a => a.Kind == "acolyte");

    private static Roguelike.Core.Map.Zone Tower(World world) => world.Map.Zones.Single(z => z.FactionId == "undead");

    /// <summary>A free cell near the necromancer that he can see, for staging a corpse.</summary>
    private static Point SpotInSight(World world, Actor necromancer) =>
        world.Map.Tiles.Positions()
            .Where(p => world.IsFree(p) && World.Distance(p, necromancer.Position) is >= 2 and <= 4
                        && world.HasLineOfSight(necromancer.Position, p))
            .OrderBy(p => World.Distance(p, necromancer.Position))
            .First();

    /// <summary>Puts his court on alert, as if it had just seen an intruder nearby.</summary>
    private static void Rouse(World world, Actor necromancer)
    {
        var squad = world.SquadOf(necromancer)!;
        squad.State = SquadState.Alert;
        squad.LastSeenTurn = world.Turn;
        squad.LastKnownTarget = squad.Destination = necromancer.Position;
    }

    [Fact]
    public void EveryLevelHasOneNecromancerAtHomeFarFromTheStart()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            var world = World.Create(seed);
            var necromancer = NecromancerOf(world);
            var squad = world.SquadOf(necromancer)!;
            Assert.NotNull(squad.Home);
            Assert.True(World.Distance(necromancer.Position, world.Player.Position) >= world.Content.SafeStartDistance / 2,
                $"seed {seed}: necromancer starts too close");
        }
    }

    [Fact]
    public void TheNecromancerBacksAwayFromSomeoneClose()
    {
        var world = World.Create(1);
        var necromancer = NecromancerOf(world);
        world.Player.Hp = 100_000;
        world.Player.Position = SpotInSight(world, necromancer);
        int before = World.Distance(necromancer.Position, world.Player.Position);
        Assert.True(before <= 3);

        world.Step(new WaitAction()); // he notices
        world.Step(new WaitAction());

        Assert.True(World.Distance(necromancer.Position, world.Player.Position) > before, "he should have stepped away");
    }

    [Fact]
    public void TheNecromancerDoesNotWalkAheadOfHisGuards()
    {
        for (ulong seed = 0; seed < 60; seed++)
        {
            var world = World.Create(seed);
            var necromancer = NecromancerOf(world);
            var squad = world.SquadOf(necromancer)!;
            var guards = squad.Members.Where(m => m != necromancer).ToList();
            if (guards.Count == 0)
                continue;

            // Somewhere he's nearer to than any of his guards, so going there would put him in front.
            var room = Tower(world).Area;
            foreach (var goal in room.Positions().Where(world.IsFree))
            {
                var toward = new DistanceMap(world.Map.Passable);
                toward.Update([goal]);
                if (toward[necromancer.Position] is not >= 2 || guards.Any(g => toward[g.Position] <= toward[necromancer.Position] + 1))
                    continue;
                var at = necromancer.Position;
                squad.State = SquadState.Engage;
                squad.LastKnownTarget = goal;
                squad.LastSeenTurn = world.Turn;

                world.Step(new WaitAction());

                Assert.Equal(at, necromancer.Position);
                return;
            }
        }
        Assert.Fail("no seed gave him a place nearer to him than to his guards");
    }

    [Fact]
    public void DeadMonstersLeaveCorpses()
    {
        var world = World.Create(1);
        var victim = world.Actors.First(a => a != world.Player);
        var at = victim.Position;

        world.Damage(world.Player, victim, 1000, ranged: false);

        Assert.Contains(world.Corpses, c => c.Kind == victim.Kind && c.Position == at);
    }

    [Fact]
    public void NecromancerRaisesCorpsesIntoHisService()
    {
        var world = World.Create(1);
        world.ObserverMode = true; // keep the player out of it
        var necromancer = NecromancerOf(world);
        foreach (var bones in world.Corpses.ToList())
            world.RemoveCorpse(bones); // only the corpse staged here may be raised
        var goblin = world.SpawnMonster("goblin_warrior", SpotInSight(world, necromancer));
        world.Damage(necromancer, goblin, 1000, ranged: false);
        Rouse(world, necromancer);

        CorpseRaised? raised = null;
        for (int i = 0; i < 10 && raised is null; i++)
            raised = world.Step(new WaitAction()).OfType<CorpseRaised>().FirstOrDefault();

        Assert.NotNull(raised);
        Assert.Equal("goblin_warrior", raised.CorpseKind);
        var zombie = world.GetActor(raised.RaisedId)!;
        Assert.Equal("goblin_zombie", zombie.Kind);
        Assert.Equal(necromancer.Faction, zombie.Faction);
        Assert.Equal(necromancer.Id, zombie.MasterId);
        Assert.Same(world.SquadOf(necromancer), world.SquadOf(zombie));
        Assert.DoesNotContain(world.Corpses, c => c.Position == raised.At && c.Kind == "goblin_warrior");
    }

    [Fact]
    public void ServantsCrumbleWhenTheirMasterDies()
    {
        var world = World.Create(1);
        var necromancer = NecromancerOf(world);
        var servant = world.SpawnMonster("goblin_zombie", SpotInSight(world, necromancer),
            world.SquadOf(necromancer), necromancer.Faction);
        servant.MasterId = necromancer.Id;
        var unbound = world.Actors.First(a => a.Kind == "skeleton_warrior" && a.MasterId is null);

        world.Damage(world.Player, necromancer, 1000, ranged: false);
        var events = world.Step(new WaitAction());

        Assert.True(servant.IsDead);
        Assert.False(unbound.IsDead); // only what he raised is bound to him
        Assert.DoesNotContain(servant, world.Actors);
        Assert.Empty(world.CorpsesAt(servant.Position)); // the dead rise only once
    }

    private sealed class CountingBrain : IBrain
    {
        public int Decisions { get; private set; }
        public GameAction Decide(World world, Actor self)
        {
            Decisions++;
            return new WaitAction();
        }
    }

    [Fact]
    public void SpeedSetsHowOftenMonstersAct()
    {
        var world = World.Create(1);
        var spots = world.Map.Tiles.Positions().Where(world.IsFree).Take(2).ToList();
        var slow = world.SpawnMonster("goblin_zombie", spots[0]);
        var normal = world.SpawnMonster("goblin_warrior", spots[1]);
        var slowBrain = new CountingBrain();
        var normalBrain = new CountingBrain();
        slow.Brain = slowBrain;
        normal.Brain = normalBrain;
        world.ObserverMode = true;

        for (int i = 0; i < 10; i++)
            world.Step(new WaitAction());

        Assert.Equal(10, normalBrain.Decisions);
        Assert.Equal(5, slowBrain.Decisions);
    }

    [Fact]
    public void RaisingIntoAnUnknownMonsterIsAContentError()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rl-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
            var monsters = Path.Combine(dir, "monsters.json");
            File.WriteAllText(monsters, File.ReadAllText(monsters).Replace("\"risesAs\": \"goblin_zombie\"", "\"risesAs\": \"goblin_ghost\""));

            var e = Assert.Throws<InvalidDataException>(() => ContentDatabase.Load(dir));
            Assert.Contains("goblin_ghost", e.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheTowerHasPillarsBonesAndStaysConnected()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            var world = World.Create(seed);
            var zone = Tower(world);
            var cells = zone.Area.Positions().ToList();
            Assert.Contains(cells, p => world.Map.Tiles[p] == Roguelike.Core.Map.Tile.LairFloor);
            Assert.Contains(world.Corpses, c => zone.Area.Contains(c.Position));
            if (zone.Area.Width >= 5 && zone.Area.Height >= 5)
                Assert.Contains(cells, p => world.Map.Tiles[p] == Roguelike.Core.Map.Tile.Pillar);

            // Every walkable cell on the level is still reachable from the start.
            var fromStart = new Roguelike.Core.Map.DistanceMap(world.Map.Walkable);
            fromStart.Update([world.Player.Position]);
            Assert.All(world.Map.Tiles.Positions().Where(p => world.Map.Walkable[p]),
                p => Assert.NotNull(fromStart[p]));
        }
    }

    [Fact]
    public void TheBonesInTheTowerWaitUntilHeIsRoused()
    {
        var world = World.Create(2);
        world.ObserverMode = true;
        var bones = world.Corpses.Where(c => Tower(world).Area.Contains(c.Position)).ToList();
        Assert.NotEmpty(bones);
        for (int i = 0; i < 30; i++)
            Assert.DoesNotContain(world.Step(new WaitAction()), e => e is CorpseRaised);
        Assert.All(bones, b => Assert.Contains(b, world.Corpses));
    }

    [Fact]
    public void WalkingIntoTheTowerIsAnnounced()
    {
        var world = World.Create(1);
        var zone = Tower(world);
        var (outside, dir) = world.Map.Tiles.Positions()
            .Where(p => world.IsFree(p) && !zone.Area.Contains(p))
            .SelectMany(p => World.EightWay.Select(d => (p, d)))
            .First(x => zone.Area.Contains(x.p + x.d) && world.IsFree(x.p + x.d));
        world.Player.Position = outside;

        var events = world.Step(new MoveAction(dir));

        Assert.Contains(events, e => e is ZoneEntered z && z.ZoneName == zone.Name);
    }

    [Fact]
    public void TheCourtNeverStraysBeyondItsLeash()
    {
        for (ulong seed = 0; seed < 10; seed++)
        {
            var world = World.Create(seed);
            world.ObserverMode = true;
            var necromancer = NecromancerOf(world);
            var squad = world.SquadOf(necromancer)!;
            Assert.True(squad.IsLeashed);
            for (int t = 0; t < 400 && !necromancer.IsDead; t++)
            {
                world.Step(new WaitAction());
                Assert.True(squad.WithinLeash(necromancer.Position),
                    $"seed {seed} turn {t}: necromancer {squad.DistanceFromHome(necromancer.Position)} steps from home");
            }
        }
    }

    [Fact]
    public void GoblinsRaidTheTowerAndFeedTheNecromancer()
    {
        int raids = 0, reached = 0, raisedGoblins = 0;
        for (ulong seed = 0; seed < 10; seed++)
        {
            var world = World.Create(seed);
            world.ObserverMode = true;
            var tower = Tower(world);
            var raiders = new HashSet<int>();
            bool arrived = false;
            for (int t = 0; t < 600; t++)
            {
                foreach (var e in world.Step(new WaitAction()))
                {
                    if (e is RaidLaunched r)
                    {
                        Assert.Equal("goblins", r.FactionId);
                        Assert.Equal(tower.Name, r.ZoneName);
                        raids++;
                        foreach (var m in world.Squads.Single(s => s.Id == r.SquadId).Members)
                            raiders.Add(m.Id);
                    }
                    if (e is CorpseRaised { CorpseKind: var kind } && kind.StartsWith("goblin"))
                        raisedGoblins++;
                }
                arrived |= world.Actors.Any(a => raiders.Contains(a.Id)
                                                 && tower.Area.Expand(3, 3).Contains(a.Position));
            }
            if (arrived)
                reached++;
        }
        Assert.True(raids >= 10, $"only {raids} raids in 10 games");
        Assert.True(reached >= 5, $"raiders reached the tower in only {reached} of 10 games");
        Assert.True(raisedGoblins > 0, "no raider ever rose again in the necromancer's service");
    }

    [Fact]
    public void RoutedRaidersGoHome()
    {
        var world = World.Create(3);
        var squad = world.Squads.First(s => s.Faction.Id == "goblins");
        squad.RaidTarget = Tower(world).Heart;
        squad.Morale = 0;

        world.Step(new WaitAction());

        Assert.Equal(SquadState.Flee, squad.State);
        Assert.False(squad.IsRaiding);
    }

    [Fact]
    public void TheWarrenMustersFreshBandsUntilItFalls()
    {
        static int LairGap(World w) =>
            World.Distance(w.Lairs.Single(l => l.Zone.FactionId == "goblins").Zone.Heart, Tower(w).Heart);
        var worlds = Enumerable.Range(0, 40).Select(seed => World.Create((ulong)seed)).ToList();
        Assert.True(worlds.Count(w => LairGap(w) > 10) >= 30, "the lairs should usually be apart");

        var world = worlds.First(w => LairGap(w) > 10);
        world.ObserverMode = true;
        var warren = world.Lairs.Single(l => l.Zone.FactionId == "goblins");

        // Clear out the roaming goblin bands so the warren is below its cap.
        foreach (var m in world.Squads.Where(s => s.Faction.Id == "goblins" && s.Home is null)
                     .SelectMany(s => s.Members).ToList())
            world.Damage(world.Player, m, 1000, ranged: false);

        int mustered = 0;
        for (int t = 0; t < 250; t++)
            mustered += world.Step(new WaitAction()).OfType<SquadMustered>().Count();
        Assert.True(mustered > 0, "the warren never mustered a band");

        foreach (var m in warren.Squad!.Members.ToList())
            world.Damage(world.Player, m, 1000, ranged: false);
        Assert.True(warren.HasFallen);
        for (int t = 0; t < 300; t++)
            Assert.DoesNotContain(world.Step(new WaitAction()), e => e is SquadMustered);
    }
}
