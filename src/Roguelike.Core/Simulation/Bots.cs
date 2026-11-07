using Roguelike.Core.Actions;
using Roguelike.Core.Entities;
using Roguelike.Core.Items;
using SadRogue.Primitives;

namespace Roguelike.Core.Simulation;

/// <summary>Plays the player's side automatically, for tests and headless simulation reports.</summary>
public interface IPlayerBot
{
    string Name { get; }
    GameAction Decide(World world);
}

/// <summary>
/// Charges the nearest visible enemy (shooting first if it has a bow); otherwise heads for
/// unexplored rooms. Drinks potions when low, wears the best gear it finds. Never retreats.
/// </summary>
public sealed class AggressiveBot : IPlayerBot
{
    public string Name => "aggressive";

    public GameAction Decide(World world)
    {
        var player = world.Player;
        var target = world.Actors
            .Where(a => world.AreHostile(player, a) && world.PlayerCanSee(a))
            .OrderBy(a => World.Distance(a.Position, player.Position))
            .FirstOrDefault();

        // Use the kit: heal when low, put on better gear, grab loot, shoot before closing in.
        if (player.Hp * 10 < player.MaxHp * 4
            && player.Inventory?.Items.FirstOrDefault(i => i.Def.Category == ItemCategory.Potion) is { } potion)
            return new DrinkAction(potion.Id);
        if (BetterGear(player) is { } upgrade)
            return new EquipAction(upgrade.Id);
        if (target is null && world.ItemsAt(player.Position).Any() && player.Inventory is { IsFull: false })
            return new PickUpAction();

        if (target is not null && world.IsAdjacent(player.Position, target.Position))
            return new AttackAction(target.Id);
        if (target is not null && player.Equipment?.Bow is not null
            && World.Distance(player.Position, target.Position) <= player.Range
            && world.HasClearShot(player.Position, target.Position))
            return new FireAction(target.Id);

        Point goal;
        var fov = world.Map.PlayerFov.BooleanResultView;
        var loot = player.Inventory is { IsFull: false }
            ? world.FloorItems.Where(f => fov[f.Position])
                .OrderBy(f => World.Distance(f.Position, player.Position))
                .FirstOrDefault()
            : null;

        if (target is not null)
            goal = target.Position;
        else if (loot is not null)
            goal = loot.Position;
        else
        {
            var room = world.Map.Rooms
                .OrderBy(r => world.Map.Explored[r.Center] ? 1 : 0)
                .ThenBy(r => World.Distance(r.Center, player.Position))
                .First();
            goal = room.Center == player.Position
                ? world.Map.Rooms[world.Turn % world.Map.Rooms.Count].Center
                : room.Center;
        }
        return world.StepToward(player, goal) is { } dir ? new MoveAction(dir) : new WaitAction();
    }

    /// <summary>An unequipped item in the pack that beats what's in its slot, if any.</summary>
    private static Item? BetterGear(Actor player)
    {
        if (player.Inventory is not { } inventory || player.Equipment is not { } equipment)
            return null;
        static int Score(Item? i) => i is null ? int.MinValue : i.AttackBonus + i.DefenseBonus + i.Def.Range;
        foreach (var item in inventory.Items.Where(i => i.Def.IsEquipment && !equipment.IsEquipped(i)))
        {
            var current = item.Def.Category switch
            {
                ItemCategory.Weapon => equipment.Weapon,
                ItemCategory.Bow => equipment.Bow,
                _ => equipment.Armor,
            };
            if (Score(item) > Score(current))
                return item;
        }
        return null;
    }
}

/// <summary>Does nothing. Combined with observer mode, the dungeon runs on its own.</summary>
public sealed class IdleBot : IPlayerBot
{
    public string Name => "idle";
    public GameAction Decide(World world) => new WaitAction();
}
