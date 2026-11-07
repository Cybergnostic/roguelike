using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.Events;
using Roguelike.Core.Items;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class ItemTests
{
    private static ItemDef Def(World world, string id) => world.Content.Items[id];

    [Fact]
    public void PlayerStartsWithKitEquipped()
    {
        var world = World.Create(1);
        var player = world.Player;
        var kit = world.Content.Player;

        Assert.NotNull(player.Equipment!.Weapon);
        Assert.NotNull(player.Equipment.Bow);
        Assert.NotNull(player.Equipment.Armor);
        Assert.Equal(kit.Attack + player.Equipment.Weapon!.AttackBonus, player.Attack);
        Assert.Equal(kit.Defense + player.Equipment.Armor!.DefenseBonus, player.Defense);
        Assert.Equal(player.Equipment.Bow!.Def.Range, player.Range);

        var potions = player.Inventory!.Items.Single(i => i.Def.Category == ItemCategory.Potion);
        Assert.Equal(2, potions.Count); // the kit's two potions stack in one slot
    }

    [Fact]
    public void PickingUpMovesItemFromFloorToPack()
    {
        var world = World.Create(1);
        var item = world.CreateItem(Def(world, "long_sword"), enchantment: 2);
        world.PlaceOnFloor(item, world.Player.Position);

        var events = world.Step(new PickUpAction());

        Assert.Contains(item, world.Player.Inventory!.Items);
        Assert.Empty(world.ItemsAt(world.Player.Position));
        Assert.Contains(events, e => e is ItemPickedUp p && p.Item == item);
        Assert.Equal(1, world.Turn);
    }

    [Fact]
    public void PickingUpWithNothingThereCostsNoTurn()
    {
        var world = World.Create(1);
        var events = world.Step(new PickUpAction());
        Assert.Contains(events, e => e is ActionFailed { Reason: FailureReason.NothingToPickUp });
        Assert.Equal(0, world.Turn);
    }

    [Fact]
    public void FullPackRefusesNewItems()
    {
        var world = World.Create(1);
        var inventory = world.Player.Inventory!;
        while (!inventory.IsFull)
            inventory.Add(world.CreateItem(Def(world, "dagger")));
        world.PlaceOnFloor(world.CreateItem(Def(world, "short_sword")), world.Player.Position);

        var events = world.Step(new PickUpAction());

        Assert.Contains(events, e => e is ActionFailed { Reason: FailureReason.InventoryFull });
        Assert.Single(world.ItemsAt(world.Player.Position));
        Assert.Equal(0, world.Turn);
    }

    [Fact]
    public void EnchantedWeaponAddsItsPlusToAttack()
    {
        var world = World.Create(1);
        var player = world.Player;
        var sword = player.Inventory!.Add(world.CreateItem(Def(world, "long_sword"), enchantment: 2));
        var old = player.Equipment!.Weapon;

        var events = world.Step(new EquipAction(sword.Id));

        Assert.Equal(player.BaseAttack + 6 + 2, player.Attack);
        Assert.Contains(events, e => e is ItemEquipped q && q.Item == sword && q.Replaced == old);

        world.Step(new EquipAction(sword.Id)); // again = take it off
        Assert.Null(player.Equipment.Weapon);
        Assert.Equal(player.BaseAttack, player.Attack);
    }

    [Fact]
    public void CursedArmorLowersDefense()
    {
        var world = World.Create(1);
        var player = world.Player;
        var cursed = player.Inventory!.Add(world.CreateItem(Def(world, "leather_armor"), enchantment: -1));
        world.Step(new EquipAction(cursed.Id));
        Assert.Equal(player.BaseDefense + 1 - 1, player.Defense);
    }

    [Fact]
    public void DrinkingHealsAndUsesOnePotion()
    {
        var world = World.Create(1);
        var player = world.Player;
        var potions = player.Inventory!.Items.Single(i => i.Def.Category == ItemCategory.Potion);
        player.Hp = 5;

        var events = world.Step(new DrinkAction(potions.Id));

        Assert.Equal(5 + potions.Def.Heal, player.Hp);
        Assert.Equal(1, potions.Count);
        Assert.Contains(events, e => e is PotionDrunk { Healed: > 0 });

        world.Step(new DrinkAction(potions.Id));
        Assert.DoesNotContain(potions, player.Inventory.Items);
    }

    [Fact]
    public void DroppingAnEquippedItemUnequipsIt()
    {
        var world = World.Create(1);
        var player = world.Player;
        var sword = player.Equipment!.Weapon!;

        world.Step(new DropAction(sword.Id));

        Assert.Null(player.Equipment.Weapon);
        Assert.Contains(sword, world.ItemsAt(player.Position));
    }

    [Fact]
    public void BowShotsHitVisibleEnemiesAndNeedABow()
    {
        for (ulong seed = 0; seed < 100; seed++)
        {
            var world = World.Create(seed);
            var player = world.Player;
            player.Hp = 10_000;
            var target = world.Actors.FirstOrDefault(a => world.AreHostile(player, a));
            if (target is null)
                continue;
            var spots = world.Map.Tiles.Positions().Where(p =>
                world.IsFree(p) && World.Distance(p, target.Position) is >= 2 and <= 4
                && world.HasClearShot(p, target.Position)).Take(1).ToList();
            if (spots.Count == 0)
                continue;
            player.Position = spots[0];

            var events = world.Step(new FireAction(target.Id));
            Assert.Contains(events, e => e is ActorAttacked { Ranged: true } a && a.AttackerId == player.Id);

            world.Step(new EquipAction(player.Equipment!.Bow!.Id)); // put the bow away
            events = world.Step(new FireAction(target.Id));
            Assert.Contains(events, e => e is ActionFailed { Reason: FailureReason.NoBow });
            return;
        }
        Assert.Fail("no usable seed");
    }

    [Fact]
    public void MonstersDropLootWhereTheyFall()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            var world = World.Create(seed);
            foreach (var monster in world.Actors.Where(a => a != world.Player && a.LootTable is not null).ToList())
            {
                var at = monster.Position;
                world.Damage(world.Player, monster, 1000, ranged: false);
                var dropped = world.ItemsAt(at).ToList();
                if (dropped.Count > 0)
                    return; // found a drop at the death position
            }
        }
        Assert.Fail("no monster dropped anything in 20 levels");
    }

    [Fact]
    public void EnchantmentsFollowTheTable()
    {
        var world = World.Create(1);
        var allowed = world.Content.Enchantments.Select(e => e.Value).ToHashSet();
        var rolled = Enumerable.Range(0, 2000)
            .Select(_ => world.CreateItem(Def(world, "short_sword")).Enchantment)
            .ToList();

        Assert.All(rolled, v => Assert.Contains(v, allowed));
        Assert.Contains(0, rolled);
        Assert.Contains(rolled, v => v > 0);
        Assert.Contains(rolled, v => v < 0);
        Assert.All(Enumerable.Range(0, 50), _ =>
            Assert.Equal(0, world.CreateItem(Def(world, "healing_potion")).Enchantment));
    }
}
