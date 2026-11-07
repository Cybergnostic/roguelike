using Roguelike.Core.AI.Squads;

namespace Roguelike.Core.Entities;

/// <summary>Stat block for a kind of monster. Will move to data files once there are more.</summary>
public sealed record MonsterTemplate(
    string Kind, Faction Faction, SquadRole Role,
    int MaxHp, int Attack, int Defense, int Range = 0);

public static class MonsterTemplates
{
    public static readonly MonsterTemplate GoblinWarrior = new("goblin_warrior", Faction.Goblins, SquadRole.Warrior, MaxHp: 9, Attack: 4, Defense: 1);
    public static readonly MonsterTemplate GoblinArcher = new("goblin_archer", Faction.Goblins, SquadRole.Archer, MaxHp: 6, Attack: 3, Defense: 0, Range: 6);
    public static readonly MonsterTemplate GoblinShaman = new("goblin_shaman", Faction.Goblins, SquadRole.Shaman, MaxHp: 6, Attack: 2, Defense: 0);
    public static readonly MonsterTemplate GoblinRunner = new("goblin_runner", Faction.Goblins, SquadRole.Runner, MaxHp: 6, Attack: 3, Defense: 0);
}
