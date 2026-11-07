using GoRogue.Pathing;
using Roguelike.Core.Actions;
using Roguelike.Core.Content;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Items;
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

    public static readonly Direction[] EightWay = [.. AdjacencyRule.EightWay.DirectionsOfNeighbors()];

    public ulong Seed { get; }
    public ContentDatabase Content { get; }
    public FactionRelations Relations => Content.Relations;
    public IEnhancedRandom Rng { get; }
    public GameMap Map { get; }
    public Actor Player { get; }
    public int Turn { get; private set; }
    public IReadOnlyList<Actor> Actors => _actors;
    public IReadOnlyList<Squad> Squads => _squads;

    /// <summary>Distance map toward the player (debug heatmap, tests, bots).</summary>
    public DistanceMap TowardPlayer { get; }

    /// <summary>Per-faction "toward enemies" and "away from enemies" maps, rebuilt lazily each turn.</summary>
    private sealed class FactionMaps(GameMap map)
    {
        public readonly DistanceMap Toward = new(map.Walkable);
        public readonly SafetyMap Away = new(map.Walkable);
        public bool TowardDirty = true;
        public bool AwayDirty = true;
    }

    private readonly Dictionary<Faction, FactionMaps> _factionMaps = [];

    /// <summary>An item lying on the map.</summary>
    public sealed record FloorItem(Item Item, Point Position);

    public IReadOnlyList<FloorItem> FloorItems => _floorItems;

    /// <summary>What's left after a monster dies. Necromancers raise these.</summary>
    public sealed record Corpse(int Id, string Kind, Point Position, int Turn);

    public IReadOnlyList<Corpse> Corpses => _corpses;

    private readonly List<Corpse> _corpses = [];
    private int _nextCorpseId;
    private int _nextSquadId;

    private readonly List<FloorItem> _floorItems = [];
    private int _nextItemId;
    private readonly List<Actor> _actors = [];
    private readonly List<Squad> _squads = [];
    private readonly List<GameEvent> _events = [];
    private readonly AStar _aStar;
    private int _nextId;

    private World(ulong seed, int width, int height, ContentDatabase content)
    {
        Seed = seed;
        Content = content;
        Rng = new MizuchiRandom(seed);
        Map = MapGenerator.RoomsAndMazes(width, height, Rng);

        if (Map.Rooms.Count < 2)
            throw new InvalidOperationException("Map generation produced too few rooms.");

        Player = CreatePlayer(Map.Rooms[0].Center);
        SpawnSquads();
        SpawnFloorItems();

        TowardPlayer = new DistanceMap(Map.Walkable);
        _aStar = new AStar(Map.Walkable, SadRogue.Primitives.Distance.Chebyshev);

        UpdatePerception();
    }

    public static World Create(ulong seed, int width = 80, int height = 24, ContentDatabase? content = null) =>
        new(seed, width, height, content ?? ContentDatabase.Default);

    /// <summary>Runs one player action and, if it used a turn, everyone else's turn.</summary>
    public IReadOnlyList<GameEvent> Step(GameAction playerAction)
    {
        _events.Clear();
        if (Player.IsDead)
            return _events;

        var zoneBefore = Map.ZoneAt(Player.Position);
        if (!playerAction.Perform(this, Player))
            return [.. _events];
        if (Map.ZoneAt(Player.Position) is { } zone && zone != zoneBefore)
            Emit(new ZoneEntered(zone.Name));

        UpdatePerception();
        RunNonPlayerTurns();

        _actors.RemoveAll(a => a.IsDead && a != Player);
        Turn++;
        int regen = Content.Player.RegenInterval;
        if (regen > 0 && Turn % regen == 0 && !Player.IsDead && Player.Hp < Player.MaxHp)
            Player.Hp++;
        UpdateFov(); // the player didn't move, so the Dijkstra maps are still valid
        return [.. _events];
    }

    private void RunNonPlayerTurns()
    {
        foreach (var maps in _factionMaps.Values)
            maps.TowardDirty = maps.AwayDirty = true;

        foreach (var squad in _squads)
            SquadDirector.Think(this, squad);

        // Energy: each actor gains its speed every turn and acts once per 100 it has.
        // Actors created this turn (raised dead) wait for the next one.
        foreach (var actor in _actors.ToList())
        {
            if (actor.IsDead || actor.Brain is null)
                continue;
            actor.Energy += actor.Speed;
            while (actor.Energy >= 100 && !actor.IsDead && !Player.IsDead)
            {
                actor.Energy -= 100;
                actor.Brain.Decide(this, actor).Perform(this, actor);
            }
            if (Player.IsDead)
                break;
        }

        _squads.RemoveAll(s => s.Members.All(m => m.IsDead));
    }

    public void Emit(GameEvent e) => _events.Add(e);

    public Actor? GetActor(int id) => _actors.Find(a => a.Id == id);

    public Actor? ActorAt(Point p) => _actors.Find(a => !a.IsDead && a.Position == p);

    public Squad? SquadOf(Actor actor) => (actor.Brain as SquadMemberBrain)?.Squad;

    // ---- Combat ----

    /// <summary>Applies damage and handles death: the event, and loot dropped where the actor fell.</summary>
    public void Damage(Actor attacker, Actor target, int damage, bool ranged)
    {
        target.Hp -= damage;
        Emit(new ActorAttacked(attacker.Id, target.Id, damage, ranged));
        if (!target.IsDead)
            return;

        Emit(new ActorDied(target.Id, target.Kind, target.Faction.Id, attacker.Id));
        LeaveCorpse(target);
        if (target.LootTable is { } table && RollLoot(table) is { } loot)
        {
            PlaceOnFloor(loot, target.Position);
            Emit(new LootDropped(target.Id, loot, target.Position));
        }

        // A necromancer's servants don't outlive him.
        foreach (var minion in MinionsOf(target).ToList())
        {
            minion.Hp = 0;
            Emit(new MinionCrumbled(minion.Id, minion.Kind, target.Id));
            LeaveCorpse(minion);
        }
    }

    // ---- Corpses and the dead ----

    /// <summary>The dead rise only once: what a necromancer raised leaves nothing behind.</summary>
    private void LeaveCorpse(Actor actor)
    {
        if (actor != Player && actor.MasterId is null)
            _corpses.Add(new Corpse(_nextCorpseId++, actor.Kind, actor.Position, Turn));
    }

    public IEnumerable<Corpse> CorpsesAt(Point p) => _corpses.Where(c => c.Position == p);

    public void RemoveCorpse(Corpse corpse) => _corpses.Remove(corpse);

    /// <summary>Living actors bound to this one (raised by it).</summary>
    public IEnumerable<Actor> MinionsOf(Actor master) =>
        _actors.Where(a => a.MasterId == master.Id && !a.IsDead);

    /// <summary>
    /// Creates a monster from data and puts it in a squad: the given one, or a new squad of
    /// its own. Faction defaults to the monster's (raised dead take their raiser's instead).
    /// </summary>
    public Actor SpawnMonster(string id, Point at, Squad? squad = null, Faction? faction = null)
    {
        var def = Content.Monsters[id];
        var monster = Spawn(def, at, faction);
        if (squad is null)
        {
            squad = new Squad(_nextSquadId++, monster.Faction);
            _squads.Add(squad);
        }
        monster.Brain = new SquadMemberBrain(squad);
        squad.Add(monster, def.Role);
        return monster;
    }

    // ---- Items ----

    public IEnumerable<Item> ItemsAt(Point p) => _floorItems.Where(f => f.Position == p).Select(f => f.Item);

    public void PlaceOnFloor(Item item, Point p) => _floorItems.Add(new FloorItem(item, p));

    public bool TakeFromFloor(Item item) => _floorItems.RemoveAll(f => f.Item == item) > 0;

    /// <summary>Makes a new item. Equipment rolls its +X unless one is given.</summary>
    public Item CreateItem(ItemDef def, int? enchantment = null, int count = 1) =>
        new(_nextItemId++, def)
        {
            Enchantment = enchantment ?? (def.IsEquipment ? RollEnchantment() : 0),
            Count = count,
        };

    /// <summary>Picks one entry from a loot table; null when it rolls "nothing".</summary>
    public Item? RollLoot(string table)
    {
        var entries = Content.LootTables[table];
        int roll = Rng.NextInt(entries.Sum(e => e.Weight));
        var entry = entries.First(e => (roll -= e.Weight) < 0);
        return entry.Item is null ? null : CreateItem(Content.Items[entry.Item]);
    }

    private int RollEnchantment()
    {
        var table = Content.Enchantments;
        int roll = Rng.NextInt(table.Sum(e => e.Weight));
        return table.First(e => (roll -= e.Weight) < 0).Value;
    }

    // ---- Spatial queries used by AI ----

    public static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public bool IsAdjacent(Point a, Point b) => a != b && Distance(a, b) <= 1;

    public bool IsFree(Point p) => Map.InBounds(p) && Map.Walkable[p] && ActorAt(p) is null;

    /// <summary>
    /// Allied monsters may trade places (Brogue-style) so they can't deadlock in corridors.
    /// The player is never pushed around.
    /// </summary>
    /// <summary>
    /// Allies trade places, except with the player, and never so that a leashed actor
    /// (a lair guard) gets pushed out of its territory.
    /// </summary>
    public bool CanSwap(Actor mover, Actor other) =>
        mover != Player && other != Player && Relations.Get(mover.Faction, other.Faction) == Relation.Allied
        && (SquadOf(other) is not { IsLeashed: true } squad || squad.WithinLeash(mover.Position));

    /// <summary>
    /// Spectator mode: the player is an invisible, invulnerable observer, so the
    /// simulation can be watched running on its own.
    /// </summary>
    public bool ObserverMode { get; set; }

    /// <summary>Whether the player can see this actor (interface, bots). Uses the player's FOV.</summary>
    public bool PlayerCanSee(Actor actor) => Map.PlayerFov.BooleanResultView[actor.Position];

    /// <summary>Observer-mode guard for attacks: the player can't be harmed while spectating.</summary>
    public bool IsUntouchable(Actor target) => ObserverMode && target == Player;

    /// <summary>Hostile per the faction table. The player in observer mode is nobody's enemy.</summary>
    public bool AreHostile(Actor a, Actor b) =>
        a != b && !a.IsDead && !b.IsDead && !IsUntouchable(a) && !IsUntouchable(b)
        && Relations.AreHostile(a.Faction, b.Faction);

    /// <summary>Monster perception: within sight radius and an unobstructed line.</summary>
    public bool CanSee(Actor observer, Actor target)
    {
        if (target.IsDead || IsUntouchable(target))
            return false;
        int dx = observer.Position.X - target.Position.X, dy = observer.Position.Y - target.Position.Y;
        return dx * dx + dy * dy <= SightRadiusSquared && HasLineOfSight(observer.Position, target.Position);
    }

    private const int SightRadiusSquared = FovRadius * FovRadius + FovRadius; // ~ radius 8.5

    /// <summary>The closest hostile actor this actor can see, or null.</summary>
    public Actor? NearestVisibleHostile(Actor self)
    {
        Actor? best = null;
        int bestDistance = int.MaxValue;
        foreach (var other in _actors)
        {
            if (!AreHostile(self, other))
                continue;
            int d = Distance(self.Position, other.Position);
            if (d < bestDistance && CanSee(self, other))
            {
                best = other;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>A hostile actor next to this one (the weakest, if several), or null.</summary>
    public Actor? AdjacentHostile(Actor self)
    {
        Actor? best = null;
        foreach (var dir in EightWay)
        {
            if (ActorAt(self.Position + dir) is { } other && AreHostile(self, other)
                && (best is null || other.Hp < best.Hp))
                best = other;
        }
        return best;
    }

    /// <summary>Dijkstra map toward every actor hostile to <paramref name="faction"/>.</summary>
    public DistanceMap TowardEnemiesOf(Faction faction)
    {
        var maps = MapsFor(faction);
        if (maps.TowardDirty)
        {
            maps.Toward.Update(_actors
                .Where(a => !a.IsDead && !IsUntouchable(a) && Relations.AreHostile(faction, a.Faction))
                .Select(a => a.Position));
            maps.TowardDirty = false;
        }
        return maps.Toward;
    }

    /// <summary>Flee map away from every actor hostile to <paramref name="faction"/>.</summary>
    public SafetyMap AwayFromEnemiesOf(Faction faction)
    {
        var maps = MapsFor(faction);
        if (maps.AwayDirty)
        {
            maps.Away.Update(TowardEnemiesOf(faction));
            maps.AwayDirty = false;
        }
        return maps.Away;
    }

    private FactionMaps MapsFor(Faction faction)
    {
        if (!_factionMaps.TryGetValue(faction, out var maps))
            _factionMaps[faction] = maps = new FactionMaps(Map);
        return maps;
    }

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
            int crowding = EightWay.Count(d => ActorAt(p + d) is { } a && a != self && !AreHostile(a, self));
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
            if (IsFree(next) || (ActorAt(next) is { } blocker && CanSwap(self, blocker)))
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
    }

    private void UpdateFov()
    {
        Map.PlayerFov.Calculate(Player.Position, FovRadius, SadRogue.Primitives.Distance.Euclidean);
        foreach (var p in Map.PlayerFov.CurrentFOV)
            Map.Explored[p] = true;
    }

    private Actor AddActor(Actor actor)
    {
        _actors.Add(actor);
        return actor;
    }

    private Actor Spawn(MonsterDef def, Point at, Faction? faction = null) =>
        AddActor(new Actor(_nextId++, def.Id, faction ?? Content.Factions[def.Faction], at, def.MaxHp, def.Attack, def.Defense)
        {
            BaseRange = def.Range,
            Fearless = def.Fearless,
            LootTable = def.Loot,
            Speed = def.Speed,
        });

    /// <summary>The player, with base stats and starting kit from player.json.</summary>
    private Actor CreatePlayer(Point at)
    {
        var def = Content.Player;
        var player = AddActor(new Actor(_nextId++, "player", Content.PlayerFaction, at, def.MaxHp, def.Attack, def.Defense)
        {
            Inventory = new Inventory(def.InventoryCapacity),
            Equipment = new Equipment(),
        });
        foreach (var k in def.Kit)
        {
            var item = player.Inventory!.Add(CreateItem(Content.Items[k.Item], k.Enchantment, k.Count));
            if (k.Equip)
                player.Equipment!.Equip(item);
        }
        return player;
    }

    private void SpawnFloorItems()
    {
        if (Content.FloorItemChance <= 0)
            return;
        foreach (var room in Map.Rooms.Skip(1))
        {
            if (Rng.NextDouble() >= Content.FloorItemChance)
                continue;
            if (RollLoot("floor") is { } item && RandomFreeFloor(room) is { } p)
                PlaceOnFloor(item, p);
        }
    }

    /// <summary>
    /// Populates rooms (except the start) per spawns.json: lairs first, each in the free room
    /// farthest from the start, then random squads in the rest.
    /// </summary>
    private void SpawnSquads()
    {
        // A quiet start: nobody spawns within walking distance of the player's first room.
        var fromStart = new DistanceMap(Map.Walkable);
        fromStart.Update([Player.Position]);

        var taken = new HashSet<Rectangle>();
        var farthestFirst = Map.Rooms.Skip(1)
            .Select(r => (Room: r, Distance: ClosestReachableCell(r, fromStart)))
            .Where(x => x.Distance < double.MaxValue)
            .OrderByDescending(x => x.Distance)
            .Select(x => x.Room)
            .ToList();
        foreach (var lair in Content.Lairs)
        {
            var free = farthestFirst.Where(r => !taken.Contains(r)).Take(1).ToList();
            if (free.Count == 0)
                break;
            var room = free[0];
            taken.Add(room);
            BuildLair(lair, room);
        }

        var templates = Content.SquadTemplates;
        int totalWeight = templates.Sum(t => t.Weight);
        if (totalWeight <= 0)
            return;

        foreach (var room in Map.Rooms.Skip(1))
        {
            if (Rng.NextDouble() >= Content.RoomOccupancy)
                continue;
            if (taken.Contains(room) || ClosestReachableCell(room, fromStart) < Content.SafeStartDistance)
                continue;

            int roll = Rng.NextInt(totalWeight);
            FillSquad(templates.First(t => (roll -= t.Weight) < 0), room);
        }
    }

    /// <summary>Architecture, zone, the squad with its home and leash, and the remains lying about.</summary>
    private void BuildLair(SquadTemplateDef lair, Rectangle room)
    {
        if (lair.Architecture is { } style)
            LairBuilder.Build(Map, room, style);
        if (lair.Zone is { } name)
            Map.Zones.Add(new Zone(name, room));

        if (FillSquad(lair, room) is { } squad)
        {
            var home = room.Positions().Where(p => Map.Walkable[p])
                .OrderBy(p => Distance(p, room.Center)).First();
            squad.Home = home;
            if (lair.Leash > 0)
            {
                squad.Leash = lair.Leash;
                squad.HomeDistance = new DistanceMap(Map.Walkable);
                squad.HomeDistance.Update([home]);
            }
        }

        foreach (var entry in lair.Corpses ?? [])
            for (int i = 0; i < entry.Count; i++)
                if (RandomFreeFloor(room) is { } p && !CorpsesAt(p).Any())
                    _corpses.Add(new Corpse(_nextCorpseId++, entry.Monster, p, 0));
    }

    /// <summary>Spawns a squad from a template inside a room; null if nobody could be placed.</summary>
    private Squad? FillSquad(SquadTemplateDef template, Rectangle room)
    {
        var squad = new Squad(_nextSquadId++, Content.Factions[template.Faction]);
        foreach (var member in template.Members)
        {
            int count = Rng.NextInt(member.Min, member.Max + 1);
            for (int i = 0; i < count; i++)
            {
                if (Rng.NextDouble() >= member.Chance || RandomFreeFloor(room) is not { } p)
                    continue;
                var def = Content.Monsters[member.Monster];
                var monster = Spawn(def, p);
                monster.Brain = new SquadMemberBrain(squad);
                squad.Add(monster, def.Role);
            }
        }
        if (squad.IsWipedOut)
            return null;
        _squads.Add(squad);
        return squad;
    }

    /// <summary>Walking distance from the distance map's goal to the nearest cell of a room.</summary>
    private static double ClosestReachableCell(Rectangle room, DistanceMap map) =>
        room.Positions().Select(p => map[p]).Where(d => d is not null).Select(d => d!.Value)
            .DefaultIfEmpty(double.MaxValue).Min();

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
