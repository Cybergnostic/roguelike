using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class NecromancerTests
{
    private static Actor NecromancerOf(World world) => world.Actors.Single(a => a.Kind == "necromancer");

    /// <summary>A free cell near the necromancer that he can see, for staging a corpse.</summary>
    private static Point SpotInSight(World world, Actor necromancer) =>
        world.Map.Tiles.Positions()
            .Where(p => world.IsFree(p) && World.Distance(p, necromancer.Position) is >= 2 and <= 4
                        && world.HasLineOfSight(necromancer.Position, p))
            .OrderBy(p => World.Distance(p, necromancer.Position))
            .First();

    [Fact]
    public void EveryLevelHasOneNecromancerAtHomeFarFromTheStart()
    {
        for (ulong seed = 0; seed < 20; seed++)
        {
            var world = World.Create(seed);
            var necromancer = NecromancerOf(world);
            var squad = world.SquadOf(necromancer)!;
            Assert.NotNull(squad.Home);
            Assert.True(World.Distance(necromancer.Position, world.Player.Position) >= world.Content.SafeStartDistance / 2,
                $"seed {seed}: necromancer starts too close");
        }
    }

    [Fact]
    public void DeadMonstersLeaveCorpses()
    {
        var world = World.Create(1);
        var victim = world.Actors.First(a => a != world.Player);
        var at = victim.Position;

        world.Damage(world.Player, victim, 1000, ranged: false);

        Assert.Contains(world.Corpses, c => c.Kind == victim.Kind && c.Position == at);
    }

    [Fact]
    public void NecromancerRaisesCorpsesIntoHisService()
    {
        var world = World.Create(1);
        world.ObserverMode = true; // keep the player out of it
        var necromancer = NecromancerOf(world);
        var goblin = world.SpawnMonster("goblin_warrior", SpotInSight(world, necromancer));
        world.Damage(necromancer, goblin, 1000, ranged: false);

        CorpseRaised? raised = null;
        for (int i = 0; i < 10 && raised is null; i++)
            raised = world.Step(new WaitAction()).OfType<CorpseRaised>().FirstOrDefault();

        Assert.NotNull(raised);
        Assert.Equal("goblin_warrior", raised.CorpseKind);
        var zombie = world.GetActor(raised.RaisedId)!;
        Assert.Equal("goblin_zombie", zombie.Kind);
        Assert.Equal(necromancer.Faction, zombie.Faction);
        Assert.Equal(necromancer.Id, zombie.MasterId);
        Assert.Same(world.SquadOf(necromancer), world.SquadOf(zombie));
        Assert.DoesNotContain(world.Corpses, c => c.Position == raised.At && c.Kind == "goblin_warrior");
    }

    [Fact]
    public void ServantsCrumbleWhenTheirMasterDies()
    {
        var world = World.Create(1);
        var necromancer = NecromancerOf(world);
        var servant = world.SpawnMonster("goblin_zombie", SpotInSight(world, necromancer),
            world.SquadOf(necromancer), necromancer.Faction);
        servant.MasterId = necromancer.Id;
        var unbound = world.Actors.First(a => a.Kind == "skeleton_warrior" && a.MasterId is null);

        world.Damage(world.Player, necromancer, 1000, ranged: false);
        var events = world.Step(new WaitAction());

        Assert.True(servant.IsDead);
        Assert.False(unbound.IsDead); // only what he raised is bound to him
        Assert.DoesNotContain(servant, world.Actors);
        Assert.Empty(world.CorpsesAt(servant.Position)); // the dead rise only once
    }

    private sealed class CountingBrain : IBrain
    {
        public int Decisions { get; private set; }
        public GameAction Decide(World world, Actor self)
        {
            Decisions++;
            return new WaitAction();
        }
    }

    [Fact]
    public void SpeedSetsHowOftenMonstersAct()
    {
        var world = World.Create(1);
        var spots = world.Map.Tiles.Positions().Where(world.IsFree).Take(2).ToList();
        var slow = world.SpawnMonster("goblin_zombie", spots[0]);
        var normal = world.SpawnMonster("goblin_warrior", spots[1]);
        var slowBrain = new CountingBrain();
        var normalBrain = new CountingBrain();
        slow.Brain = slowBrain;
        normal.Brain = normalBrain;
        world.ObserverMode = true;

        for (int i = 0; i < 10; i++)
            world.Step(new WaitAction());

        Assert.Equal(10, normalBrain.Decisions);
        Assert.Equal(5, slowBrain.Decisions);
    }

    [Fact]
    public void RaisingIntoAnUnknownMonsterIsAContentError()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rl-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
            var monsters = Path.Combine(dir, "monsters.json");
            File.WriteAllText(monsters, File.ReadAllText(monsters).Replace("\"risesAs\": \"goblin_zombie\"", "\"risesAs\": \"goblin_ghost\""));

            var e = Assert.Throws<InvalidDataException>(() => ContentDatabase.Load(dir));
            Assert.Contains("goblin_ghost", e.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
