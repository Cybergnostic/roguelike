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
    IReadOnlyDictionary<SquadState, int> SquadStateTurns,
    double LowestHpFraction,
    double RoomFloorFraction,
    int PlayerXp = 0,
    int DepthReached = 1,
    bool Won = false,
    string? KilledBy = null,
    IReadOnlyList<LevelArrival>? Arrivals = null);

/// <summary>The character as it arrived on a level (depth 1: at the start), for tracking growth against the dungeon.</summary>
public sealed record LevelArrival(int Depth, int Turn, int MaxHp, int Attack, int RangedAttack, int Defense,
    int Accuracy, int Evasion, int TotalXp, int Potions)
{
    public static LevelArrival Of(World world)
    {
        var p = world.Player;
        int potions = p.Inventory?.Items.Where(i => i.Def.Category == Items.ItemCategory.Potion).Sum(i => i.Count) ?? 0;
        return new(world.Depth, world.Turn, p.MaxHp, p.Attack, p.RangedAttack, p.Defense, p.Accuracy, p.Evasion,
            p.Character?.TotalXp ?? 0, potions);
    }
}

/// <summary>Runs games without a window and counts what happened. The basis for balance work.</summary>
public static class Simulator
{
    public static GameResult Run(ulong seed, IPlayerBot bot, int maxTurns, bool observer = false, string? classId = null)
    {
        var world = World.Create(seed, classId: classId);
        world.ObserverMode = observer;

        int squadsAtStart = world.Squads.Count;
        int monstersAtStart = CountMonsters(world);
        var counts = new Dictionary<string, int>();
        var stateTurns = Enum.GetValues<SquadState>().ToDictionary(s => s, _ => 0);
        double lowestHp = 1.0;

        string? killedBy = null;
        int stalled = 0;
        var arrivals = new List<LevelArrival> { LevelArrival.Of(world) };
        while (world.Turn < maxTurns && !world.IsOver)
        {
            // A bot whose action doesn't take a turn (spending XP is free) is asked again; one that keeps
            // failing gets a wait, so a confused bot can't hang the simulation.
            int turnBefore = world.Turn;
            var action = ++stalled > 20 ? new Actions.WaitAction() : bot.Decide(world);
            var events = world.Step(action);
            if (world.Turn != turnBefore)
                stalled = 0;
            foreach (var e in events)
            {
                if (e is LevelEntered)
                    arrivals.Add(LevelArrival.Of(world));
                var key = Classify(world, e);
                if (key is not null)
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                if (e is ActorDied d && d.ActorId == world.Player.Id && world.GetActor(d.KillerId) is { } killer)
                {
                    killedBy = killer.Kind;
                    var by = $"player killed by: {killer.Kind}";
                    counts[by] = counts.GetValueOrDefault(by) + 1;
                    var where = $"player died in: {world.Map.ZoneAt(world.Player.Position)?.Name ?? "the open dungeon"}";
                    counts[where] = counts.GetValueOrDefault(where) + 1;
                }
            }
            foreach (var squad in world.Squads)
                stateTurns[squad.State]++;
            lowestHp = Math.Min(lowestHp, Math.Max(0, (double)world.Player.Hp / world.Player.MaxHp));
        }

        return new GameResult(seed, world.Player.IsDead, world.Turn,
            squadsAtStart, world.Squads.Count, monstersAtStart, CountMonsters(world),
            counts, stateTurns, lowestHp, RoomFloorFraction(world), world.Player.Character?.TotalXp ?? 0,
            world.Depth, world.IsWon, killedBy, arrivals);
    }

    /// <summary>Share of walkable cells that lie inside rooms (the rest is corridor and maze).</summary>
    public static double RoomFloorFraction(World world)
    {
        int floor = 0, inRooms = 0;
        var walkable = world.Map.Walkable;
        for (int y = 0; y < walkable.Height; y++)
        for (int x = 0; x < walkable.Width; x++)
        {
            if (!walkable[x, y])
                continue;
            floor++;
            if (world.Map.Rooms.Any(r => r.Contains(new SadRogue.Primitives.Point(x, y))))
                inRooms++;
        }
        return floor == 0 ? 0 : (double)inRooms / floor;
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
        ActorDied d when d.KillerId == world.Player.Id => $"killed by player: {d.FactionId}",
        ActorDied d => $"killed by others: {d.FactionId}",
        SquadSpotted s when s.TargetId == world.Player.Id => "squads spotted player",
        SquadSpotted => "squads spotted other factions",
        SquadRouted => "squads routed",
        LootDropped l when l.Item.Rarity > Items.Rarity.Common => $"loot dropped: {l.Item.Rarity.ToString().ToLowerInvariant()}",
        LootDropped => "loot dropped",
        ItemPickedUp => "items picked up",
        ItemEquipped q => $"items equipped: {q.Item.Rarity.ToString().ToLowerInvariant()}",
        PotionDrunk => "potions drunk",
        ReinforcementsCalled => "reinforcements called",
        CorpseRaised => "corpses raised",
        RaidLaunched => "raids launched",
        SquadMustered => "squads mustered",
        MinionCrumbled => "minions crumbled",
        XpGained x => $"xp events: {x.Source.ToString().ToLowerInvariant()}",
        AttributeRaised => "attribute points bought",
        SkillLearned => "skill ranks learned",
        SpecialUsed u => $"specials used: {u.Special}",
        LevelEntered l => $"levels entered: {l.Depth:00}",
        GameWon => "games won",
        _ => null,
    };
}
