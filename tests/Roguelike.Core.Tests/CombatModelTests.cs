using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

/// <summary>The lethal combat model (COMBAT_AND_MAGIC.md §1): steady damage, advantages anyone can see, crowding, fair shots, rest.</summary>
public class CombatModelTests
{
    private static World Empty(ulong seed = 1)
    {
        var world = World.Create(seed);
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        return world;
    }

    private static Point FreeNextTo(World world, Point p) =>
        World.EightWay.Select(d => p + d).First(world.IsFree);

    /// <summary>A goblin next to the player, its squad fighting (or not), very hard to kill so blows can be counted.</summary>
    private static Actor Goblin(World world, bool fighting)
    {
        var goblin = world.SpawnMonster("goblin_warrior", FreeNextTo(world, world.Player.Position));
        var squad = world.SquadOf(goblin)!;
        if (fighting)
        {
            squad.State = SquadState.Engage;
            squad.TargetId = world.Player.Id;
        }
        return goblin;
    }

    private static List<int> Blows(World world, Actor attacker, Actor target, int power, int count = 300)
    {
        var dealt = new List<int>();
        for (int i = 0; i < count; i++)
        {
            target.Hp = 10_000;
            var hits = world.Step(new WaitAction()); // keep turns moving; then strike outside the turn
            int before = target.Hp;
            world.ResolveAttack(attacker, target, ranged: false, power: power);
            if (target.Hp < before)
                dealt.Add(before - target.Hp);
        }
        return dealt;
    }

    [Fact]
    public void ABlowDoesAboutItsStrengthLessArmour()
    {
        var world = Empty();
        world.Player.Hp = 100_000;
        var goblin = Goblin(world, fighting: true);
        int armour = goblin.Defense;
        var dealt = Blows(world, world.Player, goblin, power: 10);
        Assert.NotEmpty(dealt);
        Assert.All(dealt, d => Assert.InRange(d, 8 - armour, 12 - armour));
        Assert.True(dealt.Distinct().Count() > 1, "some spread");
    }

    [Fact]
    public void ArmourNeverStopsMoreThanHalfABlow()
    {
        var world = Empty();
        world.Player.Hp = 100_000;
        var gear = world.Player.Equipment!;
        gear.Unequip(gear.Armor!);
        gear.Equip(world.Player.Inventory!.Add(world.CreateItem(world.Content.Items["plate_armor"], enchantment: 10)));
        Assert.True(world.Player.Defense >= 10);
        var goblin = Goblin(world, fighting: true);
        var dealt = Blows(world, goblin, world.Player, power: 4);
        Assert.NotEmpty(dealt);
        // However thick the armour, a 4-point blow (3 to 5) gets at least half through.
        Assert.All(dealt, d => Assert.True(d >= 2));
    }

    [Fact]
    public void CatchingThemUnawaresDoublesTheBlow()
    {
        var world = Empty();
        var goblin = Goblin(world, fighting: false);
        Assert.True(world.IsUnaware(goblin));
        Assert.Equal(200, world.AdvantagePercent(world.Player, goblin, ranged: false));
        Assert.False(world.IsUnaware(world.Player)); // the player is never caught so
    }

    [Fact]
    public void BackstabMakesItTriple()
    {
        var world = World.Create(1, classId: "shadow");
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        var goblin = Goblin(world, fighting: false);
        Assert.Equal(300, world.AdvantagePercent(world.Player, goblin, ranged: false));
        Assert.Equal(200, world.AdvantagePercent(world.Player, goblin, ranged: true)); // a stab, not a shot
    }

    [Fact]
    public void AStaggeredFoeCantBeMissedAndTakesMore()
    {
        var world = Empty();
        var goblin = Goblin(world, fighting: true);
        Assert.True(world.HitChance(world.Player, goblin, ranged: false) < 100);
        goblin.StaggeredUntil = world.Turn;
        Assert.Equal(100, world.HitChance(world.Player, goblin, ranged: false));
        Assert.Equal(150, world.AdvantagePercent(world.Player, goblin, ranged: false));
    }

    [Fact]
    public void StaggerLastsUntilTheVictimsNextTurnIsOver()
    {
        var world = Empty();
        var goblin = Goblin(world, fighting: true);
        world.StaggerIfAlive(goblin, world.Player);
        Assert.True(world.IsStaggered(goblin));
        world.Step(new WaitAction());
        Assert.True(world.IsStaggered(goblin)); // the player's next blow still finds it reeling
        world.Step(new WaitAction());
        Assert.False(world.IsStaggered(goblin));
    }

    [Fact]
    public void EachExtraFoeAroundYouMakesTheirBlowsSurerAndHarder()
    {
        var world = Empty();
        var player = world.Player;
        var first = Goblin(world, fighting: true);
        int alone = world.HitChance(first, player, ranged: false);
        Assert.Equal(0, world.Overwhelmed(player));
        Assert.Equal(100, world.AdvantagePercent(first, player, ranged: false));

        var squad = world.SquadOf(first);
        world.SpawnMonster("goblin_warrior", FreeNextTo(world, player.Position), squad);
        world.SpawnMonster("goblin_warrior", FreeNextTo(world, player.Position), squad);
        Assert.Equal(2, world.Overwhelmed(player));
        Assert.Equal(alone + 2 * world.Content.Combat.HitChancePerPoint, world.HitChance(first, player, ranged: false));
        Assert.Equal(140, world.AdvantagePercent(first, player, ranged: false));
    }

    [Fact]
    public void NobodyShootsThePlayerFromOutOfTheirSight()
    {
        var world = Empty();
        var player = world.Player;
        var fov = world.Map.PlayerFov.BooleanResultView;
        var spot = world.Map.Tiles.Positions().Cast<Point?>().FirstOrDefault(p => world.IsFree(p!.Value) && !fov[p.Value] && world.HasClearShot(p.Value, player.Position));
        if (spot is not { } dark)
            return; // no such spot on this level
        var archer = world.SpawnMonster("goblin_archer", dark);
        Assert.False(world.FairShot(archer, archer.Position, player));
        Assert.False(new RangedAttackAction(player.Id).Perform(world, archer));

        var lit = world.Map.Tiles.Positions().First(p => world.IsFree(p) && fov[p] && World.Distance(p, player.Position) >= 2 && world.HasClearShot(p, player.Position));
        Assert.True(world.FairShot(archer, lit, player));
    }

    [Fact]
    public void RestingWithNoEnemyInSightGetsYourBreathBackFast()
    {
        var world = Empty();
        var player = world.Player;
        player.Hp = 1;
        for (int i = 0; i < 40; i++)
            world.Step(new WaitAction());
        Assert.True(player.Hp >= player.MaxHp / 2, $"only {player.Hp}/{player.MaxHp} after 40 quiet turns");
    }

    [Fact]
    public void NoRestWhileAnEnemyIsInSight()
    {
        var world = Empty();
        var player = world.Player;
        var fov = world.Map.PlayerFov.BooleanResultView;
        var spot = world.Map.Tiles.Positions().First(p => world.IsFree(p) && fov[p] && World.Distance(p, player.Position) >= 4);
        world.SpawnMonster("goblin_warrior", spot);
        world.Step(new WaitAction());
        Assert.Equal(world.Turn, world.LastThreatTurn);
    }
}
