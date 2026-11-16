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
        /// <summary>Which corner its tag sits at (index into <see cref="CalloutSlots"/>), kept between frames so it doesn't jump about.</summary>
        public int Slot { get; set; } = -1;
    }

    /// <summary>
    /// Where a tag can sit relative to whoever is speaking: a corner (left/right, above/below) and how many
    /// cells of leader line lead to it. Earlier entries are preferred when nothing is in the way.
    /// </summary>
    private static readonly (int Dx, int Dy, int Reach)[] CalloutSlots =
        [(1, -1, 1), (-1, -1, 1), (1, 1, 1), (-1, 1, 1), (1, -1, 2), (-1, -1, 2), (1, 1, 2), (-1, 1, 2)];

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
                case WindingUp w when isVisible(w.ActorId):
                    Say(w.ActorId, Where(w.ActorId), "!! " + (_world.Content.Abilities[w.AbilityId].Kind == Roguelike.Core.Content.AbilityKind.Charge ? "charge" : "heavy blow"), Theme.Danger, SpeechSeconds);
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
        var occupied = OccupiedScreenCells();
        foreach (var popup in _popups)
        {
            var anchor = popup.ActorId is { } id && _world.GetActor(id) is { IsDead: false } actor ? actor.Position : popup.At;
            popup.At = anchor;
            if (!OnScreen(anchor, out var cell))
                continue;
            var lines = Wrap(popup.Text, SpeechWrap).ToList();
            if (popup.ActorId is not null && DrawCallout(popup, cell, lines, taken, occupied))
                continue;
            DrawFloating(popup, cell, lines, taken, occupied);
        }
        DrawTooltip();
    }

    /// <summary>
    /// A box with no leader (a noise from somewhere off in a direction): centred on its spot, but nudged up or
    /// down so it doesn't cover anyone, the space around the player, or another popup.
    /// </summary>
    private void DrawFloating(Popup popup, Point cell, List<string> lines, HashSet<(int Row, int Col)> taken, Dictionary<Point, bool> occupied)
    {
        int width = lines.Max(l => l.Length) + 2;
        int x = Math.Clamp(cell.X * 2 + 1 - width / 2, 0, UiWidth - width);
        var player = OnScreen(_world.Player.Position, out var p) ? p : new Point(-9, -9);
        int bestTop = -1, bestScore = int.MaxValue;
        foreach (int shift in (int[])[0, -1, 1, -2, 2, -3, 3, -4, 4])
        {
            int top = cell.Y - lines.Count + shift;
            if (top < 0 || top + lines.Count > MapHeight)
                continue;
            int score = Math.Abs(shift);
            for (int row = top; row < top + lines.Count; row++)
                for (int col = x; col < x + width; col++)
                {
                    if (taken.Contains((row, col)))
                        score += 1000;
                    if (Math.Abs(col / 2 - player.X) <= 1 && Math.Abs(row - player.Y) <= 1)
                        score += 100;
                    if (occupied.ContainsKey(new Point(col / 2, row)))
                        score += 60;
                }
            if (score < bestScore)
                (bestTop, bestScore) = (top, score);
        }
        if (bestTop < 0)
            return;
        for (int i = 0; i < lines.Count; i++)
        {
            _float.Print(x, bestTop + i, $" {lines[i]} ".PadRight(width), popup.Color, Theme.PopupBackground);
            for (int col = x; col < x + width; col++)
                taken.Add((bestTop + i, col));
        }
    }

    /// <summary>Map cells on screen holding someone the player can see, and whether it's the player.</summary>
    private Dictionary<Point, bool> OccupiedScreenCells()
    {
        var cells = new Dictionary<Point, bool>();
        var fov = _world.Map.PlayerFov.BooleanResultView;
        foreach (var actor in _world.Actors)
            if (!actor.IsDead && (fov[actor.Position] || _reveal) && OnScreen(actor.Position, out var at))
                cells[at] = actor == _world.Player;
        return cells;
    }

    /// <summary>
    /// A tag off to one side of the speaker, joined to it by a short diagonal leader line, like the labels
    /// object-recognition software puts on what it has found. The corner is chosen so the tag covers as few
    /// creatures and other tags as possible, and is kept between frames unless something better turns up.
    /// Returns false if no corner fits on screen.
    /// </summary>
    private bool DrawCallout(Popup popup, Point cell, List<string> lines, HashSet<(int Row, int Col)> taken, Dictionary<Point, bool> occupied)
    {
        int width = lines.Max(l => l.Length) + 2;   // the bar, the text, a space
        int best = -1, bestScore = int.MaxValue;
        for (int slot = 0; slot < CalloutSlots.Length; slot++)
        {
            var cells = CalloutCells(cell, CalloutSlots[slot], width, lines.Count, out _, out _);
            if (cells is null)
                continue;
            int score = slot * 2 - (slot == popup.Slot ? 15 : 0);
            foreach (var (col, row) in cells)
            {
                if (taken.Contains((row, col)))
                    score += 1000;
                if (occupied.TryGetValue(new Point(col / 2, row), out bool isPlayer))
                    score += isPlayer ? 200 : 60;
            }
            if (score < bestScore)
                (best, bestScore) = (slot, score);
        }
        if (best < 0)
            return false;
        popup.Slot = best;

        var (dx, dy, reach) = CalloutSlots[best];
        var all = CalloutCells(cell, CalloutSlots[best], width, lines.Count, out int labelX, out int labelTop)!;
        foreach (var (col, row) in all)
            taken.Add((row, col));

        // The leader: '/' rises to the right and falls to the left, '\' the other way.
        char leader = dx * dy < 0 ? '/' : '\\';
        for (int i = 1; i <= reach; i++)
        {
            int col = dx > 0 ? cell.X * 2 + 1 + i : cell.X * 2 - i;
            _float.SetGlyph(col, cell.Y + dy * i, leader, popup.Color, Color.Transparent);
        }

        // The tag: a bar in the speaker's colour on the side the leader joins, then the words.
        for (int i = 0; i < lines.Count; i++)
        {
            string text = dx > 0 ? " " + lines[i].PadRight(width - 1) : lines[i].PadLeft(width - 1) + " ";
            _float.Print(labelX, labelTop + i, text, popup.Color, Theme.PopupBackground);
            const int LeftHalfBlock = 221, RightHalfBlock = 222;   // CP437 ▌ ▐
            _float.SetGlyph(dx > 0 ? labelX : labelX + width - 1, labelTop + i, dx > 0 ? LeftHalfBlock : RightHalfBlock, popup.Color, Theme.PopupBackground);
        }
        return true;
    }

    /// <summary>
    /// The UI-layer cells (two per map cell across) a callout would cover in the given corner: its leader
    /// line and its tag. Null if any of it falls off the map view.
    /// </summary>
    private static List<(int Col, int Row)>? CalloutCells(Point cell, (int Dx, int Dy, int Reach) slot, int width, int height, out int labelX, out int labelTop)
    {
        var (dx, dy, reach) = slot;
        labelX = dx > 0 ? cell.X * 2 + 2 + reach : cell.X * 2 - reach - width;
        labelTop = dy < 0 ? cell.Y - reach - height : cell.Y + reach + 1;
        if (labelX < 0 || labelX + width > UiWidth || labelTop < 0 || labelTop + height > MapHeight)
            return null;
        var cells = new List<(int, int)>();
        for (int i = 1; i <= reach; i++)
            cells.Add((dx > 0 ? cell.X * 2 + 1 + i : cell.X * 2 - i, cell.Y + dy * i));
        for (int row = labelTop; row < labelTop + height; row++)
            for (int col = labelX; col < labelX + width; col++)
                cells.Add((col, row));
        return cells;
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
        if (_world.MarkAt(p) is { } mark)
            lines.Add(($"{_world.Content.Runes[mark.RuneId].Name} ({_world.Content.Runes[mark.RuneId].Meaning})", RuneColor));
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
