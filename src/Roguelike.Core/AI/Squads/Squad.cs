using Roguelike.Core.Entities;
using SadRogue.Primitives;

namespace Roguelike.Core.AI.Squads;

public enum SquadRole
{
    Warrior,
    Archer,
    Shaman,
    Runner,
}

public enum SquadState
{
    /// <summary>Unaware of the player: patrols between rooms as a group.</summary>
    Idle,
    /// <summary>Knows roughly where the player is (lost sight, or was called for help).</summary>
    Alert,
    /// <summary>Has recently seen the player and is fighting.</summary>
    Engage,
    /// <summary>Morale broke: running away until it recovers.</summary>
    Flee,
}

/// <summary>
/// A group of actors sharing knowledge, morale and a plan. Members still act
/// individually (see <see cref="SquadMemberBrain"/>); the squad is their blackboard.
/// </summary>
public sealed class Squad
{
    public const int MaxMorale = 100;
    public const int BreakingPoint = 30;
    public const int RallyPoint = 60;

    public int Id { get; }
    public Faction Faction { get; }
    public List<Actor> Members { get; } = [];
    public Dictionary<int, SquadRole> Roles { get; } = [];

    public SquadState State { get; set; } = SquadState.Idle;
    public int Morale { get; set; } = MaxMorale;

    /// <summary>Where the squad last knew the player to be, and when.</summary>
    public Point? LastKnownTarget { get; set; }
    public int LastSeenTurn { get; set; } = -1;

    /// <summary>Where the squad is heading while idle (patrol) or alert (investigating).</summary>
    public Point? Destination { get; set; }

    /// <summary>Consecutive turns spent holding a corridor mouth instead of charging in.</summary>
    public int HoldTurns { get; set; }

    /// <summary>Squad the runner is fetching; null when no mission is pending.</summary>
    public int? HelpTargetSquadId { get; set; }
    public bool HelpCalled { get; set; }

    /// <summary>Members already counted as badly wounded for morale purposes.</summary>
    internal HashSet<int> CountedWounded { get; } = [];

    public Squad(int id, Faction faction)
    {
        Id = id;
        Faction = faction;
    }

    public bool IsWipedOut => Members.Count == 0;

    public SquadRole RoleOf(Actor actor) => Roles.GetValueOrDefault(actor.Id, SquadRole.Warrior);

    public IEnumerable<Actor> WithRole(SquadRole role) => Members.Where(m => RoleOf(m) == role);

    /// <summary>The first warrior if any, else the first member. Others follow it on patrol.</summary>
    public Actor? Leader =>
        Members.FirstOrDefault(m => RoleOf(m) == SquadRole.Warrior) ?? Members.FirstOrDefault();

    public void Add(Actor actor, SquadRole role)
    {
        Members.Add(actor);
        Roles[actor.Id] = role;
    }
}
