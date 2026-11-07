using System.Text.Json;
using System.Text.Json.Serialization;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Items;

namespace Roguelike.Core.Content;

public sealed record MonsterDef(
    string Id, string Faction, SquadRole Role,
    int MaxHp, int Attack, int Defense, int Range = 0, bool Fearless = false, string? Loot = null);

/// <summary>One weighted loot table line; a null item means "nothing".</summary>
public sealed record LootEntry(string? Item, int Weight);

public sealed record EnchantmentWeight(int Value, int Weight);

public sealed record KitEntry(string Item, int Count = 1, bool Equip = false, int Enchantment = 0);

public sealed record PlayerDef(int MaxHp, int Attack, int Defense, int InventoryCapacity, IReadOnlyList<KitEntry> Kit);

public sealed record SquadMemberDef(string Monster, int Min, int Max, double Chance = 1.0);

public sealed record SquadTemplateDef(string Name, string Faction, int Weight, IReadOnlyList<SquadMemberDef> Members);

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
    public IReadOnlyDictionary<string, ItemDef> Items { get; }
    public IReadOnlyList<EnchantmentWeight> Enchantments { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<LootEntry>> LootTables { get; }
    public PlayerDef Player { get; }

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
        Dictionary<string, ItemDef> items, ItemFile itemFile, PlayerDef player)
    {
        Factions = factions;
        Relations = relations;
        Monsters = monsters;
        SquadTemplates = spawns.Squads;
        RoomOccupancy = spawns.RoomOccupancy;
        FloorItemChance = spawns.FloorItemChance;
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

        var items = new Dictionary<string, ItemDef>();
        foreach (var item in itemFile.Items)
            if (!items.TryAdd(item.Id, item))
                throw Error("items.json", $"duplicate item '{item.Id}'");
        foreach (var (table, entries) in itemFile.LootTables)
            foreach (var entry in entries)
                if (entry.Item is not null && !items.ContainsKey(entry.Item))
                    throw Error("items.json", $"loot table '{table}' uses unknown item '{entry.Item}'");
        if (itemFile.Enchantments.Sum(e => e.Weight) <= 0)
            throw Error("items.json", "enchantments need a positive total weight");

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
            if (!factions.TryAdd(f.Id, new Faction(f.Id, f.Name)))
                throw Error("factions.json", $"duplicate faction '{f.Id}'");
        if (!factions.ContainsKey(Faction.PlayerId))
            throw Error("factions.json", $"missing the '{Faction.PlayerId}' faction");

        var relations = new FactionRelations();
        foreach (var r in factionFile.Relations)
            relations.Set(FactionOrThrow(factions, r.A, "factions.json"), FactionOrThrow(factions, r.B, "factions.json"), r.Relation);

        var monsters = new Dictionary<string, MonsterDef>();
        foreach (var m in monsterFile.Monsters)
        {
            FactionOrThrow(factions, m.Faction, "monsters.json");
            if (m.MaxHp <= 0)
                throw Error("monsters.json", $"monster '{m.Id}' needs maxHp > 0");
            if (m.Loot is not null && !itemFile.LootTables.ContainsKey(m.Loot))
                throw Error("monsters.json", $"monster '{m.Id}' uses unknown loot table '{m.Loot}'");
            if (!monsters.TryAdd(m.Id, m))
                throw Error("monsters.json", $"duplicate monster '{m.Id}'");
        }

        foreach (var s in spawnFile.Squads)
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
        }

        return new ContentDatabase(factions, relations, monsters, spawnFile, items, itemFile, player);
    }

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
    private sealed record FactionEntry(string Id, string Name);
    private sealed record RelationEntry(string A, string B, Relation Relation);
    private sealed record MonsterFile(List<MonsterDef> Monsters);
    private sealed record SpawnFile(double RoomOccupancy, double FloorItemChance, List<SquadTemplateDef> Squads);
    private sealed record ItemFile(
        List<ItemDef> Items, List<EnchantmentWeight> Enchantments, Dictionary<string, List<LootEntry>> LootTables);
}
