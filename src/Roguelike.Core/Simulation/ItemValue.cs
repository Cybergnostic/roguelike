using Roguelike.Core.Items;
using Roguelike.Core.Progression;

namespace Roguelike.Core.Simulation;

/// <summary>A rough worth of a piece of gear, for bots comparing it with what they wear (same slot only).</summary>
public static class ItemValue
{
    /// <summary>
    /// Attack or defence counts in full, range by <paramref name="rangeWeight"/> per square, and
    /// each affix by how much a point of its stat is worth next to a point of attack.
    /// </summary>
    public static double Of(Item? item, double rangeWeight = 0.5)
    {
        if (item is null)
            return double.MinValue;
        double value = item.AttackBonus * 100.0 / item.Def.AttackTime + item.DefenseBonus + item.Def.Range * rangeWeight;
        foreach (var a in item.Affixes)
            value += a.Value * StatWeight(a.Def.Stat, rangeWeight);
        return value + (item.Special is null ? 0 : 3);
    }

    private static double StatWeight(Stat stat, double rangeWeight) => stat switch
    {
        Stat.MeleeAttack or Stat.RangedAttack or Stat.Defense or Stat.Accuracy or Stat.Evasion => 1,
        Stat.Range => rangeWeight,
        Stat.MaxHp => 0.25,
        Stat.Regen => 1,
        Stat.PotionHealing or Stat.XpGain => 0.05,
        _ => 0,
    };
}
