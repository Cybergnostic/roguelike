using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;
using Xunit.Abstractions;

namespace Roguelike.Core.Tests;

public class SquadTests(ITestOutputHelper output)
{
    private static World FindWorld(Func<World, bool> predicate)
    {
        for (ulong seed = 0; seed < 500; seed++)
        {
            var world = World.Create(seed);
            if (predicate(world))
                return world;
        }
        throw new InvalidOperationException("No seed matched.");
    }

    /// <summary>
    /// Teleports the player into view of a squad member, as close to <paramref name="distance"/>
    /// tiles away as the room allows, and lets one turn pass.
    /// </summary>
    private static IReadOnlyList<GameEvent> RevealPlayerTo(World world, Squad squad, int distance = 3)
    {
        world.Player.Hp = 10_000; // keep the test about AI, not survival
        var anchor = squad.Members.First().Position;
        var spot = world.Map.Tiles.Positions()
            .Where(p => world.IsFree(p) && World.Distance(p, anchor) is >= 2 and <= World.FovRadius - 1
                        && world.HasLineOfSight(p, anchor))
            .OrderBy(p => Math.Abs(World.Distance(p, anchor) - distance))
            .First();
        world.Player.Position = spot;
        return world.Step(new WaitAction());
    }

    [Fact]
    public void EveryGoblinBelongsToASquad()
    {
        var world = World.Create(3);
        Assert.NotEmpty(world.Squads);
        foreach (var goblin in world.Actors.Where(a => a.Faction == Faction.Goblins))
            Assert.NotNull(world.SquadOf(goblin));
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

    [Fact]
    public void LosingMembersLowersMoraleAndBreaksTheSquad()
    {
        var world = FindWorld(w => w.Squads.Any(s => s.Members.Count >= 4));
        var squad = world.Squads.First(s => s.Members.Count >= 4);
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
        var world = FindWorld(w => w.Squads.Count >= 2 && w.Squads.Any(s => s.WithRole(SquadRole.Runner).Any()));
        var squad = world.Squads.First(s => s.WithRole(SquadRole.Runner).Any());
        RevealPlayerTo(world, squad, distance: 5);
        Assert.NotNull(squad.HelpTargetSquadId);
        var helperId = squad.HelpTargetSquadId!.Value;

        ReinforcementsCalled? call = null;
        for (int turn = 0; turn < 300 && call is null; turn++)
            call = world.Step(new WaitAction()).OfType<ReinforcementsCalled>().FirstOrDefault();

        Assert.NotNull(call);
        Assert.Equal(helperId, call.ToSquadId);
        var helper = world.Squads.First(s => s.Id == helperId);
        Assert.NotEqual(SquadState.Idle, helper.State);
    }

    [Fact]
    public void AggressiveBotSeesEveryTacticAcrossManyGames()
    {
        var tally = new Dictionary<string, int>();
        int deaths = 0, totalTurns = 0, kills = 0;
        const int games = 30;

        for (ulong seed = 0; seed < games; seed++)
        {
            var world = World.Create(seed);
            for (int turn = 0; turn < 600 && !world.Player.IsDead; turn++)
            {
                foreach (var e in world.Step(BotAction(world)))
                {
                    string key = e switch
                    {
                        ActorAttacked { Ranged: true } => "archer shots",
                        ActorHealed => "shaman heals",
                        SquadRouted => "squads routed",
                        ReinforcementsCalled => "reinforcements called",
                        SquadSpotted => "squads spotted player",
                        ActorDied d when d.Kind != "player" => "goblins killed",
                        _ => "",
                    };
                    if (key != "")
                        tally[key] = tally.GetValueOrDefault(key) + 1;
                }
            }
            if (world.Player.IsDead) deaths++;
            totalTurns += world.Turn;
            kills += tally.GetValueOrDefault("goblins killed");
        }

        output.WriteLine($"{games} games, player died in {deaths}, avg {totalTurns / games} turns");
        foreach (var (k, v) in tally.OrderBy(kv => kv.Key))
            output.WriteLine($"  {k}: {v}");

        Assert.True(tally.GetValueOrDefault("archer shots") > 0);
        Assert.True(tally.GetValueOrDefault("shaman heals") > 0);
        Assert.True(tally.GetValueOrDefault("squads routed") > 0);
        Assert.True(tally.GetValueOrDefault("reinforcements called") > 0);
    }

    /// <summary>Charges the nearest goblin it can see, otherwise heads for the nearest unexplored-ish room.</summary>
    private static GameAction BotAction(World world)
    {
        var player = world.Player;
        var target = world.Actors
            .Where(a => a.Faction == Faction.Goblins && world.PlayerCanSee(a))
            .OrderBy(a => World.Distance(a.Position, player.Position))
            .FirstOrDefault();

        Point goal;
        if (target is not null)
            goal = target.Position;
        else
        {
            var room = world.Map.Rooms
                .OrderBy(r => world.Map.Explored[r.Center] ? 1 : 0)
                .ThenBy(r => World.Distance(r.Center, player.Position))
                .First();
            goal = room.Center == player.Position ? world.Map.Rooms[world.Turn % world.Map.Rooms.Count].Center : room.Center;
        }

        if (target is not null && world.IsAdjacent(player.Position, target.Position))
            return new AttackAction(target.Id);
        return world.StepToward(player, goal) is { } dir ? new MoveAction(dir) : new WaitAction();
    }
}
