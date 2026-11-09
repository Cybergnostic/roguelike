using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Items;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Simulation;

/// <summary>A way of playing a class: XP purchases in order ("skill:id" or "attr:id"), then attributes in rotation.</summary>
public sealed record TrialBuild(string Class, string Name, string[] Spend, string[] Attributes)
{
    public string Label => $"{Class} {Name}";
}

/// <summary>How far along a character is: where it is, the XP it has earned there, and what it has found.</summary>
public sealed record TrialLevel(string Name, int Depth, int Xp, int GearPlus, int Potions);

/// <summary>Who you fight, and at which levels of experience you would meet them.</summary>
public sealed record TrialFight(string Name, string[] Levels, params string[] Group)
{
    /// <summary>Bones lying ready for a necromancer to raise.</summary>
    public string[] Corpses { get; init; } = [];
}

/// <summary>Where the fight starts: caught in the middle of a room, or at the mouth of a corridor.</summary>
public enum TrialArena { Room, Corridor }

public enum TrialOutcome { Won, DroveOff, Stalemate, Died }

/// <summary>What a fight tends to be like to play.</summary>
public enum FightVerdict { Walkover, RealFight, Stalemate, Lethal }

/// <summary>Many games of one fight, averaged.</summary>
public sealed record TrialStats(int Games, double Won, double DroveOff, double Stalemate, double Died, double HpLeft, double CloseCalls,
    double Turns, double PeakRing, double Potions, double Skills, double Broke)
{
    public FightVerdict Verdict =>
        Died >= LethalAbove ? FightVerdict.Lethal
        : Stalemate >= StalemateAbove ? FightVerdict.Stalemate
        : Died <= WalkoverDeaths && CloseCalls <= WalkoverCloseCalls && HpLeft >= WalkoverHp ? FightVerdict.Walkover
        : FightVerdict.RealFight;

    private const double LethalAbove = 0.5, StalemateAbove = 0.3, WalkoverDeaths = 0.03, WalkoverCloseCalls = 0.08, WalkoverHp = 0.6;
}

/// <summary>
/// Controlled fights, for seeing what squad intelligence does and which fights are any good to play:
/// each build, at each stage of a game, against each enemy, with nothing else on the level.
/// </summary>
public static class Trials
{
    private const int MaxTurns = 400;
    private const int StartDistance = 7;

    /// <summary>Below this share of its health, a fight won counts as a close call.</summary>
    private const double CloseCallBelow = 0.25;

    public const string Fresh = "fresh", Seasoned = "seasoned", Veteran = "veteran", Final = "final";

    /// <summary>Nobody left in sight and none within this many cells: the fight is over, whoever is still alive.</summary>
    private const int FightOverBeyond = 10;

    // XP and potions are what the tactical bot has on arrival at these depths.
    public static readonly TrialLevel[] Levels =
    [
        new(Fresh, 1, 0, 0, 2),
        new(Seasoned, 3, 650, 1, 3),
        new(Veteran, 6, 2350, 2, 5),
        new(Final, 7, 2900, 3, 5),
    ];

    public static readonly TrialBuild[] Builds =
    [
        new("soldier", "bruiser", ["skill:weapon_training", "skill:crushing_blow", "skill:weapon_training", "skill:cleave", "skill:toughness"], ["str", "con"]),
        new("soldier", "bulwark", ["skill:toughness", "skill:hold_the_line", "skill:thick_skin", "skill:toughness", "skill:crushing_blow", "skill:thick_skin"], ["con", "str"]),
        new("ranger", "sharpshooter", ["skill:steady_aim", "skill:eagle_eye", "skill:steady_aim", "skill:pinning_shot", "skill:eagle_eye", "skill:steady_aim"], ["dex", "con"]),
        new("ranger", "skirmisher", ["skill:skirmisher", "skill:steady_aim", "skill:fleet_foot", "skill:pinning_shot", "skill:fleet_foot"], ["dex", "wis"]),
        new("rogue", "duelist", ["skill:thick_skin", "skill:fleet_foot", "skill:vanish_skill", "skill:fleet_foot", "skill:thick_skin"], ["dex", "str"]),
        new("rogue", "survivor", ["skill:vanish_skill", "skill:field_medicine", "skill:thick_skin", "skill:second_wind", "skill:field_medicine"], ["con", "dex"]),
        new("mystic", "pyromancer", ["skill:cinder_bolt", "skill:life_drain", "skill:mend", "skill:fleet_foot"], ["int", "con"]),
        new("mystic", "mender", ["skill:mend", "skill:cinder_bolt", "skill:field_medicine", "skill:second_wind", "skill:thick_skin", "skill:life_drain"], ["wis", "con"]),
    ];

