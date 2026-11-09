using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Map;
using Roguelike.Core.Simulation;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;
using Xunit.Abstractions;

namespace Roguelike.Core.Tests;

public class SquadTests(ITestOutputHelper output)
{
    private static World FindWorld(Func<World, bool> predicate, int depth = 1)
    {
        for (ulong seed = 0; seed < 500; seed++)
        {
            var world = World.Create(seed, depth: depth);
            if (predicate(world))
                return world;
        }
        throw new InvalidOperationException("No seed matched.");
    }

    /// <summary>
    /// Teleports the player into view of a squad member, as close to <paramref name="distance"/>
    /// tiles away as the room allows, and lets one turn pass. With <paramref name="openGround"/>
    /// the player never stands in a chokepoint, where they might cork the squad's only exit.
    /// </summary>
    private static IReadOnlyList<GameEvent> RevealPlayerTo(World world, Squad squad, int distance = 3, bool openGround = false)
    {
        world.Player.Hp = 10_000; // keep the test about AI, not survival
        var anchor = squad.Members.First().Position;
        var spot = world.Map.Tiles.Positions()
            .Where(p => world.IsFree(p) && World.Distance(p, anchor) is >= 2 and <= World.FovRadius - 1
                        && world.HasLineOfSight(p, anchor) && !(openGround && world.IsChokepoint(p)))
            .OrderBy(p => Math.Abs(World.Distance(p, anchor) - distance))
            .First();
        world.Player.Position = spot;
        return world.Step(new WaitAction());
    }

    [Fact]
    public void EveryMonsterBelongsToASquad()
    {
        var world = World.Create(3);
        Assert.NotEmpty(world.Squads);
        foreach (var monster in world.Actors.Where(a => a != world.Player))
            Assert.NotNull(world.SquadOf(monster));
    }

    [Fact]
    public void SquadSharesWhatOneMemberSees()
    {
        var world = FindWorld(w => w.Squads.Any(s => s.Members.Count >= 3));
        var squad = world.Squads.First(s => s.Members.Count >= 3);

        var events = RevealPlayerTo(world, squad);

        Assert.Equal(SquadState.Engage, squad.State);
        Assert.Contains(events, e => e is SquadSpotted s && s.SquadId == squad.Id);
        Assert.NotNull(squad.LastKnownTarget);
    }

    // ---- Knowledge: squads act on what they perceive, never on where the enemy really is ----

    private static Squad? RoamingSquad(World w) => w.Squads.FirstOrDefault(s => s.Members.Count >= 2 && !s.IsLeashed);

    private static IEnumerable<Point> SpotsAtLeast(World w, Squad s, int distance) =>
        w.Map.Tiles.Positions().Where(p => w.IsFree(p) && s.Members.All(m => World.Distance(m.Position, p) >= distance));

    [Fact]
    public void SquadBehavesTheSameWhereverAHiddenEnemyReallyIs()
    {
        // The same game twice: the squad sees the player, who then vanishes to opposite ends of the map.
        static (World World, Squad Squad, Point Seen) PlayWithPlayerHidden(ulong seed, bool farWest)
        {
            var world = World.Create(seed);
            var squad = RoamingSquad(world);
            if (squad is null)
                return (world, null!, default);
            foreach (var other in world.Squads.Where(s => s != squad).SelectMany(s => s.Members))
                other.Hp = 0; // nothing else may notice the player
            RevealPlayerTo(world, squad, distance: 5, openGround: true);
            var seen = world.Player.Position;
            var spots = SpotsAtLeast(world, squad, 16).ToList();
            if (squad.State == SquadState.Engage && spots.Count > 0)
                world.Player.Position = farWest ? spots.MinBy(p => p.X) : spots.MaxBy(p => p.X);
            return (world, squad, seen);
        }

        // Shortest routes to the two hiding places must part at once, or a squad that secretly
        // tracked the player would still look the same for the few turns this test watches.
        static HashSet<Point> StepsToward(World world, Point from, Point goal)
        {
            var map = new DistanceMap(world.Map.Walkable);
            map.Update([goal]);
            return World.EightWay.Select(d => from + d)
                .Where(p => world.Map.InBounds(p) && map[p] < map[from]).ToHashSet();
        }

        (World World, Squad Squad, Point Seen) west = default, east = default;
        bool found = false;
        for (ulong seed = 0; seed < 500 && !found; seed++)
        {
            west = PlayWithPlayerHidden(seed, farWest: true);
            east = PlayWithPlayerHidden(seed, farWest: false);
            if (west.Squad is not { State: SquadState.Engage } || west.World.Player.Position == west.Seen
                || east.World.Player.Position == east.Seen)
                continue;
            var leader = west.Squad.Leader!.Position;
            var toWest = StepsToward(west.World, leader, west.World.Player.Position);
            found = toWest.Count > 0 && !toWest.Overlaps(StepsToward(east.World, leader, east.World.Player.Position));
        }
        Assert.True(found, "no seed where the routes to the two hiding places part at once");

        for (int turn = 1; turn <= 5; turn++)
        {
            west.World.Step(new WaitAction());
            east.World.Step(new WaitAction());
            Assert.Equal(west.Squad.Members.Select(m => m.Position), east.Squad.Members.Select(m => m.Position));
        }
        Assert.Equal(west.Seen, west.Squad.LastKnownTarget);
        Assert.Equal(east.Seen, east.Squad.LastKnownTarget);
    }

