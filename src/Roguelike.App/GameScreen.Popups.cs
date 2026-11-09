using Roguelike.Core;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;

namespace Roguelike.App;

/// <summary>
/// Everything that floats over the map for a moment: what people say, grunts of effort and pain,
/// noises from out of sight, and the tooltip under the mouse. Purely cosmetic: none of it uses the
/// game's random numbers.
/// </summary>
public sealed partial class GameScreen
{
    private sealed class Popup(string text, Color color, int? actorId, Point at, double seconds)
    {
        public string Text { get; } = text;
        public Color Color { get; } = color;
        public int? ActorId { get; } = actorId;
        public Point At { get; set; } = at;
        public double Left { get; set; } = seconds;
    }

    private const double SpeechSeconds = 3.2;
    private const double GruntSeconds = 1.1;
    private const double SoundSeconds = 2.2;
    private const int SpeechWrap = 34;
    private const int MaxGruntsPerTurn = 4;
    private const int GruntOneIn = 2;

    private readonly List<Popup> _popups = [];
    private bool _popupLayerUsed;

    private void Say(int actorId, Point at, string text, Color color, double seconds)
    {
        _popups.RemoveAll(p => p.ActorId == actorId);
        _popups.Add(new Popup(text, color, actorId, at, seconds));
    }

    private void AddPopups(IReadOnlyList<GameEvent> events, EventText text, Dictionary<int, (string Name, Point Position)> before, Func<int, bool> isVisible)
    {
        Point Where(int id) => _world.GetActor(id)?.Position ?? before.GetValueOrDefault(id).Position;
        int grunts = 0;

        void Grunt(int id, string factionId, string kind)
        {
            if (id == _world.Player.Id || !isVisible(id) || grunts >= MaxGruntsPerTurn || (id * 31 + _world.Turn * 7) % GruntOneIn != 0)
                return;
            if (Theme.Grunt(factionId, kind, id + _world.Turn) is { } sound && _popups.All(p => p.ActorId != id || p.Left < GruntSeconds))
            {
                grunts++;
                Say(id, Where(id), sound, Theme.TextDim, GruntSeconds);
            }
        }

        foreach (var e in events)
        {
            if (text.Speech(e) is { } speech)
                Say(speech.Speaker, Where(speech.Speaker), speech.Text, Theme.Bright, SpeechSeconds);

            switch (e)
            {
                case ActorDied d:
                    Grunt(d.ActorId, d.FactionId, "die");
                    break;
                case ActorAttacked { Missed: false, Damage: > 0 } a when _world.GetActor(a.TargetId) is { } hurt:
                    Grunt(hurt.Id, hurt.Faction.Id, "hurt");
                    break;
                case ActorAttacked { Ranged: false } a when _world.GetActor(a.AttackerId) is { } attacker:
                    Grunt(attacker.Id, attacker.Faction.Id, "attack");
                    break;
                case ActorStaggered s when isVisible(s.ActorId):
                    Say(s.ActorId, Where(s.ActorId), "*staggers*", Theme.Warning, GruntSeconds);
                    break;
                case SoundHeard s:
                    Heard(EventText.SoundWords(s), s.From, s.Near);
                    break;
                case CampAlarmed:
                    Heard("a horn blares", Direction.Up, false);
                    break;
            }
        }
    }

    /// <summary>A noise shows up off in the direction it came from, as far as the player can tell.</summary>
    private void Heard(string words, Direction? from, bool near)
    {
        const int Far = 12, Close = 6;
        int reach = near ? Close : Far;
        var at = from is { } d ? _world.Player.Position + new Point(d.DeltaX * reach, d.DeltaY * reach) : _camera + new Point(MapWidth / 2, 2);
        _popups.Add(new Popup($"(( {words} ))", Theme.Teal, null, at, SoundSeconds));
    }

    private void UpdatePopups(TimeSpan delta)
    {
        foreach (var popup in _popups)
            popup.Left -= delta.TotalSeconds;
        _popups.RemoveAll(p => p.Left <= 0);

        bool show = _popups.Count > 0 || (_hover is not null && !InModalUi);
        if (!show && !_popupLayerUsed)
            return;
        _popupLayerUsed = show;
        DrawPopups();
    }

    private void DrawPopups()
    {
        _float.Clear();
        var taken = new HashSet<(int Row, int Col)>();
        foreach (var popup in _popups)
        {
            var anchor = popup.ActorId is { } id && _world.GetActor(id) is { IsDead: false } actor ? actor.Position : popup.At;
            popup.At = anchor;
            if (!OnScreen(anchor, out var cell))
                continue;
            var lines = Wrap(popup.Text, SpeechWrap).ToList();
            int y = Math.Max(0, cell.Y - lines.Count);
            foreach (var line in lines)
            {
                int x = Math.Clamp(cell.X * 2 + 1 - line.Length / 2 - 1, 0, UiWidth - line.Length - 2);
                while (y > 0 && Enumerable.Range(x, line.Length + 2).Any(c => taken.Contains((y, c))))
                    y--;
                _float.Print(x, y, $" {line} ", popup.Color, Theme.PopupBackground);
                foreach (int c in Enumerable.Range(x, line.Length + 2))
                    taken.Add((y, c));
                y++;
            }
        }
        DrawTooltip();
    }

