using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Items;
using Roguelike.Core.Progression;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class BuildTests
{
    private static World Empty(ulong seed = 1)
    {
        var world = World.Create(seed);
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        world.Player.Hp = 100_000;
        return world;
    }

    private static Item Wear(World world, string id)
    {
        var item = world.Player.Inventory!.Add(world.CreateItem(world.Content.Items[id], enchantment: 0));
        world.Player.Equipment!.Equip(item);
        return item;
    }

    private static Point Beside(World w) => World.EightWay.Select(d => w.Player.Position + d).First(w.IsFree);

    private static int TurnsToWalk(World world, int steps)
    {
        int start = world.Turn;
        for (int i = 0; i < steps; i++)
        {
            var dir = World.EightWay.First(d => world.IsFree(world.Player.Position + d));
            world.Step(new MoveAction(dir));
        }
        return world.Turn - start;
    }

    [Fact]
    public void PlateMakesYouSlowerOnYourFeet()
    {
        var plated = Empty();
        Wear(plated, "plate_armor");
        Assert.InRange(TurnsToWalk(plated, 40), 49, 51);
    }

    [Fact]
    public void HeavyArmourDoesntSlowYourAttacks()
    {
        var world = Empty();
        Wear(world, "plate_armor");
        var goblin = world.SpawnMonster("goblin_warrior", Beside(world));
        Assert.Equal(world.Player.MeleeAttackTime, new AttackAction(goblin.Id).TimeCost(world, world.Player));
        Assert.Equal(100, world.Player.MeleeAttackTime);
    }

    [Fact]
    public void FleetFootSpeedsUpEverything()
    {
        var world = Empty();
        world.GrantXp(XpSource.Kill, "test", 5000);
        for (int i = 0; i < 3; i++)
            world.Step(new LearnSkillAction("fleet_foot"));
        Assert.Equal(24, world.Player.Bonus(Stat.Speed));

        int turns = TurnsToWalk(world, 40);

        Assert.InRange(turns, 31, 33); // 40 steps at about 81 each
    }

    private static double AverageDamage(World world, Actor shooter, bool ranged)
    {
        const int Blows = 1500;
        int total = 0, landed = 0;
        for (int i = 0; i < Blows; i++)
        {
            int before = world.Player.Hp;
            world.ResolveAttack(shooter, world.Player, ranged);
            if (world.Player.Hp < before) { total += before - world.Player.Hp; landed++; }
            world.Player.Hp = 100_000;
        }
        return (double)total / Math.Max(1, landed);
    }

    [Fact]
    public void HeavyArmourStopsArrowsBetterThanBlows()
    {
        var world = Empty();
        Wear(world, "plate_armor");
        var archer = world.SpawnMonster("bone_knight", world.Map.Rooms[1].Center);
        world.Player.StoodStill = false;

        double blows = AverageDamage(world, archer, ranged: false);
        double arrows = AverageDamage(world, archer, ranged: true);

        Assert.True(arrows < blows, $"arrows {arrows:0.00}, blows {blows:0.00}");
    }

    [Fact]
    public void StandingStillInHeavyArmourBraces()
    {
        var world = Empty();
        Wear(world, "plate_armor");
        world.Player.StoodStill = false;
        Assert.Equal(0, world.Player.BraceBonus);

        world.Step(new WaitAction());
        Assert.Equal(2, world.Player.BraceBonus);

        var dir = World.EightWay.First(d => world.IsFree(world.Player.Position + d));
        world.Step(new MoveAction(dir));
        Assert.Equal(0, world.Player.BraceBonus);
    }

    [Fact]
    public void LeatherDoesntBrace()
    {
        var world = Empty();
        world.Step(new WaitAction());
        Assert.Equal(0, world.Player.BraceBonus);
    }

    [Fact]
    public void WithYourFeetSetTheFirstAttackerGetsHitBack()
    {
        var world = Empty();
        Wear(world, "plate_armor");
        var first = world.SpawnMonster("goblin_warrior", Beside(world));
        var second = world.SpawnMonster("goblin_warrior", Beside(world));
        first.Hp = second.Hp = 100_000;
        world.Step(new WaitAction());
        world.Player.Hp = 100_000;

        var events = new List<GameEvent>();
        for (int t = 0; t < 40; t++)
        {
            events.AddRange(world.Step(new WaitAction()));
            world.Player.Hp = 100_000;
        }

        Assert.Contains(events, e => e is SpecialUsed { Special: "counter" });
        var counters = events.OfType<SpecialUsed>().Count(s => s.Special == "counter");
        Assert.True(counters < 40, $"{counters} counters in 40 turns: it should be a chance, once a turn at most");
    }

    [Fact]
    public void YouDontCounterWhenYouHaveJustMoved()
    {
        var world = Empty();
        Wear(world, "plate_armor");
        var goblin = world.SpawnMonster("goblin_warrior", Beside(world));
        goblin.Hp = 100_000;
        world.Player.StoodStill = false;
        for (int i = 0; i < 40; i++)
            world.ResolveAttack(goblin, world.Player, ranged: false);
        // Attacks resolved outside a turn; nothing should have hit the goblin back.
        Assert.Equal(100_000, goblin.Hp);
    }

    /// <summary>Four goblins of 8 HP against a player in this armour, and the posture they settle on.</summary>
    private static Posture PostureAgainst(Func<World, Item> armour)
    {
        var world = World.Create(1);
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        var room = world.Map.Rooms.OrderByDescending(r => r.Area).First();
        world.Player.Position = room.Center;
        world.Player.Equipment!.Equip(world.Player.Inventory!.Add(armour(world)));
        world.Player.Hp = world.Player.MaxHp;
        var spots = world.Map.Tiles.Positions().Where(p => world.IsFree(p) && World.Distance(p, room.Center) == 5 && world.HasLineOfSight(p, room.Center)).Take(4).ToList();
        var first = world.SpawnMonster("goblin_warrior", spots[0]);
        var squad = world.SquadOf(first)!;
        foreach (var spot in spots.Skip(1))
            world.SpawnMonster("goblin_warrior", spot, squad);
        foreach (var m in squad.Members)
            m.Hp = 8;
        world.Step(new WaitAction());
        return squad.Posture;
    }

    [Fact]
    public void HeavyGearMakesPacksThinkTwice()
    {
        static Item Plate(World w) => w.CreateItem(w.Content.Items["plate_armor"], enchantment: 0);
        static Item NotFrightening(World w) => w.CreateItem(w.Content.Items["plate_armor"] with { Menace = 0 }, enchantment: 0);

        Assert.Equal(Posture.Bold, PostureAgainst(NotFrightening));
        Assert.NotEqual(Posture.Bold, PostureAgainst(Plate));
    }

    [Fact]
    public void ThePeddlerSellsPlate()
    {
        var world = Empty();
        Assert.Contains(world.Content.Monsters["peddler"].Npc!.Goods!, w => w.Item == "plate_armor");
    }
}

