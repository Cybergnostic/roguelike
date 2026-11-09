using System.Text.Json;
using System.Text.Json.Serialization;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Items;
using Roguelike.Core.Progression;

namespace Roguelike.Core.Content;

/// <param name="Speed">Energy gained per turn; 100 acts once a turn, 50 every other turn, 200 twice.</param>
/// <param name="RisesAs">The monster this one's corpse becomes when raised; null if it can't be raised.</param>
/// <param name="Raise">Necromancers only: how they raise the dead.</param>
public sealed record MonsterDef(
    string Id, string Faction, SquadRole Role,
    int MaxHp, int Attack, int Defense, int Range = 0, bool Fearless = false, string? Loot = null,
    int Speed = 100, string? RisesAs = null, RaiseDef? Raise = null, int Xp = 0,
    int Accuracy = 0, int Evasion = 0, int LootDepthBonus = 0);

/// <summary>
/// How attacks resolve (combat.json). Hit chance = BaseHitChance + HitChancePerPoint × (accuracy −
/// evasion), clamped to MinHitChance..MaxHitChance; ranged attacks on an adjacent target lose
/// PointBlankPenalty accuracy. A hit rolls 1..attack, armour absorbs 0..defence, and at least
/// MinDamage gets through.
/// </summary>
public sealed record CombatRules(int BaseHitChance = 75, int HitChancePerPoint = 5, int MinHitChance = 5,
    int MaxHitChance = 95, int PointBlankPenalty = 4, int MinDamage = 1);

/// <summary>A raiser's reach (with line of sight), turns between raisings, and how many it can control.</summary>
public sealed record RaiseDef(int Range, int Cooldown, int MaxMinions);

/// <summary>One weighted loot table line; a null item means "nothing".</summary>
public sealed record LootEntry(string? Item, int Weight);

/// <summary>One +X outcome; MinDepth: the shallowest dungeon level it can appear on.</summary>
public sealed record EnchantmentWeight(int Value, int Weight, int MinDepth = 1);

public sealed record KitEntry(string Item, int Count = 1, bool Equip = false, int Enchantment = 0);

/// <summary>
/// The player's base stats, before attributes and equipment. RegenInterval: turns per 1 HP regained
/// (0 = no regeneration). Attributes: starting scores by id (missing ones start at 10).
/// </summary>
public sealed record PlayerDef(int MaxHp, int Attack, int Defense, int InventoryCapacity, IReadOnlyList<KitEntry> Kit,
    int RegenInterval = 8, IReadOnlyDictionary<string, int>? Attributes = null, XpRules? Xp = null,
    int Accuracy = 0, int Evasion = 0);

/// <summary>
/// XP besides kills: SightFraction of a monster's kill XP the first time you see its kind,
/// and NewZone for first entering a named place.
/// </summary>
public sealed record XpRules(double SightFraction = 0, int NewZone = 0);

/// <summary>One attribute (Strength...): what each point of its modifier adds to derived stats.</summary>
public sealed record AttributeDef(string Id, string Name, string Description, IReadOnlyDictionary<Stat, double> PerModifier);

/// <summary>Buying attribute points: the first costs BaseCost XP, each later one CostPerRaise more; scores stop at MaxScore.</summary>
public sealed record AttributeRaiseRules(int BaseCost, int CostPerRaise, int MaxScore);

/// <summary>
/// A skill bought with XP. Cost[i] is the XP for rank i+1. Requires: skills needed (rank 1+) first.
/// PerRank: derived stat bonuses per rank. Special: a named ability the rules know (see ContentDatabase.Specials).
/// </summary>
public sealed record SkillDef(string Id, string Name, string Tree, string Description, int Ranks, IReadOnlyList<int> Cost,
    IReadOnlyList<string> Requires, IReadOnlyDictionary<Stat, double> PerRank, string? Special);

/// <summary>A group of related skills. Later, classes will decide which trees a character can learn.</summary>
public sealed record SkillTreeDef(string Id, string Name, IReadOnlyList<SkillDef> Skills);

