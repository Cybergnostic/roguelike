using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Map;
using Roguelike.Core.Progression;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

/// <summary>The Warrior, Ranger and Shadow skills (COMBAT_AND_MAGIC.md §3).</summary>
public class SkillTests
{
    private static World Started(string classId, ulong seed = 1)
    {
        var world = World.Create(seed, classId: classId);
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        world.Player.Hp = 100_000;
        world.GrantXp(XpSource.Kill, "test", 5000);
        return world;
    }

    private static void Learn(World world, string skill, int rank)
    {
        while (world.Player.Character!.Rank(skill) < rank)
            Assert.Contains(world.Step(new LearnSkillAction(skill)), e => e is SkillLearned);
    }

    private static Actor Foe(World world, Point at, bool fighting = true, string kind = "goblin_warrior")
    {
        var foe = world.SpawnMonster(kind, at);
        foe.Hp = 100_000;
        if (fighting)
        {
            var squad = world.SquadOf(foe)!;
            squad.State = SquadState.Engage;
            squad.TargetId = world.Player.Id;
            squad.LastKnownTarget = world.Player.Position;
            squad.LastSeenTurn = world.Turn;
        }
        return foe;
    }

    /// <summary>A free spot with free cells in a straight line from it, for pushes and shots.</summary>
    private static (Point Spot, Direction Dir) OpenLine(World world, int length) =>
        world.Map.Tiles.Positions().Where(world.IsFree)
            .SelectMany(p => World.EightWay.Select(d => (p, d)))
            .First(x => Enumerable.Range(1, length).All(i => world.IsFree(x.p + new Point(x.d.DeltaX * i, x.d.DeltaY * i))));

    private static Point Along(Point p, Direction d, int n) => p + new Point(d.DeltaX * n, d.DeltaY * n);

    // ---- Warrior ----

    [Fact]
    public void WeaponMasteryRankThreeSwingsHeavyBladesFaster()
    {
        var world = Started("warrior");
        int before = world.Player.MeleeAttackTime;
        Assert.True(world.Player.HeavyWeapon);
        Learn(world, "weapon_mastery", 3);
        Assert.Equal(before * 85 / 100, world.Player.MeleeAttackTime);
    }

    [Fact]
    public void StancesAreLearnedAndRankThreeMakesThemFree()
    {
        var world = Started("warrior");
        Assert.Contains(world.Step(new StanceAction(Stance.Aggressive)), e => e is ActionFailed { Reason: FailureReason.StanceUnknown });
        Learn(world, "combat_stances", 1);
        int turn = world.Turn;
        Assert.Contains(world.Step(new StanceAction(Stance.Aggressive)), e => e is StanceChanged);
        Assert.Equal(turn + 1, world.Turn);
        Learn(world, "combat_stances", 3);
        turn = world.Turn;
        world.Step(new StanceAction(Stance.Guarded));
        Assert.Equal(Stance.Guarded, world.Player.Stance);
        Assert.Equal(turn, world.Turn);
    }

    [Fact]
    public void HoldingTheLineYouCantBeStaggered()
    {
        var world = Started("warrior");
        Learn(world, "hold_the_line", 1);
        world.Step(new WaitAction());
        Assert.True(world.Player.StoodStill);
        var foe = Foe(world, World.EightWay.Select(d => world.Player.Position + d).First(world.IsFree));
        world.StaggerIfAlive(world.Player, foe);
        Assert.False(world.IsStaggered(world.Player));
    }

    [Fact]
    public void SlippingPastAWarriorHoldingTheLineCostsABlow()
    {
        var world = Started("warrior");
        Learn(world, "hold_the_line", 2);
        var player = world.Player;
        var around = World.EightWay.Select(d => player.Position + d).Where(world.IsFree).ToList();
        var (from, to) = around.SelectMany(a => around.Select(b => (a, b))).First(x => x.a != x.b && world.IsAdjacent(x.a, x.b));
        var foe = Foe(world, from);
        foe.Position = to;
        foe.StaggeredUntil = world.Turn; // so the blow can't miss
        world.AfterStep(foe, from);
        Assert.True(foe.Hp < 100_000);
        // A second slip in the same turn is free.
        int hp = foe.Hp;
        foe.Position = from;
        world.AfterStep(foe, to);
        Assert.Equal(hp, foe.Hp);
    }

