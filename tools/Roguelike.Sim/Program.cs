using System.Diagnostics;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Simulation;

// Headless simulation report: plays many seeded games with a bot and summarises what happened.
//
//   dotnet run --project tools/Roguelike.Sim -- [--seeds 50] [--start 0] [--turns 1000]
//                                               [--bot aggressive|idle|tactical] [--observer] [--games]
//   --map prints the level for seed --start instead of simulating.

var options = Options.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("usage: Roguelike.Sim [--seeds N] [--start S] [--turns T] [--bot aggressive|idle|tactical] [--observer] [--games] [--map]");
    return 1;
}

if (options.Trials)
{
    TrialReport.Print(options.Seeds, options.Start);
    return 0;
}

if (options.Encounters)
{
    Console.WriteLine($"{"valley fight",-26} {"won",6} {"died",6} {"hp left",8} {"turns",6}   (bot with the starting kit, {options.Seeds} games each)");
    foreach (var r in Encounters.Run(options.Seeds, options.Start, options.ClassId))
        Console.WriteLine($"{r.Name,-26} {100.0 * r.Won / r.Games,5:0}% {100.0 * r.Died / r.Games,5:0}% {100 * r.HpLeftWhenWon,7:0}% {r.Turns,6:0}");
    return 0;
}

if (options.ShowMap)
{
    // Print the level for seed --start as text: # wall, . floor, @ player, letters = monsters.
    var world = Roguelike.Core.World.Create(options.Start);
    for (int y = 0; y < world.Map.Height; y++)
    {
        var line = new System.Text.StringBuilder();
        for (int x = 0; x < world.Map.Width; x++)
        {
            var p = new SadRogue.Primitives.Point(x, y);
            var actor = world.ActorAt(p);
            line.Append(actor == world.Player ? '@'
                : actor is not null ? actor.Kind[0]
                : world.ItemsAt(p).Any() ? '!'
                : world.Map.Walkable[p] ? '.' : '#');
        }
        Console.WriteLine(line);
    }
    Console.WriteLine($"rooms {world.Map.Rooms.Count}, floor inside rooms {100 * Simulator.RoomFloorFraction(world):0}%");
    return 0;
}

int TraceEvery = int.TryParse(Environment.GetEnvironmentVariable("TRACE_EVERY"), out var te) ? te : 250;
if (options.Trace)
{
    // Follow one game (seed --start) and print a status line every 250 turns.
    var world = Roguelike.Core.World.Create(options.Start);
    var tracer = MakeBot();
    string last = "";
    while (!world.IsOver && world.Turn < options.Turns)
    {
        var action = tracer.Decide(world);
        var stepEvents = world.Step(action);
        last = action.ToString() + " " + string.Join(" ", stepEvents.OfType<Roguelike.Core.Events.ActorAttacked>()
            .Where(a => a.TargetId == world.Player.Id || a.AttackerId == world.Player.Id)
            .Select(a => $"{(a.AttackerId == world.Player.Id ? "hit" : "took")}{a.Damage}{(a.Ranged ? "r" : "")}"));
        if (world.Turn % TraceEvery == 0)
        {
            var seen = world.Actors.Where(a => a != world.Player && world.PlayerCanSee(a)).Select(a => $"{a.Kind}@{a.Position.X},{a.Position.Y}:{world.SquadOf(a)?.State}{(world.SquadOf(a)?.TargetId == world.Player.Id ? "->me" : "")}{(world.SquadOf(a)?.IsLeashed == true ? ",leashed" : "")}{(world.HasClearShot(world.Player.Position, a.Position) ? ",shot" : "")}");
            Console.WriteLine($"T{world.Turn} D{world.Depth} @{world.Player.Position.X},{world.Player.Position.Y} HP {world.Player.Hp}/{world.Player.MaxHp} " +
                              $"stairs {world.Map.StairsDown} frontier {Roguelike.Core.Simulation.Exploration.Frontier(world)} last {last} sees [{string.Join(" ", seen)}]");
        }
    }
    Console.WriteLine($"end: turn {world.Turn} depth {world.Depth} won {world.IsWon} dead {world.Player.IsDead}");
    for (int y = world.Player.Position.Y - 4; y <= world.Player.Position.Y + 4; y++)
    {
        var row = new System.Text.StringBuilder("  ");
        for (int x = world.Player.Position.X - 6; x <= world.Player.Position.X + 6; x++)
        {
            var pt = new SadRogue.Primitives.Point(x, y);
            row.Append(!world.Map.InBounds(pt) ? ' ' : world.ActorAt(pt) is { } a ? (a == world.Player ? '@' : a.Kind[^1] == 'e' ? 'Z' : a.Kind[0])
                : world.Map.Walkable[pt] ? (world.Map.Explored[pt] ? '.' : ',') : '#');
        }
        Console.WriteLine(row);
    }
    return 0;
}

