using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Map;
using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;

namespace Roguelike.App;

/// <summary>
/// Examine mode (x): a cursor that reads out what the player knows about a cell, without using
/// a turn. It only tells what they've seen or been told, never the AI's private state.
/// Also the toll prompt (t), which states the terms before any money moves.
/// </summary>
public sealed partial class GameScreen
{
    private sealed class Examining(Point cursor, List<Point> stops)
    {
        public Point Cursor { get; set; } = cursor;
        public List<Point> Stops { get; } = stops;
        public int Stop { get; set; } = -1;
    }

    private Examining? _examine;
    private bool _askingToll;

    private void StartExamining()
    {
        var player = _world.Player;
        var fov = _world.Map.PlayerFov.BooleanResultView;
        var things = _world.Actors.Where(a => a != player && !a.IsDead && (_reveal || _world.PlayerCanSee(a))).Select(a => a.Position)
            .Concat(_world.FloorItems.Where(f => _reveal || fov[f.Position]).Select(f => f.Position))
            .Concat(_world.Corpses.Where(c => _reveal || fov[c.Position]).Select(c => c.Position))
            .Concat(_world.Camps.Where(c => c.Toll is { } t && _world.Map.Explored[t.Gate]).Select(c => c.Toll!.Gate))
            .Distinct()
            .OrderBy(p => World.Distance(p, player.Position))
            .ToList();
        _examine = new Examining(things.Count > 0 ? things[0] : player.Position, things) { Stop = things.Count > 0 ? 0 : -1 };
    }

    private void ExamineInput(Keyboard keyboard)
    {
        var e = _examine!;
        if (keyboard.IsKeyPressed(Keys.X))
        {
            _examine = null;
            return;
        }
        if (keyboard.IsKeyPressed(Keys.Tab) && e.Stops.Count > 0)
        {
            e.Stop = (e.Stop + 1) % e.Stops.Count;
            e.Cursor = e.Stops[e.Stop];
            return;
        }
        foreach (var (key, dir) in MoveKeys)
        {
            if (!keyboard.IsKeyPressed(key))
                continue;
            var next = e.Cursor + dir;
            if (_world.Map.InBounds(next))
                e.Cursor = next;
            return;
        }
    }

    private void DrawExamine()
    {
        var cursor = _examine!.Cursor;
        _map.SetBackground(cursor.X, cursor.Y, Theme.Warning.GetDarker());

        _sidebar.Clear();
        _sidebar.Fill(Theme.Text, Theme.SidebarBackground, 0);
        int y = 0;
        foreach (var (text, color) in DescribeCell(cursor))
            foreach (var line in Wrap(text, SidebarWidth - 2))
                if (y < MapHeight)
                    _sidebar.Print(1, y++, line, color, Theme.SidebarBackground);

        _status.Clear();
        _status.Print(1, 0, "Examine: move the cursor  Tab: next thing  x/Esc: done  (takes no time)", Theme.Warning);
    }

    /// <summary>What the player knows about a cell, most important first.</summary>
    private List<(string Text, Color Color)> DescribeCell(Point p)
    {
        var map = _world.Map;
        var lines = new List<(string, Color)>();
        if (!map.Explored[p] && !_reveal)
        {
            lines.Add(("Unknown", Theme.Warning));
            lines.Add(("You haven't seen this place.", Theme.Text));
            return lines;
        }

        bool inView = map.PlayerFov.BooleanResultView[p] || _reveal;
        if (inView)
        {
            if (_world.ActorAt(p) is { } actor)
            {
                DescribeActor(actor, lines);
                lines.Add(("", Theme.Text));
            }
            foreach (var item in _world.ItemsAt(p))
                lines.Add((Capitalize(ItemText.AName(item)), Theme.ForItem(item).Foreground));
            foreach (var corpse in _world.CorpsesAt(p))
                lines.Add(($"The corpse of a {corpse.Kind.Replace('_', ' ')}", Theme.Text));
        }

        var (name, about) = TileText(map.Tiles[p]);
        lines.Add((name, Theme.Warning));
        lines.Add((about, Theme.Text));
        if (map.ZoneAt(p) is { } zone)
            lines.Add(($"Part of {zone.Name}.", Theme.Intent));
        DescribeGate(p, lines);
        if (!inView)
            lines.Add(("(as you remember it)", Theme.Intent));
        return lines;
    }