    public static readonly TrialFight[] Fights =
    [
        new("wolf pack", [Fresh, Seasoned], "wolf", "wolf", "wolf", "wolf"),
        new("two boars", [Fresh, Seasoned], "boar", "boar"),
        new("giant spiders", [Fresh], "giant_spider", "giant_spider", "giant_spider"),
        new("a bear", [Fresh, Seasoned], "bear"),
        new("goblin scouts", [Fresh], "goblin_warrior", "goblin_warrior", "goblin_archer"),
        new("bandits", [Fresh], "bandit", "bandit", "bandit_archer"),
        new("bandits and chief", [Fresh, Seasoned], "bandit_chief", "bandit", "bandit", "bandit_archer"),
        new("goblin band", [Seasoned, Veteran], "goblin_warrior", "goblin_warrior", "goblin_warrior", "goblin_archer", "goblin_shaman"),
        new("captained goblins", [Seasoned, Veteran], "goblin_captain", "goblin_warrior", "goblin_warrior", "goblin_warrior", "goblin_archer", "goblin_shaman"),
        new("zombies", [Seasoned], "goblin_zombie", "goblin_zombie", "goblin_zombie", "goblin_zombie"),
        new("skeleton patrol", [Seasoned, Veteran], "skeleton_warrior", "skeleton_warrior", "skeleton_warrior", "skeleton_archer"),
        new("skeletons and knight", [Veteran], "bone_knight", "skeleton_warrior", "skeleton_warrior", "skeleton_warrior", "skeleton_archer"),
        new("goblin warband", [Veteran], "goblin_warrior", "goblin_warrior", "goblin_warrior", "goblin_warrior", "goblin_archer", "goblin_archer", "goblin_shaman"),
        new("goblin warlord's host", [Veteran], "goblin_warlord", "goblin_warrior", "goblin_warrior", "goblin_warrior", "goblin_warrior", "goblin_archer", "goblin_archer", "goblin_shaman"),
        new("the necromancer's court", [Final], "necromancer", "bone_knight", "bone_knight", "wraith", "skeleton_archer")
            { Corpses = ["skeleton_warrior", "skeleton_warrior", "skeleton_warrior", "skeleton_archer", "skeleton_archer"] },
        new("skeleton cohort", [Final], "bone_knight", "skeleton_warrior", "skeleton_warrior", "skeleton_warrior", "skeleton_archer", "skeleton_archer"),
    ];

    /// <summary>The same bodies, thinking differently: what intelligence alone is worth.</summary>
    public static readonly TrialFight[] MindSubjects =
    [
        new("six wolves", [Seasoned], "wolf", "wolf", "wolf", "wolf", "wolf", "wolf"),
        new("goblin force", [Seasoned], "goblin_warrior", "goblin_warrior", "goblin_warrior", "goblin_warrior", "goblin_archer", "goblin_archer", "goblin_shaman"),
        new("skeleton force", [Seasoned], "skeleton_warrior", "skeleton_warrior", "skeleton_warrior", "skeleton_archer", "skeleton_archer"),
    ];

    public static readonly string[] MindOrder = ["mindless", "beast", "pack", "rabble", "drilled"];

    public static TrialBuild BuildFor(string classId) => Builds.First(b => b.Class == classId);

    public static TrialLevel Level(string name) => Levels.First(l => l.Name == name);

    /// <summary>Plays the fight once for each seed. With <paramref name="mind"/>, the squad is forced to think that way.</summary>
    public static TrialStats Run(TrialBuild build, TrialLevel level, TrialFight fight, int seeds, ulong start = 0,
        TrialArena arena = TrialArena.Room, string? mind = null)
    {
        var games = Enumerable.Range(0, seeds).AsParallel().AsOrdered()
            .Select(i => PlayOne(start + (ulong)i, build, level, fight, arena, mind)).ToList();
        var won = games.Where(g => g.Outcome == TrialOutcome.Won).ToList();
        double Share(Func<Game, bool> test) => (double)games.Count(test) / games.Count;
        return new TrialStats(games.Count, Share(g => g.Outcome == TrialOutcome.Won), Share(g => g.Outcome == TrialOutcome.DroveOff),
            Share(g => g.Outcome == TrialOutcome.Stalemate), Share(g => g.Outcome == TrialOutcome.Died),
            won.Count == 0 ? 0 : won.Average(g => g.HpLeft),
            Share(g => g.Outcome != TrialOutcome.Died && g.LowestHp < CloseCallBelow),
            games.Average(g => g.Turns), games.Average(g => g.PeakRing), games.Average(g => g.Potions), games.Average(g => g.Skills),
            Share(g => g.Broke));
    }

    private sealed record Game(TrialOutcome Outcome, double HpLeft, double LowestHp, int Turns, int PeakRing, int Potions, int Skills, bool Broke);

