using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

/// <summary>Passing conditions, announced blows and stances (COMBAT_AND_MAGIC.md §5 step 2).</summary>
public class StatusTests
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

    private static Point FreeNextTo(World world, Point p) => World.EightWay.Select(d => p + d).First(world.IsFree);

    private static Actor Spawn(World world, string kind, Point at, bool fighting = true)
    {
        var monster = world.SpawnMonster(kind, at);
        if (fighting)
        {
            var squad = world.SquadOf(monster)!;
            squad.State = SquadState.Engage;
            squad.TargetId = world.Player.Id;
            squad.LastKnownTarget = world.Player.Position;
            squad.LastSeenTurn = world.Turn;
        }
        return monster;
    }

    // ---- Statuses ----

    [Fact]
    public void AStatusWearsOffWhenItsTimeIsUp()
    {
        var world = Empty();
        var goblin = Spawn(world, "goblin_warrior", FreeNextTo(world, world.Player.Position));
        world.AddStatus(goblin, StatusKind.Hexed, turns: 2, amount: 25, world.Player);
        Assert.True(goblin.Has(StatusKind.Hexed));
        var ended = new List<GameEvent>();
        for (int i = 0; i < 4 && goblin.Has(StatusKind.Hexed); i++)
            ended.AddRange(world.Step(new WaitAction()));
        Assert.False(goblin.Has(StatusKind.Hexed));
        Assert.Contains(ended, e => e is StatusEnded { Kind: StatusKind.Hexed } x && x.ActorId == goblin.Id);
    }

    [Fact]
    public void HexedFoesTakeMoreAndDealLess()
    {
        var world = Empty();
        var goblin = Spawn(world, "goblin_warrior", FreeNextTo(world, world.Player.Position));
        int before = world.AdvantagePercent(world.Player, goblin, ranged: false);
        int theirs = world.AdvantagePercent(goblin, world.Player, ranged: false);
        world.AddStatus(goblin, StatusKind.Hexed, 5, 25, world.Player);
        Assert.Equal(before * 125 / 100, world.AdvantagePercent(world.Player, goblin, ranged: false));
        Assert.Equal(theirs * 75 / 100, world.AdvantagePercent(goblin, world.Player, ranged: false));
    }

    [Fact]
    public void AMarkOnlyHelpsWhoeverMadeIt()
    {
        var world = Empty();
        var goblin = Spawn(world, "goblin_warrior", FreeNextTo(world, world.Player.Position));
        var other = Spawn(world, "goblin_warrior", FreeNextTo(world, goblin.Position));
        world.AddStatus(goblin, StatusKind.Marked, 10, 30, world.Player);
        Assert.Equal(130, world.AdvantagePercent(world.Player, goblin, ranged: true));
        Assert.Equal(100, world.AdvantagePercent(other, goblin, ranged: true) / (world.IsUnaware(goblin) ? 2 : 1));
    }

    [Fact]
    public void AShieldSoaksBlowsUntilItBreaks()
    {
        var world = Empty();
        var player = world.Player;
        var goblin = Spawn(world, "goblin_warrior", FreeNextTo(world, player.Position));
        world.AddStatus(player, StatusKind.Shielded, 10, 5);
        int hp = player.Hp;
        world.Damage(goblin, player, 3, ranged: false);
        Assert.Equal(hp, player.Hp);
        Assert.Equal(2, player.StatusOf(StatusKind.Shielded)!.Amount);
        world.Damage(goblin, player, 3, ranged: false);
        Assert.Equal(hp - 1, player.Hp);
        Assert.False(player.Has(StatusKind.Shielded));
    }

    [Fact]
    public void TheHeldCantStepButCanStillStrike()
    {
        var world = Empty();
        var player = world.Player;
        world.AddStatus(player, StatusKind.Held, 3, 0);
        var free = World.EightWay.First(d => world.IsFree(player.Position + d));
        var start = player.Position;
        var events = world.Step(new MoveAction(free));
        Assert.Equal(start, player.Position);
        Assert.Contains(events, e => e is ActionFailed { Reason: FailureReason.Held });

        var goblin = Spawn(world, "goblin_warrior", player.Position + free);
        goblin.Hp = 10_000;
        events = world.Step(new MoveAction(free));
        Assert.Contains(events, e => e is ActorAttacked a && a.AttackerId == player.Id);
    }

    [Fact]
    public void SlowedMonstersActLessOften()
    {
        int Actions(bool slowed)
        {
            var world = Empty();
            var goblin = Spawn(world, "goblin_warrior", FreeNextTo(world, world.Player.Position));
            goblin.Hp = 10_000;
            int attacks = 0;
            for (int i = 0; i < 20; i++)
            {
                if (slowed)
                    world.AddStatus(goblin, StatusKind.Slowed, 5, 50);
                attacks += world.Step(new WaitAction()).Count(e => e is ActorAttacked a && a.AttackerId == goblin.Id);
            }
            return attacks;
        }
        int normal = Actions(slowed: false), slow = Actions(slowed: true);
        Assert.InRange(slow, normal / 2 - 2, normal / 2 + 2);
    }

    // ---- Announced blows ----

    private static (World, Actor) KnightBesideThePlayer()
    {
        var world = Empty();
        var knight = Spawn(world, "bone_knight", FreeNextTo(world, world.Player.Position));
        return (world, knight);
    }

    [Fact]
    public void AHeavyBlowIsAnnouncedBeforeItLands()
    {
        var (world, knight) = KnightBesideThePlayer();
        var heavy = world.Content.Abilities["heavy_blow"];
        world.WindUpBlow(knight, heavy, world.Player);
        knight.Energy = 0;
        var events = world.Step(new WaitAction());
        Assert.Contains(events, e => e is AbilityUsed u && u.ActorId == knight.Id && u.AbilityId == "heavy_blow");
        Assert.Null(knight.WindUp);
    }

    [Fact]
    public void SteppingOutOfReachSpoilsIt()
    {
        var (world, knight) = KnightBesideThePlayer();
        var player = world.Player;
        world.WindUpBlow(knight, world.Content.Abilities["heavy_blow"], player);
        // Step to a free cell not next to the knight.
        var away = World.EightWay.First(d => world.IsFree(player.Position + d) && !world.IsAdjacent(player.Position + d, knight.Position));
        var events = world.Step(new MoveAction(away));
        Assert.Contains(events, e => e is WindUpSpoiled s && s.ActorId == knight.Id);
        Assert.DoesNotContain(events, e => e is AbilityUsed u && u.ActorId == knight.Id);
    }

    [Fact]
    public void StaggeringItSpoilsIt()
    {
        var (world, knight) = KnightBesideThePlayer();
        world.WindUpBlow(knight, world.Content.Abilities["heavy_blow"], world.Player);
        var events = new List<GameEvent>();
        world.StaggerIfAlive(knight, world.Player);
        Assert.Null(knight.WindUp);
    }

    [Fact]
    public void ElitesWindUpTheirBigBlowsInAFight()
    {
        int announced = 0, struck = 0;
        for (ulong seed = 0; seed < 10; seed++)
        {
            var (world, knight) = KnightBesideThePlayer();
            for (int i = 0; i < 20; i++)
                foreach (var e in world.Step(new WaitAction()))
                {
                    if (e is WindingUp w && w.ActorId == knight.Id) announced++;
                    if (e is AbilityUsed { AbilityId: "heavy_blow" } u && u.ActorId == knight.Id) struck++;
                }
        }
        Assert.True(announced > 0);
        Assert.True(struck > 0 && struck <= announced);
    }

    // ---- Stances ----

    [Fact]
    public void OnlyThoseTaughtCanTakeAStance()
    {
        var world = Empty();
        var events = world.Step(new StanceAction(Stance.Guarded));
        Assert.Contains(events, e => e is ActionFailed { Reason: FailureReason.StanceUnknown });
        Assert.Equal(Stance.None, world.Player.Stance);
    }

    [Fact]
    public void GuardedTradesBlowsForArmourAndAggressiveTheOtherWay()
    {
        var world = Empty();
        var player = world.Player;
        var goblin = Spawn(world, "goblin_warrior", FreeNextTo(world, player.Position));
        int armour = player.Defense;
        player.Stance = Stance.Guarded;
        Assert.Equal(armour + Actor.GuardedArmour, player.Defense);
        player.Stance = Stance.Aggressive;
        Assert.Equal(Math.Max(0, armour - Actor.AggressiveArmourCost), player.Defense);
        Assert.Equal(player.AggressivePercent, world.AdvantagePercent(player, goblin, ranged: false));
    }

    [Fact]
    public void OnGuardTheFirstAttackerEachTurnIsStruckBack()
    {
        var world = Empty();
        var player = world.Player;
        player.Stance = Stance.Guarded;
        var goblin = Spawn(world, "goblin_warrior", FreeNextTo(world, player.Position));
        goblin.Hp = 10_000;
        int counters = 0, attacks = 0;
        for (int i = 0; i < 10; i++)
            foreach (var e in world.Step(new WaitAction()))
            {
                if (e is ActorAttacked a && a.AttackerId == goblin.Id && !a.Missed) attacks++;
                if (e is SpecialUsed { Special: "counter" }) counters++;
            }
        Assert.True(attacks > 0);
        Assert.True(counters >= attacks * 3 / 4, $"{counters} counters for {attacks} blows");
    }
}
