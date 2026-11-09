using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Simulation;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class MindTests
{
    /// <summary>An empty, open level with the player in its biggest room.</summary>
    private static World Empty(ulong seed = 1)
    {
        var world = World.Create(seed);
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        var room = world.Map.Rooms.OrderByDescending(r => r.Area).First();
        world.Player.Position = room.Center;
        world.Player.Hp = 100_000;
        return world;
    }

    private static List<Point> OpenCells(World world, int distance) =>
        world.Map.Tiles.Positions()
            .Where(p => world.IsFree(p) && World.Distance(p, world.Player.Position) == distance && world.HasClearShot(world.Player.Position, p))
            .ToList();

    private static Squad Spawn(World world, int distance, params string[] kinds)
    {
        var cells = OpenCells(world, distance);
        Squad? squad = null;
        for (int i = 0; i < kinds.Length; i++)
        {
            var m = world.SpawnMonster(kinds[i], cells[i], squad);
            squad ??= world.SquadOf(m);
        }
        return squad!;
    }

    [Theory]
    [InlineData("wolf", "pack")]
    [InlineData("bear", "beast")]
    [InlineData("boar", "beast")]
    [InlineData("goblin_warrior", "rabble")]
    [InlineData("bandit", "rabble")]
    [InlineData("skeleton_warrior", "mindless")]
    public void EachCreatureThinksInItsOwnWay(string kind, string mind) =>
        Assert.Equal(mind, Spawn(Empty(), 4, kind).Mind.Id);

    [Fact]
    public void ACommanderDrillsTheWholeSquad() =>
        Assert.Equal("drilled", Spawn(Empty(), 4, "goblin_warrior", "goblin_captain", "goblin_archer").Mind.Id);

    [Fact]
    public void WithoutItsCommanderASquadGoesToPieces()
    {
        var world = Empty();
        var squad = Spawn(world, 6, "goblin_captain", "goblin_warrior", "goblin_warrior");
        int morale = squad.Morale;

        squad.Commander!.Hp = 0;
        var events = world.Step(new WaitAction());

        Assert.Equal("rabble", squad.Mind.Id);
        Assert.Contains(events, e => e is SquadLostCommander);
        Assert.True(squad.Morale <= morale - world.Content.Minds["drilled"].CommanderLoss, $"morale {squad.Morale}");
    }

    [Fact]
    public void AnOrdinaryDeathIsNotALostCommander()
    {
        var world = Empty();
        var squad = Spawn(world, 6, "goblin_captain", "goblin_warrior", "goblin_warrior");
        squad.Members.First(m => m.Commands is null).Hp = 0;
        Assert.DoesNotContain(world.Step(new WaitAction()), e => e is SquadLostCommander);
        Assert.Equal("drilled", squad.Mind.Id);
    }

    [Fact]
    public void BeastsDontWeighYouUp()
    {
        var world = Empty();
        world.Player.Hp = 1; // anything with sense would be delighted
        var squad = Spawn(world, 5, "boar", "boar", "boar");
        world.Step(new WaitAction());
        Assert.Equal(Posture.Even, squad.Posture);
    }

    [Fact]
    public void RabbleGrowsBoldAgainstTheWeak()
    {
        var world = Empty();
        world.Player.Hp = 1;
        var squad = Spawn(world, 5, "goblin_warrior", "goblin_warrior", "goblin_warrior");
        world.Step(new WaitAction());
        Assert.Equal(Posture.Bold, squad.Posture);
    }

    [Fact]
    public void PacksSurroundWhereBeastsJustComeOn()
    {
        static int Spread(string kind)
        {
            // The same animals, the same place: how many sides of you are they on after closing in?
            var world = Empty();
            var squad = Spawn(world, 6, kind, kind, kind, kind);
            foreach (var m in squad.Members)
                m.Hp = 10_000;
            for (int i = 0; i < 20; i++)
                world.Step(new WaitAction());
            var at = world.Player.Position;
            return squad.Members.Select(m => (Math.Sign(m.Position.X - at.X), Math.Sign(m.Position.Y - at.Y))).Distinct().Count();
        }
        Assert.True(Spread("wolf") >= 3, $"wolves on {Spread("wolf")} sides");
    }

    [Fact]
    public void AFarOffBeastHearsNothingWhereAPackHowls()
    {
        static SquadState Hears(string mind)
        {
            var world = Empty();
            var at = world.Player.Position;
            // The leader is well out of sight; only a straggler by the player sees anything.
            var far = world.Map.Tiles.Positions()
                .Where(p => world.IsFree(p) && World.Distance(p, at) is >= 9 and <= 12 && !world.HasLineOfSight(at, p)).First();
            var near = OpenCells(world, 3).First(p => World.Distance(p, far) >= 7);
            var leader = world.SpawnMonster("wolf", far);
            var squad = world.SquadOf(leader)!;
            world.SpawnMonster("wolf", near, squad);
            squad.MindOverride = world.Content.Minds[mind];
            world.Step(new WaitAction());
            return squad.State;
        }
        Assert.Equal(SquadState.Engage, Hears("pack"));
        Assert.Equal(SquadState.Idle, Hears("beast"));
    }

    [Theory]
    [InlineData("pack", true)]
    [InlineData("rabble", false)]
    public void PacksWontFileIntoACorridor(string mind, bool holds)
    {
        var world = Empty();
        var squad = Spawn(world, 5, "wolf", "wolf");
        squad.MindOverride = world.Content.Minds[mind];
        squad.State = SquadState.Engage;
        squad.HoldTurns = 1;
        Assert.Equal(holds, squad.IsHoldingPassage);
    }

    [Fact]
    public void ADrilledShooterGivesGroundWhereRabbleStandsAndShoots()
    {
        static bool GaveGround(string mind)
        {
            var world = Empty();
            var squad = Spawn(world, 2, "goblin_archer");
            var warrior = world.SpawnMonster("goblin_warrior", OpenCells(world, 5).First(), squad);
            squad.MindOverride = world.Content.Minds[mind];
            var archer = squad.Members.First(m => squad.RoleOf(m) == SquadRole.Archer);
            var from = archer.Position;
            world.Step(new WaitAction());
            return World.Distance(archer.Position, world.Player.Position) > World.Distance(from, world.Player.Position);
        }
        Assert.True(GaveGround("drilled"));
        Assert.False(GaveGround("rabble"));
    }

    [Fact]
    public void TheBadlyHurtStepBackWhenACoverStaysOn()
    {
        var world = Empty();
        var cells = OpenCells(world, 1);
        var hurt = world.SpawnMonster("wolf", cells[0]);
        var squad = world.SquadOf(hurt)!;
        world.SpawnMonster("wolf", cells[2], squad);
        hurt.Hp = 1; // and the other is fresh, at the player's throat
        world.Player.Hp = 100_000;

        world.Step(new WaitAction());

        Assert.True(hurt.IsDead || World.Distance(hurt.Position, world.Player.Position) >= 2,
            $"wounded wolf stayed adjacent at {hurt.Position}");
    }

    [Fact]
    public void ADrilledLineWaitsForItsRearWhereRabbleRunsAhead()
    {
        static int FrontRunnerDistance(string mind)
        {
            var world = Empty();
            var at = world.Player.Position;
            var front = world.SpawnMonster("goblin_warrior", OpenCells(world, 4).First());
            var squad = world.SquadOf(front)!;
            var rearCell = world.Map.Tiles.Positions().Where(p => world.IsFree(p) && World.Distance(p, at) is >= 7 and <= 9 && World.Distance(p, front.Position) <= 5).First();
            world.SpawnMonster("goblin_warrior", rearCell, squad);
            squad.MindOverride = world.Content.Minds[mind];
            world.Step(new WaitAction());
            return World.Distance(front.Position, at);
        }
        Assert.Equal(4, FrontRunnerDistance("drilled"));
        Assert.True(FrontRunnerDistance("rabble") < 4);
    }

    [Fact]
    public void TheDrilledSoakUpLossesThatBreakARabble()
    {
        static int MoraleAfterALoss(string mind)
        {
            var world = Empty();
            var squad = Spawn(world, 6, "goblin_warrior", "goblin_warrior", "goblin_warrior");
            squad.MindOverride = world.Content.Minds[mind];
            squad.Members[1].Hp = 0;
            world.Step(new WaitAction());
            return squad.Morale;
        }
        Assert.True(MoraleAfterALoss("drilled") > MoraleAfterALoss("rabble"));
    }

    [Fact]
    public void BeastsLoseInterestFirst()
    {
        var minds = ContentDatabase.Default.Minds;
        Assert.True(minds["beast"].Memory + minds["beast"].Searches < minds["pack"].Memory + minds["pack"].Searches);
        Assert.True(minds["rabble"].BreaksAt > minds["drilled"].BreaksAt);
    }

    [Fact]
    public void AnUnknownMindIsAContentError()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rl-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
            var path = Path.Combine(dir, "monsters.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"mind\": \"pack\"", "\"mind\": \"telepathy\""));
            var e = Assert.Throws<InvalidDataException>(() => ContentDatabase.Load(dir));
            Assert.Contains("telepathy", e.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- The trial harness itself ----

    [Fact]
    public void KillingTheBossIsAWin()
    {
        var court = Trials.Fights.First(f => f.Corpses.Length > 0);
        var stats = Trials.Run(Trials.BuildFor("ranger"), Trials.Level(Trials.Final), court, 4);
        Assert.Equal(1.0, stats.Won + stats.DroveOff + stats.Stalemate + stats.Died, 3);
    }

    [Fact]
    public void EveryBuildCanSpendItsWholePlan()
    {
        foreach (var build in Trials.Builds)
            foreach (var step in build.Spend)
            {
                var id = step[(step.IndexOf(':') + 1)..];
                var known = step.StartsWith("skill:") ? ContentDatabase.Default.Skills.ContainsKey(id) : ContentDatabase.Default.Attributes.Any(a => a.Id == id);
                Assert.True(known, $"{build.Label}: {step}");
            }
    }

    [Fact]
    public void BuildsActuallyDiffer()
    {
        var bruiser = Trials.Builds.Single(b => b.Name == "bruiser");
        var bulwark = Trials.Builds.Single(b => b.Name == "bulwark");
        Assert.NotEqual(bruiser.Spend, bulwark.Spend);
    }

    [Theory]
    [InlineData(TrialArena.Room)]
    [InlineData(TrialArena.Corridor)]
    public void TrialsAreRepeatable(TrialArena arena)
    {
        var fight = Trials.Fights.First();
        var build = Trials.Builds.First();
        var level = Trials.Levels.First();
        Assert.Equal(Trials.Run(build, level, fight, 6, arena: arena), Trials.Run(build, level, fight, 6, arena: arena));
    }

    [Fact]
    public void TrialsReportSensibleShares()
    {
        var stats = Trials.Run(Trials.BuildFor("soldier"), Trials.Level(Trials.Fresh), Trials.Fights.First(f => f.Name == "goblin scouts"), 8);
        Assert.Equal(1.0, stats.Won + stats.DroveOff + stats.Stalemate + stats.Died, 3);
        Assert.InRange(stats.HpLeft, 0, 1);
        Assert.True(stats.Turns > 0);
    }

    [Fact]
    public void AForcedMindAppliesOnlyToThatGame()
    {
        var fight = Trials.MindSubjects.First();
        var build = Trials.BuildFor("soldier");
        var level = Trials.Level(Trials.Seasoned);
        Assert.NotEqual(Trials.Run(build, level, fight, 12, mind: "mindless"), Trials.Run(build, level, fight, 12, mind: "pack"));
    }
}
