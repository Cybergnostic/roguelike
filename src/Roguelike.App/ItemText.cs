using Roguelike.Core.Entities;
using Roguelike.Core.Items;
using Roguelike.Core.Progression;

namespace Roguelike.App;

/// <summary>
/// Item names for display: "+1 short sword", "2 potions of healing", "a keen dagger",
/// "leather armor of the bear", and for rare and legendary items a proper name: "Grim Fang (+1 long sword)".
/// </summary>
public static class ItemText
{
    /// <summary>Name without article: "+2 long bow", "3 potions of healing", "Raven Wing (long bow)".</summary>
    public static string Name(Item item)
    {
        var name = item.Count > 1 ? $"{item.Count} {item.Def.Plural ?? item.Def.Name + "s"}" : item.Def.Name;
        if (item.Rarity == Rarity.Magic && item.Affixes is [var affix])
            name = affix.Def.Kind == AffixKind.Prefix ? $"{affix.Def.Name} {name}" : $"{name} {affix.Def.Name}";
        if (item.Def.IsEquipment && item.Enchantment != 0)
            name = $"{item.Enchantment:+0;-0} {name}";
        return IsNamed(item) ? $"{string.Join(' ', item.NameWords!)} ({name})" : name;
    }

    /// <summary>Rare and legendary items go by their own name, without "a" or "the".</summary>
    private static bool IsNamed(Item item) => item.NameWords is { Count: > 0 };

    /// <summary>Name with an indefinite article when singular: "a +1 dagger", "an iron key".</summary>
    public static string AName(Item item)
    {
        var name = Name(item);
        if (item.Count > 1 || IsNamed(item))
            return name;
        return ("aeiou".Contains(char.ToLowerInvariant(name[0])) ? "an " : "a ") + name;
    }

    public static string TheName(Item item) => item.Count > 1 || IsNamed(item) ? Name(item) : "the " + Name(item);

    /// <summary>Suffix showing how an item is equipped, or empty.</summary>
    public static string EquippedTag(Actor actor, Item item)
    {
        if (actor.Equipment is not { } eq || !eq.IsEquipped(item))
            return "";
        return item.Def.Category switch
        {
            ItemCategory.Weapon when item == eq.OffHand => " (off hand)",
            ItemCategory.Weapon => " (wielded)",
            ItemCategory.Bow => " (readied)",
            ItemCategory.Armor => " (worn)",
            _ => "",
        };
    }

    /// <summary>
    /// Short stat summary for menus: "atk 4", "def 3", "atk 3, range 6", "heals 15", then any
    /// properties and special: "atk 6; +2 acc, +7 hp; lifesteal".
    /// </summary>
    public static string Stats(Item item)
    {
        var basic = item.Def.Category switch
        {
            ItemCategory.Weapon => $"atk {item.AttackBonus}{Pace(item)}{(item.Def.Accuracy != 0 ? $", {item.Def.Accuracy:+0;-0} acc" : "")}{(item.Def.DualWield ? ", dual" : "")}",
            ItemCategory.Bow => $"atk {item.AttackBonus}, range {item.Def.Range}{Pace(item)}",
            ItemCategory.Armor => $"def {item.DefenseBonus}{(item.Def.Noise > 0 ? $", noise {item.Def.Noise}" : "")}",
            ItemCategory.Potion => $"heals {item.Def.Heal}",
            _ => "",
        };
        if (item.Affixes.Count > 0)
            basic += "; " + string.Join(", ", item.Affixes.Select(a => StatText(a.Def.Stat, a.Value)));
        return item.Special is { } special ? $"{basic}; {special}" : basic;
    }

    /// <summary>", quick" or ", slow", with how long a blow takes; nothing for an ordinary weapon.</summary>
    private static string Pace(Item item) => item.Def.AttackTime switch
    {
        < 100 => $", quick ({item.Def.AttackTime})",
        > 100 => $", slow ({item.Def.AttackTime})",
        _ => "",
    };

    /// <summary>"+2 acc", "+5 hp", "+15% potions".</summary>
    public static string StatText(Stat stat, int value) => stat switch
    {
        Stat.MaxHp => $"{value:+0;-0} hp",
        Stat.MeleeAttack => $"{value:+0;-0} melee",
        Stat.RangedAttack => $"{value:+0;-0} ranged",
        Stat.Defense => $"{value:+0;-0} def",
        Stat.Range => $"{value:+0;-0} range",
        Stat.Regen => $"{value:+0;-0} regen",
        Stat.PotionHealing => $"{value:+0;-0}% potions",
        Stat.XpGain => $"{value:+0;-0}% xp",
        Stat.Accuracy => $"{value:+0;-0} acc",
        Stat.Evasion => $"{value:+0;-0} eva",
        Stat.Perception => $"{value:+0;-0} perception",
        Stat.Stealth => $"{value:+0;-0} stealth",
        _ => $"{value:+0;-0} {stat}",
    };
}