    /// <summary>The short card under the mouse: what's there, how it's doing, what it's up to.</summary>
    private void DrawTooltip()
    {
        if (_hover is not { } p || InModalUi || !OnScreen(p, out var cell) || (!_world.Map.Explored[p] && !_reveal))
            return;
        var lines = TooltipLines(p);
        if (lines.Count == 0)
            return;
        int width = lines.Max(l => l.Text.Length) + 2;
        int x = cell.X * 2 + 3;
        if (x + width >= UiWidth)
            x = Math.Max(0, cell.X * 2 - width - 1);
        int y = Math.Min(cell.Y, MapHeight - lines.Count);
        for (int i = 0; i < lines.Count; i++)
            _float.Print(x, y + i, $" {lines[i].Text}".PadRight(width), lines[i].Color, Theme.PopupBackground);
    }

    private List<(string Text, Color Color)> TooltipLines(Point p)
    {
        var lines = new List<(string, Color)>();
        bool inView = _world.Map.PlayerFov.BooleanResultView[p] || _reveal;
        if (inView && _world.ActorAt(p) is { } actor)
        {
            if (actor == _world.Player)
                return [("You", Theme.Bright)];
            var (color, _) = StanceOf(actor);
            lines.Add((Capitalize(actor.Name), color));
            lines.Add((HurtWord(actor), Theme.Intent));
            if (Intent(actor) is { Length: > 0 } intent)
                lines.Add((Capitalize(intent), Theme.TextDim));
            return lines;
        }
        if (inView && _world.ItemsAt(p).FirstOrDefault() is { } item)
            lines.Add((Capitalize(ItemText.AName(item)), Theme.ForItem(item).Foreground));
        lines.Add((TileText(_world.Map.Tiles[p]).Name, Theme.TextDim));
        return lines;
    }

    // ---- The mouse ----

    private Point? _hover;
    private Point? _pinned;
    private IEnumerator<Roguelike.Core.Actions.GameAction>? _walk;
    private double _walkTimer;
    private const double WalkSecondsPerStep = 0.06;

    private void OnMapMouseMove(object? sender, MouseScreenObjectState state)
    {
        var at = state.CellPosition + _camera;
        var hover = _world.Map.InBounds(at) ? at : (Point?)null;
        if (hover == _hover)
            return;
        _hover = hover;
        if (!InModalUi)
            Draw();
    }

    private void OnMapMouseExit(object? sender, MouseScreenObjectState state)
    {
        _hover = null;
        if (!InModalUi)
            Draw();
    }

    /// <summary>Left click pins what's under it in the Inspect panel; right click walks there.</summary>
    private void OnMapClicked(object? sender, MouseScreenObjectState state)
    {
        var at = state.CellPosition + _camera;
        if (InModalUi || IsFollowing || _world.IsOver || !_world.Map.InBounds(at))
            return;
        if (state.Mouse.RightClicked)
            WalkTo(at);
        else
            _pinned = at;
        Draw();
    }

    private void WalkTo(Point at)
    {
        if (at == _world.Player.Position || !_world.Map.Explored[at])
        {
            AddMessage("You know no way there.", Theme.Intent);
            return;
        }
        _walk = new Roguelike.Core.Simulation.CommandPlayer(_world).Run($"walk {at.X} {at.Y}").GetEnumerator();
        _walkTimer = WalkSecondsPerStep;
    }

    /// <summary>One step of a walk ordered with the mouse; stops itself when something comes up.</summary>
    private void UpdateWalk(TimeSpan delta)
    {
        if (_walk is null)
            return;
        _walkTimer += delta.TotalSeconds;
        if (_walkTimer < WalkSecondsPerStep)
            return;
        _walkTimer = 0;
        if (InModalUi || _world.IsOver || !_walk.MoveNext())
        {
            _walk = null;
            Draw();
            return;
        }
        PlayTurn(_walk.Current);
        Draw();
    }

    private void OnSidebarClicked(object? sender, MouseScreenObjectState state)
    {
        if (InModalUi || IsFollowing || _world.IsOver || !_gateRows.TryGetValue(state.CellPosition.Y, out int choice))
            return;
        if (GateAction(choice) is { } action)
            PlayTurn(action);
        Draw();
    }
}
