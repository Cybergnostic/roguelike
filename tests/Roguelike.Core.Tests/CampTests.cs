using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class CampTests
{
    private static readonly Point Gate = new(43, 10);
    private static readonly Point OutsideGate = new(43, 14);
    private static readonly Point FarAway = new(3, 20);

    private static World Valley(ulong seed = 1)
    {
        var world = World.Create(seed, depth: World.SurfaceDepth);
        world.Player.Hp = 10_000; // these tests are about behaviour, not survival
        return world;
    }

    private static Squad Sentries(World w) => w.Squads[0];
    private static Squad Reserve(World w) => w.Squads[1];
    private static Squad FrontPatrol(World w) => w.Squads[2];
    private static Camp CampOf(World w) => Sentries(w).Camp!;

    private static List<GameEvent> Run(World world, int turns)
    {
        var events = new List<GameEvent>();
        for (int t = 0; t < turns; t++)
            events.AddRange(world.Step(new WaitAction()));
        return events;
    }

    [Fact]
    public void GuardsLeaveATravellerOutsideTheirGroundAlone()
    {
        var world = Valley();
        world.Player.Position = OutsideGate;
        Assert.True(Sentries(world).Members.Any(m => world.CanSee(m, world.Player)), "a guard should be able to see the player");
        int hp = world.Player.Hp;

        var events = Run(world, 25);

        Assert.DoesNotContain(events, e => e is SquadSpotted);
        Assert.Equal(hp, world.Player.Hp);
    }

    [Fact]
    public void SteppingInsideTheCampMakesYouAnEnemy()
    {
        var world = Valley();
        world.Player.Position = Gate + Direction.Up;

        var events = world.Step(new WaitAction());

        Assert.Contains(events, e => e is SquadSpotted s && s.SquadId == Sentries(world).Id);
        Assert.Equal(SquadState.Engage, Sentries(world).State);
        Assert.False(CampOf(world).Provoked); // trespass alone isn't remembered
    }

    [Fact]
    public void LeavingTheCampEndsTheFightForAClearReason()
    {
        var world = Valley();
        world.Player.Position = Gate + Direction.Up;
        world.Step(new WaitAction());
        Assert.Equal(SquadState.Engage, Sentries(world).State);

        world.Player.Position = OutsideGate;
        var events = Run(world, 30);

        Assert.Contains(events, e => e is SquadStoodDown { Reason: StandDownReason.IntruderLeft } s && s.SquadId == Sentries(world).Id);
        Assert.DoesNotContain(events, e => e is ActorAttacked a && a.TargetId == world.Player.Id);
        Assert.Equal(SquadState.Idle, Sentries(world).State);
        Assert.Null(Sentries(world).LastKnownTarget);
    }

    [Fact]
    public void GuardsHoldTheirPosts()
    {
        var world = Valley();
        world.Player.Position = FarAway;
        var guards = Sentries(world).Members.Concat(Reserve(world).Members).ToList();
        var posts = guards.Select(g => g.Position).ToList();

        Run(world, 60);

        Assert.Equal(posts, guards.Select(g => g.Position));
    }

    [Fact]
    public void GuardsGoBackToTheirPostsAfterAChase()
    {
        var world = Valley();
        var guards = Sentries(world).Members.ToList();
        var posts = guards.Select(g => g.Position).ToList();
        world.Player.Position = Gate + Direction.Up;
        world.Step(new WaitAction());
        world.Step(new WaitAction());

        world.Player.Position = FarAway;
        Run(world, 40);

        Assert.Equal(posts, guards.Select(g => g.Position));
    }

    [Fact]
    public void ThePatrolWalksItsRouteOverAndOver()
    {
        var world = Valley();
        world.Player.Position = FarAway;
        var patrol = FrontPatrol(world);
        var route = patrol.Route!;
        var stopsReached = new List<int>();

        for (int t = 0; t < 200; t++)
        {
            world.Step(new WaitAction());
            int stop = patrol.RouteStop;
            if (stopsReached.Count == 0 || stopsReached[^1] != stop)
                stopsReached.Add(stop);
        }

        Assert.True(stopsReached.Count >= 4, $"only {stopsReached.Count} legs walked");
        Assert.Equal(2, route.Count);
    }

    [Fact]
    public void TheFirstBumpWarnsAndTheSecondAttacks()
    {
        var world = Valley();
        var sentry = Sentries(world).Members[0];
        sentry.Position = OutsideGate + Direction.Up;
        world.Player.Position = OutsideGate;
        int turn = world.Turn;

        var first = world.Step(new MoveAction(Direction.Up));

        Assert.Contains(first, e => e is ActionFailed { Reason: FailureReason.NotHostileYet });
        Assert.Equal(turn, world.Turn);
        Assert.False(CampOf(world).Provoked);

        var second = world.Step(new MoveAction(Direction.Up));

        Assert.Equal(turn + 1, world.Turn);
        Assert.Contains(second, e => e is ActorAttacked a && a.AttackerId == world.Player.Id);
        Assert.Contains(second, e => e is CampAlarmed);
        Assert.True(CampOf(world).Provoked);
        Assert.True(world.AreHostile(Reserve(world).Members[0], world.Player)); // the whole camp knows
    }

    [Fact]
    public void WalkingAwayCancelsTheBumpWarning()
    {
        var world = Valley();
        var sentry = Sentries(world).Members[0];
        sentry.Position = OutsideGate + Direction.Up;
        world.Player.Position = OutsideGate;

        world.Step(new MoveAction(Direction.Up));
        world.Step(new WaitAction());
        sentry.Position = OutsideGate + Direction.Up; // back in the way
        var again = world.Step(new MoveAction(Direction.Up));

        Assert.Contains(again, e => e is ActionFailed { Reason: FailureReason.NotHostileYet });
        Assert.False(CampOf(world).Provoked);
    }

    [Fact]
    public void ShootingAGuardProvokesTheCamp()
    {
        var world = Valley();
        var archer = Sentries(world).Members.Single(m => m.Kind == "goblin_archer");
        world.Player.Position = OutsideGate;
        Assert.True(world.HasClearShot(world.Player.Position, archer.Position));

        var events = world.Step(new FireAction(archer.Id));

        Assert.Contains(events, e => e is CampAlarmed);
        Assert.True(CampOf(world).Provoked);
    }

    [Fact]
    public void AnAttackedCampRemembersAfterYouComeBack()
    {
        var world = Valley();
        CampOf(world).Provoked = true;
        world.Player.Position = world.Map.StairsDown!.Value;
        world.Step(new DescendAction());
        world.Player.Position = world.Map.StairsUp!.Value;
        world.Step(new AscendAction());

        world.Player.Position = OutsideGate;
        var events = world.Step(new WaitAction());

        Assert.True(CampOf(world).Provoked);
        Assert.Contains(events, e => e is SquadSpotted s && s.SquadId == Sentries(world).Id);
    }

    [Fact]
    public void AChaseEndsWhenTheEnemyIsBeyondTheirGround()
    {
        var world = Valley();
        CampOf(world).Provoked = true;
        var squad = Sentries(world);
        world.Player.Position = OutsideGate;
        world.Step(new WaitAction());
        Assert.Equal(SquadState.Engage, squad.State);

        // Just past where they'll go, and still in plain sight of the ones who came out.
        var leader = squad.Leader!;
        var beyond = world.Map.Tiles.Positions()
            .Where(p => world.IsFree(p) && !squad.WithinLeash(p) && squad.Members.Any(m => world.CanSee(m, new Probe(p).At(world, m))))
            .OrderBy(p => World.Distance(p, leader.Position)).First();
        world.Player.Position = beyond;
        var events = world.Step(new WaitAction());

        Assert.Contains(events, e => e is SquadStoodDown { Reason: StandDownReason.TooFarFromHome } s && s.SquadId == squad.Id);
        Assert.Equal(SquadState.Idle, squad.State);
    }

    /// <summary>Answers "could this member see that cell" by standing the player there.</summary>
    private sealed record Probe(Point Cell)
    {
        public Actor At(World world, Actor _)
        {
            world.Player.Position = Cell;
            return world.Player;
        }
    }

    [Fact]
    public void ABrokenSquadFallsBackToItsRallyPoint()
    {
        var world = Valley();
        CampOf(world).Provoked = true;
        var squad = Sentries(world);
        world.Player.Position = OutsideGate;
        world.Step(new WaitAction());
        squad.Morale = 0;
        world.Step(new WaitAction());
        Assert.Equal(SquadState.Flee, squad.State);
        squad.Morale = 0;

        for (int t = 0; t < 12; t++)
        {
            squad.Morale = 0;
            world.Step(new WaitAction());
        }

        Assert.All(squad.Members, m => Assert.True(World.Distance(m.Position, squad.Rally!.Value) <= 3, $"{m.Kind} at {m.Position}"));
    }

    [Fact]
    public void GoblinsOpenAClosedGateToGetWhereTheyreGoing()
    {
        var world = Valley();
        world.Player.Position = FarAway;
        world.SetGate(Gate, open: false, world.Player);
        var squad = Sentries(world);
        squad.State = SquadState.Alert;
        squad.LastKnownTarget = squad.Destination = OutsideGate;
        squad.LastSeenTurn = world.Turn;

        var events = Run(world, 12);

        Assert.Contains(events, e => e is GateMoved { Open: true } g && g.ActorId != world.Player.Id);
    }

    [Fact]
    public void AnAttackedCampShutsItsGate()
    {
        var world = Valley();
        world.Player.Position = FarAway;
        CampOf(world).Provoked = true;

        var events = Run(world, 20);

        Assert.Equal(Roguelike.Core.Map.Tile.GateClosed, world.Map.Tiles[Gate]);
        Assert.Contains(events, e => e is GateMoved { Open: false } g && g.ActorId != world.Player.Id);
    }

    [Fact]
    public void AnUnprovokedCampLeavesItsGateOpen()
    {
        var world = Valley();
        world.Player.Position = FarAway;
        Run(world, 20);
        Assert.Equal(Roguelike.Core.Map.Tile.GateOpen, world.Map.Tiles[Gate]);
    }

    [Fact]
    public void SearchersLookAroundWhereTheyLastKnewTheEnemyWas()
    {
        Squad? squad = null;
        World? world = null;
        for (ulong seed = 0; seed < 200 && squad is null; seed++)
        {
            world = World.Create(seed);
            squad = world.Squads.FirstOrDefault(s => !s.IsLeashed && s.Members.All(m => World.Distance(m.Position, world.Player.Position) > 12));
        }
        world!.Player.Hp = 10_000;
        var spot = squad!.Leader!.Position;
        squad.State = SquadState.Alert;
        squad.LastKnownTarget = squad.Destination = spot;
        squad.LastSeenTurn = world.Turn;

        var visited = new HashSet<Point>();
        for (int t = 0; t < 20; t++)
        {
            world.Step(new WaitAction());
            if (squad.State == SquadState.Alert && squad.Destination is { } d)
                visited.Add(d);
        }

        Assert.True(visited.Count >= 2, $"searched only {visited.Count} places");
        Assert.All(visited, p => Assert.True(World.Distance(p, spot) <= 5));
    }

    [Fact]
    public void DungeonGoblinsAreStillHostile()
    {
        var world = World.Create(3);
        var goblin = world.Actors.First(a => a.Faction.Id == "goblins");
        Assert.True(world.AreHostile(goblin, world.Player));
    }

    [Fact]
    public void TheCampPlaysTheSameEveryTime()
    {
        static List<string> Play()
        {
            var world = Valley(7);
            var log = new List<string>();
            world.Player.Position = OutsideGate;
            for (int t = 0; t < 20; t++) log.AddRange(world.Step(new WaitAction()).Select(e => e.ToString()!));
            world.Player.Position = Gate + Direction.Up;
            for (int t = 0; t < 40; t++) log.AddRange(world.Step(new WaitAction()).Select(e => e.ToString()!));
            return log;
        }

        Assert.Equal(Play(), Play());
    }

    [Theory]
    [InlineData("\"order\": \"patrol\"", "\"order\": \"dance\"", "dance")]
    [InlineData("\"territory\": \"the Red Tooth camp\", \"rally\": [40, 6]", "\"territory\": \"nowhere\", \"rally\": [40, 6]", "nowhere")]
    [InlineData("\"gate\": [43, 10]", "\"gate\": [43, 12]", "gate")]
    [InlineData("\"rally\": [40, 6]", "\"rally\": [0, 0]", "rally")]
    public void ABrokenCampIsAContentError(string find, string replace, string expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rl-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
            var path = Path.Combine(dir, "surface.json");
            var text = File.ReadAllText(path);
            Assert.Contains(find, text);
            File.WriteAllText(path, text.Replace(find, replace));

            var e = Assert.Throws<InvalidDataException>(() => ContentDatabase.Load(dir));
            Assert.Contains(expected, e.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
