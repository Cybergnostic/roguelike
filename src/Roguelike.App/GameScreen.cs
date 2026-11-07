using Roguelike.Core;
using Roguelike.Core.Actions;
using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using Console = SadConsole.Console;

namespace Roguelike.App;

/// <summary>Root screen: map view, status line and message log. Owns input; draws the World.</summary>
public sealed class GameScreen : ScreenObject
{
    public const int Width = 80;
    public const int MapHeight = 24;
    public const int LogLines = 5;
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

    private readonly Console _map = new(Width, MapHeight);
    private readonly Console _status = new(Width, 1) { Position = new Point(0, MapHeight) };
    private readonly Console _log = new(Width, LogLines) { Position = new Point(0, MapHeight + 1) };
    private readonly List<(string Text, Color Color)> _messages = [];

    private World _world;

    public GameScreen()
    {
        Children.Add(_map);
        Children.Add(_status);
        Children.Add(_log);
        _world = NewWorld();
        Draw();
    }

    private World NewWorld()
    {
        var seed = (ulong)Random.Shared.NextInt64();
        var world = World.Create(seed, Width, MapHeight);
        _messages.Clear();
        AddMessage($"You enter the dungeon. (seed {seed})", Theme.Text);
        AddMessage("Move: arrows / numpad / hjklyubn.  Wait: . or 5.  Quit: Esc.", Theme.Text);
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

        var names = _world.Actors.ToDictionary(a => a.Id, a => a == _world.Player ? "you" : $"the {a.Kind}");
        foreach (var e in _world.Step(action))
        {
            var text = EventText.Describe(_world, e, id => names.GetValueOrDefault(id, "something"));
            if (text is not null)
                AddMessage(text, e is Roguelike.Core.Events.ActorDied ? Theme.Danger : Theme.Text);
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
            _map.SetGlyph(actor.Position.X, actor.Position.Y, look.Glyph, look.Foreground, Theme.Background);
        }

        var player = _world.Player;
        _status.Clear();
        _status.Print(0, 0, $" HP {Math.Max(0, player.Hp)}/{player.MaxHp}",
            player.Hp * 3 <= player.MaxHp ? Theme.Danger : Theme.Good);
        _status.Print(16, 0, $"Turn {_world.Turn}", Theme.Text);

        _log.Clear();
        for (int i = 0; i < _messages.Count; i++)
            _log.Print(1, i, _messages[i].Text, _messages[i].Color);
    }
}
