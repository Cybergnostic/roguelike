using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Map;
using Roguelike.Core.Progression;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

/// <summary>Runes (COMBAT_AND_MAGIC.md §4): learned from stones, carved on the ground, the skin and gear.</summary>
public class RuneTests
{
    /// <summary>A Mystic (who starts knowing Algiz and Berkano and how to carve), alone on the level.</summary>
    private static World Mystic(ulong seed = 1)
    {
        var world = World.Create(seed, classId: "mystic");
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        world.Player.Hp = 100_000;
        world.GrantXp(XpSource.Kill, "test", 5000);
        return world;
    }

    private static Actor Foe(World world, Point at, string kind = "goblin_warrior")
    {
        var foe = world.SpawnMonster(kind, at);
        foe.Hp = 100_000;
        return foe;
    }

    private static Point FreeNextTo(World world, Point p) => World.EightWay.Select(d => p + d).First(world.IsFree);

    [Fact]
    public void AMysticStartsKnowingTwoRunes()
    {
        var c = World.Create(1, classId: "mystic").Player.Character!;
        Assert.Equal(["algiz", "berkano"], c.KnownRunes);
        Assert.Empty(World.Create(1, classId: "warrior").Player.Character!.KnownRunes);
    }

    [Fact]
    public void YouCantCarveARuneYouDontKnow()
    {
        var world = Mystic();
        Assert.Contains(world.Step(new CarveAction("thurisaz", RuneSurface.Ground)), e => e is ActionFailed { Reason: FailureReason.RuneUnknown });
    }

    [Fact]
    public void WithoutInscriptionYouCantCarveAtAll()
    {
        var world = World.Create(1, classId: "warrior");
        world.TeachRune("algiz");
        Assert.Contains(world.Step(new CarveAction("algiz", RuneSurface.Body)), e => e is ActionFailed { Reason: FailureReason.CantCarveThere });
    }

    [Fact]
    public void CarvingTheGroundTakesTimeAndLeavesAMark()
    {
        var world = Mystic();
        world.TeachRune("thurisaz");
        int turn = world.Turn;
        var events = world.Step(new CarveAction("thurisaz", RuneSurface.Ground));
        Assert.Contains(events, e => e is RuneCarved);
        Assert.Equal(turn + 6, world.Turn);
        Assert.Equal("thurisaz", world.MarkAt(world.Player.Position)!.RuneId);
    }

    [Fact]
    public void AThornMarkWoundsWhoeverStepsOnItUntilItIsSpent()
    {
        var world = Mystic();
        world.TeachRune("thurisaz");
        world.Step(new CarveAction("thurisaz", RuneSurface.Ground));
        var at = world.Player.Position;
        world.Player.Position = FreeNextTo(world, at);
        var mark = world.MarkAt(at)!;
        var foe = Foe(world, FreeNextTo(world, at), "goblin_zombie"); // mindless: walks right in
        int charges = mark.Charges;
        for (int i = 0; i < charges; i++)
        {
            int hp = foe.Hp;
            var from = foe.Position;
            foe.Position = at;
            world.AfterStep(foe, from);
            Assert.True(foe.Hp < hp);
            foe.Position = from;
        }
        Assert.Null(world.MarkAt(at));
    }

    [Fact]
    public void TheDeadCantCrossAWardAndTheCannyStepRoundThorns()
    {
        var world = Mystic();
        world.TeachRune("thurisaz");
        world.Step(new CarveAction("algiz", RuneSurface.Ground));
        var ward = world.Player.Position;
        world.Player.Position = FreeNextTo(world, ward);
        var zombie = Foe(world, FreeNextTo(world, ward), "goblin_zombie");
        var goblin = Foe(world, FreeNextTo(world, ward));
        Assert.True(world.MarkBars(zombie, ward));
        Assert.False(world.MarkBars(goblin, ward)); // the living just break a ward

        world.Player.Position = ward;
        world.Step(new CarveAction("thurisaz", RuneSurface.Ground));
        world.Player.Position = FreeNextTo(world, ward);
        Assert.True(world.MarkBars(goblin, ward));    // rabble avoid a thorn they can see
        Assert.False(world.MarkBars(zombie, ward));   // the mindless walk into it
    }

    [Fact]
    public void TheLivingBreakAWardByWalkingThroughIt()
    {
        var world = Mystic();
        world.Step(new CarveAction("algiz", RuneSurface.Ground));
        var ward = world.Player.Position;
        world.Player.Position = FreeNextTo(world, ward);
        var goblin = Foe(world, FreeNextTo(world, ward));
        var from = goblin.Position;
        goblin.Position = ward;
        world.AfterStep(goblin, from);
        Assert.Null(world.MarkAt(ward));
    }

    [Fact]
    public void AlgizOnTheSkinIsAWard()
    {
        var world = Mystic();
        world.Step(new CarveAction("algiz", RuneSurface.Body));
        Assert.Equal(world.Content.Runes["algiz"].Power, world.Player.StatusOf(StatusKind.Shielded)!.Amount);
    }