IPlayerBot MakeBot() => options.Bot switch
{
    "idle" => new IdleBot(),
    "tactical" => new TacticalBot(),
    _ => new AggressiveBot(),
};

Console.WriteLine($"Simulating {options.Seeds} games (seeds {options.Start}..{options.Start + (ulong)options.Seeds - 1}), " +
                  $"up to {options.Turns} turns, bot={MakeBot().Name}{(options.Observer ? ", observer mode" : "")}, " +
                  $"{Environment.ProcessorCount} threads");
Console.WriteLine();

// Games are independent and deterministic per seed, so they run in parallel; results keep seed order.
var clock = Stopwatch.StartNew();
var slots = new GameResult[options.Seeds];
int done = 0;
Parallel.For(0, options.Seeds, i =>
{
    slots[i] = Simulator.Run(options.Start + (ulong)i, MakeBot(), options.Turns, options.Observer, options.ClassId);
    int d = Interlocked.Increment(ref done);
    if (options.Seeds >= 200 && d % (options.Seeds / 10) == 0)
        Console.Error.WriteLine($"  {d}/{options.Seeds} games ({clock.Elapsed.TotalSeconds:0}s)");
});
var results = slots.ToList();
if (options.PerGame)
{
    foreach (var result in results)
        Console.WriteLine($"  seed {result.Seed,6}: {(result.Won ? "WON" : result.PlayerDied ? "died" : "alive"),-5} depth {result.DepthReached,2} turn {result.Turns,6}  " +
                          $"xp {result.PlayerXp,5}  {(result.KilledBy is { } k ? "killed by " + k : "")}");
    Console.WriteLine();
}
clock.Stop();

int n = results.Count;
int deaths = results.Count(r => r.PlayerDied);
double totalTurns = results.Sum(r => r.Turns);

Console.WriteLine("Outcome");
Console.WriteLine($"  player died            {deaths,6} / {n}  ({100.0 * deaths / n:0}%)");
Console.WriteLine($"  avg turns played       {totalTurns / n,8:0.0}");
if (deaths > 0)
    Console.WriteLine($"  avg turn of death      {results.Where(r => r.PlayerDied).Average(r => r.Turns),8:0.0}");
