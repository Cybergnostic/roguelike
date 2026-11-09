using Roguelike.Core;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Items;
using SadConsole;
using SadRogue.Primitives;
using Console = SadConsole.Console;

namespace Roguelike.App;

/// <summary>Everything around the map: the place bar, in-sight list, this-place summary, status strip, log and key guide.</summary>
public sealed partial class GameScreen
{
    private int _inspectRow;

    private static void PrintBold(Console target, int x, int y, string text, Color color, Color background)
    {
        for (int i = 0; i < text.Length && x + i < target.Width; i++)
            target.SetGlyph(x + i, y, Theme.Bold(text[i]), color, background);
    }

    private (string Name, string Sub) PlaceTitle()
    {
        var world = _world;
        if (world.Map.ZoneAt(world.Player.Position) is { } zone)
            return (Capitalize(zone.Name), world.Depth == World.SurfaceDepth ? "The valley" : $"Level {world.Depth}");
        if (world.Depth == World.SurfaceDepth)
            return ("The Valley", world.Camps.FirstOrDefault(c => world.Map.Explored[c.Zone.Heart]) is { } camp ? $"Near {camp.Zone.Name}" : "The old road");
        return ($"Level {world.Depth}", world.Depth == world.Content.Dungeon.Levels ? "The bottom of everything" : "Underground");
    }

    private void DrawHeader()
    {
        var (name, sub) = PlaceTitle();
        _header.Clear();
        PrintBold(_header, 2, 0, name, Theme.Bright, Theme.PanelBackground);
        _header.Print(4 + name.Length, 0, sub, Theme.Intent, Theme.PanelBackground);
        var right = $"Turn {_world.Turn}";
        _header.Print(Width - right.Length - 2, 0, right, Theme.Intent, Theme.PanelBackground);
    }

    /// <summary>How hurt something looks: a word, not a number, for what the player can judge by eye.</summary>
    private static string HurtWord(Actor actor) =>
        actor.Hp >= actor.MaxHp ? "unhurt"
        : actor.Hp * 3 >= actor.MaxHp * 2 ? "scratched"
        : actor.Hp * 3 >= actor.MaxHp ? "wounded"
        : "near death";

    private void DrawSidebar()
    {
        _sidebar.Clear();
        _sidebar.Fill(Theme.Text, Theme.SidebarBackground, 0);
        if (_showSquads)
        {
            DrawSquadList();
            _inspectRow = MapHeight;
            return;
        }

        var bg = Theme.SidebarBackground;
        var player = _world.Player;
        var visible = _world.Actors
            .Where(a => a != player && !a.IsDead && (_reveal || _world.PlayerCanSee(a)))
            .OrderBy(a => World.Distance(a.Position, player.Position))
            .ToList();

        int y = 1;
        PrintBold(_sidebar, 2, y++, "IN SIGHT", Theme.Intent, bg);
        if (visible.Count == 0)
            _sidebar.Print(2, y++, "Nothing moving nearby.", Theme.Intent, bg);
        const int MaxRows = 6;
        foreach (var actor in visible.Take(MaxRows))
        {
            var (color, _) = StanceOf(actor);
            _sidebar.SetGlyph(2, y, Theme.Bold(Theme.ForKind(actor.Kind).Glyph), color, bg);
            var hurt = HurtWord(actor);
            _sidebar.Print(4, y, Truncate(Capitalize(actor.Name), SidebarWidth - 7 - hurt.Length), Theme.Bright, bg);
            _sidebar.Print(SidebarWidth - hurt.Length - 2, y++, hurt, Theme.Intent, bg);
            _sidebar.Print(4, y++, Truncate(Capitalize(Intent(actor)), SidebarWidth - 6), color == Theme.Danger ? Theme.DangerSoft : Theme.TextDim, bg);
        }
        if (visible.Count > MaxRows)
            _sidebar.Print(2, y++, $"...and {visible.Count - MaxRows} more", Theme.Intent, bg);

        y = DrawRule(y);
        if (_world.Camps.FirstOrDefault(c => _world.Map.Explored[c.Zone.Heart] || c.TermsKnown || (c.Toll is { } t && _world.Map.Explored[t.Gate])) is { } camp)
        {
            PrintBold(_sidebar, 2, y++, "THIS PLACE", Theme.Intent, bg);
            var (mood, color) = CampMood(camp);
            y = Fact(y, "Camp seems", mood, color);
            y = Fact(y, "Gate", GateState(camp), Theme.Bright);
            if (camp.Toll is { } toll && camp.TermsKnown && !camp.Provoked && !camp.Overrun)
                y = Fact(y, "Toll", $"{toll.Price} gold, {toll.Turns} turns", Theme.Bright);
            if (camp.HasPass(_world.Turn))
                y = Fact(y, "Your time", $"{camp.PassTurnsLeft(_world.Turn)} turns", Theme.Teal);
            y = DrawRule(y);
        }
        _inspectRow = y;
        PrintBold(_sidebar, 2, y++, "INSPECT", Theme.Intent, bg);
        _sidebar.Print(2, y, "Press x to look around.", Theme.Intent, bg);
    }

    private int DrawRule(int y)
    {
        for (int x = 0; x < SidebarWidth; x++)
            _sidebar.SetGlyph(x, y, 196, Theme.Line, Theme.SidebarBackground);
        return y + 2;
    }

    private int Fact(int y, string label, string value, Color color)
    {
        _sidebar.Print(2, y, label, Theme.Intent, Theme.SidebarBackground);
        _sidebar.Print(Math.Max(2 + label.Length + 1, SidebarWidth - value.Length - 2), y, Truncate(value, SidebarWidth - label.Length - 5), color, Theme.SidebarBackground);
        return y + 1;
    }

