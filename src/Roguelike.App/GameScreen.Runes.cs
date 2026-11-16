using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.Content;
using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;

namespace Roguelike.App;

/// <summary>
/// Runes on screen: marks on the map, and the r menu (pick a rune you know, then where to put it).
/// </summary>
public sealed partial class GameScreen
{
    private bool _runeMenu;
    private string? _runeChosen;

    /// <summary>CP437 look-alikes of the Futhark letters until the font carries the real ones.</summary>
    private static int RuneGlyph(string rune) => rune switch
    {
        "algiz" => 157,    // ¥  like ᛉ
        "thurisaz" => 16,  // ►  like ᚦ
        "isa" => 179,      // │  like ᛁ
        "naudhiz" => 216,  // ╪  like ᚾ
        "kenaz" => 174,    // «  like ᚲ
        "berkano" => 225,  // ß  like ᛒ
        _ => '*',
    };

    private static readonly Color RuneColor = new(232, 201, 139);
    private static readonly Color EnemyRuneColor = new(214, 92, 92);

    private void DrawMarks()
    {
        var fov = _world.Map.PlayerFov.BooleanResultView;
        foreach (var mark in _world.Marks)
        {
            bool mine = mark.OwnerId == _world.Player.Id;
            if ((!_world.PlayerSeesMark(mark) && !_reveal) || (!fov[mark.At] && !_reveal && !(mine && _world.Map.Explored[mark.At])) || !OnScreen(mark.At, out var at))
                continue;
            var color = mine ? RuneColor : EnemyRuneColor;
            _map.SetGlyph(at.X, at.Y, RuneGlyph(mark.RuneId), fov[mark.At] ? color : Theme.Remembered(color), _map.Surface[at.X, at.Y].Background);
        }
    }

    /// <summary>What a mark is, for examine and the tooltip.</summary>
    private string? MarkText(Point p)
    {
        if (_world.MarkAt(p) is not { } mark || !_world.Content.Runes.TryGetValue(mark.RuneId, out var rune) || (!_world.PlayerSeesMark(mark) && !_reveal))
            return null;
        if (mark.SealOf is not null)
            return $"A seal: {rune.Name}, cut deep and fed with something dark. It binds some of the dead on this level. Break it (R, next to it) and they crumble.";
        string whose = mark.OwnerId == _world.Player.Id ? "" : "Someone else's mark. ";
        string fades = mark.Until == int.MaxValue ? "" : $" Fades in {Math.Max(0, mark.Until - _world.Turn)} turns.";
        return $"{whose}{rune.Name} ({rune.Meaning}): {rune.Ground}.{(rune.Charges > 0 ? $" {mark.Charges} left." : "")}{fades}";
    }

    private void OpenRunes()
    {
        if (_world.Player.Character is not { } c || c.KnownRunes.Count == 0)
        {
            AddMessage("You know no runes. Inscribed stones teach them to those who can read them.", Theme.Intent);
            return;
        }
        _runeMenu = true;
        _runeChosen = null;
    }

    private GameAction? RuneMenuInput(Keyboard keyboard)
    {
        var runes = _world.Player.Character!.KnownRunes;
        if (_runeChosen is null)
        {
            for (int i = 0; i < runes.Count; i++)
                if (keyboard.IsKeyPressed(Keys.A + i))
                    _runeChosen = runes[i];
            return null;
        }
        RuneSurface? surface = keyboard.IsKeyPressed(Keys.G) ? RuneSurface.Ground
            : keyboard.IsKeyPressed(Keys.B) ? RuneSurface.Body
            : keyboard.IsKeyPressed(Keys.W) ? RuneSurface.Weapon
            : keyboard.IsKeyPressed(Keys.A) ? RuneSurface.Armor
            : null;
        if (surface is not { } where)
            return null;
        var rune = _runeChosen;
        _runeMenu = false;
        _runeChosen = null;
        return new CarveAction(rune, where);
    }

    private void DrawRunes()
    {
        const int Width = 100;
        var runes = _world.Player.Character!.KnownRunes.Select(id => _world.Content.Runes[id]).ToList();
        int height = (_runeChosen is null ? runes.Count : 4) + 4;
        int x0 = (UiWidth - Width) / 2, y0 = Math.Max(1, (MapHeight - height) / 2);
        var bg = Theme.SidebarBackground;
        for (int y = y0; y < y0 + height; y++)
            for (int x = x0; x < x0 + Width; x++)
                _ui.SetGlyph(x, y, ' ', Theme.Text, bg);
        _ui.DrawBox(new Rectangle(x0, y0, Width, height),
            ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, new ColoredGlyph(Theme.Intent, bg)));
        if (_runeChosen is null)
        {
            _ui.Print(x0 + 2, y0 + 1, $"Runes you know ({_world.Marks.Count(m => m.OwnerId == _world.Player.Id)}/{_world.MaxMarks(_world.Player)} marks carved)", Theme.Warning, bg);
            for (int i = 0; i < runes.Count; i++)
            {
                _ui.SetGlyph(x0 + 5, y0 + 2 + i, RuneGlyph(runes[i].Id), RuneColor, bg);
                _ui.Print(x0 + 2, y0 + 2 + i, $"{(char)('a' + i)})", Theme.Text, bg);
                _ui.Print(x0 + 7, y0 + 2 + i, Truncate($"{runes[i].Name,-9} {runes[i].Meaning}", Width - 9), Theme.Text, bg);
            }
            _ui.Print(x0 + 2, y0 + height - 1, " letter: choose  Esc: cancel ", Theme.Intent, bg);
            return;
        }
        var chosen = _world.Content.Runes[_runeChosen];
        _ui.Print(x0 + 2, y0 + 1, $"{chosen.Name} ({chosen.Meaning}): where?", Theme.Warning, bg);
        var options = new (char Key, RuneSurface Surface, string Label)[]
        {
            ('g', RuneSurface.Ground, "the ground here (6 turns)"), ('b', RuneSurface.Body, "your skin (1 turn)"),
            ('w', RuneSurface.Weapon, "your weapon (long work)"), ('a', RuneSurface.Armor, "your armour (long work)"),
        };
        for (int i = 0; i < options.Length; i++)
        {
            var (key, surface, label) = options[i];
            var effect = chosen.On(surface);
            var problem = effect is null ? "it does nothing there" : _world.CarveProblem(_world.Player, chosen.Id, surface) is { } p ? EventText.Failure(p) ?? "" : effect;
            bool ok = effect is not null && _world.CarveProblem(_world.Player, chosen.Id, surface) is null;
            _ui.Print(x0 + 2, y0 + 2 + i, Truncate($"{key}) {label,-28} {problem}", Width - 4), ok ? Theme.Text : Theme.Intent, bg);
        }
        _ui.Print(x0 + 2, y0 + height - 1, " letter: carve  Esc: cancel ", Theme.Intent, bg);
    }
}
