using System.Diagnostics;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;
using ShaiRandom.Generators;

namespace Roguelike.Core.Simulation;

/// <summary>What kind of rule a fuzzed fight broke.</summary>
public enum FuzzProblem
{
    /// <summary>Two living actors on one cell.</summary>
    Overlap,
    /// <summary>A living actor off the map or inside a wall.</summary>
    OffFloor,
    /// <summary>A squad still lists someone dead, or its target is someone who no longer exists.</summary>
    StaleReference,
    /// <summary>The same seed and the same moves gave a different game.</summary>
    Diverged,
    /// <summary>One turn of the world took far longer than it should.</summary>
    Slow,
    /// <summary>A fighting monster that knows where its enemy is did nothing for many turns while not in reach.</summary>
    Jammed,
    /// <summary>The world threw an exception.</summary>
    Crashed,
}

public sealed record FuzzFailure(ulong Seed, int Turn, FuzzProblem Problem, string Detail);

/// <summary>How the player behaves in a fuzzed fight.</summary>
public enum FuzzPlayer { Tactical, Idle, Wander }

/// <summary>One random fight, as rolled from its seed.</summary>
public sealed record FuzzScenario(ulong Seed, int Depth, string Class, string[] Group, string? Mind, bool Corridor,
    FuzzPlayer Player, int KillOneAt, int TeleportAt);

public sealed record FuzzReport(int Games, int Turns, double SlowestStepMs, IReadOnlyList<FuzzFailure> Failures);

/// <summary>
/// Random fights checked for rules that must always hold, rather than for who wins: nobody shares a cell or
/// stands in a wall, squads don't hold on to the dead, the same seed plays the same game, every turn ends
/// quickly, and fighting monsters don't freeze. Each scenario comes from its own seed, so any failure can
/// be replayed with <see cref="Play"/>.
/// </summary>
public static class Fuzz
{
    private const int MaxTurns = 150;
    /// <summary>Turns a fighting monster may go without moving or striking before it counts as jammed.</summary>
    public const int JamTurns = 25;
    private const double SlowStepMs = 250;

    public static FuzzReport Run(int seeds, ulong start = 0, int replayEvery = 10)
    {
        var games = Enumerable.Range(0, seeds).AsParallel().AsOrdered()
            .Select(i => start + (ulong)i)
            .Select(seed =>
            {
                var scenario = Roll(seed);
                var result = Play(scenario);
                if (replayEvery > 0 && seed % (ulong)replayEvery == 0 && result.Failures.All(f => f.Problem != FuzzProblem.Crashed))
                {
                    var again = Play(scenario);
                    if (!again.Fingerprints.SequenceEqual(result.Fingerprints))
                    {
                        int step = again.Fingerprints.Zip(result.Fingerprints).TakeWhile(p => p.First == p.Second).Count();
                        result.Failures.Add(new FuzzFailure(seed, step, FuzzProblem.Diverged, "replay differs from step " + step));
                    }
                }
                return result;
            }).ToList();
        return new FuzzReport(games.Count, games.Sum(g => g.Turns), games.Max(g => g.SlowestMs),
            games.SelectMany(g => g.Failures).ToList());
    }

