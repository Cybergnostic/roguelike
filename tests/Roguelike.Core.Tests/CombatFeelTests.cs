using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Items;
using Roguelike.Core.Map;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class CombatFeelTests
{
    private static World Valley() { var w = HistoryTests.ValleyHeldBy("tollkeepers"); w.Player.Hp = 100_000; return w; }

    private static Actor One(World w, string kind) => w.Actors.First(a => a.Kind == kind);

    private static Item Give(World w, string id)
    {
        var item = w.CreateItem(w.Content.Items[id], enchantment: 0);
        return w.Player.Inventory!.Add(item);
    }

    private static List<GameEvent> Run(World world, int turns)
    {
        var events = new List<GameEvent>();
        for (int t = 0; t < turns; t++)
            events.AddRange(world.Step(new WaitAction()));
        return events;
    }

    // ---- Leashes ----

    private static (World World, Squad Squad, Actor Bandit) BanditAtTheEdgeOfItsLeash()
    {
        var world = Valley();
        foreach (var other in world.Actors.Where(a => a != world.Player && a.Faction.Id == "folk"))
            other.Hp = 0;
        var bandit = One(world, "bandit");
        var squad = world.SquadOf(bandit)!;
        foreach (var m in squad.Members.Where(m => m != bandit))
            m.Hp = 0;
        bandit.Position = new Point(123, 24);
        world.Player.Position = new Point(123, 19);
        Assert.True(squad.WithinLeash(bandit.Position));
        Assert.False(squad.WithinLeash(world.Player.Position));
        squad.State = SquadState.Engage;
        squad.TargetId = world.Player.Id;
        squad.LastKnownTarget = world.Player.Position;
        squad.LastSeenTurn = world.Turn;
        return (world, squad, bandit);
    }

    [Fact]
    public void AGuardCantReachYouGoesHomeInsteadOfStandingThere()
    {
        var (world, squad, bandit) = BanditAtTheEdgeOfItsLeash();
        var start = bandit.Position;

        var events = Run(world, 12);

        Assert.Contains(events, e => e is SquadStoodDown { Reason: StandDownReason.TooFarFromHome } s && s.SquadId == squad.Id);
        Assert.True(squad.DistanceFromHome(bandit.Position) < squad.DistanceFromHome(start), "it should be walking home");
        Assert.DoesNotContain(events, e => e is ActorAttacked a && a.TargetId == world.Player.Id);
    }

    [Fact]
    public void ShootingAGuardFromPastItsLeashBringsItAfterYou()
    {
        var (world, squad, bandit) = BanditAtTheEdgeOfItsLeash();
        bandit.Hp = 10_000;
        int before = World.Distance(bandit.Position, world.Player.Position);

        world.Step(new FireAction(bandit.Id));
        Run(world, 3);

        Assert.True(World.Distance(bandit.Position, world.Player.Position) < before, "it should come for the shooter");
    }

    [Fact]
    public void AnAngryChaseStillEnds()
    {
        var (world, squad, bandit) = BanditAtTheEdgeOfItsLeash();
        bandit.Hp = 10_000;
        world.Step(new FireAction(bandit.Id));
        world.Player.Position = new Point(3, 20);

        var events = Run(world, 60);

        Assert.Contains(events, e => e is SquadStoodDown s && s.SquadId == squad.Id);
        Assert.True(squad.WithinLeash(bandit.Position));
    }

    // ---- The hamlet ----

    [Fact]
    public void TheWatchFightsBanditsInTheHamlet()
    {
        var world = Valley();
        world.Player.Position = new Point(3, 20);
        var watchman = One(world, "watchman");
        var bandit = world.SpawnMonster("bandit", world.FreeCellNear(watchman.Position, 3)!.Value);
        bandit.Hp = 10_000;

        var events = Run(world, 5);

        Assert.Contains(events, e => e is ActorAttacked a && a.AttackerId == watchman.Id && a.TargetId == bandit.Id);
    }

    [Fact]
    public void VillagersRunRatherThanFight()
    {
        var world = Valley();
        world.Player.Position = new Point(3, 20);
        foreach (var other in world.Actors.Where(a => a.Kind is "watchman" or "hunter"))
            other.Hp = 0;
        var villager = One(world, "villager");
        var bandit = world.SpawnMonster("bandit", world.FreeCellNear(villager.Position, 3)!.Value);
        bandit.Hp = 10_000;
        villager.Hp = 10_000;

        var events = Run(world, 5);

        Assert.DoesNotContain(events, e => e is ActorAttacked a && a.AttackerId == villager.Id);
    }

    [Fact]
    public void TheWatchTalksAndLeavesYouAlone()
    {
        var world = Valley();
        var watchman = One(world, "watchman");
        Assert.False(world.CouldFight(world.Player, watchman));
        Assert.NotNull(world.NpcOf(watchman));
    }

    // ---- Weapons ----

    [Fact]
    public void TheWeaponsDiffer()
    {
        var items = World.Create(1).Content.Items;
        Assert.True(items["dagger"].AttackTime < items["short_sword"].AttackTime);
        Assert.True(items["short_sword"].AttackTime < items["long_sword"].AttackTime);
        Assert.True(items["dagger"].DualWield);
        Assert.False(items["long_sword"].DualWield);
        Assert.True(items["short_sword"].Accuracy > 0);
        Assert.True(items["short_bow"].AttackTime < items["long_bow"].AttackTime);
    }

    private static (World World, Actor Target) WithTargetNextToYou()
    {
        var world = World.Create(1);
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        world.Player.Hp = 100_000;
        var target = world.SpawnMonster("goblin_warrior", World.EightWay.Select(d => world.Player.Position + d).First(world.IsFree));
        target.Hp = 100_000;
        return (world, target);
    }

    [Fact]
    public void AQuickWeaponFitsMoreBlowsIntoATurn()
    {
        var (world, target) = WithTargetNextToYou();
        var dagger = Give(world, "dagger");
        world.Step(new EquipAction(dagger.Id));
        world.Player.Hp = 100_000; // equipping trims HP to the maximum
        int start = world.Turn, blows = 0;

        while (world.Turn < start + 30 && !world.IsOver)
            blows += world.Step(new AttackAction(target.Id)).OfType<ActorAttacked>().Count(a => a.AttackerId == world.Player.Id);

        Assert.InRange(blows, 39, 41); // 30 turns at 75 a blow
    }

    [Fact]
    public void ASlowWeaponLandsFewerBlowsAndGivesTheEnemyMoreTurns()
    {
        var (world, target) = WithTargetNextToYou();
        var sword = Give(world, "long_sword");
        world.Step(new EquipAction(sword.Id));
        world.Player.Hp = 100_000;
        int start = world.Turn, blows = 0;

        while (world.Turn < start + 26 && !world.IsOver)
            blows += world.Step(new AttackAction(target.Id)).OfType<ActorAttacked>().Count(a => a.AttackerId == world.Player.Id);

        Assert.InRange(blows, 19, 21); // 26 turns at 130 a blow
    }

    [Fact]
    public void BowsTakeDifferentTimes()
    {
        var world = World.Create(1);
        var shortBow = Give(world, "short_bow");
        var longBow = Give(world, "long_bow");
        world.Step(new EquipAction(shortBow.Id));
        Assert.Equal(80, world.Player.RangedAttackTime);
        world.Step(new EquipAction(longBow.Id));
        Assert.Equal(130, world.Player.RangedAttackTime);
    }

    [Fact]
    public void ASecondDaggerGoesInTheOtherHandAndStrikesToo()
    {
        var (world, target) = WithTargetNextToYou();
        var first = Give(world, "dagger");
        var second = Give(world, "dagger");
        world.Step(new EquipAction(first.Id));
        world.Step(new EquipAction(second.Id));
        world.Player.Hp = 100_000;
        Assert.Equal(first, world.Player.Equipment!.Weapon);
        Assert.Equal(second, world.Player.Equipment.OffHand);

        var events = world.Step(new AttackAction(target.Id));

        Assert.Equal(2, events.OfType<ActorAttacked>().Count(a => a.AttackerId == world.Player.Id));
    }

    [Fact]
    public void ALongSwordTakesBothHands()
    {
        var world = World.Create(1);
        var first = Give(world, "dagger");
        var second = Give(world, "dagger");
        var sword = Give(world, "long_sword");
        world.Step(new EquipAction(first.Id));
        world.Step(new EquipAction(second.Id));

        world.Step(new EquipAction(sword.Id));

        Assert.Equal(sword, world.Player.Equipment!.Weapon);
        Assert.Null(world.Player.Equipment.OffHand);
    }

    [Fact]
    public void TakingOffTheMainWeaponMovesTheOffHandAcross()
    {
        var world = World.Create(1);
        var first = Give(world, "dagger");
        var second = Give(world, "dagger");
        world.Step(new EquipAction(first.Id));
        world.Step(new EquipAction(second.Id));

        world.Step(new EquipAction(first.Id));

        Assert.Equal(second, world.Player.Equipment!.Weapon);
        Assert.Null(world.Player.Equipment.OffHand);
    }

    // ---- Sounds ----

    /// <summary>Two enemies fighting on a level, out of the player's sight, this far away.</summary>
    private static (World World, Point Fight)? FightOutOfSight(ulong seed, int minDistance, int maxDistance)
    {
        var world = World.Create(seed);
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        world.Player.Hp = 100_000;
        var fov = world.Map.PlayerFov.BooleanResultView;
        var spots = world.Map.Tiles.Positions().Where(p => world.IsFree(p) && !fov[p]
            && World.Distance(p, world.Player.Position) >= minDistance && World.Distance(p, world.Player.Position) <= maxDistance
            && World.EightWay.Any(d => world.IsFree(p + d) && !fov[p + d])).ToList();
        if (spots.Count == 0)
            return null;
        var at = spots[0];
        var goblin = world.SpawnMonster("goblin_warrior", at);
        var skeleton = world.SpawnMonster("skeleton_warrior", World.EightWay.Select(d => at + d).First(p => world.IsFree(p) && !fov[p]));
        goblin.Hp = skeleton.Hp = 100_000;
        return (world, at);
    }

    private static SoundHeard HearTheFight(World world)
    {
        var events = Run(world, 3);
        return events.OfType<SoundHeard>().First(s => s.Kind == SoundKind.Fight);
    }

    [Fact]
    public void YouCanTellWhichWayAFightIs()
    {
        var (world, at) = FightOutOfSight(3, 10, 18)!.Value;
        var sound = HearTheFight(world);
        Assert.Equal(Direction.GetDirection(world.Player.Position, at), sound.From);
    }

    [Fact]
    public void AnOrdinaryEarCantTellWhoIsFightingFromFarAway()
    {
        var (world, _) = FightOutOfSight(3, 12, 18)!.Value;
        var sound = HearTheFight(world);
        Assert.Null(sound.FactionA);
    }

    [Fact]
    public void GoodPerceptionTellsYouWhoIsFighting()
    {
        var (world, _) = FightOutOfSight(3, 12, 18)!.Value;
        world.GrantXp(XpSource.Kill, "test", 50_000);
        for (int i = 0; i < 8; i++)
            world.Step(new RaiseAttributeAction("wis"));
        Assert.True(world.Player.Bonus(Roguelike.Core.Progression.Stat.Perception) >= 4);

        var sound = HearTheFight(world);

        Assert.Equal(["goblins", "undead"], new[] { sound.FactionA!, sound.FactionB! }.Order());
    }

    [Fact]
    public void AFarOffFightGivesNoDirection()
    {
        World? world = null;
        for (ulong seed = 0; seed < 40 && world is null; seed++)
            if (FightOutOfSight(seed, 22, 30) is { } found)
                world = found.World;
        Assert.NotNull(world);
        Assert.Null(HearTheFight(world!).From);
    }

    [Fact]
    public void ALongFightIsNotOneLongLog()
    {
        var (world, _) = FightOutOfSight(3, 10, 18)!.Value;
        int heard = Run(world, 18).OfType<SoundHeard>().Count(s => s.Kind == SoundKind.Fight);
        Assert.InRange(heard, 2, 4);
    }

    [Fact]
    public void QuickWeaponsCantBreakDeterminism()
    {
        static List<string> Play()
        {
            var (world, target) = WithTargetNextToYou();
            var dagger = Give(world, "dagger");
            world.Step(new EquipAction(dagger.Id));
            world.Player.Hp = 100_000;
            var log = new List<string>();
            for (int i = 0; i < 40; i++)
                log.AddRange(world.Step(new AttackAction(target.Id)).Select(e => e.ToString()!));
            return log;
        }

        Assert.Equal(Play(), Play());
    }
}