Console.WriteLine($"  squads surviving       {results.Sum(r => r.SquadsAtEnd),6} / {results.Sum(r => r.SquadsAtStart)}");
Console.WriteLine($"  monsters surviving     {results.Sum(r => r.MonstersAtEnd),6} / {results.Sum(r => r.MonstersAtStart)}");
var survivors = results.Where(r => !r.PlayerDied).ToList();
if (survivors.Count > 0)
{
    Console.WriteLine($"  survivors' lowest HP   {100 * survivors.Average(r => r.LowestHpFraction),7:0}%  (avg)");
    Console.WriteLine($"  close calls (<25% HP)  {survivors.Count(r => r.LowestHpFraction < 0.25),6} / {survivors.Count}");
}
int levels = results.Max(r => r.DepthReached);
if (levels > 1 || results.Any(r => r.Won))
{
    Console.WriteLine($"  games won              {results.Count(r => r.Won),6} / {n}  ({100.0 * results.Count(r => r.Won) / n:0.0}%)");
    Console.WriteLine($"  avg depth reached      {results.Average(r => r.DepthReached),8:0.00}");
    Console.WriteLine();
    Console.WriteLine("By depth   reached   died here   death rate here                          top killers here");
    for (int depth = 1; depth <= levels; depth++)
    {
        int reached = results.Count(r => r.DepthReached >= depth);
        var diedHere = results.Where(r => r.PlayerDied && r.DepthReached == depth).ToList();
        var killers = string.Join(", ", diedHere.GroupBy(r => r.KilledBy ?? "?").OrderByDescending(g => g.Count())
            .Take(3).Select(g => $"{g.Key} {g.Count()}"));
        var stillHere = results.Count(r => !r.PlayerDied && !r.Won && r.DepthReached == depth);
        Console.WriteLine($"  {depth,5} {reached,9} {diedHere.Count,11} {(reached == 0 ? 0 : 100.0 * diedHere.Count / reached),14:0.0}%" +
                          $"   {(stillHere > 0 ? $"(timed out here: {stillHere})" : ""),-22} {killers}");
    }
    Console.WriteLine();
    Console.WriteLine("Character on arrival (averages)   turn  maxHP  melee  ranged  def  acc  eva     XP  potions");
    for (int depth = 1; depth <= levels; depth++)
    {
        var at = results.SelectMany(r => r.Arrivals ?? []).Where(a => a.Depth == depth).ToList();
        if (at.Count == 0)
            continue;
        Console.WriteLine($"  {depth,5} {"",26}{at.Average(a => a.Turn),6:0} {at.Average(a => a.MaxHp),6:0.0} {at.Average(a => a.Attack),6:0.0}" +
                          $" {at.Average(a => a.RangedAttack),7:0.0} {at.Average(a => a.Defense),4:0.0} {at.Average(a => a.Accuracy),4:0.0}" +
                          $" {at.Average(a => a.Evasion),4:0.0} {at.Average(a => a.TotalXp),6:0} {at.Average(a => a.Potions),8:0.0}");
    }
    Console.WriteLine();
}
Console.WriteLine($"  floor inside rooms     {100 * results.Average(r => r.RoomFloorFraction),7:0}%  (rest is corridor/maze)");
Console.WriteLine($"  monsters per level     {results.Average(r => r.MonstersAtStart),8:0.0}");
Console.WriteLine($"  player XP earned       {results.Average(r => r.PlayerXp),8:0.0}  (avg; survivors {(survivors.Count > 0 ? survivors.Average(r => r.PlayerXp) : 0):0})");
Console.WriteLine();

Console.WriteLine("Events (total, per game)");
var keys = results.SelectMany(r => r.EventCounts.Keys).Distinct().Order().ToList();
int width = keys.Count == 0 ? 0 : keys.Max(k => k.Length);
foreach (var key in keys)
{
    int total = results.Sum(r => r.EventCounts.GetValueOrDefault(key));
    Console.WriteLine($"  {key.PadRight(width)} {total,6}  {(double)total / n,7:0.0}");
}
Console.WriteLine();

Console.WriteLine("Time squads spent in each state");
double squadTurns = results.Sum(r => r.SquadStateTurns.Values.Sum());
foreach (var state in Enum.GetValues<SquadState>())
{
    int t = results.Sum(r => r.SquadStateTurns[state]);
    double pct = squadTurns == 0 ? 0 : 100.0 * t / squadTurns;
    Console.WriteLine($"  {state,-8} {pct,5:0.0}%  {new string('#', (int)Math.Round(pct / 2.5))}");
}
Console.WriteLine();
Console.WriteLine($"Ran {totalTurns:0} turns in {clock.Elapsed.TotalSeconds:0.0}s ({totalTurns / clock.Elapsed.TotalSeconds:0} turns/s)");
return 0;

internal sealed record Options(int Seeds, ulong Start, int Turns, string Bot, bool Observer, bool PerGame, bool ShowMap = false, bool Trace = false, bool Trials = false, bool Encounters = false, string? ClassId = null)
{
    public static Options? Parse(string[] args)
    {
        var o = new Options(50, 0, 1000, "aggressive", false, false);
        for (int i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i])
            {
                case "--seeds" when int.TryParse(Next(), out var v) && v > 0: o = o with { Seeds = v }; break;
                case "--start" when ulong.TryParse(Next(), out var v): o = o with { Start = v }; break;
                case "--turns" when int.TryParse(Next(), out var v) && v > 0: o = o with { Turns = v }; break;
                case "--bot" when Next() is "aggressive" or "idle" or "tactical": o = o with { Bot = args[i] }; break;
                case "--observer": o = o with { Observer = true }; break;
                case "--games": o = o with { PerGame = true }; break;
                case "--map": o = o with { ShowMap = true }; break;
                case "--class" when Next() is { } c: o = o with { ClassId = c }; break;
                case "--encounters": o = o with { Encounters = true }; break;
                case "--trials": o = o with { Trials = true }; break;
                case "--trace": o = o with { Trace = true }; break;
                default: return null;
            }
        }
        return o;
    }
}
