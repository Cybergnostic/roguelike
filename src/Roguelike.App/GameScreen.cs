using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using Console = SadConsole.Console;

namespace Roguelike.App;

/// <summary>Root screen: map, sidebar, status line and message log. Owns input; draws the World.</summary>
public sealed partial class GameScreen : ScreenObject
{
    /// <summary>The view of the map, in square cells; the world can be larger and scrolls under it.</summary>
    public const int MapWidth = 62;
    public const int MapHeight = 36;

    /// <summary>How big dungeon levels are.</summary>
    public const int WorldWidth = 80;
    public const int WorldHeight = 24;

    // Everything else is laid out in narrow text cells of 9x18 pixels. A map cell is 18x18.
    private const int CellWidth = 9;
    private const int CellHeight = 18;
    public const int SidebarWidth = 36;
    public const int LogRows = 8;
    public const int LogLines = 7;
    public const int UiWidth = MapWidth * 2;
    public const int Width = UiWidth + SidebarWidth;
    public const int Height = 2 + MapHeight + 2 + LogRows;

    private static readonly Dictionary<Keys, Direction> MoveKeys = new()
    {
        [Keys.Up] = Direction.Up, [Keys.Down] = Direction.Down,
        [Keys.Left] = Direction.Left, [Keys.Right] = Direction.Right,
        [Keys.K] = Direction.Up, [Keys.J] = Direction.Down,
        [Keys.H] = Direction.Left, [Keys.L] = Direction.Right,
        [Keys.Y] = Direction.UpLeft, [Keys.U] = Direction.UpRight,
        [Keys.B] = Direction.DownLeft, [Keys.N] = Direction.DownRight,
        [Keys.NumPad8] = Direction.Up, [Keys.NumPad2] = Direction.Down,
        [Keys.NumPad4] = Direction.Left, [Keys.NumPad6] = Direction.Right,
        [Keys.NumPad7] = Direction.UpLeft, [Keys.NumPad9] = Direction.UpRight,
        [Keys.NumPad1] = Direction.DownLeft, [Keys.NumPad3] = Direction.DownRight,
    };

    private readonly Console _map;
    private readonly Console _ui;
    private readonly Console _sidebar;
    private readonly Console _header;
    private readonly Console _status;
    private readonly Console _log;
    private readonly Console _keys;
    private readonly List<(string Text, Color Color)> _messages = [];

    private World _world;

    public GameScreen()
    {
        var uiFont = Game.Instance.Fonts["UiFont"];
        var mapFont = Game.Instance.Fonts["MapFont"];
        Console Panel(int width, int height, int x, int y, Color background)
        {
            var panel = new Console(width, height) { Font = uiFont, FontSize = uiFont.GetFontSize(IFont.Sizes.One), UsePixelPositioning = true, Position = new Point(x, y) };
            panel.Surface.DefaultBackground = background;
            panel.Clear();
            Children.Add(panel);
            return panel;
        }

        int mapTop = 2 * CellHeight, mapPixels = MapWidth * 18, statusTop = mapTop + MapHeight * 18, logTop = statusTop + 2 * CellHeight;
        Panel(Width, 2, 0, 0, Theme.PanelBackground);
        Panel(Width, 2, 0, statusTop, Theme.PanelBackground);
        _map = new Console(MapWidth, MapHeight) { Font = mapFont, FontSize = mapFont.GetFontSize(IFont.Sizes.One), UsePixelPositioning = true, Position = new Point(0, mapTop) };
        _map.Surface.DefaultBackground = Theme.Void;
        Children.Add(_map);
        _sidebar = Panel(SidebarWidth, MapHeight, mapPixels, mapTop, Theme.SidebarBackground);
        _header = Panel(Width, 1, 0, CellHeight / 2, Theme.PanelBackground);
        _status = Panel(Width, 1, 0, statusTop + CellHeight / 2, Theme.PanelBackground);
        _log = Panel(UiWidth, LogRows, 0, logTop, Theme.LogBackground);
        _keys = Panel(SidebarWidth, LogRows, mapPixels, logTop, Theme.LogBackground);
        _ui = Panel(UiWidth, MapHeight, 0, mapTop, Color.Transparent);
        _world = NewWorld();
        ApplyStartOptions();
        Draw();
    }

