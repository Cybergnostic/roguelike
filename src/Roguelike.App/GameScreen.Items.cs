using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.Entities;
using Roguelike.Core.Items;
using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;

namespace Roguelike.App;

/// <summary>
/// Item commands and their modal UI:
///   g / ,  pick up        i  inventory (choose an item to use it)
///   e      equip/remove   q  drink         d  drop
///   f      fire bow (Tab cycles targets, f or Enter fires, Esc cancels)
/// </summary>
public sealed partial class GameScreen
{
    /// <summary>A letter-selected list of items; picking one turns it into an action.</summary>
    private sealed record ItemMenu(string Title, List<Item> Items, Func<Item, GameAction> OnPick);

    /// <summary>Choosing a target for the bow.</summary>
    private sealed class Targeting(List<Actor> targets)
    {
        public List<Actor> Targets { get; } = targets;
        public int Index { get; set; }
        public Actor Current => Targets[Index];
    }

    private ItemMenu? _menu;
    private Targeting? _targeting;

    private bool InModalUi => _menu is not null || _targeting is not null || _sheetOpen;

    /// <summary>Handles item keys in normal play. Returns the action to take, or null.</summary>
    private GameAction? ItemCommand(Keyboard keyboard)
    {
        var player = _world.Player;
        var inventory = player.Inventory!;

        if (keyboard.IsKeyPressed(Keys.G) || keyboard.IsKeyPressed(Keys.OemComma))
            return new PickUpAction();

        if (keyboard.IsKeyPressed(Keys.C) && player.Character is not null)
            _sheetOpen = true;
        else if (keyboard.IsKeyPressed(Keys.I))
            OpenMenu("Inventory: choose an item to use it", inventory.Items,
                item => item.Def.IsEquipment ? new EquipAction(item.Id) : new DrinkAction(item.Id));
        else if (keyboard.IsKeyPressed(Keys.E))
            OpenMenu("Equip or remove which item?", inventory.Items.Where(i => i.Def.IsEquipment),
                item => new EquipAction(item.Id));
        else if (keyboard.IsKeyPressed(Keys.Q))
            OpenMenu("Drink which potion?", inventory.Items.Where(i => i.Def.Category == ItemCategory.Potion),
                item => new DrinkAction(item.Id));
        else if (keyboard.IsKeyPressed(Keys.D))
            OpenMenu("Drop which item?", inventory.Items, item => new DropAction(item.Id));
        else if (keyboard.IsKeyPressed(Keys.F))
            StartTargeting();
        return null;
    }

    private void OpenMenu(string title, IEnumerable<Item> items, Func<Item, GameAction> onPick)
    {
        var list = items.Take(26).ToList();
        if (list.Count == 0)
        {
            AddMessage("You have nothing suitable.", Theme.Intent);
            return;
        }
        _menu = new ItemMenu(title, list, onPick);
    }

    private void StartTargeting()
    {
        var player = _world.Player;
        if (player.Equipment?.Bow is null)
        {
            AddMessage("You have no bow readied.", Theme.Intent);
            return;
        }
        var targets = _world.Actors
            .Where(a => _world.CouldFight(player, a) && _world.PlayerCanSee(a))
            .OrderBy(a => World.Distance(a.Position, player.Position))
            .ToList();
        if (targets.Count == 0)
        {
            AddMessage("No targets in sight.", Theme.Intent);
            return;
        }
        _targeting = new Targeting(targets);
    }

    /// <summary>Input while a menu or targeting is open. Returns the chosen action, if any.</summary>
    private GameAction? ModalInput(Keyboard keyboard)
    {
        if (keyboard.IsKeyPressed(Keys.Escape) || (_sheetOpen && keyboard.IsKeyPressed(Keys.C)))
        {
            _menu = null;
            _targeting = null;
            _sheetOpen = false;
            return null;
        }

        if (_sheetOpen)
            return SheetInput(keyboard);

        if (_menu is { } menu)
        {
            for (int i = 0; i < menu.Items.Count; i++)
            {
                if (!keyboard.IsKeyPressed(Keys.A + i))
                    continue;
                _menu = null;
                return menu.OnPick(menu.Items[i]);
            }
            return null;
        }

        if (_targeting is { } t)
        {
            if (keyboard.IsKeyPressed(Keys.Tab))
                t.Index = (t.Index + 1) % t.Targets.Count;
            else if (keyboard.IsKeyPressed(Keys.F) || keyboard.IsKeyPressed(Keys.Enter))
            {
                _targeting = null;
                return new FireAction(t.Current.Id);
            }
        }
        return null;
    }

