using Roguelike.Core.Actions;
using Roguelike.Core.Events;
using SadRogue.Primitives;

namespace Roguelike.Core.Tests;

/// <summary>The crypt (COMBAT_AND_MAGIC.md §4): seals binding the dead, and the necromancer's hidden traps.</summary>
public class CryptTests
{
    private static World Crypt(string classId, ulong seed = 3) =>
        World.Create(seed, depth: Roguelike.Core.Content.ContentDatabase.Default.Dungeon.Levels, classId: classId);

    [Fact]
    public void TheCryptHasSealsAndHiddenTraps()
    {
        int seals = 0, traps = 0;
        for (ulong seed = 0; seed < 10; seed++)
        {
            var world = Crypt("warrior", seed);
            seals += world.Marks.Count(m => m.SealOf is not null);
            traps += world.Marks.Count(m => m.Hidden);
        }
        Assert.True(seals > 0, "no seals");
        Assert.True(traps > 0, "no traps");
        Assert.Empty(World.Create(1, depth: 3).Marks);
    }

    [Fact]
    public void OnlyThoseWhoSenseRunesSeeTheTraps()
    {
        var warrior = Crypt("warrior");
        var mystic = Crypt("mystic");
        var trap = warrior.Marks.FirstOrDefault(m => m.Hidden);
        if (trap is null)
            return;
        Assert.False(warrior.PlayerSeesMark(trap));
        Assert.True(mystic.PlayerSeesMark(mystic.Marks.First(m => m.Hidden)));
    }

    [Fact]
    public void BreakingASealTurnsItsDeadToDust()
    {
        for (ulong seed = 0; seed < 10; seed++)
        {
            var world = Crypt("warrior", seed);
            if (world.Marks.FirstOrDefault(m => m.SealOf is not null) is not { } seal)
                continue;
            var bound = world.Squads.First(s => s.Id == seal.SealOf).Members.Where(m => !m.IsDead).ToList();
            Assert.NotEmpty(bound);
            world.Player.Hp = 100_000;
            world.Player.Position = World.EightWay.Select(d => seal.At + d).First(world.IsFree);
            int turn = world.Turn;
            var events = world.Step(new BreakMarkAction());
            Assert.Contains(events, e => e is MarkBroken { Crumbled: > 0 });
            Assert.All(bound, m => Assert.True(m.IsDead));
            Assert.True(world.Turn - turn >= 5, "smashing a seal by hand takes a while");
            return;
        }
        Assert.Fail("no seal found");
    }

    [Fact]
    public void AWarriorCantUnmakeATrapButCanSmashASeal()
    {
        var world = Crypt("warrior");
        if (world.Marks.FirstOrDefault(m => m.Hidden) is not { } trap)
            return;
        world.Player.Position = World.EightWay.Select(d => trap.At + d).First(world.IsFree);
        Assert.Equal(Roguelike.Core.Events.FailureReason.NoTarget, world.BreakProblem(world.Player, out _)); // can't even see it
    }
}
