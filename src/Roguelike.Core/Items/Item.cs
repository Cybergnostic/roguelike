namespace Roguelike.Core.Items;

public enum ItemCategory
{
    Weapon,
    Bow,
    Armor,
    Potion,
    /// <summary>Money: picked up into the purse (<see cref="Entities.Actor.Gold"/>), never carried in the pack.</summary>
    Coins,
    /// <summary>Something carried for its own sake: an errand's object. It can't be used or worn.</summary>
    Relic,
}

/// <summary>
/// Loot tiers, worst to best (affixes.json). Common, Magic, Rare and Legendary are generated;
/// Unique and Artifact are planned (see docs/PROJECT.md §8).
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
    int Heal = 0,
    int MinDepth = 1)
{
    public bool IsEquipment => Category is ItemCategory.Weapon or ItemCategory.Bow or ItemCategory.Armor;
    public bool IsStackable => Category == ItemCategory.Potion;
}

/// <summary>
/// One concrete item (or stack). <see cref="Enchantment"/> is the classic +X (negative = cursed),
/// independent of <see cref="Rarity"/>: it adds to the item's attack (weapons, bows) or defense (armor).
/// Magic and better items carry <see cref="Affixes"/> that add to derived stats while worn, and
/// legendaries a <see cref="Special"/>. Rare and legendary items have <see cref="NameWords"/>
/// (from affixes.json) for the front end to name them by.
/// </summary>
public sealed class Item(int id, ItemDef def)
{
    public int Id { get; } = id;
    public ItemDef Def { get; } = def;
    public int Enchantment { get; init; }
    public Rarity Rarity { get; init; } = Rarity.Common;
    public int Count { get; set; } = 1;
    public IReadOnlyList<AffixRoll> Affixes { get; init; } = [];
    public string? Special { get; init; }
    public IReadOnlyList<string>? NameWords { get; init; }

    /// <summary>What this item's properties add to a stat while it's worn.</summary>
    public int Bonus(Progression.Stat stat)
    {
        int sum = 0;
        foreach (var a in Affixes)
            if (a.Def.Stat == stat)
                sum += a.Value;
        return sum;
    }

    public int AttackBonus => Def.Category is ItemCategory.Weapon or ItemCategory.Bow ? Def.Attack + Enchantment : 0;
    public int DefenseBonus => Def.Category == ItemCategory.Armor ? Def.Defense + Enchantment : 0;

    /// <summary>Whether this can merge into <paramref name="other"/>'s stack.</summary>
    public bool StacksWith(Item other) =>
        Def.IsStackable && other.Def == Def && other.Enchantment == Enchantment && other.Rarity == Rarity;
}
