using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.App;

/// <summary>
/// Developer views for watching the simulation:
///   F1 reveal map and all actors      F2 squad overlay and squad list
///   F3 cycle Dijkstra-map heatmaps    F5 spectator mode (invisible, invulnerable)
///   P  autoplay (spectating: time passes; playing: the aggressive bot plays for you)
///   Space advances one turn while spectating
/// </summary>
public sealed partial class GameScreen
{

    private const double AutoplaySecondsPerTurn = 0.12;

    private bool _reveal;
    private bool _showSquads;
    private int _heatmap; // 0 = off; otherwise an index into HeatmapChoices()
    private bool _autoplay;
    private readonly Roguelike.Core.Simulation.AggressiveBot _autoplayBot = new();
    private double _autoplayTimer;

    /// <summary>Selectable Dijkstra maps: toward the player, then per faction "toward enemies" and "flee".</summary>
    private List<(string Name, Func<IGridView<double?>> View)> HeatmapChoices()
    {
        var choices = new List<(string, Func<IGridView<double?>>)>
        {
            ("off", () => null!),
            ("toward player", () => _world.TowardPlayer),
        };
        foreach (var faction in _world.Content.Factions.Values.Where(f => f.Id != "player"))
        {
            choices.Add(($"{faction.Id}: toward enemies", () => _world.TowardEnemiesOf(faction)));
            choices.Add(($"{faction.Id}: flee", () => _world.AwayFromEnemiesOf(faction)));
        }
        return choices;
    }

    private string HeatmapName => HeatmapChoices()[_heatmap].Name;

    /// <summary>From --spectate: start the first game watching, with every debug view on.</summary>
    public static bool StartSpectating { get; set; }

    private void ApplyStartOptions()
    {
        if (!StartSpectating)
            return;
        StartSpectating = false;
        _world.ObserverMode = _reveal = _showSquads = _autoplay = true;
        AddMessage("Spectating. F5 to play, P to pause, Space to step.", Theme.Intent);
    }

    private bool HandleDebugKeys(Keyboard keyboard)
    {
        if (keyboard.IsKeyPressed(Keys.F1))
            _reveal = !_reveal;
        else if (keyboard.IsKeyPressed(Keys.F2))
            _showSquads = !_showSquads;
        else if (keyboard.IsKeyPressed(Keys.F3))
            _heatmap = (_heatmap + 1) % HeatmapChoices().Count;
        else if (keyboard.IsKeyPressed(Keys.F5))
        {
            _world.ObserverMode = !_world.ObserverMode;
            if (_world.ObserverMode)
            {
                _reveal = true;
                AddMessage("Spectator mode: you are invisible and untouchable. Space steps, P autoplays.", Theme.Intent);
            }
            else
            {
                _autoplay = false;
                AddMessage("Spectator mode off. The goblins can see you again.", Theme.Intent);
            }
        }
        else if (keyboard.IsKeyPressed(Keys.P) && !_world.IsOver)
        {
            _autoplay = !_autoplay;
            _autoplayTimer = 0;
        }
        else
            return false;
        return true;
    }

    public override void Update(TimeSpan delta)
    {
        base.Update(delta);
        if (IsFollowing)
        {
            UpdateFollow(delta);
            return;
        }
        if (!_autoplay || _world.IsOver)
            return;

        _autoplayTimer += delta.TotalSeconds;
        if (_autoplayTimer < AutoplaySecondsPerTurn)
            return;
        _autoplayTimer = 0;
        // Spectating, the dungeon just runs; otherwise the sim's bot plays the character.
        PlayTurn(_world.ObserverMode ? new WaitAction() : _autoplayBot.Decide(_world));
        Draw();
    }

    /// <summary>Background colour per cell for the selected Dijkstra map, or null when off.</summary>
    private Func<Point, Color?>? HeatmapView()
    {
        if (_heatmap == 0)
            return null;
        var view = HeatmapChoices()[_heatmap].View();

        double min = double.MaxValue, max = double.MinValue;
        foreach (var p in view.Positions())
        {
            if (view[p] is not { } v) continue;
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }
        double range = Math.Max(1e-9, max - min);

        return p => view[p] is { } v ? Theme.Heat((v - min) / range) : null;
    }

    /// <summary>Squad destinations (x) and last known player positions (?), in each squad's colour.</summary>
    private void DrawSquadMarkers()
    {
        foreach (var squad in _world.Squads)
        {
            var color = Theme.SquadColor(squad.Id);
            if (squad.Destination is { } d && squad.State is SquadState.Idle or SquadState.Alert)
                _map.SetGlyph(d.X, d.Y, 'x', color);
            if (squad.LastKnownTarget is { } k && squad.State is SquadState.Alert or SquadState.Engage)
                _map.SetGlyph(k.X, k.Y, '?', color);
        }
    }

    /// <summary>Sidebar replacement while the squad overlay is on: every squad's state and morale.</summary>
    private void DrawSquadList()
    {
        var bg = Theme.SidebarBackground;
        int y = 0;
        _sidebar.Print(1, y++, $"Squads ({_world.Squads.Count})", Theme.Text, bg);
        y++;
        foreach (var squad in _world.Squads)
        {
            if (y + 3 >= MapHeight)
            {
                _sidebar.Print(1, y, "...", Theme.Text, bg);
                break;
            }
            var color = Theme.SquadColor(squad.Id);
            _sidebar.Print(1, y, $"#{squad.Id}", color, bg);
            _sidebar.Print(5, y++, StateLabel(squad), StateColor(squad.State), bg);

            int x = 1;
            foreach (var m in squad.Members)
            {
                var look = Theme.ForKind(m.Kind);
                _sidebar.SetGlyph(x, y, look.Glyph, look.Foreground, bg);
                x += 2;
                if (x >= SidebarWidth - 1) break;
            }
            y++;
            DrawBar(1, y++, SidebarWidth - 2, squad.Morale, Squad.MaxMorale);
        }
    }

    private static string StateLabel(Squad squad) => squad.State switch
    {
        SquadState.Engage when squad.HelpTargetSquadId is not null => "fighting, runner out",
        SquadState.Engage when squad.IsHoldingPassage => "holding passage",
        SquadState.Engage => "fighting",
        SquadState.Alert => "searching",
        SquadState.Flee => "fleeing",
        _ when squad.Home is not null => "guarding",
        _ when squad.IsRaiding => "raiding",
        _ => "patrolling",
    };

    private static Color StateColor(SquadState state) => state switch
    {
        SquadState.Engage => Theme.Danger,
        SquadState.Alert => Theme.Warning,
        SquadState.Flee => Theme.Good,
        _ => Theme.Intent,
    };
}