    private static Game PlayOne(ulong seed, TrialBuild build, TrialLevel level, TrialFight fight, TrialArena arena, string? mind)
    {
        var world = World.Create(seed, depth: level.Depth, classId: build.Class);
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        Prepare(world, build, level);
        var enemies = Place(world, fight, arena);
        var squad = world.SquadOf(enemies[0])!;
        if (mind is not null)
            squad.MindOverride = world.Content.Minds[mind];

        var player = world.Player;
        var bot = new TacticalBot(spendsXp: false);
        int startTurn = world.Turn, depth = world.Depth, ring = 0, potions = 0, skills = 0;
        double lowest = 1;
        bool broke = false;
        for (int steps = 0; world.Turn - startTurn < MaxTurns && steps < MaxTurns * 4 && !world.IsOver && world.Depth == depth && enemies.Any(e => !e.IsDead); steps++)
        {
            foreach (var e in world.Step(bot.Decide(world)))
                switch (e)
                {
                    case PotionDrunk p when p.ActorId == player.Id: potions++; break;
                    case AbilityUsed a when a.ActorId == player.Id: skills++; break;
                    case SquadRouted or SquadCowed or SquadPleaded: broke = true; break;
                }
            // Lairs keep sending out squads; nobody else is invited to this fight.
            foreach (var stranger in world.Actors.Where(a => a != player && !a.IsDead && a.MasterId is null && world.SquadOf(a) != squad))
                stranger.Hp = 0;
            ring = Math.Max(ring, World.EightWay.Count(d => world.ActorAt(player.Position + d) is { } a && world.AreHostile(a, player)));
            lowest = Math.Min(lowest, (double)Math.Max(0, player.Hp) / player.MaxHp);
        }

        var outcome = player.IsDead ? TrialOutcome.Died
            : enemies.All(e => e.IsDead) || world.IsOver ? TrialOutcome.Won // killing the boss ends the game at once
            : squad.State == SquadState.Flee || squad.Surrendered || squad.Spared || FightIsOver(world, enemies) ? TrialOutcome.DroveOff
            : TrialOutcome.Stalemate;
        return new Game(outcome, (double)Math.Max(0, player.Hp) / player.MaxHp, lowest, world.Turn - startTurn, ring, potions, skills, broke);
    }

    private static bool FightIsOver(World world, List<Actor> enemies) =>
        enemies.Where(e => !e.IsDead).All(e => World.Distance(e.Position, world.Player.Position) > FightOverBeyond && !world.CanSee(world.Player, e));

    /// <summary>Spends the XP, upgrades the gear and tops up the potions for this stage of a game.</summary>
    private static void Prepare(World world, TrialBuild build, TrialLevel level)
    {
        var player = world.Player;
        var c = player.Character!;
        if (level.Xp > 0)
            world.GrantXp(XpSource.Kill, "trial", level.Xp);
        foreach (var step in build.Spend)
        {
            var id = step[(step.IndexOf(':') + 1)..];
            var problem = step.StartsWith("skill:") ? c.CheckLearn(id) : c.CheckRaise(id);
            if (problem == FailureReason.NotEnoughXp)
                break; // saving up for it
            if (problem is null)
                world.Step(step.StartsWith("skill:") ? new LearnSkillAction(id) : new RaiseAttributeAction(id));
        }
        for (int i = 0; build.Attributes.Any(a => c.CheckRaise(a) is null); i++)
            if (build.Attributes[i % build.Attributes.Length] is var attribute && c.CheckRaise(attribute) is null)
                world.Step(new RaiseAttributeAction(attribute));

        if (level.GearPlus > 0)
        {
            var gear = player.Equipment!;
            foreach (var worn in new[] { gear.Weapon, gear.OffHand, gear.Bow, gear.Armor }.OfType<Item>().ToList())
            {
                gear.Unequip(worn);
                player.Inventory!.Remove(worn);
                gear.Equip(player.Inventory.Add(world.CreateItem(worn.Def, level.GearPlus)));
            }
        }
        var potion = player.Inventory!.Items.FirstOrDefault(i => i.Def.Category == ItemCategory.Potion);
        int have = potion?.Count ?? 0;
        if (have < level.Potions)
            player.Inventory.Add(world.CreateItem(potion?.Def ?? world.Content.Items["healing_potion"], count: level.Potions - have));
        player.Hp = player.MaxHp;
    }

    private static List<Actor> Place(World world, TrialFight fight, TrialArena arena)
    {
        var map = world.Map;
        var room = map.Rooms.OrderByDescending(r => r.Area).First();
        var free = map.Tiles.Positions().Where(world.IsFree).ToList();
        var chokepoints = arena == TrialArena.Corridor ? free.Where(world.IsChokepoint).ToList() : [];
        var from = chokepoints.Count > 0 ? chokepoints : free;
        var player = world.Player;
        player.Position = from.OrderBy(p => World.Distance(p, room.Center)).First();

        var center = free.Where(p => p != player.Position && world.HasLineOfSight(player.Position, p))
            .OrderBy(p => Math.Abs(World.Distance(p, player.Position) - StartDistance)).First();
        // Everyone in plain sight of the middle of the group if there's room, or else as near as they can get.
        var spots = free.Where(p => p != player.Position && World.Distance(p, player.Position) >= 3)
            .OrderBy(p => world.HasLineOfSight(center, p) ? 0 : 1).ThenBy(p => World.Distance(p, center)).ToList();

        var enemies = new List<Actor>();
        Squad? squad = null;
        for (int i = 0; i < fight.Group.Length; i++)
        {
            var enemy = world.SpawnMonster(fight.Group[i], spots[i], squad);
            squad ??= world.SquadOf(enemy);
            enemies.Add(enemy);
        }
        foreach (var (monster, i) in fight.Corpses.Select((m, i) => (m, i)))
            world.LayBones(monster, spots[fight.Group.Length + i]);
        return enemies;
    }
}
