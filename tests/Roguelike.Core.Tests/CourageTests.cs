using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Map;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class CourageTests
{
    private static List<GameEvent> Run(World world, int turns)
    {
        var events = new List<GameEvent>();
        for (int t = 0; t < turns; t++)
            events.AddRange(world.Step(new WaitAction()));
        return events;
    }

    /// <summary>An empty level with the player in the biggest room and a band of this many goblins a few cells away.</summary>
    private static (World World, Squad Squad) Encounter(int goblins, string kind = "goblin_warrior", int playerHp = 36, ulong seed = 1, int distance = 5)
    {
        var world = World.Create(seed);
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        var room = world.Map.Rooms.OrderByDescending(r => r.Area).First();
        world.Player.Position = room.Center;
        world.Player.Hp = playerHp;
        var spots = world.Map.Tiles.Positions()
            .Where(p => world.IsFree(p) && World.Distance(p, room.Center) == distance && world.HasLineOfSight(p, room.Center))
            .Take(goblins).ToList();
        Assert.Equal(goblins, spots.Count);
        var first = world.SpawnMonster(kind, spots[0]);
        var squad = world.SquadOf(first)!;
        foreach (var spot in spots.Skip(1))
            world.SpawnMonster(kind, spot, squad);
        return (world, squad);
    }

    private static void NoBow(World world)
    {
        if (world.Player.Equipment!.Bow is { } bow)
            world.Player.Equipment.Unequip(bow);
    }

    [Fact]
    public void ABigBandOnALonePlayerGetsBold()
    {
        var (world, squad) = Encounter(4);
        world.Step(new WaitAction());
        Assert.Equal(Posture.Bold, squad.Posture);
    }

    [Fact]
    public void ABoldPackSurroundsInsteadOfQueueing()
    {
        var (world, squad) = Encounter(4);
        bool opposite = false;
        for (int t = 0; t < 14 && !opposite && !world.IsOver; t++)
        {
            world.Step(new WaitAction());
            world.Player.Hp = 36;
            var near = squad.Members.Where(m => world.IsAdjacent(m.Position, world.Player.Position)).Select(m => m.Position - world.Player.Position).ToList();
            opposite = near.Any(a => near.Any(b => a.X * b.X + a.Y * b.Y < 0));
        }
        Assert.True(opposite, "the pack should end up on opposite sides of the player");
    }

    [Fact]
    public void AWeakBandFacingAStrongFoeWithNoBowHangsBack()
    {
        var (world, squad) = Encounter(2, seed: 1, distance: 6, playerHp: 36);
        NoBow(world);
        foreach (var m in squad.Members) m.Hp = 6;
        world.Step(new WaitAction());
        Assert.Equal(Posture.Wary, squad.Posture);
        Run(world, 6);
        Assert.All(squad.Members, m => Assert.True(World.Distance(m.Position, world.Player.Position) >= 2));
    }

    [Fact]
    public void ALoneGoblinRunsFromAStrongBowlessFoe()
    {
        var (world, squad) = Encounter(1, distance: 6);
        NoBow(world);
        world.Player.Hp = 100;
        var events = Run(world, 2);
        Assert.True(squad.State == SquadState.Flee || squad.Surrendered, $"state {squad.State}");
        if (squad.State == SquadState.Flee)
            Assert.Contains(events, e => e is SquadCowed);
    }

    [Fact]
    public void WithABowAtTheirBackTheNearOnesChargeRatherThanRun()
    {
        var (world, squad) = Encounter(1, distance: 2);
        world.Player.Hp = 100;
        world.Step(new WaitAction());
        Assert.True(squad.Posture == Posture.Desperate || squad.Surrendered, $"posture {squad.Posture}");
        Assert.NotEqual(SquadState.Flee, squad.State);
    }

    [Fact]
    public void FarFromABowTheyStillRun()
    {
        var (world, squad) = Encounter(1, distance: 6);
        world.Player.Hp = 100;
        world.Step(new WaitAction());
        Assert.True(squad.State == SquadState.Flee || squad.Surrendered, $"state {squad.State}");
    }

    [Fact]
    public void TheyJumpInWhenSomethingElseIsAlreadyOnThePlayer()
    {
        var (world, squad) = Encounter(1, distance: 6);
        NoBow(world);
        world.Player.Hp = 36;
        foreach (var m in squad.Members) m.Hp = 30;
        world.Step(new WaitAction());
        Assert.NotEqual(Posture.Bold, squad.Posture);

        for (int i = 0; i < 3; i++)
            world.SpawnMonster("goblin_warrior", World.EightWay.Select(d => world.Player.Position + d).First(world.IsFree)).Hp = 100;
        squad.State = SquadState.Engage;
        world.Step(new WaitAction());

        Assert.Equal(Posture.Bold, squad.Posture);
    }

    [Fact]
    public void GuardsDontAbandonTheirPostOutOfFear()
    {
        var world = HistoryTests.ValleyHeldBy("tollkeepers");
        world.Camps[0].Provoked = true;
        var sentries = world.Squads[0];
        world.Player.Hp = 100_000;
        world.Player.Position = new Point(43, 12);

        Run(world, 4);

        Assert.NotEqual(SquadState.Flee, sentries.State);
        Assert.True(sentries.WithinLeash(sentries.Members[0].Position));
    }

    [Fact]
    public void TheUndeadNeverLoseTheirNerve()
    {
        var (world, squad) = Encounter(1, "skeleton_warrior", distance: 6);
        world.Player.Hp = 100_000;
        var events = Run(world, 4);
        Assert.DoesNotContain(events, e => e is SquadCowed or SquadPleaded);
        Assert.NotEqual(SquadState.Flee, squad.State);
    }

    /// <summary>A goblin down on its luck, within earshot, on a seed where it decides to beg.</summary>
    private static (World World, Squad Squad, Actor Goblin, List<GameEvent> Events) Beggar()
    {
        for (ulong seed = 1; seed < 80; seed++)
        {
            var (world, squad) = Encounter(1, distance: 3, seed: seed);
            world.Player.Hp = 100;
            var events = world.Step(new WaitAction()).ToList();
            if (squad.Surrendered)
                return (world, squad, squad.Members[0], events);
        }
        throw new InvalidOperationException("nobody begged");
    }

    [Fact]
    public void AHopelessGoblinMayBegAndThenLeavesYouAlone()
    {
        var (world, squad, goblin, events) = Beggar();
        Assert.Contains(events, e => e is SquadPleaded);
        Assert.False(world.AreHostile(goblin, world.Player));
        var at = goblin.Position;

        var later = Run(world, 5);

        Assert.Equal(at, goblin.Position);
        Assert.DoesNotContain(later, e => e is ActorAttacked a && a.TargetId == world.Player.Id);
    }

    [Fact]
    public void WalkingIntoABeggarAsksWhatYouWillDo()
    {
        var (world, _, goblin, _) = Beggar();
        world.Player.Position = World.EightWay.Select(d => goblin.Position + d).First(world.IsFree);
        int turn = world.Turn;

        var events = world.Step(new MoveAction(Direction.GetDirection(world.Player.Position, goblin.Position)));

        Assert.Contains(events, e => e is MercyAsked);
        Assert.Equal(turn, world.Turn);
    }

    [Fact]
    public void SparingOneRevealsSomethingOnTheMap()
    {
        var (world, squad, goblin, _) = Beggar();
        var stairs = world.Map.StairsDown!.Value;
        Assert.False(world.Map.Explored[stairs]);

        var events = world.Step(new SpareAction(goblin.Id));

        Assert.Contains(events, e => e is SecretTold { Kind: SecretKind.Stairs });
        Assert.True(world.Map.Explored[stairs]);
        Assert.True(squad.Spared);
        Assert.False(world.AreHostile(goblin, world.Player));
    }

    [Fact]
    public void AttackingABeggarEndsTheTruce()
    {
        var (world, squad, goblin, _) = Beggar();
        goblin.Hp = 1000;

        world.Step(new AttackAction(goblin.Id));

        Assert.False(squad.Surrendered);
        Assert.True(world.AreHostile(goblin, world.Player));
    }

    [Fact]
    public void TheSameFightsPlayOutTheSameWay()
    {
        static List<string> Play()
        {
            var (world, _) = Encounter(3, seed: 5);
            return Run(world, 20).Select(e => e.ToString()!).ToList();
        }

        Assert.Equal(Play(), Play());
    }
}