    private void DescribeActor(Actor actor, List<(string, Color)> lines)
    {
        var player = _world.Player;
        if (actor == player)
        {
            lines.Add(("You", Theme.Warning));
            lines.Add(($"HP {Math.Max(0, player.Hp)}/{player.MaxHp}", Theme.Text));
            lines.Add(($"Gold {player.Gold}", Theme.Text));
            return;
        }
        lines.Add((Capitalize(actor.Name), Theme.ForKind(actor.Kind).Foreground));
        lines.Add((actor.Faction.Name, Theme.Intent));
        lines.Add(($"HP {actor.Hp}/{actor.MaxHp}", Theme.Text));
        if (Intent(actor) is { Length: > 0 } intent)
            lines.Add((Capitalize(intent) + ".", Theme.Intent));
        lines.Add(Stance(actor));
        if (_world.CouldFight(player, actor))
        {
            bool ranged = actor.Range > 0 && !_world.IsAdjacent(actor.Position, player.Position);
            lines.Add(($"You hit it {_world.HitChance(player, actor, false)}% in melee, {_world.HitChance(player, actor, true)}% with a bow.", Theme.Text));
            lines.Add(($"It hits you {_world.HitChance(actor, player, ranged)}%.", Theme.Text));
        }
    }

    /// <summary>Why this actor will or won't fight the player, in terms the player can act on.</summary>
    private (string, Color) Stance(Actor actor)
    {
        var player = _world.Player;
        if (!_world.CouldFight(player, actor))
            return ("Not your enemy.", Theme.Good);
        if (_world.SquadOf(actor)?.Camp is not { } camp)
            return ("Hostile.", Theme.Danger);
        if (camp.Provoked)
            return ("Hostile: you attacked its camp.", Theme.Danger);
        if (camp.HasPass(_world.Turn))
            return ($"You've paid. It lets you be for {camp.PassTurnsLeft(_world.Turn)} more turns.", Theme.Good);
        return _world.AreHostile(actor, player)
            ? ("Hostile: you're inside its camp.", Theme.Danger)
            : ("Leaves you alone while you keep out of its camp.", Theme.Warning);
    }

    private void DescribeGate(Point p, List<(string, Color)> lines)
    {
        if (_world.Camps.FirstOrDefault(c => c.IsThreshold(p)) is not { } camp)
            return;
        if (camp.Provoked)
            lines.Add(("They won't deal with you now.", Theme.Danger));
        else if (camp.HasPass(_world.Turn))
            lines.Add(($"Paid: {camp.PassTurnsLeft(_world.Turn)} turns left.", Theme.Good));
        else if (camp.TermsKnown)
            lines.Add(($"Toll: {camp.Toll!.Price} gold for {camp.Toll.Turns} turns. (t near the gate)", Theme.Warning));
    }

    private static (string Name, string About) TileText(Tile tile) => tile switch
    {
        Tile.Wall => ("Wall", "Solid rock."),
        Tile.Floor => ("Floor", "Bare stone."),
        Tile.LairFloor => ("Worn floor", "Someone lives here."),
        Tile.Pillar => ("Pillar", "Blocks movement and sight: good cover."),
        Tile.StairsDown => ("Stairs down", "They lead deeper."),
        Tile.StairsUp => ("Stairs up", "The way back."),
        Tile.Grass => ("Grass", "Open ground."),
        Tile.Road => ("Road", "The old road. Walking it is no crime."),
        Tile.Brush => ("Brush", "You can push through. Nobody can see through it."),
        Tile.Tree => ("Trees", "Too thick to pass."),
        Tile.Palisade => ("Palisade", "A wall of sharpened logs."),
        Tile.GateOpen => ("Open gate", "One step inside is trespassing."),
        Tile.GateClosed => ("Closed gate", "Walk into it to open it. It blocks sight."),
        Tile.CaveMouth => ("Cave mouth", "The way down into the dark."),
        _ => (tile.ToString(), ""),
    };

    private GameAction? AskToPayToll()
    {
        // Nothing to confirm: let the action report why it can't happen.
        if (_world.TollProblem() is not null)
            return new PayTollAction();
        _askingToll = true;
        return null;
    }

    private GameAction? TollPromptInput(Keyboard keyboard)
    {
        if (keyboard.IsKeyPressed(Keys.Y) || keyboard.IsKeyPressed(Keys.Enter))
        {
            _askingToll = false;
            return new PayTollAction();
        }
        if (keyboard.IsKeyPressed(Keys.N))
            _askingToll = false;
        return null;
    }

    private void DrawTollPrompt()
    {
        if (_world.TollCampNearby()?.Toll is not { } toll)
            return;
        _status.Clear();
        _status.Print(1, 0, $"Pay {toll.Price} of your {_world.Player.Gold} gold for {toll.Turns} turns inside the camp? (y/n)", Theme.Warning);
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static IEnumerable<string> Wrap(string text, int width)
    {
        if (text.Length == 0)
        {
            yield return "";
            yield break;
        }
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line;
                line = "";
            }
            line = line.Length == 0 ? word : line + " " + word;
        }
        yield return line;
    }
}
