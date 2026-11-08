using Roguelike.Core.Actions;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Items;
using Roguelike.Core.Progression;

namespace Roguelike.Core.Tests;

public class AffixTests
{
    private static ItemDef Def(World world, string id) => world.Content.Items[id];

    private static AffixDef Affix(World world, string id) => world.Content.Affixes.Pool.Single(a => a.Id == id);

    /// <summary>Many randomly generated pieces of equipment found at <paramref name="depth"/>.</summary>
    private static List<Item> Generate(int depth, int count = 3000)
    {
        var world = World.Create(5);
        string[] kinds = ["short_sword", "short_bow", "leather_armor"];
        return Enumerable.Range(0, count).Select(i => world.CreateItem(Def(world, kinds[i % 3]), depth: depth)).ToList();
    }

    private static Item Wear(World world, Item item)
    {
        world.Player.Inventory!.Add(item);
        world.Player.Equipment!.Equip(item);
        return item;
    }

    [Fact]
    public void TiersCarryTheirAffixesSpecialsAndNames()
    {
        var items = Generate(depth: 10);
        var content = World.Create(1).Content;
        foreach (var item in items)
        {
            var distinct = item.Affixes.Select(a => a.Def.Id).Distinct().Count();
            Assert.Equal(item.Affixes.Count, distinct);
            Assert.All(item.Affixes, a => Assert.Contains(item.Def.Category, a.Def.Slots));
            switch (item.Rarity)
            {
                case Rarity.Common:
                    Assert.Empty(item.Affixes);
                    Assert.Null(item.Special);
                    break;
                case Rarity.Magic:
                    Assert.Single(item.Affixes);
                    Assert.Null(item.NameWords);
                    break;
                case Rarity.Rare:
                    Assert.InRange(item.Affixes.Count, 2, 3);
                    Assert.Equal(2, item.NameWords!.Count);
                    Assert.Null(item.Special);
                    break;
                case Rarity.Legendary:
                    Assert.InRange(item.Affixes.Count, 3, 4);
                    Assert.Contains(item.Special, content.Affixes.Specials[item.Def.Category]);
                    Assert.NotNull(item.NameWords);
                    break;
            }
        }
        foreach (var rarity in new[] { Rarity.Common, Rarity.Magic, Rarity.Rare, Rarity.Legendary })
            Assert.Contains(items, i => i.Rarity == rarity);
    }

    [Fact]
    public void TiersFollowTheirChancesAndWaitForDepth()
    {
        // Depth 1: magic about 10%; rare and legendary not yet.
        var shallow = Generate(depth: 1);
        double magic = shallow.Count(i => i.Rarity == Rarity.Magic) / (double)shallow.Count;
        Assert.InRange(magic, 0.08, 0.12);
        Assert.DoesNotContain(shallow, i => i.Rarity > Rarity.Magic);

        // Deeper, the better tiers show up more.
        var deep = Generate(depth: 10);
        Assert.True(deep.Count(i => i.Rarity >= Rarity.Rare) > 0.04 * deep.Count);
    }

    [Fact]
    public void AffixesGrowWithDepth()
    {
        double Average(int depth) => Generate(depth)
            .SelectMany(i => i.Affixes).Where(a => a.Def.Id == "of_the_bear").Average(a => a.Value);
        Assert.True(Average(10) > Average(2) + 3);
    }

    [Fact]
    public void TheStartingKitIsPlain()
    {
        var world = World.Create(1);
        Assert.All(world.Player.Inventory!.Items, i => Assert.Equal(Rarity.Common, i.Rarity));
    }

    [Fact]
    public void WornAffixesAddToStatsAndComeOffWithTheItem()
    {
        var world = World.Create(1);
        var player = world.Player;
        int attack = player.Attack, accuracy = player.Accuracy;
        var sword = Wear(world, new Item(900, Def(world, "short_sword"))
        {
            Rarity = Rarity.Rare,
            Affixes = [new AffixRoll(Affix(world, "vicious"), 2), new AffixRoll(Affix(world, "keen"), 1)],
        });
        int swordGain = sword.AttackBonus - new Item(0, Def(world, "short_sword")).AttackBonus;

        Assert.Equal(attack + swordGain + 2, player.Attack);
        Assert.Equal(accuracy + 1, player.Accuracy);

        world.Step(new EquipAction(sword.Id)); // takes it off
        Assert.Equal(accuracy, player.Accuracy);
    }