    [Fact]
    public void UnyieldingIgnoresBeingSurrounded()
    {
        var world = Started("warrior");
        var player = world.Player;
        var squad = world.SquadOf(Foe(world, World.EightWay.Select(d => player.Position + d).First(world.IsFree)));
        for (int i = 0; i < 2; i++)
            world.SpawnMonster("goblin_warrior", World.EightWay.Select(d => player.Position + d).First(world.IsFree), squad);
        Assert.Equal(2, world.Overwhelmed(player));
        Learn(world, "hold_the_line", 3);
        Assert.Equal(0, world.Overwhelmed(player));
    }

    [Fact]
    public void BreakthroughRankTwoShovesTwoCellsAndAlwaysStaggers()
    {
        var world = Started("warrior");
        Learn(world, "breakthrough", 2);
        var (spot, dir) = OpenLine(world, 4);
        world.Player.Position = spot;
        var foe = Foe(world, Along(spot, dir, 1));
        var events = world.Step(new UseAbilityAction("shove", foe.Id));
        Assert.Contains(events, e => e is ActorMoved m && m.ActorId == foe.Id && m.To == Along(spot, dir, 3));
        Assert.Contains(events, e => e is ActorStaggered s && s.ActorId == foe.Id);
    }

    [Fact]
    public void AShoveSpoilsAWindUp()
    {
        var world = Started("warrior");
        var (spot, dir) = OpenLine(world, 3);
        world.Player.Position = spot;
        var knight = Foe(world, Along(spot, dir, 1), kind: "bone_knight");
        world.WindUpBlow(knight, world.Content.Abilities["heavy_blow"], world.Player);
        var events = world.Step(new UseAbilityAction("shove", knight.Id));
        Assert.Contains(events, e => e is WindUpSpoiled { AbilityId: "heavy_blow" } w && w.ActorId == knight.Id);
        Assert.DoesNotContain(events, e => e is AbilityUsed { AbilityId: "heavy_blow" });
    }

    [Fact]
    public void AKillMakesTheNextActionQuick()
    {
        var world = Started("warrior");
        Learn(world, "battle_momentum", 1);
        var foe = Foe(world, World.EightWay.Select(d => world.Player.Position + d).First(world.IsFree));
        foe.Hp = 1;
        for (int i = 0; i < 20 && !foe.IsDead; i++)
            world.Step(new AttackAction(foe.Id));
        Assert.True(foe.IsDead);
        int turn = world.Turn;
        world.Step(new WaitAction()); // half a turn: the world doesn't move yet
        Assert.Equal(turn, world.Turn);
    }

    // ---- Ranger ----

    [Fact]
    public void CarefulAimIsSlowButLandsHarder()
    {
        var world = Started("ranger");
        Learn(world, "shooting_techniques", 2);
        var (spot, dir) = OpenLine(world, 4);
        world.Player.Position = spot;
        var foe = Foe(world, Along(spot, dir, 4));
        int turn = world.Turn;
        var events = world.Step(new UseAbilityAction("careful_aim", foe.Id));
        Assert.Contains(events, e => e is AbilityUsed { AbilityId: "careful_aim" });
        Assert.Equal(turn + 1, world.Turn); // 150: one turn now, half a turn owed
    }

    [Fact]
    public void SuppressiveFireShootsWhoeverStepsIn()
    {
        var world = Started("ranger");
        Learn(world, "shooting_techniques", 3);
        var (spot, dir) = OpenLine(world, 5);
        world.Player.Position = spot;
        var foe = Foe(world, Along(spot, dir, 4));
        world.Step(new UseAbilityAction("suppressive_fire", foe.Id));
        var runner = Foe(world, Along(spot, dir, 5));
        int hp = runner.Hp;
        var from = runner.Position;
        runner.Position = Along(spot, dir, 3);
        runner.StaggeredUntil = world.Turn; // so the shot can't miss
        world.AfterStep(runner, from);
        Assert.True(runner.Hp < hp, "covered ground: the runner was shot");
    }

    [Fact]
    public void DisengageStepsBackAndShoots()
    {
        var world = Started("ranger");
        Learn(world, "skirmishing", 1);
        var (spot, dir) = OpenLine(world, 3);
        world.Player.Position = Along(spot, dir, 1);
        var foe = Foe(world, spot);
        var events = world.Step(new UseAbilityAction("disengage", foe.Id));
        Assert.True(events.Any(e => e is ActorMoved m && m.ActorId == world.Player.Id), string.Join("; ", events));
        Assert.Contains(events, e => e is ActorAttacked { Ranged: true } a && a.AttackerId == world.Player.Id);
    }

