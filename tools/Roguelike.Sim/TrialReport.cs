using Roguelike.Core.Simulation;

/// <summary>Prints the controlled fights: what intelligence does, and what each fight is like to play.</summary>
internal static class TrialReport
{
    private static readonly IReadOnlyDictionary<FightVerdict, string> Marks = new Dictionary<FightVerdict, string>
    {
        [FightVerdict.Walkover] = ".", [FightVerdict.RealFight] = "*", [FightVerdict.Stalemate] = "~", [FightVerdict.Lethal] = "!",
    };

    public static void Print(int seeds, ulong start)
    {
        Minds(seeds, start);
        var cells = new List<(TrialBuild Build, TrialLevel Level, TrialFight Fight, TrialStats Stats)>();
        foreach (var level in Trials.Levels)
            cells.AddRange(Roster(level, seeds, start));
        Summary(cells);
    }

    private static void Minds(int seeds, ulong start)
    {
        string[] classes = ["soldier", "ranger", "rogue", "mystic"];
        var level = Trials.Level(Trials.Seasoned);
        Console.WriteLine($"HOW MUCH THINKING MATTERS  (same bodies, seasoned characters, {seeds} games per class; columns: player win % per class)");
        Console.WriteLine($"{"enemy",-16}{"arena",-10}{"mind",-10}{"soldier",8}{"ranger",8}{"rogue",8}{"mystic",8} |{"died",6}{"close",7}{"ring",6}{"turns",7}{"broke",7}{"stale",7}");
        foreach (var subject in Trials.MindSubjects)
            foreach (var arena in Enum.GetValues<TrialArena>())
                foreach (var mind in Trials.MindOrder)
                {
                    var runs = classes.Select(c => Trials.Run(Trials.BuildFor(c), level, subject, seeds, start, arena, mind)).ToList();
                    Console.Write($"{subject.Name,-16}{arena,-10}{mind,-10}");
                    foreach (var r in runs)
                        Console.Write($"{100 * (r.Won + r.DroveOff),7:0}%");
                    Console.WriteLine($" |{100 * runs.Average(r => r.Died),5:0}%{100 * runs.Average(r => r.CloseCalls),6:0}%{runs.Average(r => r.PeakRing),6:0.0}{runs.Average(r => r.Turns),7:0}{100 * runs.Average(r => r.Broke),6:0}%{100 * runs.Average(r => r.Stalemate),6:0}%");
                }
        Console.WriteLine();
    }

    private static List<(TrialBuild, TrialLevel, TrialFight, TrialStats)> Roster(TrialLevel level, int seeds, ulong start)
    {
        var fights = Trials.Fights.Where(f => f.Levels.Contains(level.Name)).ToList();
        var results = new List<(TrialBuild, TrialLevel, TrialFight, TrialStats)>();
        Console.WriteLine($"{level.Name.ToUpperInvariant()}  (depth {level.Depth}, {level.Xp} XP, gear +{level.GearPlus}, {level.Potions} potions, {seeds} games each)   win%  .=walkover *=real fight ~=stalemate !=lethal");
        Console.Write($"{"",-22}");
        foreach (var b in Trials.Builds)
            Console.Write($"{b.Class[..3] + " " + b.Name[..Math.Min(4, b.Name.Length)],9}");
        Console.WriteLine();
        foreach (var fight in fights)
        {
            Console.Write($"{fight.Name,-22}");
            foreach (var build in Trials.Builds)
            {
                var s = Trials.Run(build, level, fight, seeds, start);
                results.Add((build, level, fight, s));
                Console.Write($"{100 * (s.Won + s.DroveOff),7:0}{Marks[s.Verdict],-2}");
            }
            Console.WriteLine();
        }
        Console.WriteLine();
        return results;
    }

    private static void Summary(List<(TrialBuild Build, TrialLevel Level, TrialFight Fight, TrialStats Stats)> cells)
    {
        string Row(IEnumerable<TrialStats> stats)
        {
            var list = stats.ToList();
            double Share(FightVerdict v) => 100.0 * list.Count(s => s.Verdict == v) / list.Count;
            return $"{list.Average(s => 100 * (s.Won + s.DroveOff)),5:0}%{list.Average(s => 100 * s.Died),6:0}%{list.Average(s => 100 * s.CloseCalls),7:0}%{list.Average(s => s.Potions),8:0.0}{list.Average(s => s.Skills),8:0.0}{list.Average(s => s.Turns),7:0} |{Share(FightVerdict.Walkover),6:0}%{Share(FightVerdict.RealFight),6:0}%{Share(FightVerdict.Stalemate),6:0}%{Share(FightVerdict.Lethal),6:0}%";
        }
        string header = $"{"win",6}{"died",7}{"close",8}{"potions",8}{"skills",8}{"turns",7} |{"walk",7}{"real",7}{"stale",7}{"lethal",7}";
        Console.WriteLine($"{"BY BUILD",-24}{header}");
        foreach (var g in cells.GroupBy(c => c.Build.Label))
            Console.WriteLine($"{g.Key,-24}{Row(g.Select(c => c.Stats))}");
        Console.WriteLine();
        Console.WriteLine($"{"BY FIGHT",-24}{header}");
        foreach (var g in cells.GroupBy(c => c.Fight.Name))
            Console.WriteLine($"{g.Key,-24}{Row(g.Select(c => c.Stats))}");
        Console.WriteLine();
        Console.WriteLine($"{"BY STAGE",-24}{header}");
        foreach (var g in cells.GroupBy(c => c.Level.Name))
            Console.WriteLine($"{g.Key,-24}{Row(g.Select(c => c.Stats))}");
    }
}