    [Fact]
    public void GearHitPointsDontHealAndComeOffSafely()
    {
        var world = World.Create(1);
        var player = world.Player;
        var armour = new Item(900, Def(world, "leather_armor"))
        {
            Rarity = Rarity.Magic,
            Affixes = [new AffixRoll(Affix(world, "of_the_bear"), 6)],
        };
        player.Inventory!.Add(armour);
        int max = player.MaxHp;
        player.Hp = max;

        world.Step(new EquipAction(armour.Id));
        Assert.Equal(max + 6, player.MaxHp);
        Assert.Equal(max, player.Hp); // more room, not more blood

        world.Step(new EquipAction(armour.Id));
        Assert.Equal(max, player.MaxHp);
        Assert.True(player.Hp <= player.MaxHp);
    }

    [Fact]
    public void ThornsHurtWhoeverStrikesTheWearer()
    {
        var world = World.Create(1);
        var at = World.EightWay.Select(d => world.Player.Position + d).First(world.IsFree);
        var goblin = world.SpawnMonster("goblin_warrior", at);
        goblin.Brain = null;
        goblin.Hp = 1000;
        world.Player.Hp = 1000;
        Wear(world, new Item(900, Def(world, "leather_armor")) { Rarity = Rarity.Legendary, Special = "thorns" });

        for (int i = 0; i < 20; i++)
            world.ResolveAttack(goblin, world.Player, ranged: false);

        Assert.True(goblin.Hp < 1000, "the attacker never felt the thorns");
    }

    [Fact]
    public void LifestealHealsTheStriker()
    {
        var world = World.Create(1);
        var at = World.EightWay.Select(d => world.Player.Position + d).First(world.IsFree);
        var goblin = world.SpawnMonster("goblin_warrior", at);
        goblin.Brain = null;
        goblin.Hp = 1000;
        Wear(world, new Item(900, Def(world, "short_sword")) { Rarity = Rarity.Legendary, Special = "lifesteal" });
        world.Player.Hp = 5;

        for (int i = 0; i < 20; i++)
            world.ResolveAttack(world.Player, goblin, ranged: false);

        Assert.True(world.Player.Hp > 5);
        Assert.True(world.Player.Hp <= world.Player.MaxHp);
    }

    [Fact]
    public void PiercingArrowsMeetLessArmour()
    {
        static double AverageDamage(bool piercing)
        {
            var world = World.Create(1);
            Wear(world, new Item(900, world.Content.Items["long_bow"]) { Enchantment = 5, Special = piercing ? "piercing" : null });
            // A heavily armoured target standing next to the player (not added to the world; it only soaks shots).
            var faction = world.Content.Factions["goblins"];
            var target = new Actor(999, "goblin_warrior", faction, world.Player.Position + SadRogue.Primitives.Direction.Right,
                100_000, attack: 1, defense: 12);
            int dealt = 0, hits = 0;
            for (int i = 0; i < 400; i++)
            {
                int before = target.Hp;
                world.ResolveAttack(world.Player, target, ranged: true);
                if (target.Hp < before)
                {
                    dealt += before - target.Hp;
                    hits++;
                }
            }
            return (double)dealt / hits;
        }
        Assert.True(AverageDamage(piercing: true) > AverageDamage(piercing: false) + 1);
    }

    [Fact]
    public void UnknownAffixStatIsAContentError()
    {
        var e = Assert.Throws<InvalidDataException>(() => WithChangedAffixes(s => s.Replace("\"stat\": \"accuracy\"", "\"stat\": \"luck\"")));
        Assert.Contains("luck", e.Message);
    }

    [Fact]
    public void UnknownSpecialIsAContentError()
    {
        var e = Assert.Throws<InvalidDataException>(() => WithChangedAffixes(s => s.Replace("\"thorns\"]", "\"spikes\"]")));
        Assert.Contains("spikes", e.Message);
    }

    private static void WithChangedAffixes(Func<string, string> change)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rl-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
            var path = Path.Combine(dir, "affixes.json");
            File.WriteAllText(path, change(File.ReadAllText(path)));
            ContentDatabase.Load(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
