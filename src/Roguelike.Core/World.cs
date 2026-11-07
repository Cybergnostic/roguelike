using GoRogue.Pathing;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Map;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;
using ShaiRandom.Generators;

namespace Roguelike.Core;

/// <summary>
/// The whole simulation. No rendering, no input, no text: give it actions, get events back.
/// Fully determined by its seed and the sequence of player actions.
/// </summary>
public sealed class World
{
    public const int FovRadius = 8;
    private const int PlayerRegenInterval = 8;

    public static readonly Direction[] EightWay = [.. AdjacencyRule.EightWay.DirectionsOfNeighbors()];

    public ulong Seed { get; }
    public IEnhancedRandom Rng { get; }
    public GameMap Map { get; }
    public Actor Player { get; }
    public int Turn { get; private set; }
    public IReadOnlyList<Actor> Actors => _actors;
    public IReadOnlyList<Squad> Squads => _squads;

    /// <summary>Dijkstra map toward the player, shared by every monster that is chasing.</summary>
    public DistanceMap TowardPlayer { get; }

    /// <summary>
    /// Brogue-style safety map: following it downhill moves away from the player intelligently
    /// (toward exits, not into corners). Expensive, so it is only recalculated when first
    /// asked for after the player has moved.
    /// </summary>
    public FleeMap AwayFromPlayer
    {
        get
        {
            if (_fleeDirty)
            {
                _fleeSource.Update(); // FleeMap recalculates itself from its source
                _fleeDirty = false;
            }
            return _awayFromPlayer;
        }
    }

    private readonly GoalMap _fleeSource;
    private readonly FleeMap _awayFromPlayer;
    private bool _fleeDirty = true;

    private readonly List<Actor> _actors = [];
    private readonly List<Squad> _squads = [];
    private readonly List<GameEvent> _events = [];
    private readonly AStar _aStar;
    private int _nextId;

    private World(ulong seed, int width, int height)
    {
        Seed = seed;
        Rng = new MizuchiRandom(seed);
        Map = MapGenerator.RoomsAndMazes(width, height, Rng);

        if (Map.Rooms.Count < 2)
            throw new InvalidOperationException("Map generation produced too few rooms.");

        Player = Spawn("player", Faction.Player, Map.Rooms[0].Center, maxHp: 30, attack: 6, defense: 1);
        SpawnGoblinSquads();

        var goals = new LambdaGridView<GoalState>(Map.Width, Map.Height, p =>
            !Map.Walkable[p] ? GoalState.Obstacle
            : p == Player.Position ? GoalState.Goal
            : GoalState.Clear);
        TowardPlayer = new DistanceMap(Map.Walkable);
        _fleeSource = new GoalMap(goals, SadRogue.Primitives.Distance.Chebyshev);
        _awayFromPlayer = new FleeMap(_fleeSource);
        _aStar = new AStar(Map.Walkable, SadRogue.Primitives.Distance.Chebyshev);

        UpdatePerception();
    }

    public static World Create(ulong seed, int width = 80, int height = 24) => new(seed, width, height);

    /// <summary>Runs one player action and, if it used a turn, everyone else's turn.</summary>
    public IReadOnlyList<GameEvent> Step(GameAction playerAction)
    {
        _events.Clear();
        if (Player.IsDead)
            return _events;

        if (!playerAction.Perform(this, Player))
            return [.. _events];

        UpdatePerception();
        RunNonPlayerTurns();

        _actors.RemoveAll(a => a.IsDead && a != Player);
        Turn++;
        if (Turn % PlayerRegenInterval == 0 && !Player.IsDead && Player.Hp < Player.MaxHp)
            Player.Hp++;
        UpdateFov(); // the player didn't move, so the Dijkstra maps are still valid
        return [.. _events];
    }

    private void RunNonPlayerTurns()
    {
        foreach (var squad in _squads)
            SquadDirector.Think(this, squad);

        foreach (var actor in _actors.ToList())
        {
            if (actor.IsDead || actor.Brain is null)
                continue;
            actor.Brain.Decide(this, actor).Perform(this, actor);
            if (Player.IsDead)
                break;
        }

        _squads.RemoveAll(s => s.Members.All(m => m.IsDead));
    }

    public void Emit(GameEvent e) => _events.Add(e);

    public Actor? GetActor(int id) => _actors.Find(a => a.Id == id);

    public Actor? ActorAt(Point p) => _actors.Find(a => !a.IsDead && a.Position == p);

    public Squad? SquadOf(Actor actor) => (actor.Brain as SquadMemberBrain)?.Squad;

    // ---- Spatial queries used by AI ----

    public static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public bool IsAdjacent(Point a, Point b) => a != b && Distance(a, b) <= 1;

    public bool IsFree(Point p) => Map.InBounds(p) && Map.Walkable[p] && ActorAt(p) is null;

    /// <summary>
    /// Spectator mode: the player is an invisible, invulnerable observer, so the
    /// simulation can be watched running on its own.
    /// </summary>
    public bool ObserverMode { get; set; }

    /// <summary>Whether the player can see this actor (interface, bots).</summary>
    public bool PlayerCanSee(Actor actor) => Map.PlayerFov.BooleanResultView[actor.Position];

    /// <summary>
    /// Whether this actor can see the player (AI). Symmetric approximation: a monster
    /// sees the player iff the player sees its cell. Always false in observer mode.
    /// </summary>
    public bool CanSeePlayer(Actor actor) => !Player.IsDead && !ObserverMode && PlayerCanSee(actor);