    /// <summary>Seed for the first game, from --seed; later games are random.</summary>
    public static ulong? StartSeed { get; set; }

    /// <summary>Start at dungeon level 1 instead of the surface (--dungeon, and always when following a command file).</summary>
    public static bool StartInDungeon { get; set; }

    private World NewWorld()
    {
        var seed = StartSeed ?? (ulong)Random.Shared.NextInt64();
        StartSeed = null;
        var world = World.Create(seed, WorldWidth, WorldHeight, depth: StartInDungeon ? 1 : World.SurfaceDepth);
        _messages.Clear();
        _commands = null; // follow mode drives the new world from here on
        _following = null;
        AddMessage(StartInDungeon ? $"You enter the goblin-infested halls. (seed {seed})" : $"You come down the old road into the valley. (seed {seed})", Theme.Text);
        if (FollowFile is not null)
            AddMessage($"Following commands from {Path.GetFileName(FollowFile)}.", Theme.Intent);
        if (world.Objective is { } errand)
            AddMessage($"Errand: bring the {errand.Item.Def.Name} from the back of the goblin camp to the waystone (far west).", Theme.Warning);
        AddMessage("Move: arrows, numpad or hjklyubn. Walk into things to fight, talk or open them. Wait: . or Space.", Theme.Intent);
        AddMessage("Debug: F1 reveal  F2 squads  F3 heatmap  F5 spectator  P autoplay", Theme.Intent);
        return world;
    }

    public override bool ProcessKeyboard(Keyboard keyboard)
    {
        if (InModalUi)
        {
            if (ModalInput(keyboard) is { } chosen && !_world.IsOver)
                PlayTurn(chosen);
            Draw();
            return true;
        }

        if (keyboard.IsKeyPressed(Keys.Escape))
        {
            Game.Instance.MonoGameInstance.Exit();
            return true;
        }

        if (HandleDebugKeys(keyboard))
        {
            Draw();
            return true;
        }

        if (_world.IsOver)
        {
            bool sameSeed = keyboard.IsKeyPressed(Keys.S);
            if (sameSeed || keyboard.IsKeyPressed(Keys.R))
            {
                if (sameSeed)
                    StartSeed = _world.Seed;
                _world = NewWorld();
                Draw();
            }
            return true;
        }

        if (IsFollowing)
            return true; // the command file is playing; only debug keys and Esc work

        GameAction? action = null;
        foreach (var (key, dir) in MoveKeys)
            if (keyboard.IsKeyPressed(key)) { action = new MoveAction(dir); break; }
        bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        if (keyboard.IsKeyPressed(Keys.OemPeriod) && shift)
            action = new DescendAction(); // '>'
        else if (keyboard.IsKeyPressed(Keys.OemComma) && shift)
            action = new AscendAction(); // '<'
        else if (keyboard.IsKeyPressed(Keys.C) && shift)
            action = new CloseGateAction();
        else if (keyboard.IsKeyPressed(Keys.OemPeriod) || keyboard.IsKeyPressed(Keys.NumPad5)
            || keyboard.IsKeyPressed(Keys.Space))
            action = new WaitAction();
        int messagesBefore = _messageCount;
        action ??= ItemCommand(keyboard);

        if (action is null)
        {
            // An item key may have opened a menu or just printed a message.
            bool changed = InModalUi || _messageCount != messagesBefore;
            if (changed)
                Draw();
            return changed;
        }

        PlayTurn(action);
        Draw();
        return true;
    }

