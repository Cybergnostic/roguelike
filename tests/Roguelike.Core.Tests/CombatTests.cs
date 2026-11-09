using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Items;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class CombatTests
{
    private static (World world, Actor goblin) Duel(ulong seed = 1)
    {
        var world = World.Create(seed);
        world.ObserverMode = false;
        var at = World.EightWay.Select(d => world.Player.Position + d).First(world.IsFree);
        var goblin = world.SpawnMonster("goblin_warrior", at);
        goblin.Brain = null; // a punching bag
        return (world, goblin);
    }

    [Fact]
    public void HeavyArmourSoftensBlowsButNeverMakesYouUntouchable()
    {
        var (world, goblin) = Duel();
        var armour = world.CreateItem(world.Content.Items["chain_mail"], enchantment: 20);
        world.Player.Inventory!.Add(armour);
        world.Player.Equipment!.Equip(armour);
        Assert.True(world.Player.Defense > goblin.Attack * 3);

        int hits = 0;
        for (int i = 0; i < 300; i++)
        {
            world.Player.Hp = world.Player.MaxHp;
            world.ResolveAttack(goblin, world.Player, ranged: false);
            if (world.Player.Hp < world.Player.MaxHp)
                hits++;
        }
        Assert.InRange(hits, 150, 290); // ~75% hit, every hit does at least 1
    }

    [Fact]
    public void AccuracyAndEvasionSetTheHitChance()
    {
        var (world, goblin) = Duel();
        var rules = world.Content.Combat;
        int baseline = world.HitChance(goblin, world.Player, ranged: false);
        Assert.Equal(rules.BaseHitChance + rules.HitChancePerPoint * (goblin.Accuracy - world.Player.Evasion), baseline);

        var zombie = world.SpawnMonster("goblin_zombie", world.Map.Tiles.Positions().First(world.IsFree));
        Assert.True(world.HitChance(world.Player, zombie, ranged: false) > world.HitChance(world.Player, goblin, ranged: false),
            "shambling zombies should be easier to hit");
    }

    [Fact]
    public void ShootingPointBlankIsHarder()
    {
        var (world, goblin) = Duel();
        int adjacent = world.HitChance(world.Player, goblin, ranged: true);
        goblin.Position = world.Map.Tiles.Positions()
            .First(p => world.IsFree(p) && World.Distance(p, world.Player.Position) == 3);
        int apart = world.HitChance(world.Player, goblin, ranged: true);
        Assert.Equal(world.Content.Combat.HitChancePerPoint * world.Content.Combat.PointBlankPenalty, apart - adjacent);
    }

    [Fact]
    public void MissesAreReportedAsMisses()
    {
        var (world, goblin) = Duel();
        world.Player.Hp = 100_000;
        var attacks = new List<ActorAttacked>();
        for (int i = 0; i < 100; i++)
            attacks.AddRange(world.Step(new AttackAction(goblin.Id)).OfType<ActorAttacked>());
        Assert.Contains(attacks, a => a.Missed && a.Damage == 0);
        Assert.Contains(attacks, a => !a.Missed && a.Damage >= 1);
    }

    [Fact]
    public void GoodGearWaitsForDeeperLevels()
    {
        static IEnumerable<Item> Loot(World w, string table, int n) =>
            Enumerable.Range(0, n).Select(_ => w.RollLoot(table)).OfType<Item>().ToList();

        var shallow = World.Create(1, depth: 1);
        var deep = World.Create(1, depth: 4);
        var top = Loot(shallow, "floor", 2000).ToList();

        Assert.DoesNotContain(top, i => i.Def.MinDepth > 1);
        Assert.DoesNotContain(top, i => i.Enchantment >= 2);
        Assert.Contains(Loot(deep, "floor", 2000), i => i.Def.Id == "chain_mail");
        Assert.Contains(Loot(deep, "floor", 2000), i => i.Enchantment == 3);
    }

    [Fact]
    public void BossesDropFromDeeper()
    {
        var world = World.Create(1);
        var necromancer = world.Actors.Single(a => a.Kind == "acolyte");
        var drops = new List<Item>();
        for (int i = 0; i < 40; i++)
        {
            var n = world.SpawnMonster("acolyte", world.Map.Tiles.Positions().First(world.IsFree));
            world.Damage(world.Player, n, 1000, ranged: false);
            drops.AddRange(world.FloorItems.Where(f => f.Position == n.Position).Select(f => f.Item));
            foreach (var f in world.FloorItems.Where(f => f.Position == n.Position).ToList())
                world.TakeFromFloor(f.Item);
        }
        Assert.Contains(drops, i => i.Def.MinDepth > 1);
    }

    [Fact]
    public void ShootersDontWalkIntoMeleeReach()
    {
        // Over many fights, an archer or necromancer that moves never ends its move next to an enemy.
        for (ulong seed = 0; seed < 8; seed++)
        {
            var world = World.Create(seed);
            world.ObserverMode = true;
            for (int t = 0; t < 400; t++)
            {
                var shooters = world.Actors.Where(a => a.Range > 0 && a != world.Player)
                    .ToDictionary(a => a.Id, a => a.Position);
                var before = world.Actors.ToDictionary(a => a.Id, a => a.Position);
                // Only enemies that stood still this turn: one that walked up afterwards isn't the shooter's doing.
                bool StillEnemyAt(Actor a, Point p) => world.ActorAt(p) is { } o && world.AreHostile(a, o)
                                                       && before.TryGetValue(o.Id, out var was) && was == o.Position;
                foreach (var e in world.Step(new WaitAction()).OfType<ActorMoved>())
                {
                    if (!shooters.TryGetValue(e.ActorId, out var from) || world.GetActor(e.ActorId) is not { } a
                        || world.SquadOf(a)?.State != Roguelike.Core.AI.Squads.SquadState.Engage)
                        continue;
                    bool wasNear = World.EightWay.Any(d => StillEnemyAt(a, from + d));
                    bool isNear = World.EightWay.Any(d => StillEnemyAt(a, e.To + d));
                    Assert.False(!wasNear && isNear && e.From == from,
                        $"seed {seed} turn {t}: {a.Kind} stepped from {from} into melee reach at {e.To}");
                }
            }
        }
    }
}
