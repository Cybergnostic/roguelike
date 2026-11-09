using GoRogue.Pathing;
using Roguelike.Core.Actions;
using Roguelike.Core.Content;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.AI.Strategy;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.History;
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

    /// <summary>The depth of the hand-drawn surface map; the dungeon starts at 1.</summary>
    public const int SurfaceDepth = 0;

    public static readonly Direction[] EightWay = [.. AdjacencyRule.EightWay.DirectionsOfNeighbors()];

    public ulong Seed { get; }
    public ContentDatabase Content { get; }
    public FactionRelations Relations => Content.Relations;

    /// <summary>
    /// The randomness in use now: while a map is being built, that map's own stream, so what a
    /// seed generates can't depend on how the game has been played; otherwise the play stream.
    /// </summary>
    public IEnhancedRandom Rng => _generating ?? _playRng;
    private readonly IEnhancedRandom _playRng;
    private IEnhancedRandom? _generating;

    public GameMap Map => _location.Map;
    public Actor Player { get; }

    /// <summary>The player took the stairs down from the last level.</summary>
    public bool IsWon { get; private set; }

    /// <summary>Whether the game is over, either way.</summary>
    public bool IsOver => IsWon || Player.IsDead;
    public int Turn { get; private set; }
    public IReadOnlyList<Actor> Actors => _actors;
    public IReadOnlyList<Squad> Squads => _squads;

    /// <summary>What happened here before the player came.</summary>
    public SiteHistory History { get; }

    /// <summary>Distance map toward the player (debug heatmap, tests, bots).</summary>
    public DistanceMap TowardPlayer => _location.TowardPlayer;

    /// <summary>Per-faction "toward enemies" and "away from enemies" maps, rebuilt lazily each turn.</summary>
    private sealed class FactionMaps(GameMap map)
    {
        public readonly DistanceMap Toward = new(map.Walkable);
        public readonly SafetyMap Away = new(map.Walkable);
        public bool TowardDirty = true;
        public bool AwayDirty = true;
    }

    /// <summary>
    /// Everything that belongs to one map. Locations are kept for the whole game, so a place you
    /// leave is still there when you come back; time stands still in the ones you aren't in.
    /// </summary>
    private sealed class Location(int depth, GameMap map)
    {
        public int Depth { get; } = depth;
        public GameMap Map { get; } = map;
        public List<Actor> Actors { get; } = [];
        public List<Squad> Squads { get; } = [];
        public List<FloorItem> FloorItems { get; } = [];
        public List<Corpse> Corpses { get; } = [];
        public List<Lair> Lairs { get; } = [];
        public List<Camp> Camps { get; } = [];
        public List<PlaceIncursion> PendingIncursions { get; } = [];
        public Dictionary<Faction, FactionMaps> FactionMaps { get; } = [];
        public Dictionary<int, CachedPath> Paths { get; } = [];
        public DistanceMap TowardPlayer { get; } = new(map.Walkable);
        public AStar AStar { get; } = new(map.Passable, SadRogue.Primitives.Distance.Chebyshev);
    }

    private readonly Dictionary<int, Location> _locations = [];
    private Location _location = null!; // set by the constructor

    // The state of wherever the player is now.
    private List<Actor> _actors => _location.Actors;
    private List<Squad> _squads => _location.Squads;
    private List<FloorItem> _floorItems => _location.FloorItems;
    private List<Corpse> _corpses => _location.Corpses;
    private List<Lair> _lairs => _location.Lairs;
    private Dictionary<Faction, FactionMaps> _factionMaps => _location.FactionMaps;
    private Dictionary<int, CachedPath> _paths => _location.Paths;
    private AStar _aStar => _location.AStar;

    /// <summary>An item lying on the map. Owner: the camp it belongs to, if any.</summary>
    public sealed record FloorItem(Item Item, Point Position, Camp? Owner = null);

    /// <summary>The run's errand, if it began in the valley.</summary>
    public sealed class Errand(PlaceObjective def, Item item)
    {
        public PlaceObjective Def { get; } = def;
        public Item Item { get; } = item;
        public bool Completed { get; internal set; }
    }

    public Errand? Objective { get; private set; }

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

    /// <summary>The camps on this map.</summary>
    public IReadOnlyList<Camp> Camps => _location.Camps;

    private int _nextCorpseId;
    private int _nextSquadId;
    private int _nextItemId;
    private readonly List<GameEvent> _events = [];

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

    private int _nextId;
    private readonly int _width, _height;

    private World(ulong seed, int width, int height, ContentDatabase content, int depth)
    {
        Seed = seed;
        Content = content;
        _playRng = new MizuchiRandom(seed);
        (_width, _height) = (width, height);
        History = SiteHistory.Generate(content.History, new MizuchiRandom(LocationSeed(seed, HistoryStream)));
        BeginLocation(depth);
        Player = CreatePlayer(depth == SurfaceDepth ? new Point(content.Surface.Start[0], content.Surface.Start[1]) : Map.Rooms[0].Center);
        FinishLocation();
    }

    /// <summary>Not a place: the "location" whose stream the site's history is rolled from.</summary>
    private const int HistoryStream = -1;

    /// <summary>A map's own seed, from the world seed and where the map is (SplitMix64's mixer).</summary>
    private static ulong LocationSeed(ulong worldSeed, int depth)
    {
        ulong z = worldSeed + (ulong)depth * 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Starts building a new map; the player must be placed before <see cref="FinishLocation"/>.</summary>
    private void BeginLocation(int depth)
    {
        _generating = new MizuchiRandom(LocationSeed(Seed, depth));
        _location = new Location(depth, depth == SurfaceDepth ? PlaceBuilder.Build(Content.Surface, History.Tribe) : GenerateMap());
        _locations[depth] = _location;
    }

    private GameMap GenerateMap()
    {
        var map = MapGenerator.RoomsAndMazes(_width, _height, Rng);
        if (map.Rooms.Count < 2)
            throw new InvalidOperationException("Map generation produced too few rooms.");
        return map;
    }

    /// <summary>Squads, items and stairs for the map being built, then perception.</summary>
    private void FinishLocation()
    {
        if (Depth == SurfaceDepth)
            PopulateSurface();
        else
        {
            ShapeLevelFromHistory();
            SpawnSquads();
            SpawnFloorItems();
            PlaceStairs();
            LeaveTracesOfHistory();
        }
        _generating = null;
        UpdatePerception();
    }

    /// <summary>The old mine's architecture, built before anyone is placed so nobody ends up inside a rock.</summary>
    private void ShapeLevelFromHistory()
    {
        var rooms = Map.Rooms.Skip(1).OrderBy(_ => Rng.NextDouble()).ToList();
        int workings = Depth <= History.MineDepth ? Math.Min(Content.History.WorkingsRooms, rooms.Count) : 0;
        foreach (var room in rooms.Take(workings))
            LairBuilder.Workings(Map, room);
        if (Depth == History.FallDepth && History.Fall == FallKind.Collapse)
            foreach (var room in rooms.Skip(workings).Take(Content.History.Collapse.Rooms))
                LairBuilder.Rubble(Map, room, Rng);
    }

    /// <summary>What the past left on this level: its stone, and where the mine fell, the dead and the heirloom.</summary>
    private void LeaveTracesOfHistory()
    {
        var lairRooms = _lairs.Select(l => l.Zone.Area).ToHashSet();
        var rooms = Map.Rooms.Skip(1).Where(r => !lairRooms.Contains(r)).OrderBy(_ => Rng.NextDouble()).ToList();
        if (rooms.Count == 0)
            return;

        if (History.Eras.Any(e => e.Depth == Depth) && QuietFloorCell(rooms) is { } stone)
            Map.SetTile(stone, Tile.Inscription);
        if (Depth != History.FallDepth)
            return;

        if (QuietFloorCell(rooms) is { } resting)
            PlaceOnFloor(CreateHeirloom(), resting);

        switch (History.Fall)
        {
            case FallKind.War:
                LayOutTheDead(Content.History.Battlefield, rooms);
                break;
            case FallKind.Plague:
                LayOutTheDead(Content.History.Plague, rooms);
                break;
            case FallKind.Collapse:
                for (int i = 0; i < Content.History.Collapse.Items; i++)
                    if (RollLoot("floor") is { } item && QuietFloorCell(rooms) is { } p)
                        PlaceOnFloor(item, p);
                break;
        }
    }

    /// <summary>The bodies where they fell, and the ones that got up.</summary>
    private void LayOutTheDead(BattlefieldDef field, List<Rectangle> rooms)
    {
        foreach (var entry in field.Corpses)
            for (int i = 0; i < entry.Count; i++)
                if (RandomFreeFloor(rooms[i % rooms.Count]) is { } p && !CorpsesAt(p).Any())
                    _corpses.Add(new Corpse(_nextCorpseId++, entry.Monster, p, 0));
        var template = Content.SquadTemplates.First(t => t.Name == field.Squad);
        for (int i = 0; i < field.Squads; i++)
            FillSquad(template, rooms[i % rooms.Count]);
    }

    /// <summary>A plain floor cell with nothing on it, in the first of these rooms that has one.</summary>
    private Point? QuietFloorCell(List<Rectangle> rooms)
    {
        foreach (var room in rooms)
            for (int tries = 0; tries < 20; tries++)
                if (RandomFreeFloor(room) is { } p && Map.Tiles[p] == Tile.Floor && !ItemsAt(p).Any())
                    return p;
        return null;
    }

    /// <summary>The founder's own item: a named unique, the same quality in every game.</summary>
    private Item CreateHeirloom()
    {
        var rules = Content.History.Heirloom;
        var def = Content.Items[History.HeirloomItem];
        return new(_nextItemId++, def)
        {
            Enchantment = rules.Enchantment,
            Rarity = Rarity.Unique,
            Affixes = RollAffixes(def.Category, DangerDepth, Rng.NextInt(rules.Affixes[0], rules.Affixes[1] + 1)),
            Special = rules.Special ? Pick(Content.Affixes.Specials[def.Category]) : null,
            NameWords = History.HeirloomWords,
        };
    }

    /// <summary>The squads and loot drawn on the surface map.</summary>
    private void PopulateSurface()
    {
        var camps = new Dictionary<string, Camp>();
        foreach (var group in Content.Surface.Groups)
        {
            var squad = new Squad(_nextSquadId++, Content.Factions[group.Faction]);
            foreach (var member in Garrison(group))
            {
                var def = Content.Monsters[member.Monster];
                var monster = Spawn(def, new Point(member.At[0], member.At[1]));
                monster.Brain = new SquadMemberBrain(squad);
                squad.Add(monster, def.Role);
            }
            var home = new Point(group.Home[0], group.Home[1]);
            squad.Home = home;
            squad.Leash = group.Leash;
            squad.HomeDistance = new DistanceMap(Map.Passable);
            squad.HomeDistance.Update([home]);
            if (group.Territory is { } territory)
                squad.Camp = camps.TryGetValue(territory, out var known) ? known
                    : camps[territory] = BuildCamp(territory);
            if (group.Order == "guard")
                foreach (var member in squad.Members)
                    squad.Posts[member.Id] = member.Position;
            else
                squad.Route = group.Route!.Select(p => new Point(p[0], p[1])).ToList();
            if (group.Rally is { } rally)
                squad.Rally = new Point(rally[0], rally[1]);
            if (group.Gate is { } gate)
                squad.GateToClose = new Point(gate[0], gate[1]);
            _squads.Add(squad);
        }
        foreach (var spot in Content.Surface.Loot)
            if (RollLoot(spot.Table) is { } item)
                PlaceOnFloor(item, new Point(spot.At[0], spot.At[1]), spot.Owner is { } owner ? camps[owner] : null);
        if (Content.Surface.Objective is { } objective)
        {
            var relic = CreateItem(Content.Items[objective.Item]);
            Objective = new Errand(objective, relic);
            PlaceOnFloor(relic, new Point(objective.At[0], objective.At[1]));
        }
        _location.PendingIncursions.AddRange(Content.Surface.Incursions ?? []);
    }

    /// <summary>The map's zone for a name as written in surface.json (before "{tribe}" is filled in).</summary>
    private Zone SurfaceZone(string name) =>
        Map.Zones[Content.Surface.Zones.ToList().FindIndex(z => z.Name == name)];

    /// <summary>The camp in a zone, on the terms of the tribe that holds it now.</summary>
    private Camp BuildCamp(string zoneName)
    {
        var holders = History.Holders;
        var toll = Content.Surface.Zones.First(z => z.Name == zoneName).Toll is { } t
            ? new TollTerms(new Point(t.Gate[0], t.Gate[1]), holders.Price ?? t.Price, holders.Turns ?? t.Turns, t.Range, t.WarnBefore)
            : null;
        var camp = new Camp(SurfaceZone(zoneName), toll);
        _location.Camps.Add(camp);
        return camp;
    }

    /// <summary>A group's members under the tribe that holds the camp now.</summary>
    private IEnumerable<PlaceMember> Garrison(PlaceGroup group)
    {
        var removed = (History.Holders.Remove ?? []).Where(r => r.Group == group.Name).Select(r => r.Monster).ToList();
        foreach (var member in group.Members)
            if (!removed.Remove(member.Monster))
                yield return member;
        foreach (var added in (History.Holders.Add ?? []).Where(a => a.Group == group.Name))
            yield return new PlaceMember(added.Monster, added.At!);
    }

    /// <summary>Opens or closes a gate. Routes don't change: actors plan through closed gates and open them.</summary>
    public void SetGate(Point at, bool open, Actor actor)
    {
        Map.SetTile(at, open ? Tile.GateOpen : Tile.GateClosed);
        Emit(new GateMoved(actor.Id, at, open));
    }

    /// <summary>
    /// The player goes down the stairs, to a level built on the first visit and kept from then on
    /// (they keep everything they carry and know). From the last level, the stairs lead out: the game is won.
    /// </summary>
    public void Descend()
    {
        if (Depth >= Content.Dungeon.Levels)
        {
            IsWon = true;
            Emit(new GameWon(Turn));
            return;
        }
        Travel(Depth + 1);
    }

    /// <summary>The player climbs back up the stairs to the level above, just as they left it.</summary>
    public void Ascend() => Travel(Depth - 1);

    private void Travel(int depth)
    {
        bool goingUp = depth < Depth;
        _actors.Remove(Player);
        if (_locations.TryGetValue(depth, out var known))
        {
            _location = known;
            var arrival = (goingUp ? Map.StairsDown : Map.StairsUp)!.Value;
            // Whoever is standing on the stairs makes room.
            if (ActorAt(arrival) is { } inTheWay)
                inTheWay.Position = Map.Tiles.Positions().Where(IsFree).MinBy(p => Distance(p, arrival));
            Player.Position = arrival;
            _actors.Insert(0, Player);
            UpdatePerception();
        }
        else
        {
            BeginLocation(depth);
            Player.Position = Map.Rooms[0].Center;
            _actors.Add(Player);
            FinishLocation();
        }
        Player.Energy = 0;
        Emit(new LevelEntered(depth, goingUp));
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

        // The way back: under the player's feet, if there's a level above to go back to.
        if (_locations.ContainsKey(Depth - 1))
        {
            Map.SetTile(Player.Position, Tile.StairsUp);
            Map.StairsUp = Player.Position;
        }
    }

    public static World Create(ulong seed, int width = 80, int height = 24, ContentDatabase? content = null, int depth = 1) =>
        new(seed, width, height, content ?? ContentDatabase.Default, depth);

    /// <summary>How deep this level is (1 = the first). Gates which items and +X values can appear.</summary>
    public int Depth => _location.Depth;

    /// <summary>How dangerous and rewarding this place is: the surface counts as the first level.</summary>
    private int DangerDepth => Math.Max(1, Depth);

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
        _bumpWarnedAbout = null;
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
        ReadStoneUnderfoot();
        UpdateCamps();
        CheckObjective();
        LaunchIncursions();
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

    // ---- Camps: challenges, tolls and paid time ----

    /// <summary>A step past the challenge range before guards forget they've already spoken to you.</summary>
    private const int ChallengeForgetMargin = 2;

    /// <summary>What the guards say this turn, and whether the player's paid time has run out.</summary>
    private void UpdateCamps()
    {
        foreach (var camp in _location.Camps)
        {
            if (camp.Toll is not { } toll || camp.Provoked)
                continue;
            var at = Player.Position;
            UpdatePass(camp, toll, at);
            if (camp.HasPass(Turn))
                continue;

            int distance = Distance(at, toll.Gate);
            if (distance > toll.Range + ChallengeForgetMargin)
            {
                camp.Challenged = camp.WarnedAtGate = false;
                continue;
            }
            if (distance > toll.Range || !camp.Tolerates(at, Turn) || GuardWatching(camp) is not { } guard)
                continue;
            if (!camp.Challenged)
            {
                camp.Challenged = camp.TermsKnown = true;
                Emit(new GateChallenged(guard.Id, camp.Zone.Name, toll.Price, toll.Turns));
            }
            if (camp.IsThreshold(at) && !camp.WarnedAtGate)
            {
                camp.WarnedAtGate = true;
                Emit(new TrespassWarned(guard.Id, camp.Zone.Name));
            }
        }
    }

    private void UpdatePass(Camp camp, TollTerms toll, Point at)
    {
        if (camp.PassUntil is not { } until)
            return;
        if (Turn >= until)
        {
            camp.PassUntil = null;
            camp.Challenged = camp.WarnedAtGate = false; // they'll ask again
            Emit(new PassExpired(camp.Zone.Name, camp.IsInside(at)));
        }
        else if (camp.IsInside(at) && !camp.PassWarned && until - Turn <= toll.WarnBefore)
        {
            camp.PassWarned = true;
            Emit(new PassRunningOut(camp.Zone.Name, until - Turn));
        }
    }

    private void Alarm(Camp camp)
    {
        camp.Provoked = true;
        Emit(new CampAlarmed(camp.Zone.Name));
    }

    /// <summary>Call before taking an item off the floor: being seen taking a camp's goods turns the camp against the thief.</summary>
    public void NoteTheft(Actor thief, Item item)
    {
        if (thief != Player || _floorItems.Find(f => f.Item == item)?.Owner is not { } camp)
            return;
        var witness = GuardWatching(camp);
        Emit(new TheftCommitted(camp.Zone.Name, witness?.Id));
        if (witness is not null && !camp.Provoked)
            Alarm(camp);
    }

    private static string LoreKey(Era era) => "lore:" + era.Kind;

    /// <summary>Whether the player has read the stone that tells this part of the past.</summary>
    public bool KnowsOf(Era era) => Player.Character?.HasDiscovered(LoreKey(era)) == true;

    private void ReadStoneUnderfoot()
    {
        if (Map.Tiles[Player.Position] != Tile.Inscription || Player.Character is not { } character
            || History.Eras.FirstOrDefault(e => e.Depth == Depth) is not { } era || !character.Discover(LoreKey(era)))
            return;
        Emit(new LoreDiscovered(era));
        GrantXp(XpSource.Lore, era.Kind.ToString(), Content.History.LoreXp);
    }

    /// <summary>Hands the errand's object over when the player carries it to the place it belongs.</summary>
    private void CheckObjective()
    {
        if (Objective is not { Completed: false } errand || Depth != SurfaceDepth
            || Player.Position != new Point(errand.Def.ReturnTo[0], errand.Def.ReturnTo[1])
            || Player.Inventory?.Find(errand.Item.Id) is null)
            return;
        errand.Completed = true;
        Player.Inventory.Remove(errand.Item);
        Player.Gold += errand.Def.Gold;
        Item? reward = null;
        if (errand.Def.Reward is { } id)
        {
            reward = CreateItem(Content.Items[id], count: errand.Def.RewardCount);
            Player.Inventory.Add(reward);
        }
        Emit(new ObjectiveCompleted(errand.Item, errand.Def.Gold, errand.Def.Xp, reward));
        GrantXp(XpSource.Objective, errand.Item.Def.Id, errand.Def.Xp);
    }

    /// <summary>Squads scheduled to arrive on this map, whose time has come.</summary>
    private void LaunchIncursions()
    {
        var pending = _location.PendingIncursions;
        foreach (var incursion in pending.Where(i => Turn >= i.Turn).ToList())
        {
            pending.Remove(incursion);
            var target = SurfaceZone(incursion.Target);
            var squad = new Squad(_nextSquadId++, Content.Factions[incursion.Faction]);
            _squads.Add(squad);
            foreach (var member in incursion.Members)
            {
                var wanted = new Point(member.At[0], member.At[1]);
                var at = IsFree(wanted) ? wanted : Map.Tiles.Positions().Where(IsFree).MinBy(p => Distance(p, wanted));
                SpawnMonster(member.Monster, at, squad);
            }
            squad.RaidTarget = squad.Destination = target.Heart;
            squad.RaidZone = target.Name;
            Emit(new IncursionBegan(squad.Id, incursion.Faction, target.Name));
        }
    }

    /// <summary>A living defender of the camp who can see the player, or null.</summary>
    private Actor? GuardWatching(Camp camp) =>
        _squads.Where(s => s.Camp == camp).SelectMany(s => s.Members).FirstOrDefault(m => !m.IsDead && CanSee(m, Player));

    /// <summary>The camp whose gate is close enough to deal with, or null.</summary>
    public Camp? TollCampNearby() =>
        _location.Camps.FirstOrDefault(c => c.Toll is { } t && Distance(Player.Position, t.Gate) <= t.Range);

    /// <summary>Why the player can't pay a toll right now, or null if they can.</summary>
    public FailureReason? TollProblem()
    {
        if (TollCampNearby() is not { } camp)
            return FailureReason.NoTollHere;
        if (!camp.Tolerates(Player.Position, Turn))
            return FailureReason.TollRefused; // too late to talk: they're already fighting you
        if (camp.PassTurnsLeft(Turn) > camp.Toll!.WarnBefore)
            return FailureReason.TollAlreadyPaid;
        if (GuardWatching(camp) is null)
            return FailureReason.NoOneToPay;
        return Player.Gold < camp.Toll.Price ? FailureReason.NotEnoughGold : null;
    }

    /// <summary>Hands over the money and starts the paid time. Check <see cref="TollProblem"/> first.</summary>
    public void PayToll()
    {
        var camp = TollCampNearby()!;
        var toll = camp.Toll!;
        Player.Gold -= toll.Price;
        camp.PassUntil = Turn + toll.Turns;
        camp.PassWarned = false;
        camp.TermsKnown = true;
        Emit(new TollPaid(camp.Zone.Name, toll.Price, toll.Turns));
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
        if (SquadOf(target) is { } targetSquad)
        {
            targetSquad.LearnOfAttacker(attacker, Turn);
            if (attacker == Player && targetSquad.Camp is { Provoked: false } camp)
                Alarm(camp);
        }
        if (Rng.NextInt(100) >= HitChance(attacker, target, ranged))
        {
            Emit(new ActorAttacked(attacker.Id, target.Id, 0, ranged, Missed: true));
            return;
        }
        int power = ranged ? attacker.RangedAttack : attacker.Attack;
        int roll = Rng.NextInt(1, Math.Max(1, power) + 1);
        int armour = ranged && attacker.HasSpecial("piercing") ? target.Defense / 2 : target.Defense;
        int absorbed = Rng.NextInt(0, Math.Max(0, armour) + 1);
        int dealt = Math.Max(Content.Combat.MinDamage, roll - absorbed);
        Damage(attacker, target, dealt, ranged);
        if (ranged)
            return;

        // Lifesteal: a melee hit heals the striker by a quarter of the damage (at least 1).
        if (dealt > 0 && !attacker.IsDead && attacker.Hp < attacker.MaxHp && attacker.HasSpecial("lifesteal"))
        {
            attacker.Hp = Math.Min(attacker.MaxHp, attacker.Hp + Math.Max(1, dealt / 4));
            Emit(new SpecialUsed(attacker.Id, "lifesteal", target.Id));
        }
        // Thorns: whoever lands a melee blow on the wearer takes 1-3 damage back.
        if (!target.IsDead && !attacker.IsDead && target.HasSpecial("thorns"))
        {
            Emit(new SpecialUsed(target.Id, "thorns", attacker.Id));
            Damage(target, attacker, Rng.NextInt(1, 4), ranged: false);
        }
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
            GrantXp(XpSource.Kill, target.Kind, Content.Dungeon.XpFor(killed.Xp, DangerDepth));
        LeaveCorpse(target);
        int lootDepth = DangerDepth + (Content.Monsters.TryGetValue(target.Kind, out var victimDef) ? victimDef.LootDepthBonus : 0);
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

    public void PlaceOnFloor(Item item, Point p, Camp? owner = null) => _floorItems.Add(new FloorItem(item, p, owner));

    public bool TakeFromFloor(Item item) => _floorItems.RemoveAll(f => f.Item == item) > 0;

    /// <summary>Makes a new item. Equipment rolls its +X unless one is given.</summary>
    /// <summary>
    /// A new item. Equipment generated at random (no <paramref name="enchantment"/> given) rolls
    /// its +X, then its rarity tier and that tier's affixes, special and name, for <paramref name="depth"/>
    /// (default: this level). An item with a given enchantment (the starting kit, tests) is plain.
    /// </summary>
    public Item CreateItem(ItemDef def, int? enchantment = null, int count = 1, int? depth = null)
    {
        if (!def.IsEquipment || enchantment is not null)
            return new(_nextItemId++, def) { Enchantment = enchantment ?? 0, Count = count };

        int d = depth ?? DangerDepth;
        int plus = RollEnchantment(d);
        var rules = Content.Affixes;
        if (RollTier(d) is not { } tier)
            return new(_nextItemId++, def) { Enchantment = plus, Count = count };

        var affixes = RollAffixes(def.Category, d, Rng.NextInt(tier.AffixMin, tier.AffixMax + 1));
        string? special = tier.Special ? Pick(rules.Specials[def.Category]) : null;
        IReadOnlyList<string>? words = tier.Rarity >= Rarity.Rare
            ? [Pick(rules.Names.First), Pick(rules.Names.Second[def.Category])]
            : null;
        return new(_nextItemId++, def)
        {
            Enchantment = plus,
            Count = count,
            Rarity = affixes.Count > 0 || special is not null ? tier.Rarity : Rarity.Common,
            Affixes = affixes,
            Special = special,
            NameWords = words,
        };
    }

    /// <summary>A tier above common for an item found at <paramref name="depth"/>, or null for common.</summary>
    private RarityTierDef? RollTier(int depth)
    {
        double roll = Rng.NextDouble() * 100;
        foreach (var tier in Content.Affixes.Tiers)
        {
            double chance = tier.ChanceAt(depth);
            if (roll < chance)
                return tier;
            roll -= chance;
        }
        return null;
    }

    /// <summary>Up to <paramref name="count"/> different affixes for the slot, picked by weight.</summary>
    private List<AffixRoll> RollAffixes(ItemCategory slot, int depth, int count)
    {
        var pool = Content.Affixes.Pool.Where(a => a.Slots.Contains(slot) && a.MinDepth <= depth).ToList();
        var rolled = new List<AffixRoll>();
        while (rolled.Count < count && pool.Count > 0)
        {
            int roll = Rng.NextInt(pool.Sum(a => a.Weight));
            var def = pool.First(a => (roll -= a.Weight) < 0);
            pool.Remove(def);
            rolled.Add(new AffixRoll(def, def.ValueAt(Rng.NextInt(def.Min, def.Max + 1), depth)));
        }
        return rolled;
    }

    private T Pick<T>(IReadOnlyList<T> list) => list[Rng.NextInt(list.Count)];

    /// <summary>
    /// Picks one entry from a loot table, as found at <paramref name="depth"/> (default: this level).
    /// Items deeper than that are left out of the draw. Null when it rolls "nothing".
    /// </summary>
    public Item? RollLoot(string table, int? depth = null)
    {
        int d = depth ?? DangerDepth;
        var entries = Content.LootTables[table]
            .Where(e => e.Item is null || Content.Items[e.Item].MinDepth <= d)
            .ToList();
        int total = entries.Sum(e => e.Weight);
        if (total <= 0)
            return null;
        int roll = Rng.NextInt(total);
        var entry = entries.First(e => (roll -= e.Weight) < 0);
        if (entry.Item is null)
            return null;
        int count = entry.Max > entry.Min ? Rng.NextInt(entry.Min, entry.Max + 1) : entry.Min;
        return CreateItem(Content.Items[entry.Item], count: count, depth: d);
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

    /// <summary>Like <see cref="IsFree"/>, but counts a closed gate: stepping into one opens it.</summary>
    private bool IsFreeToStep(Point p) => Map.InBounds(p) && Map.Passable[p] && ActorAt(p) is null;

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
    public bool AreHostile(Actor a, Actor b) => CouldFight(a, b) && !Tolerated(a, b);

    /// <summary>Their factions are enemies, whether or not they're fighting right now.</summary>
    public bool CouldFight(Actor a, Actor b) =>
        a != b && !a.IsDead && !b.IsDead && !IsUntouchable(a) && !IsUntouchable(b)
        && Relations.AreHostile(a.Faction, b.Faction);

    /// <summary>One is the player and the other belongs to a camp that's leaving the player alone.</summary>
    private bool Tolerated(Actor a, Actor b)
    {
        var other = a == Player ? b : b == Player ? a : null;
        return other is not null && SquadOf(other)?.Camp is { } camp && camp.Tolerates(Player.Position, Turn);
    }

    private int? _bumpWarnedAbout;

    /// <summary>True if the player was already warned about attacking this actor; otherwise records the warning.</summary>
    public bool AlreadyWarnedAbout(Actor target)
    {
        if (_bumpWarnedAbout == target.Id)
            return true;
        _bumpWarnedAbout = target.Id;
        return false;
    }

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

    /// <summary>
    /// Dijkstra map toward every actor hostile to <paramref name="faction"/>, seen or not.
    /// Omniscient, so for debug views only; AI uses <see cref="Squad.TowardKnownThreats"/>.
    /// </summary>
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

    /// <summary>Flee map away from every actor hostile to <paramref name="faction"/>, seen or not. Debug views only.</summary>
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
            if (!IsFreeToStep(p) || map[p] is not { } value || value >= here)
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

    /// <summary>
    /// The next step of a route that goes around everyone in the way. Costs a whole-map search,
    /// so it's for scripted player travel and for actors whose direct way is blocked, not routine AI steps.
    /// </summary>
    public Direction? StepAroundOthers(Actor self, Point goal)
    {
        var open = new ArrayView<bool>(Map.Width, Map.Height);
        var occupied = _actors.Where(a => !a.IsDead).Select(a => a.Position).ToHashSet();
        foreach (var p in open.Positions())
            open[p] = Map.Passable[p] && (p == self.Position || p == goal || !occupied.Contains(p));
        var map = new DistanceMap(open);
        map.Update([goal]);
        return StepDownhill(self, map);
    }

    public Direction? StepToward(Actor self, Point goal)
    {
        if (self.Position == goal)
            return null;

        if (NextPathStep(self, goal) is { } next)
        {
            if (IsFreeToStep(next) || (ActorAt(next) is { } blocker && CanSwap(self, blocker) && !SameQueue(self, blocker)))
                return Direction.GetDirection(self.Position, next);
        }

        int current = Distance(self.Position, goal);
        foreach (var dir in EightWay)
        {
            var p = self.Position + dir;
            if (IsFree(p) && Distance(p, goal) < current) // shuffling closer never opens a gate
                return dir;
        }
        return StepAroundOthers(self, goal);
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
            Content.Dungeon.HpFor(def.MaxHp, DangerDepth), def.Attack + Content.Dungeon.AttackBonus(DangerDepth),
            def.Defense + Content.Dungeon.DefenseBonus(DangerDepth))
        {
            BaseAccuracy = def.Accuracy + Content.Dungeon.AccuracyBonus(DangerDepth),
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
            Gold = def.Gold,
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
