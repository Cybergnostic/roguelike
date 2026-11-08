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
    int PlayerXp = 0);

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
        double lowestHp = 1.0;

        while (world.Turn < maxTurns && !world.Player.IsDead)
        {
            foreach (var e in world.Step(bot.Decide(world)))
            {
                var key = Classify(world, e);
                if (key is not null)
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                if (e is ActorDied d && d.ActorId == world.Player.Id && world.GetActor(d.KillerId) is { } killer)
                {
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
            counts, stateTurns, lowestHp, RoomFloorFraction(world), world.Player.Character?.TotalXp ?? 0);
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
        LootDropped => "loot dropped",
        ItemPickedUp => "items picked up",
        ItemEquipped => "items equipped",
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
        _ => null,
    };
}