    [Fact]
    public void APinnedFoeIsSlowedAndAtRankTwoHeld()
    {
        var world = Started("ranger");
        Learn(world, "pinning_shot", 2);
        var (spot, dir) = OpenLine(world, 4);
        world.Player.Position = spot;
        var foe = Foe(world, Along(spot, dir, 3));
        foe.StaggeredUntil = world.Turn + 1; // so the arrow can't miss
        world.Step(new UseAbilityAction("pinning_shot", foe.Id));
        Assert.True(foe.Has(StatusKind.Slowed));
        Assert.True(foe.Has(StatusKind.Held) || world.Turn > foe.StatusOf(StatusKind.Slowed)!.Until - 2);
    }

    [Fact]
    public void MarkingPreyTakesNoTime()
    {
        var world = Started("ranger");
        Learn(world, "hunters_instinct", 1);
        var (spot, dir) = OpenLine(world, 4);
        world.Player.Position = spot;
        var foe = Foe(world, Along(spot, dir, 4));
        int turn = world.Turn;
        world.Step(new UseAbilityAction("mark_prey", foe.Id));
        Assert.Equal(turn, world.Turn);
        int mark = world.Content.Abilities["mark_prey"].Amount;
        Assert.Equal(mark, foe.StatusOf(StatusKind.Marked)!.Amount);
        Assert.Equal(100 + mark, world.AdvantagePercent(world.Player, foe, ranged: true));
    }

    // ---- Shadow ----

    [Fact]
    public void TumbleSlipsPastToTheFarSide()
    {
        var world = Started("shadow");
        Learn(world, "evasion", 2);
        var (spot, dir) = OpenLine(world, 3);
        world.Player.Position = spot;
        var foe = Foe(world, Along(spot, dir, 1));
        world.Step(new UseAbilityAction("tumble", foe.Id));
        Assert.Equal(Along(spot, dir, 2), world.Player.Position);
    }

    [Fact]
    public void AThrownStoneDrawsCalmSquadsToLook()
    {
        var world = Started("shadow");
        Learn(world, "misdirection", 1);
        var (spot, dir) = OpenLine(world, 7);
        world.Player.Position = spot;
        var foe = Foe(world, Along(spot, dir, 3), fighting: false);
        var squad = world.SquadOf(foe)!;
        var events = world.Step(new UseAbilityAction("throw_stone", foe.Id));
        var thrown = Assert.Single(events.OfType<StoneThrown>());
        Assert.True(World.Distance(thrown.At, spot) > World.Distance(foe.Position, spot) - 1);
        Assert.True(squad.State is SquadState.Alert or SquadState.Engage);
    }

    [Fact]
    public void SmokeBlocksSightThenClears()
    {
        var world = Started("shadow");
        Learn(world, "misdirection", 2);
        var player = world.Player;
        world.Step(new UseAbilityAction("smoke"));
        var smoky = World.EightWay.Select(d => player.Position + d).Where(p => world.Map.Tiles[p] == Tile.Smoke).ToList();
        Assert.NotEmpty(smoky);
        Assert.All(smoky, p => Assert.False(world.Map.Transparent[p]));
        for (int i = 0; i < 12; i++)
            world.Step(new WaitAction());
        Assert.All(smoky, p => Assert.NotEqual(Tile.Smoke, world.Map.Tiles[p]));
    }

    [Fact]
    public void ACutthroatCatchesTheIsolatedOffGuard()
    {
        var world = Started("shadow");
        var foe = Foe(world, World.EightWay.Select(d => world.Player.Position + d).First(world.IsFree));
        Assert.False(world.CaughtOffGuard(world.Player, foe));
        Learn(world, "opportunist", 2);
        Assert.True(world.CaughtOffGuard(world.Player, foe)); // nobody of its own within 2
        Assert.Equal(300, world.AdvantagePercent(world.Player, foe, ranged: false));
    }

    [Fact]
    public void KeepingStillInTheShadowsYouCantBeSeenFromAfar()
    {
        var world = Started("shadow");
        var player = world.Player;
        var (spot, dir) = OpenLine(world, 4);
        player.Position = spot;
        var foe = Foe(world, Along(spot, dir, 4), fighting: false);
        world.Step(new ToggleSneakAction());
        world.Step(new WaitAction());
        Learn(world, "shadowcraft", 2);
        world.Step(new WaitAction());
        Assert.True(player.Sneaking && player.StoodStill);
        Assert.False(world.CanSee(foe, player));
    }
}
