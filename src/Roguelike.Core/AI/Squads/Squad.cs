using Roguelike.Core.Entities;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.AI.Squads;

public enum SquadRole
{
    Warrior,
    Archer,
    Shaman,
    Runner,
    /// <summary>Raises corpses into undead servants and casts bolts from behind the line.</summary>
    Necromancer,
    /// <summary>Someone who isn't here to fight: stays put, and talks when spoken to.</summary>
    Civilian,
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

    /// <summary>How far a shout carries. A sighting farther than this from the leader stays with whoever made it.</summary>
    public const int ShoutRange = 6;

    private Map.DistanceMap? _towardThreats;
    private Map.SafetyMap? _awayFromThreats;
    private bool _towardStale = true, _awayStale = true;
    private readonly List<Point> _seenThreats = [];

    /// <summary>Starts this turn's reports of enemies in sight; the movement maps follow them.</summary>
    internal void ClearSightings()
    {
        _seenThreats.Clear();
        _towardStale = _awayStale = true;
    }

    internal void ReportSighting(Point position)
    {
        if (!_seenThreats.Contains(position))
            _seenThreats.Add(position);
    }

    /// <summary>
    /// Distance to what the squad knows of its enemies: those in sight now, otherwise where one
    /// was last known to be. Never where an unseen enemy really is.
    /// </summary>
    public Map.DistanceMap TowardKnownThreats(IGridView<bool> walkable)
    {
        _towardThreats ??= new Map.DistanceMap(walkable);
        if (_towardStale)
        {
            _towardThreats.Update(_seenThreats.Count > 0 || LastKnownTarget is not { } known ? _seenThreats : [known]);
            _towardStale = false;
        }
        return _towardThreats;
    }

    /// <summary>Flee map away from what the squad knows of its enemies; see <see cref="TowardKnownThreats"/>.</summary>
    public Map.SafetyMap AwayFromKnownThreats(IGridView<bool> walkable)
    {
        _awayFromThreats ??= new Map.SafetyMap(walkable);
        if (_awayStale)
        {
            _awayFromThreats.Update(TowardKnownThreats(walkable));
            _awayStale = false;
        }
        return _awayFromThreats;
    }

    /// <summary>
    /// A member was attacked, so the squad knows where from, even if the attacker is too far off
    /// to see (a blow or arrow gives away its source). A calm squad goes to look.
    /// </summary>
    internal void LearnOfAttacker(Actor attacker, int turn)
    {
        TargetId = attacker.Id;
        LastKnownTarget = attacker.Position;
        LastSeenTurn = turn;
        if (State is SquadState.Idle or SquadState.Alert)
        {
            State = SquadState.Alert;
            Destination = attacker.Position;
        }
    }

    /// <summary>A lair squad's home: it waits there when idle instead of patrolling.</summary>
    public Point? Home { get; set; }

    /// <summary>A raid in progress: where the squad is marching, and the zone it's raiding.</summary>
    public Point? RaidTarget { get; set; }
    public string? RaidZone { get; set; }

    public bool IsRaiding => RaidTarget is not null;

    /// <summary>Turns the raid leader has waited for stragglers in a row; it gives up waiting after a while.</summary>
    public int StragglerWait { get; set; }
    public const int MaxStragglerWait = 10;

    /// <summary>Time has passed: whatever the squad was doing, it's over, and it goes back to its orders.</summary>
    internal void SettleDown()
    {
        State = SquadState.Idle;
        Destination = null;
        TargetId = null;
        LastKnownTarget = null;
        LastSeenTurn = -1;
        HelpTargetSquadId = null;
        HoldTurns = 0;
        CallOffRaid();
        ClearSightings();
    }

    public void CallOffRaid()
    {
        RaidTarget = null;
        RaidZone = null;
    }

    /// <summary>How far (walking) from home the squad will go; 0 = no limit.</summary>
    public int Leash { get; set; }

    /// <summary>Walking distances from home, set together with a leash.</summary>
    public Map.DistanceMap? HomeDistance { get; set; }

    public bool IsLeashed => Leash > 0 && HomeDistance is not null;

    /// <summary>Walking distance from home (infinite where unreachable or unleashed).</summary>
    public double DistanceFromHome(Point p) => HomeDistance?[p] ?? double.MaxValue;

    public bool WithinLeash(Point p) => !IsLeashed || DistanceFromHome(p) <= Leash;

    /// <summary>The camp this squad defends, if any; it decides who the squad treats as an enemy.</summary>
    public Camp? Camp { get; set; }

    /// <summary>Guard order: the spot each member holds while nothing is happening.</summary>
    public Dictionary<int, Point> Posts { get; } = [];

    /// <summary>What kind of guard holds each post, so a fallen one can be replaced in kind.</summary>
    public Dictionary<int, string> PostKinds { get; } = [];

    /// <summary>Patrol order: the stops the squad walks in turn, and the one it's heading for.</summary>
    public IReadOnlyList<Point>? Route { get; set; }
    public int RouteStop { get; set; }

    /// <summary>Where the squad runs when its nerve breaks, instead of just away from the enemy.</summary>
    public Point? Rally { get; set; }

    /// <summary>The gate this squad shuts once its camp has been attacked.</summary>
    public Point? GateToClose { get; set; }

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
