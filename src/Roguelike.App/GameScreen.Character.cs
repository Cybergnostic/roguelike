using Roguelike.Core.Actions;
using Roguelike.Core.Progression;
using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;

namespace Roguelike.App;

/// <summary>
/// The character sheet (c): attributes, skill trees and derived stats. Press a letter to spend
/// XP on that line; spending is free (no turn passes), so the sheet stays open.
/// </summary>
public sealed partial class GameScreen
{
    private bool _sheetOpen;

    /// <summary>One buyable line on the sheet, in letter order.</summary>
    private sealed record SheetLine(char Letter, GameAction Buy, bool Available);

    /// <summary>Attributes first (a–f), then skills in tree order.</summary>
    private List<SheetLine> SheetLines()
    {
        var c = _world.Player.Character!;
        var content = _world.Content;
        var lines = new List<SheetLine>();
        char letter = 'a';
        foreach (var a in content.Attributes)
            lines.Add(new SheetLine(letter++, new RaiseAttributeAction(a.Id), c.CheckRaise(a.Id) is null));
        foreach (var s in content.SkillTrees.SelectMany(t => t.Skills))
            lines.Add(new SheetLine(letter++, new LearnSkillAction(s.Id), c.CheckLearn(s.Id) is null));
        return lines;
    }

    /// <summary>Whether the player can afford anything right now (shown on the status line).</summary>
    private bool CanSpendXp() => _world.Player.Character is not null && SheetLines().Any(l => l.Available);

    private GameAction? SheetInput(Keyboard keyboard)
    {
        foreach (var line in SheetLines())
            if (line.Letter - 'a' < 26 && keyboard.IsKeyPressed(Keys.A + (line.Letter - 'a')))
                return line.Buy;
        return null;
    }

    private void DrawSheet()
    {
        var player = _world.Player;
        var c = player.Character!;
        var content = _world.Content;
        var bg = Theme.SidebarBackground;
        int x0 = 2, y0 = 0, width = UiWidth - 4, height = MapHeight;

        for (int y = y0; y < y0 + height; y++)
        for (int x = x0; x < x0 + width; x++)
            _ui.SetGlyph(x, y, ' ', Theme.Text, bg);
        _ui.DrawBox(new Rectangle(x0, y0, width, height),
            ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, new ColoredGlyph(Theme.Intent, bg)));

        int y1 = y0 + 1;
        _ui.Print(x0 + 2, y1, "Character", Theme.Warning, bg);
        var xp = $"{c.Xp} XP to spend ({c.TotalXp} earned)";
        _ui.Print(x0 + width - xp.Length - 2, y1++, xp, c.Xp > 0 ? Theme.Good : Theme.Text, bg);

        var lines = SheetLines();
        int i = 0;
        Color ColorOf(SheetLine l, bool done) => done ? Theme.Intent : l.Available ? Theme.Good : Theme.Text;

        _ui.Print(x0 + 2, y1++, $"Attributes  (next point: {c.RaiseCost} XP)", Theme.Warning, bg);
        foreach (var a in content.Attributes)
        {
            var line = lines[i++];
            int score = c.Score(a.Id), mod = c.ModifierOf(a.Id);
            bool maxed = score >= content.AttributeRaise.MaxScore;
            var text = $"{line.Letter}) {a.Name,-13} {score,2} ({(mod >= 0 ? "+" : "")}{mod})  {a.Description}";
            _ui.Print(x0 + 3, y1++, Truncate(text, width - 5), ColorOf(line, maxed), bg);
        }

        foreach (var tree in content.SkillTrees)
        {
            _ui.Print(x0 + 2, y1++, tree.Name, Theme.Warning, bg);
            foreach (var s in tree.Skills)
            {
                var line = lines[i++];
                int rank = c.Rank(s.Id);
                var cost = c.NextRankCost(s.Id) is { } n ? $"{n} XP" : "mastered";
                var missing = s.Requires.Where(r => c.Rank(r) == 0).Select(r => content.Skills[r].Name).ToList();
                var note = missing.Count > 0 ? $"needs {string.Join(", ", missing)}" : s.Description;
                var text = $"{line.Letter}) {s.Name,-16} {rank}/{s.Ranks}  {cost,-8}  {note}";
                _ui.Print(x0 + 3, y1++, Truncate(text, width - 5), ColorOf(line, rank >= s.Ranks), bg);
            }
        }

        var derived = $"Melee {player.Attack}  Bow {player.RangedAttack}  Def {player.Defense}  HP {player.MaxHp}  " +
                      $"Regen 1 HP/{_world.RegenInterval} turns  Potions +{player.Bonus(Stat.PotionHealing)}%  XP +{player.Bonus(Stat.XpGain)}%  Speed +{player.Bonus(Stat.Speed)}%  Step {player.MoveTime}";
        _ui.Print(x0 + 2, y0 + height - 2, Truncate(derived, width - 4), Theme.Text, bg);
        _ui.Print(x0 + 2, y0 + height - 1, " letter: spend XP (free action)  Esc/c: close ", Theme.Intent, bg);
    }
}