    private void DrawFloorItems()
    {
        var fov = _world.Map.PlayerFov.BooleanResultView;
        foreach (var floorItem in _world.FloorItems)
        {
            var p = floorItem.Position;
            if (!fov[p] && !_reveal)
                continue;
            var look = Theme.ForItem(floorItem.Item);
            _map.SetGlyph(p.X, p.Y, look.Glyph, look.Foreground, _map.Surface[p.X, p.Y].Background);
        }
    }

    private void DrawModalUi()
    {
        if (_targeting is { } t)
            DrawTargeting(t);
        if (_menu is { } menu)
            DrawMenu(menu);
        if (_sheetOpen)
            DrawSheet();
    }

    private void DrawTargeting(Targeting t)
    {
        var player = _world.Player;
        var target = t.Current;
        bool inRange = World.Distance(player.Position, target.Position) <= player.Range;
        bool clear = _world.HasClearShot(player.Position, target.Position);
        var color = inRange && clear ? Theme.Warning : Theme.Danger;

        foreach (var p in Lines.GetLine(player.Position, target.Position, Lines.Algorithm.Bresenham))
        {
            if (p == player.Position || p == target.Position)
                continue;
            _map.SetGlyph(p.X, p.Y, '*', color);
        }
        _map.SetBackground(target.Position.X, target.Position.Y, color.GetDarker());

        var note = !inRange ? "out of range" : !clear ? "blocked" : $"{player.RangedAttack} atk";
        _status.Clear();
        _status.Print(1, 0, $"Fire at {target.Name} ({note}). Tab: next target  f/Enter: fire  Esc: cancel", Theme.Warning);
    }

    private void DrawMenu(ItemMenu menu)
    {
        var player = _world.Player;
        var lines = menu.Items
            .Select((item, i) => $"{(char)('a' + i)}) {ItemText.Name(item)}{ItemText.EquippedTag(player, item)}  [{ItemText.Stats(item)}]")
            .ToList();
        int width = Math.Min(MapWidth - 4, Math.Max(menu.Title.Length, lines.Max(l => l.Length)) + 4);
        int height = lines.Count + 4;
        int x0 = (MapWidth - width) / 2, y0 = Math.Max(1, (MapHeight - height) / 2);

        var bg = Theme.SidebarBackground;
        for (int y = y0; y < y0 + height; y++)
        for (int x = x0; x < x0 + width; x++)
            _map.SetGlyph(x, y, ' ', Theme.Text, bg);
        _map.DrawBox(new Rectangle(x0, y0, width, height),
            ShapeParameters.CreateStyledBox(ICellSurface.ConnectedLineThin, new ColoredGlyph(Theme.Intent, bg)));

        _map.Print(x0 + 2, y0 + 1, menu.Title, Theme.Warning, bg);
        for (int i = 0; i < lines.Count; i++)
        {
            var item = menu.Items[i];
            var color = item.Rarity != Roguelike.Core.Items.Rarity.Common ? Theme.ForRarity(item.Rarity)
                : player.Equipment!.IsEquipped(item) ? Theme.Good : Theme.Text;
            _map.Print(x0 + 2, y0 + 2 + i, lines[i], color, bg);
        }
        _map.Print(x0 + 2, y0 + height - 1, " Esc: cancel ", Theme.Intent, bg);
    }

    /// <summary>"You see here: ..." after stepping onto items.</summary>
    private void DescribeItemsUnderfoot()
    {
        var here = _world.ItemsAt(_world.Player.Position).ToList();
        if (here.Count > 0)
            AddMessage($"You see here: {string.Join(", ", here.Select(ItemText.AName))}. (g to pick up)", Theme.Text);
    }
}
