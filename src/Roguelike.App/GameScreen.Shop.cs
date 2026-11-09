using Roguelike.Core.Actions;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;

namespace Roguelike.App;

/// <summary>Buying from someone in the valley: opens after talking to anyone with goods to sell.</summary>
public sealed partial class GameScreen
{
    private Actor? _shop;

    /// <summary>Someone begging for mercy, while the player decides.</summary>
    private Actor? _beggar;

    private GameAction? MercyInput(Keyboard keyboard)
    {
        var beggar = _beggar!;
        if (keyboard.IsKeyPressed(Keys.S))
        {
            _beggar = null;
            return new SpareAction(beggar.Id);
        }
        if (keyboard.IsKeyPressed(Keys.K))
        {
            _beggar = null;
            return new AttackAction(beggar.Id);
        }
        return null;
    }

    private void DrawMercyPrompt()
    {
        _status.Clear();
        _status.Print(2, 0, $"The {_beggar!.Name} begs for its life.   s: spare it and hear what it knows   k: kill it   Esc: not yet", Theme.Warning, Theme.PanelBackground);
    }

    private IReadOnlyList<WareDef> Wares => _world.NpcOf(_shop!)?.Goods ?? [];

    private GameAction? ShopInput(Keyboard keyboard)
    {
        var seller = _shop!;
        for (int i = 0; i < Wares.Count; i++)
            if (keyboard.IsKeyPressed(Keys.A + i))
                return new BuyAction(seller.Id, Wares[i].Item);
        return null;
    }

    private void DrawShop()
    {
        const int Width = 50;
        var wares = Wares;
        int height = wares.Count + 4;
        int x0 = (UiWidth - Width) / 2, y0 = Math.Max(1, (MapHeight - height) / 2);
        var bg = Theme.SidebarBackground;
        for (int y = y0; y < y0 + height; y++)
        for (int x = x0; x < x0 + Width; x++)
            _ui.SetGlyph(x, y, ' ', Theme.Text, bg);
        _ui.DrawBox(new Rectangle(x0, y0, Width, height),
            ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, new ColoredGlyph(Theme.Intent, bg)));
        _ui.Print(x0 + 2, y0 + 1, $"{Capitalize(_shop!.Name)}'s goods. You have {_world.Player.Gold} gold.", Theme.Warning, bg);
        for (int i = 0; i < wares.Count; i++)
        {
            var def = _world.Content.Items[wares[i].Item];
            bool afford = wares[i].Price <= _world.Player.Gold;
            _ui.Print(x0 + 2, y0 + 2 + i, $"{(char)('a' + i)}) {def.Name,-24} {wares[i].Price,4} gold", afford ? Theme.Text : Theme.Intent, bg);
        }
        _ui.Print(x0 + 2, y0 + height - 1, " letter: buy  Esc: leave ", Theme.Intent, bg);
    }
}