    /// <summary>Runs one turn and logs what happened.</summary>
    private void PlayTurn(GameAction action)
    {
        // Snapshot names and positions before the turn, so actors that die mid-turn can still be described.
        var before = _world.Actors.ToDictionary(a => a.Id, a => (a.Name, a.Position));
        var events = _world.Step(action);

        var fov = _world.Map.PlayerFov.BooleanResultView;
        bool IsVisible(int id) => _reveal ||
            (_world.GetActor(id) is { } a ? fov[a.Position] : before.TryGetValue(id, out var b) && fov[b.Position]);
        var text = new EventText(_world, id => $"the {before.GetValueOrDefault(id).Name ?? "thing"}", IsVisible);
        bool playerMoved = false;

        var said = new HashSet<string>();
        foreach (var e in events)
        {
            playerMoved |= e is ActorMoved m && m.ActorId == _world.Player.Id;
            // One copy of each line per turn ("You hear the clash of weapons." can fire a lot).
            if (text.Describe(e) is not { } line || !said.Add(line))
                continue;
            var color = e switch
            {
                ItemPickedUp { Item.Rarity: > Roguelike.Core.Items.Rarity.Common } p => Theme.ForRarity(p.Item.Rarity),
                LootDropped { Item.Rarity: > Roguelike.Core.Items.Rarity.Common } l => Theme.ForRarity(l.Item.Rarity),
                ItemEquipped { Item.Rarity: > Roguelike.Core.Items.Rarity.Common } q => Theme.ForRarity(q.Item.Rarity),
                AbilityUsed => Theme.Warning,
                ActorDied or SquadRouted => Theme.Good,
                ActorAttacked a when a.TargetId == _world.Player.Id && a.Damage > 0 => Theme.Danger,
                SquadSpotted or ReinforcementsCalled or CorpseRaised or ZoneEntered or RaidLaunched or SquadMustered => Theme.Warning,
                GateChallenged or TrespassWarned or PassRunningOut or PassExpired or CampAlarmed => Theme.Warning,
                LoreDiscovered or SquadTaunted or SquadCowed or SquadPleaded => Theme.Warning,
                SecretTold or RansomPaid => Theme.Good,
                Talked or ItemBought => Theme.Intent,
                TheftCommitted { WitnessId: not null } => Theme.Danger,
                IncursionBegan or CampAttackedWhileAway or CampReinforced => Theme.Warning,
                LevelEntered or GameWon or TollPaid or ObjectiveCompleted => Theme.Good,
                MinionCrumbled or AttributeRaised or SkillLearned or SpecialUsed => Theme.Good,
                XpGained => Theme.Intent,
                _ => Theme.Text,
            };
            if (e is ActorDied d && d.ActorId == _world.Player.Id)
                color = Theme.Danger;
            AddMessage(line, color);
        }
        if (events.OfType<Talked>().FirstOrDefault(t => t.HasGoods) is { } trade && _world.GetActor(trade.NpcId) is { } seller)
            _shop = seller;
        if (events.OfType<MercyAsked>().FirstOrDefault() is { } plea && _world.GetActor(plea.ActorId) is { } beggar)
            _beggar = beggar;
        if (playerMoved)
            DescribeItemsUnderfoot();
        if (playerMoved && _world.Map.StairsDown == _world.Player.Position)
            AddMessage(_world.Depth == World.SurfaceDepth ? "A cave mouth opens here. (> to climb down)"

                : "There are stairs down here. (> to descend)", Theme.Warning);
        if (playerMoved && _world.Map.StairsUp == _world.Player.Position)
            AddMessage("There are stairs up here. (< to climb back)", Theme.Warning);
        bool canSpend = CanSpendXp();
        if (canSpend && !_couldSpendXp && !_sheetOpen)
            AddMessage("You feel you could learn something new. (c: character)", Theme.Warning);
        _couldSpendXp = canSpend;
        if (_world.IsOver)
        {
            _autoplay = false;
            AddMessage($"R: new game.  S: retry this seed ({_world.Seed}).  Esc: quit.", _world.IsWon ? Theme.Good : Theme.Danger);
        }
    }

    private int _messageCount;
    private bool _couldSpendXp;

    private void AddMessage(string text, Color color)
    {
        _messageCount++;
        _messages.Add((text, color));
        if (_messages.Count > LogLines)
            _messages.RemoveAt(0);
    }

    private Point _camera;

    /// <summary>Keeps the player (or the examine cursor) in the middle of the view, as far as the map allows.</summary>
    private void UpdateCamera()
    {
        var focus = _examine?.Cursor ?? _world.Player.Position;
        _camera = new Point(CameraAxis(focus.X, MapWidth, _world.Map.Width), CameraAxis(focus.Y, MapHeight, _world.Map.Height));
    }

    /// <summary>Follows the focus, stops at the map's edges, and centres a map smaller than the view.</summary>
    private static int CameraAxis(int focus, int view, int size) =>
        size <= view ? (size - view) / 2 : Math.Clamp(focus - view / 2, 0, size - view);