    [Fact]
    public void BerkanoOnTheSkinHeals()
    {
        var world = Mystic();
        world.Player.Hp = 5;
        world.Step(new CarveAction("berkano", RuneSurface.Body));
        Assert.True(world.Player.Hp > 5);
    }

    [Fact]
    public void GearNeedsInscriptionTwoAndQuiet()
    {
        var world = World.Create(1, classId: "warrior");
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        world.GrantXp(XpSource.Kill, "test", 5000);
        world.TeachRune("thurisaz");
        world.Step(new LearnSkillAction("inscription"));
        Assert.Contains(world.Step(new CarveAction("thurisaz", RuneSurface.Weapon)), e => e is ActionFailed { Reason: FailureReason.CantCarveThere });
        world.Step(new LearnSkillAction("inscription"));
        int attack = world.Player.Attack;
        world.Step(new CarveAction("thurisaz", RuneSurface.Weapon));
        Assert.Equal("thurisaz", world.Player.Equipment!.Weapon!.Rune);
        Assert.Equal(attack + World.ThurisazWeaponBonus, world.Player.Attack);

        world.TeachRune("isa");
        var fov = world.Map.PlayerFov.BooleanResultView;
        Foe(world, world.Map.Tiles.Positions().First(p => world.IsFree(p) && fov[p] && World.Distance(p, world.Player.Position) >= 3));
        Assert.Contains(world.Step(new CarveAction("isa", RuneSurface.Armor)), e => e is ActionFailed { Reason: FailureReason.EnemiesNear });
    }

    [Fact]
    public void IsaOnArmourMeansYouCantBeStaggered()
    {
        var world = Mystic();
        world.Player.Equipment!.Armor!.Rune = "isa";
        var foe = Foe(world, FreeNextTo(world, world.Player.Position));
        world.StaggerIfAlive(world.Player, foe);
        Assert.False(world.IsStaggered(world.Player));
    }

    [Fact]
    public void ACarverKeepsOnlySoManyMarks()
    {
        var world = Mystic();
        int max = world.MaxMarks(world.Player);
        for (int i = 0; i < max + 2; i++)
        {
            world.Step(new CarveAction("algiz", RuneSurface.Ground));
            var next = World.EightWay.Select(d => world.Player.Position + d).First(p => world.IsFree(p) && world.MarkAt(p) is null);
            world.Player.Position = next;
        }
        Assert.Equal(max, world.Marks.Count(m => m.OwnerId == world.Player.Id));
    }

    [Fact]
    public void MarksWearAway()
    {
        var world = Mystic();
        world.Step(new CarveAction("algiz", RuneSurface.Ground));
        var mark = world.Marks.Single();
        mark.Until = world.Turn;
        world.Step(new WaitAction());
        world.Step(new WaitAction());
        Assert.Empty(world.Marks);
    }

    [Fact]
    public void ABindingRuneKeepsTheDeadDown()
    {
        var world = Mystic();
        world.TeachRune("naudhiz");
        world.Step(new CarveAction("naudhiz", RuneSurface.Ground));
        Assert.True(world.IsBound(world.Player.Position));
    }

    [Fact]
    public void KenazLetsYouSeeAroundIt()
    {
        var world = Mystic();
        world.TeachRune("kenaz");
        world.Step(new CarveAction("kenaz", RuneSurface.Ground));
        var light = world.Player.Position;
        Assert.True(world.InPlayersLight(FreeNextTo(world, light)));
    }

    [Fact]
    public void ReadingAStoneTeachesARuneIfYouCanReadRunes()
    {
        // Find a level with a stone, then stand on it with and without Rune Lore.
        for (ulong seed = 0; seed < 40; seed++)
        {
            var world = World.Create(seed, classId: "mystic");
            var stone = world.Map.Tiles.Positions().Cast<Point?>().FirstOrDefault(p => world.Map.Tiles[p!.Value] == Tile.Inscription);
            if (stone is not { } at)
                continue;
            int known = world.Player.Character!.KnownRunes.Count;
            world.Player.Position = World.EightWay.Select(d => at + d).First(world.IsFree);
            var step = World.EightWay.First(d => world.Player.Position + d == at);
            var events = world.Step(new MoveAction(step));
            Assert.Contains(events, e => e is RuneLearned);
            Assert.Equal(known + 1, world.Player.Character.KnownRunes.Count);
            return;
        }
        Assert.Fail("no stone on any of 40 first levels");
    }

    [Fact]
    public void WithoutRuneLoreAStoneTeachesNothing()
    {
        for (ulong seed = 0; seed < 40; seed++)
        {
            var world = World.Create(seed, classId: "warrior");
            var stone = world.Map.Tiles.Positions().Cast<Point?>().FirstOrDefault(p => world.Map.Tiles[p!.Value] == Tile.Inscription);
            if (stone is not { } at)
                continue;
            world.Player.Position = World.EightWay.Select(d => at + d).First(world.IsFree);
            var step = World.EightWay.First(d => world.Player.Position + d == at);
            Assert.DoesNotContain(world.Step(new MoveAction(step)), e => e is RuneLearned);
            return;
        }
    }
}
