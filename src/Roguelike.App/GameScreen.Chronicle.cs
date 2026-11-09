using Roguelike.Core;
using Roguelike.Core.History;
using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;

namespace Roguelike.App;

/// <summary>The chronicle (m): the parts of the site's past the player has read off its stones.</summary>
public sealed partial class GameScreen
{
    private bool _chronicleOpen;

    private void ChronicleInput(Keyboard keyboard)
    {
        if (keyboard.IsKeyPressed(Keys.M))
            _chronicleOpen = false;
    }

    private void DrawChronicle()
    {
        const int Width = 72;
        var lines = new List<(string Text, Color Color)>();
        foreach (var era in _world.History.InOrder)
        {
            if (_world.KnowsOf(era))
                lines.AddRange(Wrap(EraText(era), Width - 4).Select(l => (l, Theme.Text)));
            else
                lines.Add(("A stone you haven't found.", Theme.Intent));
            lines.Add(("", Theme.Text));
        }
        lines.RemoveAt(lines.Count - 1);

        int height = lines.Count + 4;
        int x0 = (MapWidth - Width) / 2, y0 = Math.Max(1, (MapHeight - height) / 2);
        var bg = Theme.SidebarBackground;
        for (int y = y0; y < y0 + height; y++)
        for (int x = x0; x < x0 + Width; x++)
            _map.SetGlyph(x, y, ' ', Theme.Text, bg);
        _map.DrawBox(new Rectangle(x0, y0, Width, height),
            ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, new ColoredGlyph(Theme.Intent, bg)));

        _map.Print(x0 + 2, y0 + 1, "What the stones tell", Theme.Warning, bg);
        for (int i = 0; i < lines.Count; i++)
            _map.Print(x0 + 2, y0 + 2 + i, lines[i].Text, lines[i].Color, bg);
        _map.Print(x0 + 2, y0 + height - 1, " m/Esc: close ", Theme.Intent, bg);
    }

    private string EraText(Era era)
    {
        var h = _world.History;
        var heirloom = string.Join(' ', h.HeirloomWords);
        var item = _world.Content.Items[h.HeirloomItem].Name;
        return era.Kind switch
        {
            EraKind.Founding => $"{h.Founder} of the {h.Clan} clan dug a mine under this valley, {h.MineDepth} levels deep.",
            EraKind.Fall when h.Fall == FallKind.War =>
                $"Goblins came up the shafts and met the {h.Clan} clan on level {h.FallDepth}. {h.Founder} fell there with the {item} called {heirloom}, and the dead were never buried.",
            EraKind.Fall when h.Fall == FallKind.Plague =>
                $"A sickness came up from level {h.FallDepth}. {h.Founder} sealed the sick below and stayed with them, with the {item} called {heirloom}. Those who died got up again.",
            EraKind.Fall =>
                $"The roof came down on level {h.FallDepth}. {h.Founder} was lost under it with the {item} called {heirloom}, and the {h.Clan} clan left what they could not carry.",
            EraKind.GoblinsCame => h.Holders.Story.Replace("{tribe}", h.Tribe),
            EraKind.NecromancerCame => $"{h.Necromancer} came for the unburied dead, and has not left.",
            _ => "",
        };
    }
}