    /// <summary>Where a map cell falls in the view, if it's on screen.</summary>
    private bool OnScreen(Point p, out Point screen)
    {
        screen = p - _camera;
        return screen.X >= 0 && screen.Y >= 0 && screen.X < MapWidth && screen.Y < MapHeight;
    }

    private void Draw()
    {
        UpdateCamera();
        DrawHeader();
        DrawMap();
        DrawSidebar();
        DrawStatus();
        DrawLog();
        DrawKeys();
        _ui.Clear();
        _ui.IsVisible = _menu is not null || _sheetOpen || _chronicleOpen || _shop is not null;
        DrawModalUi();
    }

    /// <summary>F4: plain colours, no lighting.</summary>
    private bool _classic;

    /// <summary>Light sources in the view: the player's torch and any fires. Radius and strength as in the visual prototype.</summary>
    private List<(Point At, double Radius, double Power)> LightSources()
    {
        var lights = new List<(Point, double, double)> { (_world.Player.Position, 7, 0.75) };
        var map = _world.Map;
        for (int y = 0; y < MapHeight; y++)
        for (int x = 0; x < MapWidth; x++)
        {
            var p = new Point(x, y) + _camera;
            if (map.InBounds(p) && map.Tiles[p] == Roguelike.Core.Map.Tile.Fire)
                lights.Add((p, 11, 1.2));
        }
        return lights;
    }

    private static double Glow(Point p, List<(Point At, double Radius, double Power)> lights)
    {
        double glow = 0;
        foreach (var (at, radius, power) in lights)
        {
            double d = Math.Sqrt((p.X - at.X) * (p.X - at.X) + (p.Y - at.Y) * (p.Y - at.Y));
            if (d < radius)
                glow += Math.Pow(1 - d / radius, 1.4) * power;
        }
        return glow;
    }

    /// <summary>What an actor's stance looks like: its colour and, when it matters, a background.</summary>
    private (Color Color, Color? Background) StanceOf(Actor actor)
    {
        var look = Theme.ForKind(actor.Kind);
        if (actor == _world.Player)
            return (Theme.Player, null);
        if (_world.SquadOf(actor) is not { } squad)
            return (look.Foreground, null);
        if (squad.State == SquadState.Flee)
            return (Theme.Teal, Theme.FleeingBackground);
        if (squad.State == SquadState.Engage && squad.TargetId == _world.Player.Id)
            return (Theme.Danger, Theme.HostileBackground);
        if (squad.Camp is { } camp && !_world.AreHostile(actor, _world.Player))
        {
            if (camp.HasPass(_world.Turn))
                return (Theme.Teal, null);
            if (_world.CanSee(actor, _world.Player))
                return (Theme.Wary, null);
        }
        return (look.Foreground, null);
    }

    private void DrawMap()
    {
        var map = _world.Map;
        var fov = map.PlayerFov.BooleanResultView;
        var heat = HeatmapView();

        _map.Clear();
        var lights = LightSources();
        for (int y = 0; y < MapHeight; y++)
        for (int x = 0; x < MapWidth; x++)
        {
            var p = new Point(x, y) + _camera;
            if (!map.InBounds(p) || (!map.Explored[p] && !_reveal))
                continue;
            var look = Theme.ForTile(map.Tiles[p]);
            int glyph = Theme.GlyphAt(look, p.X, p.Y);
            Color fg, bg;
            if (_classic)
                (fg, bg) = (fov[p] ? look.Foreground : Theme.Remembered(look.Foreground), Theme.Ground);
            else if (fov[p])
                (fg, bg) = Theme.Lit(look, Glow(p, lights));
            else
                (fg, bg) = (Theme.Remembered(look.Foreground), Theme.RememberedGround);
            _map.SetGlyph(x, y, glyph, fg, heat?.Invoke(p) ?? bg);
        }

        foreach (var corpse in _world.Corpses)
        {
            if ((!fov[corpse.Position] && !_reveal) || !OnScreen(corpse.Position, out var at))
                continue;
            var look = Theme.ForCorpse(corpse.Kind, _world.IsWithered(corpse));
            _map.SetGlyph(at.X, at.Y, look.Glyph, look.Foreground, _map.Surface[at.X, at.Y].Background);
        }
        DrawFloorItems();
        if (_showSquads)
            DrawSquadMarkers();

        foreach (var actor in _world.Actors)
        {
            if (actor.IsDead || !(fov[actor.Position] || _reveal) || !OnScreen(actor.Position, out var at))
                continue;
            var look = Theme.ForKind(actor.Kind);
            var squad = _world.SquadOf(actor);
            var (stance, stanceBg) = _classic ? (look.Foreground, null) : StanceOf(actor);
            var bg = _showSquads && squad is not null ? Theme.SquadTint(squad.Id)
                : actor.Rank != Rank.Normal ? Theme.ForRank(actor.Rank)
                : stanceBg ?? _map.Surface[at.X, at.Y].Background;
            var fg = actor == _world.Player && _world.ObserverMode ? Theme.Intent : stance;
            _map.SetGlyph(at.X, at.Y, Theme.Bold(look.Glyph), fg, bg);
        }
    }