    /// <summary>Observer-mode guard for attacks: the player can't be harmed while spectating.</summary>
    public bool IsUntouchable(Actor target) => ObserverMode && target == Player;

    /// <summary>A corridor cell or doorway: walls on both sides along one axis.</summary>
    public bool IsChokepoint(Point p)
    {
        bool Wall(Point q) => !Map.InBounds(q) || !Map.Walkable[q];
        return Map.Walkable[p] &&
               ((Wall(p + Direction.Left) && Wall(p + Direction.Right)) ||
                (Wall(p + Direction.Up) && Wall(p + Direction.Down)));
    }

    public bool HasLineOfSight(Point from, Point to) =>
        InnerLine(from, to).All(p => Map.Transparent[p]);

    /// <summary>Line of sight that also isn't blocked by any actor in between.</summary>
    public bool HasClearShot(Point from, Point to) =>
        InnerLine(from, to).All(p => Map.Transparent[p] && ActorAt(p) is null);

    private static IEnumerable<Point> InnerLine(Point from, Point to) =>
        Lines.GetLine(from, to, Lines.Algorithm.Bresenham).Where(p => p != from && p != to);

    /// <summary>
    /// Best free neighbouring step that goes downhill on a Dijkstra/flee map. Among equally
    /// good steps it prefers cells with fewer allies nearby, which makes groups spread out
    /// and surround rather than queue.
    /// </summary>
    public Direction? StepDownhill(Actor self, IGridView<double?> map)
    {
        if (map[self.Position] is not { } here)
            return null;

        Direction? best = null;
        double bestValue = here;
        int bestCrowding = int.MaxValue;
        foreach (var dir in EightWay)
        {
            var p = self.Position + dir;
            if (!IsFree(p) || map[p] is not { } value || value >= here)
                continue;
            int crowding = EightWay.Count(d => ActorAt(p + d) is { } a && a != self && !a.IsHostileTo(self));
            if (value < bestValue - 1e-9 || (Math.Abs(value - bestValue) < 1e-9 && crowding < bestCrowding))
            {
                best = dir;
                bestValue = value;
                bestCrowding = crowding;
            }
        }
        return best;
    }

    /// <summary>Next step of an A* path to <paramref name="goal"/>, sidestepping actors in the way.</summary>
    public Direction? StepToward(Actor self, Point goal)
    {
        if (self.Position == goal)
            return null;

        var path = _aStar.ShortestPath(self.Position, goal);
        if (path is not null && path.Length > 0)
        {
            var next = path.GetStep(0);
            if (IsFree(next))
                return Direction.GetDirection(self.Position, next);
        }

        int current = Distance(self.Position, goal);
        foreach (var dir in EightWay)
        {
            var p = self.Position + dir;
            if (IsFree(p) && Distance(p, goal) < current)
                return dir;
        }
        return null;
    }

    // ---- Setup ----

    private void UpdatePerception()
    {
        UpdateFov();
        TowardPlayer.Update([Player.Position]);
        _fleeDirty = true;
    }

    private void UpdateFov()
    {
        Map.PlayerFov.Calculate(Player.Position, FovRadius, SadRogue.Primitives.Distance.Euclidean);
        foreach (var p in Map.PlayerFov.CurrentFOV)
            Map.Explored[p] = true;
    }

    private Actor Spawn(string kind, Faction faction, Point at, int maxHp, int attack, int defense, int range = 0)
    {
        var actor = new Actor(_nextId++, kind, faction, at, maxHp, attack, defense) { Range = range };
        _actors.Add(actor);
        return actor;
    }

    private Actor Spawn(MonsterTemplate t, Point at) =>
        Spawn(t.Kind, t.Faction, at, t.MaxHp, t.Attack, t.Defense, t.Range);

    private void SpawnGoblinSquads()
    {
        foreach (var room in Map.Rooms.Skip(1))
        {
            if (Rng.NextInt(3) == 0)
                continue;

            var composition = new List<MonsterTemplate>();
            int warriors = Rng.NextInt(1, 4);
            for (int i = 0; i < warriors; i++) composition.Add(MonsterTemplates.GoblinWarrior);
            int archers = Rng.NextInt(0, 3);
            for (int i = 0; i < archers; i++) composition.Add(MonsterTemplates.GoblinArcher);
            if (Rng.NextInt(4) == 0) composition.Add(MonsterTemplates.GoblinShaman);
            if (composition.Count >= 3 && Rng.NextBool()) composition.Add(MonsterTemplates.GoblinRunner);

            var squad = new Squad(_squads.Count, Faction.Goblins);
            foreach (var template in composition)
            {
                if (RandomFreeFloor(room) is not { } p)
                    continue;
                var goblin = Spawn(template, p);
                goblin.Brain = new SquadMemberBrain(squad);
                squad.Add(goblin, template.Role);
            }
            if (!squad.IsWipedOut)
                _squads.Add(squad);
        }
    }

    private Point? RandomFreeFloor(Rectangle room)
    {
        for (int tries = 0; tries < 20; tries++)
        {
            var p = new Point(Rng.NextInt(room.MinExtentX, room.MaxExtentX + 1),
                              Rng.NextInt(room.MinExtentY, room.MaxExtentY + 1));
            if (IsFree(p))
                return p;
        }
        return null;
    }
}
