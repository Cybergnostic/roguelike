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
    /// <summary>Armour: absorbs a random 0..Defense of each hit taken (a hit still does at least 1).</summary>
    Defense,
    /// <summary>Chance to hit: each point is worth combat.json's hitChancePerPoint against the target's evasion.</summary>
    Accuracy,
    /// <summary>Chance to be missed: each point cancels a point of the attacker's accuracy.</summary>
    Evasion,
    /// <summary>Extra bow range, in cells.</summary>
    Range,
    /// <summary>Fewer turns per HP regained.</summary>
    Regen,
    /// <summary>Percent more healing from potions.</summary>
    PotionHealing,
    /// <summary>Percent more XP from every source.</summary>
    XpGain,
    /// <summary>How much the player can make out of what they hear (see dungeon.json's hearing).</summary>
    Perception,
    /// <summary>Quieter footsteps, and harder to spot when sneaking.</summary>
    Stealth,
    /// <summary>Percent faster at everything: moving, attacking, drinking.</summary>
    Speed,
}