public class HeavyBlowTests
{
    private static World Empty(ulong seed = 1)
    {
        var world = World.Create(seed);
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        world.Player.Hp = 100_000;
        return world;
    }

    private static void Wear(World world, string id)
    {
        var item = world.Player.Inventory!.Add(world.CreateItem(world.Content.Items[id], enchantment: 0));
        world.Player.Equipment!.Equip(item);
    }

    private static Point Beside(World w) => World.EightWay.Select(d => w.Player.Position + d).First(w.IsFree);

    [Fact]
    public void ALongSwordStaggersAndADaggerDoesNot()
    {
        var world = Empty();
        Assert.Equal(0, world.Player.StaggerChance); // short sword
        Wear(world, "long_sword");
        Assert.True(world.Player.StaggerChance >= 20);
        Wear(world, "dagger");
        Assert.Equal(0, world.Player.StaggerChance);
    }

    [Fact]
    public void AStaggeredMonsterLosesItsNextAction()
    {
        var world = Empty();
        Wear(world, "long_sword");
        var goblin = world.SpawnMonster("goblin_warrior", Beside(world));
        goblin.Hp = 1_000_000;
        var staggered = false;
        for (int i = 0; i < 80 && !staggered; i++)
        {
            int energy = goblin.Energy;
            var events = world.Step(new AttackAction(goblin.Id));
            staggered = events.OfType<ActorStaggered>().Any(s => s.ActorId == goblin.Id);
            if (staggered)
                Assert.True(goblin.Energy < energy + goblin.Speed, "it should have lost its action");
            world.Player.Hp = 100_000;
        }
        Assert.True(staggered, "80 blows and no stagger");
    }