public sealed record SquadMemberDef(string Monster, int Min, int Max, double Chance = 1.0);

/// <summary>
/// A squad recipe. Lairs also use: Zone (the place's name, announced on entry), Architecture
/// (a LairBuilder style), Leash (how far, walking, the squad will go from home; 0 = no limit)
/// and Corpses (remains lying in the lair from the start).
/// </summary>
public sealed record SquadTemplateDef(string Name, string Faction, int Weight, IReadOnlyList<SquadMemberDef> Members,
    string? Zone = null, string? Architecture = null, int Leash = 0, IReadOnlyList<CorpseEntry>? Corpses = null,
    MusterDef? Muster = null, int MinDepth = 1, int MaxDepth = 99)
{
    public bool AppearsAt(int depth) => depth >= MinDepth && depth <= MaxDepth;
}

/// <summary>
/// The dungeon's shape and how it gets harder (dungeon.json). Levels: how many; taking the stairs on
/// the last one wins. Per level below the first, monsters gain MonsterHpPerLevel (fraction) of their
/// HP and XpPerLevel of their XP, +1 attack every AttackEveryLevels levels and +1 accuracy every
/// AccuracyEveryLevels (0 = never), and rooms are RoomOccupancyPerLevel more likely to be occupied.
/// </summary>
public sealed record DungeonDef(int Levels = 1, double MonsterHpPerLevel = 0, double XpPerLevel = 0,
    int AttackEveryLevels = 0, int AccuracyEveryLevels = 0, double RoomOccupancyPerLevel = 0, int DefenseEveryLevels = 0)
{
    public int HpFor(int baseHp, int depth) => (int)Math.Round(baseHp * (1 + MonsterHpPerLevel * (depth - 1)));
    public int XpFor(int baseXp, int depth) => (int)Math.Round(baseXp * (1 + XpPerLevel * (depth - 1)));
    public int AttackBonus(int depth) => AttackEveryLevels > 0 ? (depth - 1) / AttackEveryLevels : 0;
    public int AccuracyBonus(int depth) => AccuracyEveryLevels > 0 ? (depth - 1) / AccuracyEveryLevels : 0;
    public int DefenseBonus(int depth) => DefenseEveryLevels > 0 ? (depth - 1) / DefenseEveryLevels : 0;
}

/// <summary>Everything affixes.json defines: tiers above common, the affix pool, legendary specials per slot, rare names.</summary>
public sealed record AffixRules(
    IReadOnlyList<RarityTierDef> Tiers,
    IReadOnlyList<AffixDef> Pool,
    IReadOnlyDictionary<ItemCategory, IReadOnlyList<string>> Specials,
    ItemNameWords Names);

/// <summary>A lair sends out a new squad (by template name) every Every turns, while its faction has fewer than Max roaming squads.</summary>
public sealed record MusterDef(string Squad, int Every, int Max);

/// <summary>Remains placed at level creation: Count corpses of a monster kind.</summary>
public sealed record CorpseEntry(string Monster, int Count);

public sealed record PlaceZone(string Name, string Faction, int[] Area);
public sealed record PlaceMember(string Monster, int[] At);

/// <summary>A squad placed by hand: it waits at Home and goes at most Leash steps from it.</summary>
public sealed record PlaceGroup(string Name, string Faction, int Leash, int[] Home, IReadOnlyList<PlaceMember> Members);
public sealed record PlaceLoot(string Table, int[] At);

/// <summary>A hand-drawn map (surface.json): terrain as rows of characters, plus who and what is on it.</summary>
public sealed record PlaceDef(string Name, IReadOnlyDictionary<string, Map.Tile> Legend, IReadOnlyList<string> Rows, int[] Start,
    IReadOnlyList<PlaceZone> Zones, IReadOnlyList<PlaceGroup> Groups, IReadOnlyList<PlaceLoot> Loot)
{
    public int Width => Rows[0].Length;
    public int Height => Rows.Count;
    public Map.Tile TileAt(int x, int y) => Legend[Rows[y][x].ToString()];
}

