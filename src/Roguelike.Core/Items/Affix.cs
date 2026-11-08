using Roguelike.Core.Progression;

namespace Roguelike.Core.Items;

public enum AffixKind
{
    Prefix,
    Suffix,
}

/// <summary>
/// A property equipment can roll (affixes.json): adds <see cref="Stat"/> by Min..Max, plus
/// PerLevel for each dungeon level below the first. Name is the word a magic item is called by.
/// </summary>
public sealed record AffixDef(
    string Id,
    AffixKind Kind,
    string Name,
    IReadOnlyList<ItemCategory> Slots,
    Stat Stat,
    int Min,
    int Max,
    double PerLevel = 0,
    int Weight = 1,
    int MinDepth = 1)
{
    public int ValueAt(int roll, int depth) => roll + (int)Math.Floor(PerLevel * (depth - 1));
}

/// <summary>A rolled property on a concrete item.</summary>
public sealed record AffixRoll(AffixDef Def, int Value);

/// <summary>
/// How often a tier above common appears (percent at depth 1, plus ChancePerLevel per level below)
/// and what it carries: between AffixMin and AffixMax properties, and for legendaries a special.
/// </summary>
public sealed record RarityTierDef(Rarity Rarity, double Chance, double ChancePerLevel, int AffixMin, int AffixMax,
    int MinDepth = 1, bool Special = false)
{
    public double ChanceAt(int depth) => depth < MinDepth ? 0 : Chance + ChancePerLevel * (depth - 1);
}

/// <summary>Word lists for rare and legendary names: a first word, and a second word per slot.</summary>
public sealed record ItemNameWords(IReadOnlyList<string> First, IReadOnlyDictionary<ItemCategory, IReadOnlyList<string>> Second);