    /// <summary>Rolls a fight from the seed alone (its own generator, so the game's streams are untouched).</summary>
    public static FuzzScenario Roll(ulong seed, Content.ContentDatabase? content = null)
    {
        content ??= Content.ContentDatabase.Default;
        var rng = new MizuchiRandom(seed * 7919 + 17);
        // Anyone who fights: not people in the valley, not the necromancer (killing him ends the game).
        var fighters = content.Monsters.Values.Where(m => m.Npc is null && m.Role != SquadRole.Civilian && !m.EndsRun)
            .Select(m => m.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        // Mostly one kind with a few others of its faction, the way real squads look.
        var main = fighters[rng.NextInt(fighters.Count)];
        var faction = content.Monsters[main].Faction;
        var kin = fighters.Where(id => content.Monsters[id].Faction == faction).ToList();
        var group = Enumerable.Range(0, 2 + rng.NextInt(7))
            .Select(_ => rng.NextInt(3) == 0 ? kin[rng.NextInt(kin.Count)] : main).ToArray();
        var commanders = kin.Where(id => content.Monsters[id].Commands is not null).ToList();
        if (commanders.Count > 0 && rng.NextInt(2) == 0)
            group[0] = commanders[rng.NextInt(commanders.Count)];
        var minds = content.Minds.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
        string? mind = rng.NextInt(2) == 0 ? minds[rng.NextInt(minds.Count)] : null;
        var classes = content.Classes.Select(c => c.Id).ToList();
        return new FuzzScenario(seed, 1 + rng.NextInt(7), classes[rng.NextInt(classes.Count)], group, mind,
            Corridor: rng.NextInt(3) == 0, Player: (FuzzPlayer)rng.NextInt(3),
            KillOneAt: rng.NextInt(2) == 0 ? 3 + rng.NextInt(40) : -1,
            TeleportAt: rng.NextInt(3) == 0 ? 3 + rng.NextInt(60) : -1);
    }

    public sealed record Result(int Turns, double SlowestMs, List<FuzzFailure> Failures, List<int> Fingerprints);

    public static Result Play(FuzzScenario s, Action<World, int>? observe = null)
    {
        var failures = new List<FuzzFailure>();
        var prints = new List<int>();
        double slowest = 0;
        int turns = 0;
        World world;
        try
        {
            world = World.Create(s.Seed, depth: s.Depth, classId: s.Class);
        }
        catch (Exception e)
        {
            failures.Add(new FuzzFailure(s.Seed, 0, FuzzProblem.Crashed, "create: " + e.Message));
            return new Result(0, 0, failures, prints);
        }
        var shocks = new MizuchiRandom(s.Seed * 104729 + 3);
        var wander = new MizuchiRandom(s.Seed * 15485863 + 5);
        var player = world.Player;
        var enemies = new List<Actor>();
        Squad? squad = null;
        var idle = new Dictionary<int, int>();
        var reported = new HashSet<int>();
        var deadListed = new Dictionary<(int Squad, int Actor), int>();
        int startTurn = 0;
        try
        {
            foreach (var other in world.Actors.Where(a => a != player))
                other.Hp = 0;
            world.Step(new WaitAction());
            startTurn = world.Turn;
            (enemies, squad) = Place(world, s);
            var bot = new TacticalBot(spendsXp: false);
            int depth = world.Depth;

            while (world.Turn - startTurn < MaxTurns && !world.IsOver && world.Depth == depth && enemies.Any(e => !e.IsDead))
            {
                int turn = world.Turn - startTurn;
                if (turn == s.KillOneAt && enemies.Where(e => !e.IsDead).ToList() is { Count: > 0 } living)
                {
                    var victim = living[shocks.NextInt(living.Count)];   // e.g. the commander, mid-fight
                    world.Damage(victim, victim, victim.Hp, ranged: false);
                }
                if (turn == s.TeleportAt)
                {
                    var spots = world.Map.Tiles.Positions().Where(world.IsFree).ToList();
                    player.Position = spots[shocks.NextInt(spots.Count)];
                }

                GameAction action = s.Player switch
                {
                    FuzzPlayer.Tactical => bot.Decide(world),
                    FuzzPlayer.Wander => new MoveAction(World.EightWay[wander.NextInt(World.EightWay.Length)]),
                    _ => new WaitAction(),
                };
                var before = enemies.ToDictionary(e => e.Id, e => e.Position);
                int turnBefore = world.Turn;
                var clock = Stopwatch.StartNew();
                var events = world.Step(action);
                double ms = clock.Elapsed.TotalMilliseconds;
                slowest = Math.Max(slowest, ms);
                turns++;
                if (ms > SlowStepMs)
                    failures.Add(new FuzzFailure(s.Seed, turn, FuzzProblem.Slow, $"{ms:0} ms"));

                // Lairs keep sending out squads; nobody else is invited.
                foreach (var stranger in world.Actors.Where(a => a != player && !a.IsDead && a.MasterId is null && world.SquadOf(a) != squad))
                    stranger.Hp = 0;

                Check(world, s.Seed, turn, deadListed, failures);
                if (world.Turn != turnBefore)   // a bump into a wall uses no time, and the world doesn't move
                    CheckJams(world, s.Seed, turn, squad!, enemies, before, events, idle, reported, failures);
                prints.Add(Fingerprint(world));
                observe?.Invoke(world, turn);
                if (failures.Count > 20)
                    break;
            }
        }
        catch (Exception e)
        {
            failures.Add(new FuzzFailure(s.Seed, world.Turn - startTurn, FuzzProblem.Crashed, e.GetType().Name + ": " + e.Message
                + " at " + e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()));
        }
        return new Result(turns, slowest, failures, prints);
    }

    private static (List<Actor>, Squad) Place(World world, FuzzScenario s)
    {
        var free = world.Map.Tiles.Positions().Where(world.IsFree).ToList();
        var shuffle = new MizuchiRandom(s.Seed * 31 + 11);
        var chokepoints = s.Corridor ? free.Where(world.IsChokepoint).ToList() : [];
        var from = chokepoints.Count > 0 ? chokepoints : free;
        var player = world.Player;
        player.Position = from[shuffle.NextInt(from.Count)];
        // A random spot some way off, reachable or not; the group gathers round it.
        var far = free.Where(p => World.Distance(p, player.Position) is >= 4 and <= 14).ToList();
        var center = far.Count > 0 ? far[shuffle.NextInt(far.Count)] : free[shuffle.NextInt(free.Count)];
        var spots = free.Where(p => p != player.Position && World.Distance(p, player.Position) >= 2)
            .OrderBy(p => World.Distance(p, center)).ToList();
        var enemies = new List<Actor>();
        Squad? squad = null;
        for (int i = 0; i < s.Group.Length && i < spots.Count; i++)
        {
            var enemy = world.SpawnMonster(s.Group[i], spots[i], squad);
            squad ??= world.SquadOf(enemy);
            enemies.Add(enemy);
        }
        if (s.Mind is not null)
            squad!.MindOverride = world.Content.Minds[s.Mind];
        return (enemies, squad!);
    }

    private static void Check(World world, ulong seed, int turn, Dictionary<(int Squad, int Actor), int> deadListed, List<FuzzFailure> failures)
    {
        var seen = new Dictionary<Point, Actor>();
        foreach (var a in world.Actors.Where(a => !a.IsDead))
        {
            if (!world.Map.InBounds(a.Position) || !world.Map.Walkable[a.Position])
                failures.Add(new FuzzFailure(seed, turn, FuzzProblem.OffFloor, $"{a.Kind}#{a.Id} at {a.Position}"));
            if (seen.TryGetValue(a.Position, out var other))
                failures.Add(new FuzzFailure(seed, turn, FuzzProblem.Overlap, $"{a.Kind}#{a.Id} and {other.Kind}#{other.Id} at {a.Position}"));
            seen[a.Position] = a;
        }
        // The dead stay on a squad's list until it next thinks (that's when their loss is felt), but no longer.
        // (Counted in world turns: a quick action can pass without the world moving.)
        var stillListed = new Dictionary<(int, int), int>();
        foreach (var squad in world.Squads)
        {
            foreach (var dead in squad.Members.Where(m => m.IsDead))
            {
                int since = deadListed.GetValueOrDefault((squad.Id, dead.Id), world.Turn);
                if (world.Turn > since + 1)
                    failures.Add(new FuzzFailure(seed, turn, FuzzProblem.StaleReference, $"squad {squad.Id} ({squad.State}) still lists {dead.Kind}#{dead.Id}, dead since turn {since}, now {world.Turn}"));
                stillListed[(squad.Id, dead.Id)] = since;
            }
            if (squad.TargetId is { } target && world.GetActor(target) is null && squad.State == SquadState.Engage && squad.Members.Count > 0
                && squad.LastSeenTurn == world.Turn)
                failures.Add(new FuzzFailure(seed, turn, FuzzProblem.StaleReference, $"squad {squad.Id} sees target #{target}, who is gone"));
        }
        deadListed.Clear();
        foreach (var (key, since) in stillListed)
            deadListed[key] = since;
    }

    /// <summary>
    /// A member of a fighting squad that knows where its enemy is, isn't next to it, isn't keeping its
    /// distance on purpose, and neither moves nor strikes for <see cref="JamTurns"/> turns.
    /// </summary>
    private static void CheckJams(World world, ulong seed, int turn, Squad squad, List<Actor> enemies, Dictionary<int, Point> before,
        IReadOnlyList<GameEvent> events, Dictionary<int, int> idle, HashSet<int> reported, List<FuzzFailure> failures)
    {
        var player = world.Player;
        var acted = new HashSet<int>();
        foreach (var e in events)
            switch (e)
            {
                case ActorAttacked a: acted.Add(a.AttackerId); break;
                case AbilityUsed a: acted.Add(a.ActorId); break;
                case ActorHealed h: acted.Add(h.HealerId); break;
                case CorpseRaised r: acted.Add(r.CasterId); break;
            }
        bool fighting = squad.State == SquadState.Engage && !squad.Surrendered && !squad.Spared && squad.LastKnownTarget is not null
            && squad.Posture is Posture.Even or Posture.Bold && !squad.IsLeashed && !player.IsDead;
        foreach (var e in enemies.Where(e => !e.IsDead))
        {
            bool busy = acted.Contains(e.Id) || before[e.Id] != e.Position || world.IsAdjacent(e.Position, player.Position)
                || squad.RoleOf(e) is not (SquadRole.Warrior or SquadRole.Runner) || Queued(world, squad, e);
            idle[e.Id] = fighting && !busy ? idle.GetValueOrDefault(e.Id) + 1 : 0;
            if (idle[e.Id] >= JamTurns && reported.Add(e.Id))
                failures.Add(new FuzzFailure(seed, turn, FuzzProblem.Jammed,
                    $"{e.Kind}#{e.Id} ({squad.Mind.Id}, {squad.Posture}) stuck at {e.Position}, player at {player.Position}, "
                    + $"{(world.Map.Walkable[player.Position] ? "" : "unwalkable ")}distance {World.Distance(e.Position, player.Position)}"));
        }
    }

    /// <summary>Waiting its turn behind a comrade who stands on its way: a queue, not a jam.</summary>
    private static bool Queued(World world, Squad squad, Actor self)
    {
        var toward = squad.TowardKnownThreats(world.Map.Passable);
        return toward[self.Position] is { } here && World.EightWay.Any(d => world.ActorAt(self.Position + d) is { } other
            && other != world.Player && !world.AreHostile(self, other) && toward[other.Position] < here);
    }

    /// <summary>A cheap summary of the state that matters: who is where, how hurt, and the turn.</summary>
    private static int Fingerprint(World world)
    {
        var hash = new HashCode();
        hash.Add(world.Turn);
        foreach (var a in world.Actors)
        {
            hash.Add(a.Id);
            hash.Add(a.Position);
            hash.Add(a.Hp);
        }
        foreach (var squad in world.Squads)
        {
            hash.Add(squad.State);
            hash.Add(squad.Morale);
        }
        return hash.ToHashCode();
    }
}
