using GoRogue.Pathing;
using Roguelike.Core.Actions;
using Roguelike.Core.Content;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.AI.Strategy;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Items;
using Roguelike.Core.Map;
using Roguelike.Core.Progression;
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
    public GameMap Map { get; private set; }
    public Actor Player { get; }

    /// <summary>The player took the stairs down from the last level.</summary>
    public bool IsWon { get; private set; }

    /// <summary>Whether the game is over, either way.</summary>
    public bool IsOver => IsWon || Player.IsDead;
    public int Turn { get; private set; }
    public IReadOnlyList<Actor> Actors => _actors;
    public IReadOnlyList<Squad> Squads => _squads;

    /// <summary>Distance map toward the player (debug heatmap, tests, bots).</summary>
    public DistanceMap TowardPlayer { get; private set; }

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

    /// <summary>
    /// A faction's stronghold: its zone, the recipe it was built from, and the squad that holds
    /// it. A lair whose squad is wiped out has fallen: it no longer musters or draws raids.
    /// </summary>
    public sealed class Lair(Zone zone, SquadTemplateDef template)
    {
        public Zone Zone { get; } = zone;
        public SquadTemplateDef Template { get; } = template;
        public Squad? Squad { get; set; }
        public bool HasFallen => Squad is null || Squad.Members.All(m => m.IsDead);
    }

    public IReadOnlyList<Lair> Lairs => _lairs;

    private readonly List<Lair> _lairs = [];

    private readonly List<Corpse> _corpses = [];
    private int _nextCorpseId;
    private int _nextSquadId;

    private readonly List<FloorItem> _floorItems = [];
    private int _nextItemId;
    private readonly List<Actor> _actors = [];
    private readonly List<Squad> _squads = [];
    private readonly List<GameEvent> _events = [];
    private AStar _aStar;

    /// <summary>
    /// Each actor's last A* path and how far along it is. Walls don't change once a level is
    /// built and A* ignores actors, so a path stays good while its owner keeps to it and heads
    /// for the same goal; re-planning every turn was a quarter of the simulation's time.
    /// </summary>
    private sealed class CachedPath(Point goal, GoRogue.Pathing.Path path)
    {
        public readonly Point Goal = goal;
        public readonly GoRogue.Pathing.Path Path = path;
        public int Next; // index of the next step to take
    }

    private readonly Dictionary<int, CachedPath> _paths = [];
    private int _nextId;
    private readonly int _width, _height;

    private World(ulong seed, int width, int height, ContentDatabase content, int depth)
    {
        Seed = seed;
        Depth = depth;
        Content = content;
        Rng = new MizuchiRandom(seed);
        (_width, _height) = (width, height);
        Map = GenerateMap();
        Player = CreatePlayer(Map.Rooms[0].Center);
        (TowardPlayer, _aStar) = PopulateLevel();
    }

    private GameMap GenerateMap()
    {
        var map = MapGenerator.RoomsAndMazes(_width, _height, Rng);
        if (map.Rooms.Count < 2)
            throw new InvalidOperationException("Map generation produced too few rooms.");
        return map;
    }

    /// <summary>Squads, items and stairs for the current map, then fresh pathing and perception.</summary>
    private (DistanceMap, AStar) PopulateLevel()
    {
        SpawnSquads();
        SpawnFloorItems();
        PlaceStairs();
        TowardPlayer = new DistanceMap(Map.Walkable);
        _aStar = new AStar(Map.Walkable, SadRogue.Primitives.Distance.Chebyshev);
        UpdatePerception();
        return (TowardPlayer, _aStar);
    }

    /// <summary>
    /// The player goes down the stairs: a new level is generated around them (they keep everything
    /// they carry and know). From the last level, the stairs lead out: the game is won.
    /// </summary>
    public void Descend()
    {
        if (Depth >= Content.Dungeon.Levels)
        {
            IsWon = true;
            Emit(new GameWon(Turn));
            return;
        }
        Depth++;
        _actors.RemoveAll(a => a != Player);
        _squads.Clear();
        _floorItems.Clear();
        _corpses.Clear();
        _lairs.Clear();
        _factionMaps.Clear();
        _paths.Clear();
        Map = GenerateMap();
        Player.Position = Map.Rooms[0].Center;
        Player.Energy = 0;
        PopulateLevel();
        Emit(new LevelEntered(Depth));
    }

    /// <summary>Stairs down in the room farthest (walking) from the arrival point that no lair holds.</summary>
    private void PlaceStairs()
    {
        var fromStart = new DistanceMap(Map.Walkable);
        fromStart.Update([Player.Position]);
        var lairRooms = _lairs.Select(l => l.Zone.Area).ToHashSet();
        var room = Map.Rooms.Skip(1).Where(r => !lairRooms.Contains(r))
            .OrderByDescending(r => ClosestReachableCell(r, fromStart) is var d && d == double.MaxValue ? -1 : d)
            .FirstOrDefault(Map.Rooms[^1]);
        var spot = room.Positions().Where(p => Map.Tiles[p] == Tile.Floor && ActorAt(p) is null && !ItemsAt(p).Any())
            .OrderBy(p => Distance(p, room.Center)).FirstOrDefault(room.Center);
        Map.SetTile(spot, Tile.StairsDown);
        Map.StairsDown = spot;
    }

    public static World Create(ulong seed, int width = 80, int height = 24, ContentDatabase? content = null, int depth = 1) =>
        new(seed, width, height, content ?? ContentDatabase.Default, depth);

    /// <summary>How deep this level is (1 = the first). Gates which items and +X values can appear.</summary>
    public int Depth { get; private set; }

    /// <summary>Runs one player action and, if it used a turn, everyone else's turn.</summary>
    public IReadOnlyList<GameEvent> Step(GameAction playerAction)
    {
        _events.Clear();
        if (IsOver)
            return _events;

        var zoneBefore = Map.ZoneAt(Player.Position);
        int depthBefore = Depth;
        if (!playerAction.Perform(this, Player))
            return [.. _events];
        if (IsWon || Depth != depthBefore)
        {
            Turn++;
            return [.. _events]; // a new level (or the way out): the monsters there haven't noticed yet
        }
        if (Map.ZoneAt(Player.Position) is { } zone && zone != zoneBefore)
        {
            Emit(new ZoneEntered(zone.Name));
            if (Player.Character?.Discover("zone:" + zone.Name) == true)
                GrantXp(XpSource.Zone, zone.Name, Content.Xp.NewZone);
        }

        UpdatePerception();
        RunNonPlayerTurns();

        _actors.RemoveAll(a =>
        {
            if (!a.IsDead || a == Player)
                return false;
            _paths.Remove(a.Id);
            return true;
        });
        Turn++;
        int regen = RegenInterval;
        if (regen > 0 && Turn % regen == 0 && !Player.IsDead && Player.Hp < Player.MaxHp)
            Player.Hp++;
        UpdateFov(); // the player didn't move, so the Dijkstra maps are still valid
        return [.. _events];
    }

    /// <summary>Turns per HP the player regains: player.json's interval, shortened by Regen bonuses (never below 2).</summary>
    public int RegenInterval =>
        Content.Player.RegenInterval <= 0 ? 0 : Math.Max(2, Content.Player.RegenInterval - Player.Bonus(Stat.Regen));

    // ---- Experience ----

    /// <summary>Gives the player XP, scaled by XpGain bonuses (Intelligence).</summary>
    public void GrantXp(XpSource source, string subject, double baseAmount)
    {
        if (Player.Character is not { } character)
            return;
        int amount = (int)Math.Floor(baseAmount * (100 + Player.Bonus(Stat.XpGain)) / 100.0 + 1e-9);
        if (amount <= 0)
            return;
        character.Gain(amount);
        Emit(new XpGained(Player.Id, amount, source, subject));
    }

    /// <summary>The first time the player sees a kind of monster, they learn something from it.</summary>
    private void NoticeNewKinds()
    {
        if (Player.Character is not { } character || ObserverMode)
            return;
        var fov = Map.PlayerFov.BooleanResultView;
        foreach (var actor in _actors)
            if (actor != Player && !actor.IsDead && fov[actor.Position]
                && Content.Monsters.TryGetValue(actor.Kind, out var def) && character.Discover("kind:" + actor.Kind))
                GrantXp(XpSource.Sighting, actor.Kind, def.Xp * Content.Xp.SightFraction);
    }

    private void RunNonPlayerTurns()
    {
        foreach (var maps in _factionMaps.Values)
            maps.TowardDirty = maps.AwayDirty = true;

        FactionPlanner.Think(this);
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

    /// <summary>Chance (percent) that this attack connects, from accuracy against evasion.</summary>
    public int HitChance(Actor attacker, Actor target, bool ranged)
    {
        var rules = Content.Combat;
        int accuracy = attacker.Accuracy - (ranged && IsAdjacent(attacker.Position, target.Position) ? rules.PointBlankPenalty : 0);
        return Math.Clamp(rules.BaseHitChance + rules.HitChancePerPoint * (accuracy - target.Evasion),
            rules.MinHitChance, rules.MaxHitChance);
    }

    /// <summary>
    /// One melee blow or shot: roll to hit (accuracy vs evasion), then damage 1..attack minus a
    /// random 0..defence absorbed by armour, never below the minimum damage. Armour softens hits;
    /// it can't make anyone untouchable.
    /// </summary>
    public void ResolveAttack(Actor attacker, Actor target, bool ranged)
    {
        if (Rng.NextInt(100) >= HitChance(attacker, target, ranged))
        {
            Emit(new ActorAttacked(attacker.Id, target.Id, 0, ranged, Missed: true));
            return;
        }
        int power = ranged ? attacker.RangedAttack : attacker.Attack;
        int roll = Rng.NextInt(1, Math.Max(1, power) + 1);
        int absorbed = Rng.NextInt(0, Math.Max(0, target.Defense) + 1);
        Damage(attacker, target, Math.Max(Content.Combat.MinDamage, roll - absorbed), ranged);
    }

    /// <summary>Applies damage and handles death: the event, and loot dropped where the actor fell.</summary>
    public void Damage(Actor attacker, Actor target, int damage, bool ranged)
    {
        target.Hp -= damage;
        Emit(new ActorAttacked(attacker.Id, target.Id, damage, ranged));
        if (!target.IsDead)
            return;

        Emit(new ActorDied(target.Id, target.Kind, target.Faction.Id, attacker.Id));
        if (attacker == Player && Content.Monsters.TryGetValue(target.Kind, out var killed))
            GrantXp(XpSource.Kill, target.Kind, Content.Dungeon.XpFor(killed.Xp, Depth));
        LeaveCorpse(target);
        int lootDepth = Depth + (Content.Monsters.TryGetValue(target.Kind, out var victimDef) ? victimDef.LootDepthBonus : 0);
        if (target.LootTable is { } table && RollLoot(table, lootDepth) is { } loot)
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
    public Item CreateItem(ItemDef def, int? enchantment = null, int count = 1, int? depth = null) =>
        new(_nextItemId++, def)
        {
            Enchantment = enchantment ?? (def.IsEquipment ? RollEnchantment(depth ?? Depth) : 0),
            Count = count,
        };

    /// <summary>
    /// Picks one entry from a loot table, as found at <paramref name="depth"/> (default: this level).
    /// Items deeper than that are left out of the draw. Null when it rolls "nothing".
    /// </summary>
    public Item? RollLoot(string table, int? depth = null)
    {
        int d = depth ?? Depth;
        var entries = Content.LootTables[table]
            .Where(e => e.Item is null || Content.Items[e.Item].MinDepth <= d)
            .ToList();
        int total = entries.Sum(e => e.Weight);
        if (total <= 0)
            return null;
        int roll = Rng.NextInt(total);
        var entry = entries.First(e => (roll -= e.Weight) < 0);
        return entry.Item is null ? null : CreateItem(Content.Items[entry.Item], depth: d);
    }

    private int RollEnchantment(int depth)
    {
        var table = Content.Enchantments.Where(e => e.MinDepth <= depth).ToList();
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
    /// <summary>
    /// Followers of the same squad queue rather than swap: they're all heading for the leader,
    /// so swapping would only make two of them trade places back and forth forever.
    /// The leader may still swap through its own followers.
    /// </summary>
    private bool SameQueue(Actor self, Actor other) =>
        SquadOf(self) is { } squad && SquadOf(other) == squad && squad.Leader != self;

    /// <summary>The next cell on a shortest path to <paramref name="goal"/>, reusing the cached path when it still applies.</summary>
    private Point? NextPathStep(Actor self, Point goal)
    {
        if (_paths.TryGetValue(self.Id, out var cached) && cached.Goal == goal)
        {
            // Took the step we suggested last time: move along. Didn't (blocked, chose otherwise): suggest it again.
            if (cached.Next < cached.Path.Length && self.Position == cached.Path.GetStep(cached.Next))
                cached.Next++;
            var expectedAt = cached.Next == 0 ? cached.Path.Start : cached.Path.GetStep(cached.Next - 1);
            if (self.Position == expectedAt && cached.Next < cached.Path.Length)
                return cached.Path.GetStep(cached.Next);
        }

        var path = _aStar.ShortestPath(self.Position, goal);
        if (path is null || path.Length == 0)
        {
            _paths.Remove(self.Id);
            return null;
        }
        _paths[self.Id] = new CachedPath(goal, path);
        return path.GetStep(0);
    }

    public Direction? StepToward(Actor self, Point goal)
    {
        if (self.Position == goal)
            return null;

        if (NextPathStep(self, goal) is { } next)
        {
            if (IsFree(next) || (ActorAt(next) is { } blocker && CanSwap(self, blocker) && !SameQueue(self, blocker)))
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
        NoticeNewKinds();
    }

    private Actor AddActor(Actor actor)
    {
        _actors.Add(actor);
        return actor;
    }

    /// <summary>A monster from its stat block, grown for this depth (dungeon.json).</summary>
    private Actor Spawn(MonsterDef def, Point at, Faction? faction = null) =>
        AddActor(new Actor(_nextId++, def.Id, faction ?? Content.Factions[def.Faction], at,
            Content.Dungeon.HpFor(def.MaxHp, Depth), def.Attack + Content.Dungeon.AttackBonus(Depth),
            def.Defense + Content.Dungeon.DefenseBonus(Depth))
        {
            BaseAccuracy = def.Accuracy + Content.Dungeon.AccuracyBonus(Depth),
            BaseEvasion = def.Evasion,
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
            Character = new Character(Content, def.Attributes),
            BaseAccuracy = def.Accuracy,
            BaseEvasion = def.Evasion,
            Inventory = new Inventory(def.InventoryCapacity),
            Equipment = new Equipment(),
        });
        foreach (var k in def.Kit)
        {
            var item = player.Inventory!.Add(CreateItem(Content.Items[k.Item], k.Enchantment, k.Count));
            if (k.Equip)
                player.Equipment!.Equip(item);
        }
        player.Hp = player.MaxHp; // Constitution adds to the base
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

        // Lairs go where they're farthest from the start and from each other.
        var taken = new HashSet<Rectangle>();
        var awayFrom = new List<DistanceMap> { fromStart };
        foreach (var lair in Content.Lairs.Where(l => l.AppearsAt(Depth)))
        {
            var best = Map.Rooms.Skip(1)
                .Where(r => !taken.Contains(r))
                .Select(r => (Room: r, Score: awayFrom.Min(m => ClosestReachableCell(r, m))))
                .Where(x => x.Score < double.MaxValue)
                .OrderByDescending(x => x.Score)
                .Take(1).ToList();
            if (best.Count == 0)
                break;
            var room = best[0].Room;
            taken.Add(room);
            var built = BuildLair(lair, room);
            var fromLair = new DistanceMap(Map.Walkable);
            fromLair.Update([built.Zone.Heart]);
            awayFrom.Add(fromLair);
        }

        var templates = Content.SquadTemplates.Where(t => t.AppearsAt(Depth)).ToList();
        int totalWeight = templates.Sum(t => t.Weight);
        if (totalWeight <= 0)
            return;

        double occupancy = Content.RoomOccupancy + Content.Dungeon.RoomOccupancyPerLevel * (Depth - 1);
        foreach (var room in Map.Rooms.Skip(1))
        {
            if (Rng.NextDouble() >= occupancy)
                continue;
            if (taken.Contains(room) || ClosestReachableCell(room, fromStart) < Content.SafeStartDistance)
                continue;

            int roll = Rng.NextInt(totalWeight);
            FillSquad(templates.First(t => (roll -= t.Weight) < 0), room);
        }
    }

    /// <summary>Architecture, zone, the squad with its home and leash, and the remains lying about.</summary>
    private Lair BuildLair(SquadTemplateDef lair, Rectangle room)
    {
        if (lair.Architecture is { } style)
            LairBuilder.Build(Map, room, style);
        var home = room.Positions().Where(p => Map.Walkable[p])
            .OrderBy(p => Distance(p, room.Center)).First();
        var zone = new Zone(lair.Zone ?? lair.Name, room, lair.Faction, home);
        Map.Zones.Add(zone);
        var built = new Lair(zone, lair);
        _lairs.Add(built);

        if (FillSquad(lair, room) is { } squad)
        {
            built.Squad = squad;
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
        return built;
    }

    /// <summary>
    /// Sends a fresh squad out of a lair (see the lair's "muster"). Null if nobody could be placed.
    /// </summary>
    public Squad? Muster(Lair lair, SquadTemplateDef template) => FillSquad(template, lair.Zone.Area);

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
