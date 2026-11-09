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

public class SneakingTests
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

    private static Direction FreeStep(World w) => World.EightWay.First(d => w.IsFree(w.Player.Position + d));

    /// <summary>A goblin that can't see the player, this many cells away through something solid.</summary>
    private static (World World, Actor Goblin)? GoblinBehindAWall(ulong seed, int distance)
    {
        var world = Empty(seed);
        var at = world.Player.Position;
        var spots = world.Map.Tiles.Positions().Where(p => world.IsFree(p) && World.Distance(p, at) == distance && !world.HasLineOfSight(p, at)).ToList();
        if (spots.Count == 0 || !World.EightWay.Any(d => world.IsFree(at + d)))
            return null;
        return (world, world.SpawnMonster("goblin_warrior", spots[0]));
    }

    [Fact]
    public void ChainMailIsLouderThanLeather()
    {
        var world = Empty();
        Wear(world, "leather_armor");
        int leather = world.FootstepNoise(world.Player);
        Wear(world, "chain_mail");
        Assert.True(world.FootstepNoise(world.Player) > leather);
    }

    [Fact]
    public void SneakingIsQuieterAndSlower()
    {
        var world = Empty();
        Wear(world, "chain_mail");
        int loud = world.FootstepNoise(world.Player);
        var step = new MoveAction(FreeStep(world));
        Assert.Equal(world.Player.MoveTime, step.TimeCost(world, world.Player));

        world.Step(new ToggleSneakAction());

        Assert.True(world.Player.Sneaking);
        Assert.True(world.FootstepNoise(world.Player) < loud);
        Assert.Equal(world.Content.Dungeon.SneakStepTime * world.Player.MoveTime / 100, step.TimeCost(world, world.Player));
    }

    [Fact]
    public void TogglingSneakTakesNoTime()
    {
        var world = Empty();
        int turn = world.Turn;
        world.Step(new ToggleSneakAction());
        Assert.Equal(turn, world.Turn);
    }

    [Fact]
    public void SomeoneWhoHearsYouGoesToLook()
    {
        (World World, Actor Goblin)? found = null;
        for (ulong seed = 0; seed < 60 && found is null; seed++)
            found = GoblinBehindAWall(seed, 3);
        var (world, goblin) = found!.Value;
        var squad = world.SquadOf(goblin)!;

        var at = world.Player.Position + FreeStep(world);
        var events = world.Step(new MoveAction(Direction.GetDirection(world.Player.Position, at)));

        Assert.Contains(events, e => e is SquadHeardPlayer h && h.SquadId == squad.Id);
        Assert.Equal(SquadState.Alert, squad.State);
        Assert.Equal(at, squad.LastKnownTarget);
    }

    [Fact]
    public void SneakingPastSomeoneGoesUnheard()
    {
        (World World, Actor Goblin)? found = null;
        for (ulong seed = 0; seed < 60 && found is null; seed++)
            found = GoblinBehindAWall(seed, 3);
        var (world, goblin) = found!.Value;
        world.Step(new ToggleSneakAction());

        var events = world.Step(new MoveAction(FreeStep(world)));

        Assert.DoesNotContain(events, e => e is SquadHeardPlayer);
        Assert.Equal(SquadState.Idle, world.SquadOf(goblin)!.State);
    }

    [Fact]
    public void StandingStillMakesNoSound()
    {
        (World World, Actor Goblin)? found = null;
        for (ulong seed = 0; seed < 60 && found is null; seed++)
            found = GoblinBehindAWall(seed, 3);
        var (world, _) = found!.Value;
        Assert.DoesNotContain(world.Step(new WaitAction()), e => e is SquadHeardPlayer);
    }

    [Fact]
    public void ASneakerIsSpottedFromCloserIn()
    {
        var world = Empty();
        var goblin = world.SpawnMonster("goblin_warrior", world.Map.Tiles.Positions()
            .First(p => world.IsFree(p) && World.Distance(p, world.Player.Position) == 7 && world.HasLineOfSight(p, world.Player.Position)));
        Assert.True(world.CanSee(goblin, world.Player));

        world.Step(new ToggleSneakAction());

        Assert.False(world.CanSee(goblin, world.Player));
    }

    [Fact]
    public void TheStealthSkillMakesYouQuieterAndHarderToSpot()
    {
        var world = Empty();
        Wear(world, "chain_mail");
        int before = world.FootstepNoise(world.Player);
        world.GrantXp(XpSource.Kill, "test", 5000);

        world.Step(new LearnSkillAction("stealth"));
        world.Step(new LearnSkillAction("stealth"));

        Assert.True(world.Player.Bonus(Stat.Stealth) >= 3);
        Assert.True(world.FootstepNoise(world.Player) < before);
    }

    [Fact]
    public void ThePerceptionSkillAddsToHearing()
    {
        var world = Empty();
        int before = world.Player.Bonus(Stat.Perception);
        world.GrantXp(XpSource.Kill, "test", 5000);
        world.Step(new LearnSkillAction("perception"));
        Assert.Equal(before + 1, world.Player.Bonus(Stat.Perception));
    }

    [Fact]
    public void YouCanHearEnemiesWalkingNearby()
    {
        (World World, Actor Goblin)? found = null;
        for (ulong seed = 0; seed < 60 && found is null; seed++)
            found = GoblinBehindAWall(seed, 4);
        var (world, goblin) = found!.Value;
        var squad = world.SquadOf(goblin)!;
        squad.State = SquadState.Alert;
        squad.LastKnownTarget = squad.Destination = world.Player.Position;
        squad.LastSeenTurn = world.Turn;

        var events = new List<GameEvent>();
        for (int t = 0; t < 3; t++)
            events.AddRange(world.Step(new WaitAction()));

        Assert.Contains(events, e => e is SoundHeard { Kind: SoundKind.Footsteps });
    }

    [Fact]
    public void GuardsThatTolerateYouDontComeLooking()
    {
        var world = HistoryTests.ValleyHeldBy("tollkeepers");
        world.Player.Hp = 100_000;
        world.Player.Position = new Point(43, 12);

        var events = world.Step(new MoveAction(Direction.Down));

        Assert.DoesNotContain(events, e => e is SquadHeardPlayer h && world.Squads.First(s => s.Id == h.SquadId).Camp is not null);
    }

    [Fact]
    public void SneakingThroughABackFencePlaysTheSameEveryTime()
    {
        static List<string> Play()
        {
            var world = World.Create(4);
            world.Player.Hp = 100_000;
            world.Step(new ToggleSneakAction());
            var log = new List<string>();
            for (int t = 0; t < 30; t++)
            {
                var step = World.EightWay.First(d => world.IsFree(world.Player.Position + d));
                log.AddRange(world.Step(new MoveAction(step)).Select(e => e.ToString()!));
            }
            return log;
        }

        Assert.Equal(Play(), Play());
    }
}
