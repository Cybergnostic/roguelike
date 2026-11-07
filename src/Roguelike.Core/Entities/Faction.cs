namespace Roguelike.Core.Entities;

public enum Relation
{
    Neutral,
    Hostile,
    Allied,
}

/// <summary>A side in the dungeon's conflicts. Defined in data (factions.json).</summary>
public sealed class Faction(string id, string name)
{
    public const string PlayerId = "player";

    public string Id { get; } = id;
    public string Name { get; } = name;

    public override string ToString() => Id;
}

/// <summary>Who is hostile to whom. Same faction is always allied; unlisted pairs are neutral.</summary>
public sealed class FactionRelations
{
    private readonly Dictionary<(string, string), Relation> _relations = [];

    public void Set(Faction a, Faction b, Relation relation)
    {
        _relations[(a.Id, b.Id)] = relation;
        _relations[(b.Id, a.Id)] = relation;
    }

    public Relation Get(Faction a, Faction b) =>
        a == b ? Relation.Allied : _relations.GetValueOrDefault((a.Id, b.Id), Relation.Neutral);

    public bool AreHostile(Faction a, Faction b) => Get(a, b) == Relation.Hostile;
}
