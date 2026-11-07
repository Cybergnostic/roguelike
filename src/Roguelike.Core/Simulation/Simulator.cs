using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;

namespace Roguelike.Core.Simulation;

/// <summary>Outcome and statistics of one headless game.</summary>
public sealed record GameResult(
    ulong Seed,
    bool PlayerDied,
    int Turns,
    int SquadsAtStart,
    int SquadsAtEnd,
    int MonstersAtStart,
    int MonstersAtEnd,
    IReadOnlyDictionary<string, int> EventCounts,
    IReadOnlyDictionary<SquadState, int> SquadStateTurns);

/// <summary>Runs games without a window and counts what happened. The basis for balance work.</summary>
public static class Simulator
{
    public static GameResult Run(ulong seed, IPlayerBot bot, int maxTurns, bool observer = false)
    {
        var world = World.Create(seed);
        world.ObserverMode = observer;

        int squadsAtStart = world.Squads.Count;
        int monstersAtStart = CountMonsters(world);
        var counts = new Dictionary<string, int>();
        var stateTurns = Enum.GetValues<SquadState>().ToDictionary(s => s, _ => 0);

        while (world.Turn < maxTurns && !world.Player.IsDead)
        {
            foreach (var e in world.Step(bot.Decide(world)))
            {
                var key = Classify(world, e);
                if (key is not null)
                    counts[key] = counts.GetValueOrDefault(key) + 1;
            }
            foreach (var squad in world.Squads)
                stateTurns[squad.State]++;
        }

        return new GameResult(seed, world.Player.IsDead, world.Turn,
            squadsAtStart, world.Squads.Count, monstersAtStart, CountMonsters(world),
            counts, stateTurns);
    }

    private static int CountMonsters(World world) =>
        world.Actors.Count(a => a != world.Player && !a.IsDead);

    /// <summary>Stable names for event categories; reports and tests key off these.</summary>
    public static string? Classify(World world, GameEvent e) => e switch
    {
        ActorAttacked { Ranged: true } a when a.TargetId == world.Player.Id => "attacks on player (ranged)",
        ActorAttacked a when a.TargetId == world.Player.Id => "attacks on player (melee)",
        ActorAttacked a when a.AttackerId == world.Player.Id => "player attacks",
        ActorAttacked { Ranged: true } => "ranged shots between factions",
        ActorAttacked => "melee attacks between factions",
        ActorHealed => "shaman heals",
        ActorDied d when d.ActorId == world.Player.Id => "player deaths",
        ActorDied d => $"killed: {d.FactionId}",
        SquadSpotted s when s.TargetId == world.Player.Id => "squads spotted player",
        SquadSpotted => "squads spotted other factions",
        SquadRouted => "squads routed",
        ReinforcementsCalled => "reinforcements called",
        _ => null,
    };
}