    /// <summary>What the camp looks like from where the player stands: only what they could tell by watching.</summary>
    private (string, Color) CampMood(Camp camp)
    {
        if (camp.Overrun)
            return ("Overrun", Theme.Danger);
        if (camp.Provoked)
            return ("Alarmed", Theme.Danger);
        if (camp.HasPass(_world.Turn))
            return ("Tolerating you", Theme.Teal);
        var guards = _world.Squads.Where(s => s.Camp == camp).SelectMany(s => s.Members).Where(m => _world.PlayerCanSee(m)).ToList();
        if (guards.Any(g => _world.AreHostile(g, _world.Player)))
            return ("Hostile", Theme.Danger);
        if (guards.Any(g => _world.CanSee(g, _world.Player)))
            return ("Wary", Theme.Warning);
        return ("Calm", Theme.Good);
    }

    private string GateState(Camp camp)
    {
        if (camp.Toll is not { } toll || !_world.Map.Explored[toll.Gate])
            return "Not seen yet";
        return _world.Map.Tiles[toll.Gate] switch
        {
            Roguelike.Core.Map.Tile.GateClosed => "Shut",
            Roguelike.Core.Map.Tile.GateBroken => "Broken open",
            _ when camp.Overrun => "Open, unguarded",
            _ => "Open, guarded",
        };
    }

    private void DrawStatus()
    {
        var player = _world.Player;
        var bg = Theme.PanelBackground;
        _status.Clear();

        double health = (double)Math.Max(0, player.Hp) / player.MaxHp;
        var hpColor = health < 0.34 ? Theme.Danger : health < 0.67 ? Theme.Warning : Theme.Good;
        var hp = $"HP {Math.Max(0, player.Hp)}/{player.MaxHp}";
        PrintBold(_status, 2, 0, hp, hpColor, bg);
        const int BarWidth = 16;
        int barX = 3 + hp.Length;
        int filled = (int)Math.Ceiling(BarWidth * health);
        for (int i = 0; i < BarWidth; i++)
            _status.SetGlyph(barX + i, 0, 220, i < filled ? hpColor : Theme.Line, bg);

        int potions = player.Inventory?.Items.Where(i => i.Def.Category == ItemCategory.Potion).Sum(i => i.Count) ?? 0;
        var bow = player.Equipment?.Bow is null ? "No bow" : $"Bow {player.RangedAttack} r{player.Range}";
        _status.Print(barX + BarWidth + 3, 0, $"Atk {player.Attack}   Def {player.Defense}   {bow}   Potions {potions}", Theme.Text, bg);
        var gold = $"Gold {player.Gold}";
        _status.Print(barX + BarWidth + 48, 0, gold, Theme.Gold, bg);
        if (player.Character is { } c)
            _status.Print(barX + BarWidth + 60, 0, CanSpendXp() ? $"XP {c.Xp} (c)" : $"XP {c.Xp}", CanSpendXp() ? Theme.Warning : Theme.Text, bg);

        var flags = new List<string>();
        if (_world.ObserverMode) flags.Add(_autoplay ? "SPECTATING (auto)" : "SPECTATING");
        if (_reveal) flags.Add("REVEAL");
        if (_showSquads) flags.Add("SQUADS");
        if (_heatmap != 0) flags.Add($"HEAT: {HeatmapName}");
        if (_classic) flags.Add("CLASSIC");
        flags.Add(_world.Depth == World.SurfaceDepth ? "Depth: surface" : $"Depth: {_world.Depth}");
        var text = string.Join("   ", flags);
        _status.Print(Width - text.Length - 2, 0, text, flags.Count > 1 ? Theme.Warning : Theme.Intent, bg);
    }

    private void DrawLog()
    {
        _log.Clear();
        _log.Fill(Theme.Text, Theme.LogBackground, 0);
        int y = _log.Height - 1 - (_messages.Count - 1) - 1;
        for (int i = 0; i < _messages.Count; i++)
        {
            var color = i == _messages.Count - 1 ? _messages[i].Color : Theme.Fade(_messages[i].Color);
            _log.Print(2, Math.Max(0, y + i), Truncate(_messages[i].Text, _log.Width - 4), color, Theme.LogBackground);
        }
    }

    private static readonly (string Key, string What)[] KeyGuide =
    [
        ("x", "Inspect"), ("i", "Inventory"),
        ("m", "Stones"), ("c", "Character"),
        ("t", "Toll"), ("f", "Fire bow"),
        ("g", "Pick up"), ("e", "Equip"),
        ("q", "Drink"), ("<>", "Stairs"),
    ];

    private void DrawKeys()
    {
        _keys.Clear();
        _keys.Fill(Theme.Text, Theme.LogBackground, 0);
        for (int y = 0; y < _keys.Height; y++)
            _keys.SetGlyph(0, y, 179, Theme.Line, Theme.LogBackground);
        for (int i = 0; i < KeyGuide.Length; i++)
        {
            var (key, what) = KeyGuide[i];
            int x = 2 + i % 2 * 17, y = 1 + i / 2;
            _keys.Print(x, y, key, Theme.Warning, Theme.LogBackground);
            _keys.Print(x + key.Length + 1, y, what, Theme.TextDim, Theme.LogBackground);
        }
        _keys.Print(2, _keys.Height - 1, "Esc: quit   F4: classic look", Theme.Intent, Theme.LogBackground);
    }
}