    [Fact]
    public void BossesCantBeStaggered()
    {
        var world = World.Create(1, depth: 7);
        var boss = world.Actors.Single(a => a.Rank == Rank.Boss);
        Wear(world, "long_sword");
        world.Player.Hp = 100_000;
        boss.Hp = 1_000_000;
        var events = new List<GameEvent>();
        for (int i = 0; i < 120; i++)
            events.AddRange(world.Step(new AttackAction(boss.Id)));
        Assert.DoesNotContain(events, e => e is ActorStaggered s && s.ActorId == boss.Id);
    }

    [Fact]
    public void ABearCanStaggerYou()
    {
        var world = Empty();
        var bear = world.SpawnMonster("bear", Beside(world));
        Assert.True(bear.StaggerChance > 0);
        var events = new List<GameEvent>();
        for (int i = 0; i < 150 && !events.Any(e => e is ActorStaggered); i++)
        {
            events.AddRange(world.Step(new WaitAction()));
            world.Player.Hp = 100_000;
        }
        Assert.Contains(events, e => e is ActorStaggered s && s.ActorId == world.Player.Id);
    }

    private static (World World, Actor Target) ChargeSetup(int distance)
    {
        var world = Empty();
        var at = world.Map.Tiles.Positions().First(p => world.IsFree(p) && World.Distance(p, world.Player.Position) == distance && world.HasClearShot(world.Player.Position, p));
        var target = world.SpawnMonster("goblin_archer", at);
        target.Hp = 100_000;
        return (world, target);
    }

    [Fact]
    public void YouNeedHeavyArmourToCharge()
    {
        var (world, target) = ChargeSetup(4);
        Assert.Contains(world.Step(new ChargeAction(target.Id)), e => e is ActionFailed { Reason: FailureReason.TooLightToCharge });
    }

    [Fact]
    public void AChargeClosesTheGapAndHits()
    {
        var (world, target) = ChargeSetup(4);
        Wear(world, "plate_armor");
        world.Player.Hp = 100_000;

        var events = world.Step(new ChargeAction(target.Id));

        Assert.Contains(events, e => e is ChargedAt);
        Assert.True(world.IsAdjacent(world.Player.Position, target.Position) || World.Distance(world.Player.Position, target.Position) <= 2);
        Assert.Contains(events, e => e is ActorAttacked a && a.AttackerId == world.Player.Id && !a.Ranged);
        Assert.True(world.ChargeCooldownLeft > 0);
    }

    [Fact]
    public void AChargeHasACooldownAndARange()
    {
        var (world, target) = ChargeSetup(4);
        Wear(world, "plate_armor");
        world.Step(new ChargeAction(target.Id));
        Assert.Contains(world.Step(new ChargeAction(target.Id)), e => e is ActionFailed { Reason: FailureReason.ChargeNotReady });

        var (far, distant) = ChargeSetup(7);
        Wear(far, "plate_armor");
        Assert.Contains(far.Step(new ChargeAction(distant.Id)), e => e is ActionFailed { Reason: FailureReason.OutOfRange });
    }

    [Fact]
    public void TheGuardsWaitForAnAnswerAtTheGate()
    {
        var world = HistoryTests.ValleyHeldBy("tollkeepers");
        world.Player.Hp = 100_000;
        world.Player.Position = new Point(43, 13);
        Assert.Null(world.CampInTalk());
        world.Step(new WaitAction());
        Assert.NotNull(world.CampInTalk());

        var events = world.Step(new ThreatenAction());

        Assert.Contains(events, e => e is CampThreatened);
        Assert.Contains(events, e => e is CampAlarmed);
        Assert.Null(world.CampInTalk());
    }

    [Fact]
    public void ANoAnswerIsNeededOnceYouHavePaid()
    {
        var world = HistoryTests.ValleyHeldBy("tollkeepers");
        world.Player.Hp = 100_000;
        world.Player.Position = new Point(43, 13);
        world.Step(new WaitAction());
        world.Step(new PayTollAction());
        Assert.Null(world.CampInTalk());
    }

    [Fact]
    public void WalkingOnlyUsesGroundYouKnow()
    {
        var world = World.Create(1);
        var unknown = world.Map.Tiles.Positions().First(p => world.IsFree(p) && !world.Map.Explored[p]);
        Assert.Null(world.StepAroundOthers(world.Player, unknown, knownOnly: true));
        Assert.NotNull(world.StepAroundOthers(world.Player, unknown));
    }
}
