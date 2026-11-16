using Roguelike.Core.Simulation;

namespace Roguelike.Core.Tests;

public class FuzzTests
{
    /// <summary>
    /// Random fights keep every rule: no overlaps, no actor in a wall, squads let go of their dead, replays
    /// match, and no fighting monster freezes. Run more with <c>Roguelike.Sim --fuzz --seeds 5000</c>.
    /// </summary>
    [Fact]
    public void RandomFightsBreakNoRule()
    {
        var report = Fuzz.Run(seeds: 200, start: 0, replayEvery: 5);
        Assert.True(report.Turns > 10_000);
        Assert.Empty(report.Failures.Where(f => f.Problem != FuzzProblem.Slow).Select(f => $"seed {f.Seed} turn {f.Turn}: {f.Problem} {f.Detail}"));
    }

    /// <summary>
    /// These fights jammed before drilled fighters stopped waiting for the line in corridors and could trade
    /// places with a shaman in their way: the front waited for a rear that could never come up.
    /// </summary>
    [Theory]
    [InlineData(36ul)]
    [InlineData(90ul)]
    [InlineData(399ul)]
    [InlineData(1039ul)]
    public void DrilledSquadsDontDeadlockWaitingForTheLine(ulong seed)
    {
        var result = Fuzz.Play(Fuzz.Roll(seed));
        Assert.DoesNotContain(result.Failures, f => f.Problem == FuzzProblem.Jammed);
    }
}
