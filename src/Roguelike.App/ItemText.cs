using Roguelike.Core.Entities;
using Roguelike.Core.Items;

namespace Roguelike.App;

/// <summary>Item names for display: "+1 short sword", "2 potions of healing", "a dagger".</summary>
public static class ItemText
{
    /// <summary>Name without article: "+2 long bow", "3 potions of healing".</summary>
    public static string Name(Item item)
    {
        var name = item.Count > 1 ? $"{item.Count} {item.Def.Plural ?? item.Def.Name + "s"}" : item.Def.Name;
        return item.Def.IsEquipment && item.Enchantment != 0 ? $"{item.Enchantment:+0;-0} {name}" : name;
    }

    /// <summary>Name with an indefinite article when singular: "a +1 dagger", "an iron key".</summary>
    public static string AName(Item item)
    {
        var name = Name(item);
        if (item.Count > 1)
            return name;
        return ("aeiou".Contains(char.ToLowerInvariant(name[0])) ? "an " : "a ") + name;
    }

    public static string TheName(Item item) => item.Count > 1 ? Name(item) : "the " + Name(item);

    /// <summary>Suffix showing how an item is equipped, or empty.</summary>
    public static string EquippedTag(Actor actor, Item item)
    {
        if (actor.Equipment is not { } eq || !eq.IsEquipped(item))
            return "";
        return item.Def.Category switch
        {
            ItemCategory.Weapon => " (wielded)",
            ItemCategory.Bow => " (readied)",
            ItemCategory.Armor => " (worn)",
            _ => "",
        };
    }

    /// <summary>Short stat summary for menus: "atk 4", "def 3", "atk 3, range 6", "heals 15".</summary>
    public static string Stats(Item item) => item.Def.Category switch
    {
        ItemCategory.Weapon => $"atk {item.AttackBonus}",
        ItemCategory.Bow => $"atk {item.AttackBonus}, range {item.Def.Range}",
        ItemCategory.Armor => $"def {item.DefenseBonus}",
        ItemCategory.Potion => $"heals {item.Def.Heal}",
        _ => "",
    };
}
