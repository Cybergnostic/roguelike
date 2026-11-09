using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Items;

namespace Roguelike.Core.Actions;

/// <summary>Picks up everything at the actor's feet that fits.</summary>
public sealed record PickUpAction : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (actor.Inventory is not { } inventory)
            return false;

        var here = world.ItemsAt(actor.Position).ToList();
        if (here.Count == 0)
            return Fail(world, actor, FailureReason.NothingToPickUp);

        bool any = false;
        foreach (var item in here)
        {
            if (item.Def.Category == ItemCategory.Coins)
            {
                // Money goes in the purse, not the pack.
                world.TakeFromFloor(item);
                actor.Gold += item.Count;
                world.Emit(new ItemPickedUp(actor.Id, item));
                any = true;
                continue;
            }
            if (!inventory.CanAdd(item))
            {
                world.Emit(new ActionFailed(actor.Id, FailureReason.InventoryFull));
                break;
            }
            world.TakeFromFloor(item);
            inventory.Add(item);
            world.Emit(new ItemPickedUp(actor.Id, item));
            any = true;
        }
        return any;
    }

    internal static bool Fail(World world, Actor actor, FailureReason reason)
    {
        world.Emit(new ActionFailed(actor.Id, reason));
        return false;
    }
}

/// <summary>Drops an item (the whole stack) at the actor's feet, unequipping it first.</summary>
public sealed record DropAction(int ItemId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (actor.Inventory?.Find(ItemId) is not { } item)
            return false;
        actor.Equipment?.Unequip(item);
        EquipAction.ClampHp(actor);
        actor.Inventory.Remove(item);
        world.PlaceOnFloor(item, actor.Position);
        world.Emit(new ItemDropped(actor.Id, item));
        return true;
    }
}

/// <summary>Equips an item into its slot, or takes it off if it's already equipped.</summary>
public sealed record EquipAction(int ItemId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (actor.Inventory?.Find(ItemId) is not { } item || actor.Equipment is not { } equipment)
            return false;
        if (!item.Def.IsEquipment)
            return PickUpAction.Fail(world, actor, FailureReason.CannotEquip);

        if (equipment.IsEquipped(item))
        {
            equipment.Unequip(item);
            ClampHp(actor);
            world.Emit(new ItemUnequipped(actor.Id, item));
            return true;
        }

        var replaced = equipment.Equip(item);
        ClampHp(actor);
        world.Emit(new ItemEquipped(actor.Id, item, replaced));
        return true;
    }

    /// <summary>
    /// Max HP from gear doesn't come with HP (else swapping it on and off would heal), and
    /// taking it off can't leave you above your new maximum.
    /// </summary>
    internal static void ClampHp(Actor actor) => actor.Hp = Math.Min(actor.Hp, actor.MaxHp);
}

/// <summary>Drinks one potion from a stack.</summary>
public sealed record DrinkAction(int ItemId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (actor.Inventory?.Find(ItemId) is not { } item)
            return false;
        if (item.Def.Category != ItemCategory.Potion)
            return PickUpAction.Fail(world, actor, FailureReason.NotDrinkable);

        int heal = item.Def.Heal * (100 + actor.Bonus(Progression.Stat.PotionHealing)) / 100;
        int healed = Math.Min(heal, actor.MaxHp - actor.Hp);
        actor.Hp += healed;
        item.Count--;
        if (item.Count <= 0)
            actor.Inventory.Remove(item);
        world.Emit(new PotionDrunk(actor.Id, item, healed));
        return true;
    }
}

/// <summary>The player's bow shot, with explicit reasons when it can't be taken.</summary>
public sealed record FireAction(int TargetId) : GameAction
{
    public override bool Perform(World world, Actor actor)
    {
        if (actor.Equipment?.Bow is null)
            return PickUpAction.Fail(world, actor, FailureReason.NoBow);
        if (world.GetActor(TargetId) is not { IsDead: false } target)
            return false;
        if (World.Distance(actor.Position, target.Position) > actor.Range)
            return PickUpAction.Fail(world, actor, FailureReason.OutOfRange);
        if (!world.HasClearShot(actor.Position, target.Position))
            return PickUpAction.Fail(world, actor, FailureReason.NoLineOfFire);
        return new RangedAttackAction(TargetId).Perform(world, actor);
    }
}
