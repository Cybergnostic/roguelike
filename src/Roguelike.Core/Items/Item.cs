namespace Roguelike.Core.Items;

public enum ItemCategory
{
    Weapon,
    Bow,
    Armor,
    Potion,
}

/// <summary>
/// Loot tiers, worst to best. Only Common is generated so far; the rest are planned
/// (see docs/PROJECT.md §8): Magic green, Rare orange, Legendary red, Unique gold, Artifact violet.
/// </summary>
public enum Rarity
{
    Common,
    Magic,
    Rare,
    Legendary,
    Unique,
    Artifact,
}

/// <summary>An item type from items.json. Names live in data so the core never builds text.</summary>
public sealed record ItemDef(
    string Id,
    string Name,
    string? Plural,
    ItemCategory Category,
    int Attack = 0,
    int Defense = 0,
    int Range = 0,
    int Heal = 0)
{
    public bool IsEquipment => Category is ItemCategory.Weapon or ItemCategory.Bow or ItemCategory.Armor;
    public bool IsStackable => Category == ItemCategory.Potion;
}

/// <summary>
/// One concrete item (or stack). <see cref="Enchantment"/> is the classic +X (negative = cursed),
/// independent of <see cref="Rarity"/>: it adds to the item's attack (weapons, bows) or defense (armor).
/// </summary>
public sealed class Item(int id, ItemDef def)
{
    public int Id { get; } = id;
    public ItemDef Def { get; } = def;
    public int Enchantment { get; init; }
    public Rarity Rarity { get; init; } = Rarity.Common;
    public int Count { get; set; } = 1;

    public int AttackBonus => Def.Category is ItemCategory.Weapon or ItemCategory.Bow ? Def.Attack + Enchantment : 0;
    public int DefenseBonus => Def.Category == ItemCategory.Armor ? Def.Defense + Enchantment : 0;

    /// <summary>Whether this can merge into <paramref name="other"/>'s stack.</summary>
    public bool StacksWith(Item other) =>
        Def.IsStackable && other.Def == Def && other.Enchantment == Enchantment && other.Rarity == Rarity;
}
