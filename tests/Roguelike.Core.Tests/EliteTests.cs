using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class EliteTests
{
    private static int LastLevel => ContentDatabase.Default.Dungeon.Levels;

    private static Point OpenSpotNear(World w, Actor a, int distance) =>
        w.Map.Tiles.Positions().Where(p => w.IsFree(p) && World.Distance(p, a.Position) == distance && w.HasClearShot(a.Position, p)).First();

    /// <summary>The only other actor left is this one and the player, face to face.</summary>
    private static (World World, Actor Monster) Duel(string kind, int distance, ulong seed = 1)
    {
        var world = World.Create(seed, depth: 5);
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Player.Hp = 100_000;
        world.Step(new WaitAction());
        var spot = world.Map.Rooms.Skip(1).OrderByDescending(r => r.Area).First().Center;
        var monster = world.SpawnMonster(kind, spot);
        world.Player.Position = OpenSpotNear(world, monster, distance);
        return (world, monster);
    }

    [Fact]
    public void TheDungeonIsSevenLevelsAndEndsInTheCrypt()
    {
        Assert.Equal(7, LastLevel);
        foreach (ulong seed in new ulong[] { 0, 1, 2, 3, 4 })
        {
            var world = World.Create(seed, depth: LastLevel);
            var boss = Assert.Single(world.Actors, a => a.Rank == Rank.Boss);
            Assert.Equal("the necromancer's crypt", world.Map.ZoneAt(boss.Position)?.Name);
        }
    }

    [Fact]
    public void OnlyTheLastLevelHasTheNecromancerHimself()
    {
        for (int depth = 1; depth < LastLevel; depth++)
        {
            var world = World.Create(1, depth: depth);
            Assert.DoesNotContain(world.Actors, a => a.Kind == "necromancer");
            Assert.Contains(world.Actors, a => a.Kind == "acolyte");
        }
    }

    [Fact]
    public void ElitesTurnUpBelowTheFirstLevelAndAreStronger()
    {
        var firstLevel = Enumerable.Range(0, 30).SelectMany(s => World.Create((ulong)s, depth: 1).Actors).ToList();
        Assert.DoesNotContain(firstLevel, a => a.Title is not null);

        var elite = Enumerable.Range(0, 60).SelectMany(s => World.Create((ulong)s, depth: 4).Actors)
            .First(a => a.Title == "brutal" && a.Kind == "goblin_warrior");
        var plain = Enumerable.Range(0, 60).SelectMany(s => World.Create((ulong)s, depth: 4).Actors)
            .First(a => a.Title is null && a.Kind == "goblin_warrior");

        Assert.Equal(Rank.Elite, elite.Rank);
        Assert.Equal("brutal goblin warrior", elite.Name);
        Assert.True(elite.MaxHp > plain.MaxHp);
        Assert.True(elite.Attack > plain.Attack);
        Assert.Contains(elite.Abilities, a => a.Id == "charge");
    }

    [Fact]
    public void ElitesAlwaysDropSomething()
    {
        for (ulong seed = 0; seed < 12; seed++)
        {
            var world = World.Create(seed, depth: 5);
            world.Player.Hp = 100_000;
            var brute = world.SpawnMonster("goblin_brute", world.Map.Rooms[1].Center);
            int items = world.FloorItems.Count;
            world.Damage(world.Player, brute, 999, ranged: false);
            Assert.Equal(items + 1, world.FloorItems.Count);
        }
    }

    [Fact]
    public void AChargeClosesTheDistanceAndStrikes()
    {
        var (world, brute) = Duel("goblin_brute", 3);

        var events = world.Step(new WaitAction());

        Assert.Contains(events, e => e is AbilityUsed { AbilityId: "charge" } a && a.ActorId == brute.Id);
        Assert.True(world.IsAdjacent(brute.Position, world.Player.Position));
        Assert.Contains(events, e => e is ActorAttacked a && a.AttackerId == brute.Id);
    }

    [Fact]
    public void SkillsWaitForTheirCooldown()
    {
        var (world, brute) = Duel("goblin_brute", 3);
        world.Step(new WaitAction());
        var cooldown = brute.Abilities.Single(a => a.Id == "charge").Cooldown;

        world.Player.Position = OpenSpotNear(world, brute, 3);
        var again = world.Step(new WaitAction());

        Assert.DoesNotContain(again, e => e is AbilityUsed);
        Assert.True(cooldown > 1);
    }

    [Fact]
    public void AVolleyShootsMoreThanOnce()
    {
        var world = World.Create(1, depth: 5);
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Player.Hp = 100_000;
        world.Step(new WaitAction());
        var archer = world.SpawnMonster("goblin_archer", world.Map.Rooms.Skip(1).OrderByDescending(r => r.Area).First().Center);
        world.Player.Position = OpenSpotNear(world, archer, 4);
        var volley = world.Content.Abilities["volley"];

        var used = new AbilityAction(volley, world.Player.Id).Perform(world, archer);

        Assert.True(used);
    }

    [Fact]
    public void AWarcryRestoresASquadsNerve()
    {
        var (world, warlord) = Duel("goblin_warlord", 4);
        var squad = world.SquadOf(warlord)!;
        world.SpawnMonster("goblin_warrior", world.FreeCellNear(warlord.Position, 2)!.Value, squad);
        squad.Morale = 50;

        var events = world.Step(new WaitAction());

        Assert.Contains(events, e => e is AbilityUsed { AbilityId: "warcry" });
        Assert.True(squad.Morale > 50);
    }

    [Fact]
    public void TheNecromancerBlinksAwayFromAnAdjacentEnemy()
    {
        var world = World.Create(1, depth: LastLevel);
        var boss = world.Actors.Single(a => a.Kind == "necromancer");
        foreach (var other in world.Actors.Where(a => a != world.Player && a != boss))
            other.Hp = 0;
        world.Player.Hp = 100_000;
        world.Player.Position = OpenSpotNear(world, boss, 1);

        var events = world.Step(new WaitAction());

        Assert.Contains(events, e => e is AbilityUsed { AbilityId: "blink" });
        Assert.True(World.Distance(boss.Position, world.Player.Position) >= 4);
    }

    [Fact]
    public void TheNecromancerCallsUpGuardsAndTheyDieWithHim()
    {
        var world = World.Create(1, depth: LastLevel);
        var boss = world.Actors.Single(a => a.Kind == "necromancer");
        foreach (var other in world.Actors.Where(a => a != world.Player && a != boss))
            other.Hp = 0;
        world.Player.Hp = 100_000;
        world.Player.Position = OpenSpotNear(world, boss, 5);

        var events = new List<GameEvent>();
        for (int t = 0; t < 20 && !events.Any(e => e is AbilityUsed { AbilityId: "raise_guard" }); t++)
            events.AddRange(world.Step(new WaitAction()));

        Assert.Contains(events, e => e is AbilityUsed { AbilityId: "raise_guard" });
        Assert.NotEmpty(world.MinionsOf(boss));

        world.Damage(world.Player, boss, 100_000, ranged: false);

        Assert.True(world.IsWon);
        Assert.Empty(world.MinionsOf(boss));
    }

    [Fact]
    public void ADrainHealsTheOneDraining()
    {
        var world = World.Create(1, depth: LastLevel);
        var boss = world.Actors.Single(a => a.Kind == "necromancer");
        world.Player.Hp = 100_000;
        world.Player.Position = OpenSpotNear(world, boss, 4);
        boss.Hp = 5;
        var drain = world.Content.Abilities["soul_drain"];

        int healed = 0;
        for (int i = 0; i < 20 && healed == 0; i++)
        {
            int before = boss.Hp;
            new AbilityAction(drain, world.Player.Id).Perform(world, boss);
            healed = boss.Hp - before;
        }

        Assert.Equal(drain.Amount, healed);
    }

    [Fact]
    public void AnUnknownAbilityIsAContentError()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rl-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
            var path = Path.Combine(dir, "elites.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("[\"charge\"]", "[\"juggle\"]"));

            var e = Assert.Throws<InvalidDataException>(() => ContentDatabase.Load(dir));
            Assert.Contains("juggle", e.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
