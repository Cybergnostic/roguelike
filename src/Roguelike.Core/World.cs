using GoRogue.Pathing;
using Roguelike.Core.Actions;
using Roguelike.Core.AI;
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

    public ulong Seed { get; }
    public IEnhancedRandom Rng { get; }
    public GameMap Map { get; }
    public Actor Player { get; }
    public int Turn { get; private set; }
    public IReadOnlyList<Actor> Actors => _actors;

    /// <summary>Dijkstra map toward the player, shared by every monster that is chasing.</summary>
    public GoalMap TowardPlayer { get; }

    private readonly List<Actor> _actors = [];
    private readonly List<GameEvent> _events = [];
    private int _nextId;

    private World(ulong seed, int width, int height)
    {
        Seed = seed;
        Rng = new MizuchiRandom(seed);
        Map = MapGenerator.RoomsAndMazes(width, height, Rng);

        if (Map.Rooms.Count < 2)
            throw new InvalidOperationException("Map generation produced too few rooms.");

        Player = Spawn("player", Faction.Player, Map.Rooms[0].Center, maxHp: 30, attack: 6, defense: 1, brain: null);
        SpawnGoblins();

        var goals = new LambdaGridView<GoalState>(Map.Width, Map.Height, p =>
            !Map.Walkable[p] ? GoalState.Obstacle
            : p == Player.Position ? GoalState.Goal
            : GoalState.Clear);
        TowardPlayer = new GoalMap(goals, Distance.Chebyshev);

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

        foreach (var actor in _actors.ToList())
        {
            if (actor.IsDead || actor.Brain is null)
                continue;
            actor.Brain.Decide(this, actor).Perform(this, actor);
            if (Player.IsDead)
                break;
        }

        _actors.RemoveAll(a => a.IsDead && a != Player);
        Turn++;
        UpdateFov(); // the player didn't move, so the Dijkstra map is still valid
        return [.. _events];
    }

    public void Emit(GameEvent e) => _events.Add(e);

    public Actor? GetActor(int id) => _actors.Find(a => a.Id == id);

    public Actor? ActorAt(Point p) => _actors.Find(a => !a.IsDead && a.Position == p);

    private void UpdatePerception()
    {
        UpdateFov();
        TowardPlayer?.Update();
    }

    private void UpdateFov()
    {
        Map.PlayerFov.Calculate(Player.Position, FovRadius, Distance.Euclidean);
        foreach (var p in Map.PlayerFov.CurrentFOV)
            Map.Explored[p] = true;
    }

    private Actor Spawn(string kind, Faction faction, Point at, int maxHp, int attack, int defense, IBrain? brain)
    {
        var actor = new Actor(_nextId++, kind, faction, at, maxHp, attack, defense, brain);
        _actors.Add(actor);
        return actor;
    }

    private void SpawnGoblins()
    {
        // Small groups in the other rooms: the seed of what will become goblin squads.
        foreach (var room in Map.Rooms.Skip(1))
        {
            if (Rng.NextInt(3) == 0)
                continue;
            int count = Rng.NextInt(1, 4);
            for (int i = 0; i < count; i++)
            {
                var spot = RandomFreeFloor(room);
                if (spot is { } p)
                    Spawn("goblin", Faction.Goblins, p, maxHp: 8, attack: 4, defense: 0, brain: new ChaseBrain());
            }
        }
    }

    private Point? RandomFreeFloor(Rectangle room)
    {
        for (int tries = 0; tries < 20; tries++)
        {
            var p = new Point(Rng.NextInt(room.MinExtentX, room.MaxExtentX + 1),
                              Rng.NextInt(room.MinExtentY, room.MaxExtentY + 1));
            if (Map.Walkable[p] && ActorAt(p) is null)
                return p;
        }
        return null;
    }
}
