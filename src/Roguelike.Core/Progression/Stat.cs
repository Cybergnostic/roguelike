namespace Roguelike.Core.Progression;

/// <summary>
/// Derived stats that attributes, skills (and later item affixes) add to. One shared vocabulary,
/// so content can say "+1 meleeAttack per Strength modifier" or "+5 maxHp per rank" in data.
/// </summary>
public enum Stat
{
    /// <summary>Hit points.</summary>
    MaxHp,
    /// <summary>Added to the melee attack roll's maximum.</summary>
    MeleeAttack,
    /// <summary>Added to the ranged attack roll's maximum.</summary>
    RangedAttack,
    /// <summary>Subtracted from each hit taken.</summary>
    Defense,
    /// <summary>Extra bow range, in cells.</summary>
    Range,
    /// <summary>Fewer turns per HP regained.</summary>
    Regen,
    /// <summary>Percent more healing from potions.</summary>
    PotionHealing,
    /// <summary>Percent more XP from every source.</summary>
    XpGain,
}