/// <summary>
/// All game content loaded from the data directory (factions.json, monsters.json, spawns.json),
/// validated so broken references fail at startup with a clear message.
/// </summary>
public sealed class ContentDatabase
{
    public IReadOnlyDictionary<string, Faction> Factions { get; }
    public FactionRelations Relations { get; }
    public IReadOnlyDictionary<string, MonsterDef> Monsters { get; }
    public IReadOnlyList<SquadTemplateDef> SquadTemplates { get; }
    public double RoomOccupancy { get; }
    public double FloorItemChance { get; }

    /// <summary>Unique squads with a home: each takes the free room farthest from the start.</summary>
    public IReadOnlyList<SquadTemplateDef> Lairs { get; }

    /// <summary>No squad spawns in a room any of whose cells is closer than this (walking) to the player's start.</summary>
    public int SafeStartDistance { get; }
    public IReadOnlyDictionary<string, ItemDef> Items { get; }
    public IReadOnlyList<EnchantmentWeight> Enchantments { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<LootEntry>> LootTables { get; }
    public PlayerDef Player { get; }
    public XpRules Xp => Player.Xp ?? new XpRules();
    public CombatRules Combat { get; }
    public DungeonDef Dungeon { get; }

    /// <summary>The hand-drawn surface map (surface.json).</summary>
    public PlaceDef Surface { get; }
    public IReadOnlyList<AttributeDef> Attributes { get; }
    public AttributeRaiseRules AttributeRaise { get; }
    public IReadOnlyList<SkillTreeDef> SkillTrees { get; }
    public IReadOnlyDictionary<string, SkillDef> Skills { get; }

    /// <summary>Rarity tiers, affixes, legendary specials and rare names (affixes.json).</summary>
    public AffixRules Affixes { get; }

    /// <summary>Special abilities skills and legendary items can grant; each is implemented in the rules.</summary>
    public static readonly IReadOnlyList<string> Specials = ["cleave", "lifesteal", "piercing", "thorns"];

    public Faction PlayerFaction => Factions[Faction.PlayerId];

    private static readonly Lazy<ContentDatabase> DefaultContent =
        new(() => Load(Path.Combine(AppContext.BaseDirectory, "data")));

    /// <summary>Content from the "data" folder next to the running program.</summary>
    public static ContentDatabase Default => DefaultContent.Value;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private ContentDatabase(
        Dictionary<string, Faction> factions, FactionRelations relations,
        Dictionary<string, MonsterDef> monsters, SpawnFile spawns,
        Dictionary<string, ItemDef> items, ItemFile itemFile, PlayerDef player,
        List<AttributeDef> attributes, AttributeRaiseRules raise, List<SkillTreeDef> trees, CombatRules combat,
        DungeonDef dungeon, AffixRules affixes, PlaceDef surface)
    {
        Surface = surface;
        Affixes = affixes;
        Combat = combat;
        Dungeon = dungeon;
        Attributes = attributes;
        AttributeRaise = raise;
        SkillTrees = trees;
        Skills = trees.SelectMany(t => t.Skills).ToDictionary(s => s.Id);
        Factions = factions;
        Relations = relations;
        Monsters = monsters;
        SquadTemplates = spawns.Squads;
        Lairs = spawns.Lairs ?? [];
        RoomOccupancy = spawns.RoomOccupancy;
        FloorItemChance = spawns.FloorItemChance;
        SafeStartDistance = spawns.SafeStartDistance;
        Items = items;
        Enchantments = itemFile.Enchantments;
        LootTables = itemFile.LootTables.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<LootEntry>)kv.Value);
        Player = player;
    }

    public static ContentDatabase Load(string directory)
    {
        var factionFile = Read<FactionFile>(directory, "factions.json");
        var monsterFile = Read<MonsterFile>(directory, "monsters.json");
        var spawnFile = Read<SpawnFile>(directory, "spawns.json");
        var itemFile = Read<ItemFile>(directory, "items.json");
        var player = Read<PlayerDef>(directory, "player.json");
        var (attributes, raise) = LoadAttributes(Read<AttributeFile>(directory, "attributes.json"));
        var trees = LoadSkills(Read<SkillFile>(directory, "skills.json"));
        var combat = Read<CombatRules>(directory, "combat.json");
        var dungeon = Read<DungeonDef>(directory, "dungeon.json");
        if (dungeon.Levels < 1 || dungeon.MonsterHpPerLevel < 0 || dungeon.XpPerLevel < 0
            || dungeon.AttackEveryLevels < 0 || dungeon.AccuracyEveryLevels < 0 || dungeon.DefenseEveryLevels < 0)
            throw Error("dungeon.json", "levels must be >= 1 and the per-level growth can't be negative");
        if (combat.MinHitChance < 0 || combat.MaxHitChance > 100 || combat.MinHitChance > combat.MaxHitChance
            || combat.HitChancePerPoint < 0 || combat.MinDamage < 0)
            throw Error("combat.json", "hit chances must satisfy 0 <= min <= max <= 100; per-point and minDamage >= 0");
        foreach (var id in player.Attributes?.Keys ?? Enumerable.Empty<string>())
            if (attributes.All(a => a.Id != id))
                throw Error("player.json", $"unknown attribute '{id}'");
        if (player.Xp is { SightFraction: < 0 } or { NewZone: < 0 })
            throw Error("player.json", "xp rewards can't be negative");

        var items = new Dictionary<string, ItemDef>();
        foreach (var item in itemFile.Items)
            if (!items.TryAdd(item.Id, item))
                throw Error("items.json", $"duplicate item '{item.Id}'");
        foreach (var (table, entries) in itemFile.LootTables)
            foreach (var entry in entries)
                if (entry.Item is not null && !items.ContainsKey(entry.Item))
                    throw Error("items.json", $"loot table '{table}' uses unknown item '{entry.Item}'");
        if (itemFile.Enchantments.Where(e => e.MinDepth <= 1).Sum(e => e.Weight) <= 0)
            throw Error("items.json", "enchantments need a positive total weight at depth 1");
        foreach (var (table, entries) in itemFile.LootTables)
            if (entries.Sum(e => e.Weight) <= 0)
                throw Error("items.json", $"loot table '{table}' needs a positive total weight");

        if (player.RegenInterval < 0)
            throw Error("player.json", "regenInterval can't be negative");
        foreach (var k in player.Kit)
        {
            if (!items.TryGetValue(k.Item, out var def))
                throw Error("player.json", $"kit uses unknown item '{k.Item}'");
            if (k.Equip && !def.IsEquipment)
                throw Error("player.json", $"kit item '{k.Item}' can't be equipped");
        }
        if (spawnFile.FloorItemChance > 0 && !itemFile.LootTables.ContainsKey("floor"))
            throw Error("items.json", "floorItemChance is set but there is no \"floor\" loot table");

        var factions = new Dictionary<string, Faction>();
        foreach (var f in factionFile.Factions)
            if (!factions.TryAdd(f.Id, new Faction(f.Id, f.Name) { Raid = f.Raid }))
                throw Error("factions.json", $"duplicate faction '{f.Id}'");
        if (!factions.ContainsKey(Faction.PlayerId))
            throw Error("factions.json", $"missing the '{Faction.PlayerId}' faction");

        var relations = new FactionRelations();
        foreach (var r in factionFile.Relations)
            relations.Set(FactionOrThrow(factions, r.A, "factions.json"), FactionOrThrow(factions, r.B, "factions.json"), r.Relation);

        foreach (var f in factions.Values.Where(f => f.Raid is not null))
        {
            var target = FactionOrThrow(factions, f.Raid!.Against, "factions.json");
            if (f.Raid.Every <= 0)
                throw Error("factions.json", $"faction '{f.Id}': raid \"every\" must be > 0");
            if (!relations.AreHostile(f, target))
                throw Error("factions.json", $"faction '{f.Id}' raids '{target.Id}', which it isn't hostile to");
        }

        var monsters = new Dictionary<string, MonsterDef>();
        foreach (var m in monsterFile.Monsters)
        {
            FactionOrThrow(factions, m.Faction, "monsters.json");
            if (m.MaxHp <= 0)
                throw Error("monsters.json", $"monster '{m.Id}' needs maxHp > 0");
            if (m.Loot is not null && !itemFile.LootTables.ContainsKey(m.Loot))
                throw Error("monsters.json", $"monster '{m.Id}' uses unknown loot table '{m.Loot}'");
            if (m.Speed <= 0)
                throw Error("monsters.json", $"monster '{m.Id}' needs speed > 0");
            if (m.Role == SquadRole.Necromancer && m.Raise is null)
                throw Error("monsters.json", $"necromancer '{m.Id}' needs a \"raise\" block");
            if (m.Xp < 0)
                throw Error("monsters.json", $"monster '{m.Id}' can't give negative xp");
            if (!monsters.TryAdd(m.Id, m))
                throw Error("monsters.json", $"duplicate monster '{m.Id}'");
        }
        foreach (var m in monsters.Values)
            if (m.RisesAs is not null && !monsters.ContainsKey(m.RisesAs))
                throw Error("monsters.json", $"monster '{m.Id}' rises as unknown monster '{m.RisesAs}'");

        foreach (var s in spawnFile.Squads.Concat(spawnFile.Lairs ?? []))
        {
            FactionOrThrow(factions, s.Faction, "spawns.json");
            foreach (var member in s.Members)
            {
                if (!monsters.TryGetValue(member.Monster, out var def))
                    throw Error("spawns.json", $"squad '{s.Name}' uses unknown monster '{member.Monster}'");
                if (def.Faction != s.Faction)
                    throw Error("spawns.json", $"squad '{s.Name}' ({s.Faction}) includes '{def.Id}' of faction '{def.Faction}'");
                if (member.Min < 0 || member.Max < member.Min)
                    throw Error("spawns.json", $"squad '{s.Name}': bad min/max for '{member.Monster}'");
            }
            if (s.Architecture is not null && !Map.LairBuilder.Styles.Contains(s.Architecture))
                throw Error("spawns.json", $"squad '{s.Name}' uses unknown architecture '{s.Architecture}'");
            if (s.MinDepth < 1 || s.MaxDepth < s.MinDepth)
                throw Error("spawns.json", $"squad '{s.Name}': needs 1 <= minDepth <= maxDepth");
            if (s.Leash < 0)
                throw Error("spawns.json", $"squad '{s.Name}': leash can't be negative");
            if (s.Muster is { } muster)
            {
                var mustered = spawnFile.Squads.FirstOrDefault(t => t.Name == muster.Squad)
                    ?? throw Error("spawns.json", $"lair '{s.Name}' musters unknown squad '{muster.Squad}'");
                if (mustered.Faction != s.Faction)
                    throw Error("spawns.json", $"lair '{s.Name}' musters '{muster.Squad}' of another faction");
                if (muster.Every <= 0 || muster.Max <= 0)
                    throw Error("spawns.json", $"lair '{s.Name}': muster needs every > 0 and max > 0");
            }
            foreach (var c in s.Corpses ?? [])
                if (!monsters.ContainsKey(c.Monster))
                    throw Error("spawns.json", $"squad '{s.Name}' lays out corpses of unknown monster '{c.Monster}'");
        }

        var surface = Read<PlaceDef>(directory, "surface.json");
        ValidateSurface(surface, factions, monsters, itemFile);

        var affixes = LoadAffixes(Read<AffixFile>(directory, "affixes.json"), dungeon.Levels);
        return new ContentDatabase(factions, relations, monsters, spawnFile, items, itemFile, player,
            attributes, raise, trees, combat, dungeon, affixes, surface);
    }

    private static void ValidateSurface(PlaceDef place, Dictionary<string, Faction> factions,
        Dictionary<string, MonsterDef> monsters, ItemFile itemFile)
    {
        const string name = "surface.json";
        if (place.Rows.Count == 0 || place.Rows[0].Length == 0 || place.Rows.Any(r => r.Length != place.Rows[0].Length))
            throw Error(name, "rows must be non-empty and all the same length");
        foreach (var c in place.Rows.SelectMany(r => r).Distinct())
            if (!place.Legend.ContainsKey(c.ToString()))
                throw Error(name, $"character '{c}' in rows isn't in the legend");

        int caves = place.Rows.SelectMany(r => r).Count(c => place.Legend[c.ToString()] == Map.Tile.CaveMouth);
        if (caves != 1)
            throw Error(name, $"needs exactly one cave mouth, found {caves}");

        void CheckCell(int[] at, string what)
        {
            if (at is not [var x, var y] || x < 0 || y < 0 || x >= place.Width || y >= place.Height)
                throw Error(name, $"{what}: position must be [x, y] inside the map");
            if (!Map.GameMap.IsWalkable(place.TileAt(x, y)))
                throw Error(name, $"{what}: position [{x}, {y}] isn't walkable");
        }

        CheckCell(place.Start, "start");
        foreach (var zone in place.Zones)
        {
            FactionOrThrow(factions, zone.Faction, name);
            if (zone.Area is not [var x, var y, var w, var h] || x < 0 || y < 0 || w <= 0 || h <= 0
                || x + w > place.Width || y + h > place.Height)
                throw Error(name, $"zone '{zone.Name}': area must be [x, y, width, height] inside the map");
        }
        foreach (var group in place.Groups)
        {
            FactionOrThrow(factions, group.Faction, name);
            if (group.Leash < 0 || group.Members.Count == 0)
                throw Error(name, $"group '{group.Name}': needs members and a leash >= 0");
            CheckCell(group.Home, $"group '{group.Name}' home");
            foreach (var member in group.Members)
            {
                if (!monsters.TryGetValue(member.Monster, out var def))
                    throw Error(name, $"group '{group.Name}' uses unknown monster '{member.Monster}'");
                if (def.Faction != group.Faction)
                    throw Error(name, $"group '{group.Name}' ({group.Faction}) includes '{def.Id}' of faction '{def.Faction}'");
                CheckCell(member.At, $"group '{group.Name}' member '{member.Monster}'");
            }
        }
        foreach (var loot in place.Loot)
        {
            if (!itemFile.LootTables.ContainsKey(loot.Table))
                throw Error(name, $"loot uses unknown table '{loot.Table}'");
            CheckCell(loot.At, $"loot '{loot.Table}'");
        }
    }

    private static AffixRules LoadAffixes(AffixFile file, int levels)
    {
        const string name = "affixes.json";
        ItemCategory[] slots = [ItemCategory.Weapon, ItemCategory.Bow, ItemCategory.Armor];

        var tiers = new List<RarityTierDef>();
        foreach (var t in file.Tiers)
        {
            if (t.Rarity is not (Rarity.Magic or Rarity.Rare or Rarity.Legendary))
                throw Error(name, $"tier '{t.Rarity}' can't be generated (only magic, rare, legendary)");
            if (tiers.Any(x => x.Rarity == t.Rarity))
                throw Error(name, $"duplicate tier '{t.Rarity}'");
            if (t.Affixes is not [var min, var max] || min < 0 || max < min)
                throw Error(name, $"tier '{t.Rarity}': \"affixes\" must be [min, max] with 0 <= min <= max");
            if (t.Chance < 0 || t.ChancePerLevel < 0)
                throw Error(name, $"tier '{t.Rarity}': chances can't be negative");
            tiers.Add(new RarityTierDef(t.Rarity, t.Chance, t.ChancePerLevel, min, max, t.MinDepth, t.Special));
        }
        if (tiers.Sum(t => t.ChanceAt(levels)) > 100)
            throw Error(name, $"tier chances add up to more than 100% by depth {levels}");

        var affixes = new List<AffixDef>();
        foreach (var a in file.Affixes)
        {
            var stat = Stats(new() { [a.Stat] = 0 }, name, $"affix '{a.Id}'").Keys.Single();
            if (affixes.Any(x => x.Id == a.Id))
                throw Error(name, $"duplicate affix '{a.Id}'");
            if (a.Min > a.Max || a.Weight <= 0 || a.PerLevel < 0)
                throw Error(name, $"affix '{a.Id}': needs min <= max, weight > 0 and perLevel >= 0");
            if (a.Slots.Count == 0 || a.Slots.Any(c => !slots.Contains(c)))
                throw Error(name, $"affix '{a.Id}': slots must be some of weapon, bow, armor");
            affixes.Add(new AffixDef(a.Id, a.Kind, a.Name, a.Slots, stat, a.Min, a.Max, a.PerLevel, a.Weight, a.MinDepth));
        }
        foreach (var slot in slots)
            if (!affixes.Any(a => a.Slots.Contains(slot) && a.MinDepth <= 1))
                throw Error(name, $"no affix for {slot.ToString().ToLowerInvariant()} at depth 1");

        var specials = new Dictionary<ItemCategory, IReadOnlyList<string>>();
        foreach (var (key, list) in file.Specials)
        {
            if (!Enum.TryParse<ItemCategory>(key, ignoreCase: true, out var slot) || !slots.Contains(slot))
                throw Error(name, $"specials: unknown slot '{key}'");
            foreach (var sp in list.Where(sp => !Specials.Contains(sp)))
                throw Error(name, $"specials: unknown special '{sp}' (known: {string.Join(", ", Specials)})");
            specials[slot] = list;
        }

        var second = new Dictionary<ItemCategory, IReadOnlyList<string>>();
        foreach (var (key, list) in file.Names.Second)
        {
            if (!Enum.TryParse<ItemCategory>(key, ignoreCase: true, out var slot) || !slots.Contains(slot))
                throw Error(name, $"names: unknown slot '{key}'");
            second[slot] = list;
        }
        if (file.Names.First.Count == 0 || slots.Any(s => second.GetValueOrDefault(s) is not { Count: > 0 }))
            throw Error(name, "names need first words and second words for weapon, bow and armor");
        foreach (var t in tiers.Where(t => t.Special))
            foreach (var slot in slots)
                if (specials.GetValueOrDefault(slot) is not { Count: > 0 })
                    throw Error(name, $"tier '{t.Rarity}' carries a special, but none is listed for {slot.ToString().ToLowerInvariant()}");

        return new AffixRules(tiers, affixes, specials, new ItemNameWords(file.Names.First, second));
    }

    private static (List<AttributeDef>, AttributeRaiseRules) LoadAttributes(AttributeFile file)
    {
        const string name = "attributes.json";
        var list = new List<AttributeDef>();
        foreach (var a in file.Attributes)
        {
            if (list.Any(x => x.Id == a.Id))
                throw Error(name, $"duplicate attribute '{a.Id}'");
            list.Add(new AttributeDef(a.Id, a.Name, a.Description ?? "", Stats(a.PerModifier, name, $"attribute '{a.Id}'")));
        }
        if (file.Raise.BaseCost <= 0 || file.Raise.CostPerRaise < 0 || file.Raise.MaxScore <= Character.BaseScore)
            throw Error(name, "raise needs baseCost > 0, costPerRaise >= 0 and maxScore > 10");
        return (list, file.Raise);
    }

    private static List<SkillTreeDef> LoadSkills(SkillFile file)
    {
        const string name = "skills.json";
        var trees = new List<SkillTreeDef>();
        var ids = new HashSet<string>();
        foreach (var t in file.Trees)
        {
            var skills = new List<SkillDef>();
            foreach (var s in t.Skills)
            {
                if (!ids.Add(s.Id))
                    throw Error(name, $"duplicate skill '{s.Id}'");
                if (s.Ranks <= 0 || s.Cost.Count != s.Ranks || s.Cost.Any(c => c <= 0))
                    throw Error(name, $"skill '{s.Id}' needs ranks > 0 and one positive cost per rank");
                if (s.Special is not null && !Specials.Contains(s.Special))
                    throw Error(name, $"skill '{s.Id}' grants unknown special '{s.Special}' (known: {string.Join(", ", Specials)})");
                skills.Add(new SkillDef(s.Id, s.Name, t.Id, s.Description ?? "", s.Ranks, s.Cost, s.Requires ?? [],
                    Stats(s.PerRank, name, $"skill '{s.Id}'"), s.Special));
            }
            trees.Add(new SkillTreeDef(t.Id, t.Name, skills));
        }
        foreach (var s in trees.SelectMany(t => t.Skills))
            foreach (var r in s.Requires)
                if (!ids.Contains(r))
                    throw Error(name, $"skill '{s.Id}' requires unknown skill '{r}'");
        return trees;
    }

    /// <summary>Parses { "meleeAttack": 1 } into stat bonuses, naming the bad key on error.</summary>
    private static Dictionary<Stat, double> Stats(Dictionary<string, double>? raw, string file, string owner)
    {
        var result = new Dictionary<Stat, double>();
        foreach (var (key, value) in raw ?? [])
        {
            if (!Enum.TryParse<Stat>(key, ignoreCase: true, out var stat) || int.TryParse(key, out _))
                throw Error(file, $"{owner} uses unknown stat '{key}' (known: {string.Join(", ", Enum.GetNames<Stat>().Select(Camel))})");
            result[stat] = value;
        }
        return result;
    }

    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    private static T Read<T>(string directory, string file)
    {
        var path = Path.Combine(directory, file);
        if (!File.Exists(path))
            throw new InvalidDataException($"Content file not found: {path}");
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
                   ?? throw Error(file, "file is empty");
        }
        catch (JsonException e)
        {
            throw Error(file, e.Message);
        }
    }

    private static Faction FactionOrThrow(Dictionary<string, Faction> factions, string id, string file) =>
        factions.TryGetValue(id, out var f) ? f : throw Error(file, $"unknown faction '{id}'");

    private static InvalidDataException Error(string file, string message) => new($"{file}: {message}");

    private sealed record FactionFile(List<FactionEntry> Factions, List<RelationEntry> Relations);
    private sealed record FactionEntry(string Id, string Name, RaidPlan? Raid = null);
    private sealed record RelationEntry(string A, string B, Relation Relation);
    private sealed record MonsterFile(List<MonsterDef> Monsters);
    private sealed record SpawnFile(double RoomOccupancy, double FloorItemChance, List<SquadTemplateDef> Squads,
        int SafeStartDistance = 0, List<SquadTemplateDef>? Lairs = null);
    private sealed record AttributeFile(List<AttributeEntry> Attributes, AttributeRaiseRules Raise);
    private sealed record AttributeEntry(string Id, string Name, string? Description, Dictionary<string, double>? PerModifier);
    private sealed record SkillFile(List<SkillTreeEntry> Trees);
    private sealed record SkillTreeEntry(string Id, string Name, List<SkillEntry> Skills);
    private sealed record SkillEntry(string Id, string Name, string? Description, int Ranks, List<int> Cost,
        List<string>? Requires, Dictionary<string, double>? PerRank, string? Special);
    private sealed record AffixFile(List<TierEntry> Tiers, List<AffixEntry> Affixes,
        Dictionary<string, List<string>> Specials, NamesEntry Names);
    private sealed record TierEntry(Rarity Rarity, double Chance, double ChancePerLevel, int[] Affixes,
        int MinDepth = 1, bool Special = false);
    private sealed record AffixEntry(string Id, AffixKind Kind, string Name, List<ItemCategory> Slots, string Stat,
        int Min, int Max, double PerLevel = 0, int Weight = 1, int MinDepth = 1);
    private sealed record NamesEntry(List<string> First, Dictionary<string, List<string>> Second);
    private sealed record ItemFile(
        List<ItemDef> Items, List<EnchantmentWeight> Enchantments, Dictionary<string, List<LootEntry>> LootTables);
}
