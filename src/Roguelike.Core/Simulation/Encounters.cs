using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Simulation;

/// <summary>One fight from the valley, played out many times: how a fresh character fares against it.</summary>
public sealed record EncounterResult(string Name, int Games, int Won, int Died, double HpLeftWhenWon, double Turns);

/// <summary>
/// The balance simulation never plays the surface, so this measures it fight by fight: the bot with
/// the starting kit against each group of valley monsters, with everything else on the map removed.
/// </summary>
public static class Encounters
{
    private const int MaxTurns = 400;
    private const int StartDistance = 8;

    public static IReadOnlyList<EncounterResult> Run(int seeds, ulong start = 0)
    {
        var groups = Content.ContentDatabase.Default.Surface.Groups;
        var fights = groups.Select((g, i) => (g.Name, g.Faction, Index: i))
            .Where(g => g.Faction is "beasts" or "bandits")
            .Select(g => (g.Name, Squads: new[] { g.Index }))
            .ToList();
        var den = groups.Select((g, i) => (g, i)).Where(x => x.g.Faction == "bandits").Select(x => x.i).ToArray();
        if (den.Length > 1)
            fights.Add(("the whole bandit den", den));

        return fights.Select(f => Play(f.Name, f.Squads, seeds, start)).ToList();
    }

    private static EncounterResult Play(string name, int[] squadIndexes, int seeds, ulong start)
    {
        var outcomes = Enumerable.Range(0, seeds).AsParallel().AsOrdered()
            .Select(i => PlayOne(start + (ulong)i, squadIndexes)).ToList();
        var won = outcomes.Where(o => o.Won).ToList();
        return new EncounterResult(name, seeds, won.Count, outcomes.Count(o => o.Died),
            won.Count == 0 ? 0 : won.Average(o => o.HpLeft), outcomes.Average(o => o.Turns));
    }

    private static (bool Won, bool Died, double HpLeft, int Turns) PlayOne(ulong seed, int[] squadIndexes)
    {
        var world = World.Create(seed, depth: World.SurfaceDepth);
        var enemies = squadIndexes.Select(i => world.Squads[i]).SelectMany(s => s.Members).ToList();
        foreach (var other in world.Actors.Where(a => a != world.Player && !enemies.Contains(a)))
            other.Hp = 0;
        world.Step(new WaitAction());

        var near = enemies[0].Position;
        world.Player.Position = world.Map.Tiles.Positions()
            .Where(p => world.IsFree(p) && world.HasLineOfSight(p, near))
            .OrderBy(p => Math.Abs(World.Distance(p, near) - StartDistance)).First();

        var bot = new TacticalBot();
        int start = world.Turn;
        for (int steps = 0; world.Turn - start < MaxTurns && !world.IsOver && enemies.Any(e => !e.IsDead) && steps < MaxTurns * 4; steps++)
            world.Step(bot.Decide(world));
        bool won = enemies.All(e => e.IsDead) && !world.Player.IsDead;
        return (won, world.Player.IsDead, (double)Math.Max(0, world.Player.Hp) / world.Player.MaxHp, world.Turn - start);
    }
}
