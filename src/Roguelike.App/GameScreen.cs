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
public sealed class GameScreen : ScreenObject
{
    public const int MapWidth = 80;
    public const int MapHeight = 24;
    public const int SidebarWidth = 22;
    public const int LogLines = 5;
    public const int Width = MapWidth + SidebarWidth;
    public const int Height = MapHeight + 1 + LogLines;

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

    private readonly Console _map = new(MapWidth, MapHeight);
    private readonly Console _sidebar = new(SidebarWidth, MapHeight) { Position = new Point(MapWidth, 0) };
    private readonly Console _status = new(Width, 1) { Position = new Point(0, MapHeight) };
    private readonly Console _log = new(Width, LogLines) { Position = new Point(0, MapHeight + 1) };
    private readonly List<(string Text, Color Color)> _messages = [];

    private World _world;

    public GameScreen()
    {
        Children.Add(_map);
        Children.Add(_sidebar);
        Children.Add(_status);
        Children.Add(_log);
        _world = NewWorld();
        Draw();
    }

    /// <summary>Seed for the first game, from --seed; later games are random.</summary>
    public static ulong? StartSeed { get; set; }

    private World NewWorld()
    {
        var seed = StartSeed ?? (ulong)Random.Shared.NextInt64();
        StartSeed = null;
        var world = World.Create(seed, MapWidth, MapHeight);
        _messages.Clear();
        AddMessage($"You enter the goblin-infested halls. (seed {seed})", Theme.Text);
        AddMessage("Move/attack: arrows, numpad, hjklyubn.  Wait: . or 5.  Quit: Esc.", Theme.Text);
        return world;
    }

    public override bool ProcessKeyboard(Keyboard keyboard)
    {
        if (keyboard.IsKeyPressed(Keys.Escape))
        {
            Game.Instance.MonoGameInstance.Exit();
            return true;
        }

        if (_world.Player.IsDead)
        {
            if (keyboard.IsKeyPressed(Keys.R))
            {
                _world = NewWorld();
                Draw();
            }
            return true;
        }

        GameAction? action = null;
        foreach (var (key, dir) in MoveKeys)
            if (keyboard.IsKeyPressed(key)) { action = new MoveAction(dir); break; }
        if (keyboard.IsKeyPressed(Keys.OemPeriod) || keyboard.IsKeyPressed(Keys.NumPad5))
            action = new WaitAction();

        if (action is null)
            return false;

        // Snapshot names and positions before the turn, so actors that die mid-turn can still be described.
        var before = _world.Actors.ToDictionary(a => a.Id, a => (a.Name, a.Position));
        var events = _world.Step(action);

        var fov = _world.Map.PlayerFov.BooleanResultView;
        bool IsVisible(int id) =>
            _world.GetActor(id) is { } a ? fov[a.Position] : before.TryGetValue(id, out var b) && fov[b.Position];
        var text = new EventText(_world, id => $"the {before.GetValueOrDefault(id).Name ?? "thing"}", IsVisible);

        foreach (var e in events)
        {
            if (text.Describe(e) is not { } line)
                continue;
            var color = e switch
            {
                ActorDied or SquadRouted => Theme.Good,
                ActorAttacked a when a.TargetId == _world.Player.Id && a.Damage > 0 => Theme.Danger,
                SquadSpotted or ReinforcementsCalled => Theme.Warning,
                _ => Theme.Text,
            };
            if (e is ActorDied d && d.ActorId == _world.Player.Id)
                color = Theme.Danger;
            AddMessage(line, color);
        }
        if (_world.Player.IsDead)
            AddMessage("Press R to start a new game, Esc to quit.", Theme.Danger);

        Draw();
        return true;
    }

    private void AddMessage(string text, Color color)
    {
        _messages.Add((text, color));
        if (_messages.Count > LogLines)
            _messages.RemoveAt(0);
    }

    private void Draw()
    {
        DrawMap();
        DrawSidebar();
        DrawStatus();

        _log.Clear();
        for (int i = 0; i < _messages.Count; i++)
            _log.Print(1, i, _messages[i].Text, _messages[i].Color);
    }

    private void DrawMap()
    {
        var map = _world.Map;
        var fov = map.PlayerFov.BooleanResultView;

        _map.Clear();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var p = new Point(x, y);
            if (!map.Explored[p])
                continue;
            var look = Theme.ForTile(map.Tiles[p]);
            var fg = fov[p] ? look.Foreground : Theme.Remembered(look.Foreground);
            _map.SetGlyph(x, y, look.Glyph, fg, Theme.Background);
        }

        foreach (var actor in _world.Actors)
        {
            if (actor.IsDead || !fov[actor.Position])
                continue;
            var look = Theme.ForKind(actor.Kind);
            var bg = _world.SquadOf(actor)?.State == SquadState.Flee ? Theme.FleeingBackground : Theme.Background;
            _map.SetGlyph(actor.Position.X, actor.Position.Y, look.Glyph, look.Foreground, bg);
        }
    }

    /// <summary>Brogue-style list of visible monsters, with health and what they're up to.</summary>
    private void DrawSidebar()
    {
        _sidebar.Clear();
        _sidebar.Fill(Theme.Text, Theme.SidebarBackground, 0);

        var player = _world.Player;
        var visible = _world.Actors
            .Where(a => a != player && !a.IsDead && _world.PlayerCanSee(a))
            .OrderBy(a => World.Distance(a.Position, player.Position))
            .ToList();

        int y = 0;
        _sidebar.Print(1, y++, visible.Count == 0 ? "Nothing in sight" : "In sight", Theme.Text, Theme.SidebarBackground);
        y++;
        foreach (var actor in visible)
        {
            if (y + 2 >= MapHeight)
            {
                _sidebar.Print(1, y, $"...and {visible.Count - visible.IndexOf(actor)} more", Theme.Text, Theme.SidebarBackground);
                break;
            }
            var look = Theme.ForKind(actor.Kind);
            _sidebar.SetGlyph(1, y, look.Glyph, look.Foreground, Theme.SidebarBackground);
            _sidebar.Print(3, y++, Truncate(actor.Name, SidebarWidth - 4), Theme.Text, Theme.SidebarBackground);
            DrawBar(1, y++, SidebarWidth - 2, actor.Hp, actor.MaxHp);
            _sidebar.Print(1, y++, Truncate(Intent(actor), SidebarWidth - 2), Theme.Intent, Theme.SidebarBackground);
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
        return squad.State switch
        {
            SquadState.Flee => "fleeing in panic",
            SquadState.Idle => "patrolling",
            SquadState.Alert => "searching for you",
            SquadState.Engage => role switch
            {
                SquadRole.Runner when squad.HelpTargetSquadId is not null => "running for help!",
                SquadRole.Archer => "shooting at you",
                SquadRole.Shaman => "supporting allies",
                _ when squad.HoldTurns > 0 && !_world.IsAdjacent(actor.Position, _world.Player.Position)
                    => "holding the passage",
                _ => "attacking you",
            },
            _ => "",
        };
    }

    private void DrawStatus()
    {
        var player = _world.Player;
        _status.Clear();
        _status.Print(1, 0, $"HP {Math.Max(0, player.Hp)}/{player.MaxHp}",
            player.Hp * 3 <= player.MaxHp ? Theme.Danger : Theme.Good);
        _status.Print(14, 0, $"Turn {_world.Turn}", Theme.Text);
        int bands = _world.Squads.Count;
        _status.Print(28, 0, $"War bands remaining: {bands}", Theme.Text);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
