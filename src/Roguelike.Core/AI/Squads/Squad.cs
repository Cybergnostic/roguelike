using Roguelike.Core.Entities;
using SadRogue.Primitives;

namespace Roguelike.Core.AI.Squads;

public enum SquadRole
{
    Warrior,
    Archer,
    Shaman,
    Runner,
    /// <summary>Raises corpses into undead servants and casts bolts from behind the line.</summary>
    Necromancer,
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

    /// <summary>The enemy the squad is focused on, where it was last seen, and when.</summary>
    public int? TargetId { get; set; }
    public Point? LastKnownTarget { get; set; }
    public int LastSeenTurn { get; set; } = -1;

    /// <summary>A lair squad's home: it waits there when idle instead of patrolling.</summary>
    public Point? Home { get; set; }

    /// <summary>How far (walking) from home the squad will go; 0 = no limit.</summary>
    public int Leash { get; set; }

    /// <summary>Walking distances from home, set together with a leash.</summary>
    public Map.DistanceMap? HomeDistance { get; set; }

    public bool IsLeashed => Leash > 0 && HomeDistance is not null;

    /// <summary>Walking distance from home (infinite where unreachable or unleashed).</summary>
    public double DistanceFromHome(Point p) => HomeDistance?[p] ?? double.MaxValue;

    public bool WithinLeash(Point p) => !IsLeashed || DistanceFromHome(p) <= Leash;

    /// <summary>Where the squad is heading while idle (patrol) or alert (investigating).</summary>
    public Point? Destination { get; set; }

    /// <summary>Consecutive turns the enemy has stood in a chokepoint while the squad is engaged.</summary>
    public int HoldTurns { get; set; }

    /// <summary>How long warriors will wait at a corridor mouth before charging in anyway.</summary>
    public const int MaxHoldTurns = 8;

    /// <summary>
    /// Warriors wait at the mouth of the enemy's corridor instead of filing in one at a time,
    /// but only while someone can shoot at the enemy meanwhile, and only for a while.
    /// </summary>
    public bool IsHoldingPassage =>
        State == SquadState.Engage && HoldTurns is > 0 and <= MaxHoldTurns && WithRole(SquadRole.Archer).Any();

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

    /// <summary>Squads made only of fearless members (e.g. undead) ignore morale.</summary>
    public bool IsFearless => Members.Count > 0 && Members.All(m => m.Fearless);

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