    [Fact]
    public void ASightingBeyondShoutingRangeDoesNotAlertTheSquad()
    {
        var world = FindWorld(w => RoamingSquad(w) is { } s && SpotsAtLeast(w, s, 14).Any());
        var squad = RoamingSquad(world)!;
        var scout = squad.Members.First(m => m != squad.Leader);

        // The scout wanders off alone and stumbles on the player, far from anyone it could tell.
        scout.Position = SpotsAtLeast(world, squad, 14).First(p => World.EightWay.Any(d => world.IsFree(p + d)));
        world.Player.Position = World.EightWay.Select(d => scout.Position + d).First(world.IsFree);
        world.Player.Hp = 10_000;
        var events = world.Step(new WaitAction());

        Assert.DoesNotContain(events, e => e is SquadSpotted s && s.SquadId == squad.Id);
        Assert.Equal(SquadState.Idle, squad.State);
        Assert.Null(squad.LastKnownTarget);
    }

    [Fact]
    public void BeingShotAtGivesAwayTheShooter()
    {
        var world = FindWorld(w => RoamingSquad(w) is { } s && SpotsAtLeast(w, s, 12).Any());
        var squad = RoamingSquad(world)!;
        var target = squad.Members[0];
        target.Hp = 10_000;
        var shooter = SpotsAtLeast(world, squad, 12).First();
        world.Player.Position = shooter; // out of the squad's sight; a long bow with Eagle Eye reaches this far

        world.ResolveAttack(world.Player, target, ranged: true);

        Assert.Equal(SquadState.Alert, squad.State);
        Assert.Equal(shooter, squad.LastKnownTarget);
        Assert.Equal(shooter, squad.Destination);
    }

    [Fact]
    public void LosingMembersLowersMoraleAndBreaksTheSquad()
    {
        var world = FindWorld(w => w.Squads.Any(s => s.Members.Count >= 4 && !s.IsFearless));
        var squad = world.Squads.First(s => s.Members.Count >= 4 && !s.IsFearless);
        RevealPlayerTo(world, squad, distance: 5);

        foreach (var m in squad.Members.Take(3))
            m.Hp = 0;
        var events = world.Step(new WaitAction());

        Assert.True(squad.Morale < Squad.BreakingPoint, $"morale {squad.Morale}");
        Assert.Equal(SquadState.Flee, squad.State);
        Assert.Contains(events, e => e is SquadRouted r && r.SquadId == squad.Id);
    }

    [Fact]
    public void RunnerFetchesAnotherSquad()
    {
        static bool HasRunnerAndAllies(World w, Squad s) =>
            s.WithRole(SquadRole.Runner).Any() && w.Squads.Any(o => o != s && o.Faction == s.Faction);
        // Runners join the war bands from the second level down.
        var world = FindWorld(w => w.Squads.Any(s => HasRunnerAndAllies(w, s)), depth: 2);
        var squad = world.Squads.First(s => HasRunnerAndAllies(world, s));
        RevealPlayerTo(world, squad, distance: 5, openGround: true);
        Assert.NotNull(squad.HelpTargetSquadId);

        ReinforcementsCalled? call = null;
        for (int turn = 0; turn < 300 && call is null; turn++)
            call = world.Step(new WaitAction()).OfType<ReinforcementsCalled>().FirstOrDefault();

        Assert.NotNull(call);
        // Not necessarily the first choice: if the fight lulls, the runner is recalled and a later
        // alarm sends it to whichever squad is nearest then.
        var helper = world.Squads.First(s => s.Id == call.ToSquadId);
        Assert.Equal(squad.Faction, helper.Faction);
        Assert.NotEqual(SquadState.Idle, helper.State);
    }

    [Fact]
    public void AggressiveBotSeesEveryTacticAcrossManyGames()
    {
        var results = Enumerable.Range(0, 30)
            .Select(seed => Simulator.Run((ulong)seed, new AggressiveBot(), maxTurns: 600))
            .ToList();
        int Count(string key) => results.Sum(r => r.EventCounts.GetValueOrDefault(key));

        output.WriteLine($"{results.Count} games, player died in {results.Count(r => r.PlayerDied)}");
        foreach (var key in results.SelectMany(r => r.EventCounts.Keys).Distinct().Order())
            output.WriteLine($"  {key}: {Count(key)}");

        Assert.True(Count("attacks on player (ranged)") > 0);
        Assert.True(Count("shaman heals") > 0);
        Assert.True(Count("squads routed") > 0);
        Assert.True(Count("reinforcements called") > 0);
    }

    [Fact]
    public void AlliesSwapPlacesButNeverWithThePlayer()
    {
        var world = FindWorld(w => w.Squads.Any(s => s.Members.Count >= 2));
        var squad = world.Squads.First(s => s.Members.Count >= 2);
        var (a, b) = (squad.Members[0], squad.Members[1]);
        var spot = World.EightWay.Select(d => a.Position + d).First(world.IsFree);
        b.Position = spot;
        var (pa, pb) = (a.Position, b.Position);

        Assert.True(new MoveAction(SadRogue.Primitives.Direction.GetDirection(pa, pb)).Perform(world, a));
        Assert.Equal(pb, a.Position);
        Assert.Equal(pa, b.Position);

        Assert.False(world.CanSwap(a, world.Player));
        Assert.False(world.CanSwap(world.Player, a));
    }

    [Fact]
    public void ObserverIsNeverNoticedOrHarmed()
    {
        var result = Simulator.Run(11, new AggressiveBot(), maxTurns: 300, observer: true);
        Assert.False(result.PlayerDied);
        Assert.Equal(0, result.EventCounts.GetValueOrDefault("attacks on player (melee)"));
        Assert.Equal(0, result.EventCounts.GetValueOrDefault("attacks on player (ranged)"));
        Assert.Equal(0, result.EventCounts.GetValueOrDefault("squads spotted player"));
    }
}
