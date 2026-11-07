using System.Diagnostics;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Simulation;

// Headless simulation report: plays many seeded games with a bot and summarises what happened.
//
//   dotnet run --project tools/Roguelike.Sim -- [--seeds 50] [--start 0] [--turns 1000]
//                                               [--bot aggressive|idle] [--observer] [--games]

var options = Options.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("usage: Roguelike.Sim [--seeds N] [--start S] [--turns T] [--bot aggressive|idle] [--observer] [--games]");
    return 1;
}

IPlayerBot bot = options.Bot switch
{
    "idle" => new IdleBot(),
    _ => new AggressiveBot(),
};

Console.WriteLine($"Simulating {options.Seeds} games (seeds {options.Start}..{options.Start + (ulong)options.Seeds - 1}), " +
                  $"up to {options.Turns} turns, bot={bot.Name}{(options.Observer ? ", observer mode" : "")}");
Console.WriteLine();

var clock = Stopwatch.StartNew();
var results = new List<GameResult>();
for (int i = 0; i < options.Seeds; i++)
{
    var result = Simulator.Run(options.Start + (ulong)i, bot, options.Turns, options.Observer);
    results.Add(result);
    if (options.PerGame)
        Console.WriteLine($"  seed {result.Seed,6}: {(result.PlayerDied ? "died" : "alive"),-5} turn {result.Turns,5}  " +
                          $"squads {result.SquadsAtStart,2}->{result.SquadsAtEnd,-2} monsters {result.MonstersAtStart,3}->{result.MonstersAtEnd,-3}");
}
clock.Stop();
if (options.PerGame)
    Console.WriteLine();

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

internal sealed record Options(int Seeds, ulong Start, int Turns, string Bot, bool Observer, bool PerGame)
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
                case "--bot" when Next() is "aggressive" or "idle": o = o with { Bot = args[i] }; break;
                case "--observer": o = o with { Observer = true }; break;
                case "--games": o = o with { PerGame = true }; break;
                default: return null;
            }
        }
        return o;
    }
}