    private void DrawBar(int x, int y, int width, int value, int max)
    {
        int filled = max <= 0 ? 0 : (int)Math.Ceiling(width * Math.Clamp(value, 0, max) / (double)max);
        var color = value * 3 <= max ? Theme.Danger : value * 3 <= max * 2 ? Theme.Warning : Theme.Good;
        for (int i = 0; i < width; i++)
            _sidebar.SetGlyph(x + i, y, ' ', Color.Black, i < filled ? color.GetDarker() : Theme.BarEmpty);
        var label = $"{Math.Max(0, value)}/{max}";
        _sidebar.Print(x + (width - label.Length) / 2, y, label, Color.White, color.GetDarker());
        for (int i = 0; i < label.Length; i++)
        {
            int cx = x + (width - label.Length) / 2 + i;
            _sidebar.SetBackground(cx, y, cx - x < filled ? color.GetDarker() : Theme.BarEmpty);
        }
    }

    /// <summary>Show intent, so the player can plan around it.</summary>
    private string Intent(Actor actor)
    {
        if (_world.SquadOf(actor) is not { } squad)
            return "";
        var role = squad.RoleOf(actor);
        if (role == SquadRole.Civilian)
            return "going about their day";
        if (squad.Surrendered)
            return squad.Offer switch { Ransom.Secret => "offering a secret", Ransom.Gold => "offering gold", _ => "surrendering" };
        if (squad.Spared)
            return "keeping well clear of you";
        if (squad.State == SquadState.Engage && role is SquadRole.Warrior or SquadRole.Runner)
        {
            if (squad.Posture == Posture.Bold) return "closing in on you";
            if (squad.Posture == Posture.Wary) return "circling, keeping back";
            if (squad.Posture == Posture.Desperate) return "charging, nothing to lose";
        }
        return squad.State switch
        {
            SquadState.Flee when squad.Rally is not null => "falling back",
            SquadState.Flee => "fleeing in panic",
            SquadState.Idle when squad.Camp is not null && !_world.AreHostile(actor, _world.Player) && _world.CanSee(actor, _world.Player) => "watching you",
            SquadState.Idle when squad.Route is not null => "on patrol",
            SquadState.Idle when role == SquadRole.Necromancer => "brooding",
            SquadState.Idle when squad.Home is not null => "guarding",
            SquadState.Idle when squad.IsRaiding => "marching to raid",
            SquadState.Idle => "patrolling",
            SquadState.Alert => squad.TargetId == _world.Player.Id ? "searching for you" : "searching",
            SquadState.Engage => role switch
            {
                SquadRole.Runner when squad.HelpTargetSquadId is not null => "running for help!",
                SquadRole.Archer => $"shooting at {TargetName(squad)}",
                SquadRole.Necromancer => $"cursing {TargetName(squad)}",
                SquadRole.Shaman => "supporting allies",
                _ when squad.IsHoldingPassage && _world.AdjacentHostile(actor) is null
                    => "holding the passage",
                _ => $"attacking {TargetName(squad)}",
            },
            _ => "",
        };
    }

    private string TargetName(Squad squad) =>
        squad.TargetId == _world.Player.Id ? "you"
        : squad.TargetId is { } id && _world.GetActor(id) is { } t ? t.Name.Split(' ')[0] + "s"
        : "enemies";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
