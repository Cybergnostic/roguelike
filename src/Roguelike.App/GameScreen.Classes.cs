using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.Content;
using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;

namespace Roguelike.App;

/// <summary>Choosing who to play at the start of a game, and the menu of active skills (a).</summary>
public sealed partial class GameScreen
{
    /// <summary>The class to start as, from --class.</summary>
    public static string? StartClass { get; set; }

    private string? _classId;
    private bool _choosing;
    private bool _abilityMenu;

    private void ChooserInput(Keyboard keyboard)
    {
        if (keyboard.IsKeyPressed(Keys.Escape))
        {
            Game.Instance.MonoGameInstance.Exit();
            return;
        }
        var classes = _world.Content.Classes;
        for (int i = 0; i < classes.Count; i++)
        {
            if (!keyboard.IsKeyPressed(Keys.A + i))
                continue;
            _classId = classes[i].Id;
            StartSeed = _world.Seed; // the layout was already rolled for this game
            _world = NewWorld();
            _choosing = false;
            return;
        }
    }

    private void DrawChooser()
    {
        var bg = Theme.SidebarBackground;
        _status.Clear(); // the world behind this isn't anyone yet
        _status.Fill(Theme.Text, Theme.SidebarBackground, 0);
        for (int y = 0; y < MapHeight; y++)
            for (int x = 0; x < UiWidth; x++)
                _ui.SetGlyph(x, y, ' ', Theme.Text, bg);

        const int Left = 8;
        int row = 2;
        PrintBold(_ui, Left, row, "WHO COMES DOWN THE OLD ROAD?", Theme.Bright, bg);
        row += 2;
        var content = _world.Content;
        for (int i = 0; i < content.Classes.Count; i++)
        {
            var c = content.Classes[i];
            _ui.SetGlyph(Left, row, Theme.Bold('a' + i), Theme.Warning, bg);
            PrintBold(_ui, Left + 2, row++, c.Name, Theme.Bright, bg);
            _ui.Print(Left + 2, row++, c.Description, Theme.TextDim, bg);
            _ui.Print(Left + 2, row++, string.Join("  ", c.Attributes.Select(a => $"{a.Key.ToUpperInvariant()} {a.Value}")), Theme.Intent, bg);
            var kit = c.Kit.Select(k => k.Count > 1 ? $"{k.Count} {content.Items[k.Item].Plural ?? content.Items[k.Item].Name + "s"}" : content.Items[k.Item].Name);
            _ui.Print(Left + 2, row++, $"Carries: {string.Join(", ", kit)}, {c.Gold} gold", Theme.Intent, bg);
            _ui.Print(Left + 2, row++, $"Knows: {string.Join(", ", c.Starts.Select(s => content.Skills[s].Name))}.  Learns: {string.Join(", ", c.Trees.Select(t => content.SkillTrees.First(x => x.Id == t).Name))}", Theme.Good, bg);
            row += 1;
        }
        _ui.Print(Left, row, "Press a letter to begin.  Esc: quit", Theme.Warning, bg);
    }

    private List<SkillDef> KnownActives => _world.Player.Character?.ActiveSkills.ToList() ?? [];

    private void OpenAbilities()
    {
        if (KnownActives.Count == 0)
        {
            AddMessage("You know no skills you can call on.", Theme.Intent);
            return;
        }
        _abilityMenu = true;
    }

    private GameAction? AbilityMenuInput(Keyboard keyboard)
    {
        var skills = KnownActives;
        for (int i = 0; i < skills.Count; i++)
        {
            if (!keyboard.IsKeyPressed(Keys.A + i))
                continue;
            _abilityMenu = false;
            var ability = _world.Content.Abilities[skills[i].Ability!];
            if (!ability.NeedsTarget)
                return new UseAbilityAction(ability.Id);
            var player = _world.Player;
            var targets = _world.Actors.Where(a => _world.CouldFight(player, a) && _world.PlayerCanSee(a))
                .OrderBy(a => World.Distance(a.Position, player.Position)).ToList();
            if (targets.Count == 0)
            {
                AddMessage("No targets in sight.", Theme.Intent);
                return null;
            }
            _targeting = new Targeting(targets, ability: ability);
            return null;
        }
        return null;
    }

    private void DrawAbilities()
    {
        const int Width = 100;
        var skills = KnownActives;
        int height = skills.Count + 4;
        int x0 = (UiWidth - Width) / 2, y0 = Math.Max(1, (MapHeight - height) / 2);
        var bg = Theme.SidebarBackground;
        for (int y = y0; y < y0 + height; y++)
            for (int x = x0; x < x0 + Width; x++)
                _ui.SetGlyph(x, y, ' ', Theme.Text, bg);
        _ui.DrawBox(new Rectangle(x0, y0, Width, height),
            ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, new ColoredGlyph(Theme.Intent, bg)));
        _ui.Print(x0 + 2, y0 + 1, "Skills", Theme.Warning, bg);
        for (int i = 0; i < skills.Count; i++)
        {
            var ability = _world.Content.Abilities[skills[i].Ability!];
            int wait = _world.CooldownLeft(ability);
            var state = wait > 0 ? $"{wait} turns" : "ready";
            _ui.Print(x0 + 2, y0 + 2 + i, Truncate($"{(char)('a' + i)}) {skills[i].Name,-16} {state,-9} {skills[i].Description}", Width - 4), wait > 0 ? Theme.Intent : Theme.Text, bg);
        }
        _ui.Print(x0 + 2, y0 + height - 1, " letter: use  Esc: cancel ", Theme.Intent, bg);
    }
}
