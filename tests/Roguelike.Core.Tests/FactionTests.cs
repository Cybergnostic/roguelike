using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Map;
using Roguelike.Core.Simulation;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;
using Xunit.Abstractions;

namespace Roguelike.Core.Tests;

public class FactionTests(ITestOutputHelper output)
{
    [Fact]
    public void DefaultContentLoadsAndIsConsistent()
    {
        var content = ContentDatabase.Default;
        Assert.Contains("player", content.Factions.Keys);
        Assert.NotEmpty(content.Monsters);
        Assert.NotEmpty(content.SquadTemplates);
        Assert.True(content.Relations.AreHostile(content.Factions["goblins"], content.Factions["undead"]));
        Assert.Equal(Relation.Allied, content.Relations.Get(content.Factions["goblins"], content.Factions["goblins"]));
    }

    [Fact]
    public void BrokenContentFailsWithAClearMessage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rl-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
            var spawns = Path.Combine(dir, "spawns.json");
            File.WriteAllText(spawns, File.ReadAllText(spawns).Replace("\"goblin_shaman\"", "\"goblin_wizard\""));

            var e = Assert.Throws<InvalidDataException>(() => ContentDatabase.Load(dir));
            Assert.Contains("goblin_wizard", e.Message);
            Assert.Contains("spawns.json", e.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void UnlistedFactionPairsAreNeutral()
    {
        var relations = new FactionRelations();
        var a = new Faction("a", "A");
        var b = new Faction("b", "B");
        Assert.Equal(Relation.Neutral, relations.Get(a, b));
        relations.Set(a, b, Relation.Hostile);
        Assert.True(relations.AreHostile(b, a));
    }

    [Fact]
    public void FactionsFightEachOtherWithoutThePlayer()
    {
        var results = Enumerable.Range(0, 20)
            .Select(seed => Simulator.Run((ulong)seed, new IdleBot(), maxTurns: 800, observer: true))
            .ToList();
        int Count(string key) => results.Sum(r => r.EventCounts.GetValueOrDefault(key));

        foreach (var key in results.SelectMany(r => r.EventCounts.Keys).Distinct().Order())
            output.WriteLine($"  {key}: {Count(key)}");

        Assert.True(Count("squads spotted other factions") > 0);
        Assert.True(Count("melee attacks between factions") > 0);
        Assert.True(Count("killed by others: goblins") + Count("killed by others: undead") > 0);
        Assert.Equal(0, Count("squads spotted player"));
    }

    [Fact]
    public void FearlessSquadsNeverRout()
    {
        for (ulong seed = 0; seed < 50; seed++)
        {
            var world = World.Create(seed);
            var undead = world.Squads.FirstOrDefault(s => s.IsFearless && s.Members.Count >= 2);
            if (undead is null)
                continue;
            undead.Members[0].Hp = 0;
            world.Step(new WaitAction());
            Assert.NotEqual(SquadState.Flee, undead.State);
            return;
        }
        Assert.Fail("no seed with a fearless squad");
    }

    [Fact]
    public void SafetyMapLeadsAwayFromDanger()
    {
        // Open 20x9 room, danger on the left wall: fleeing should head right.
        var walkable = new ArrayView<bool>(20, 9);
        walkable.Fill(true);
        var danger = new DistanceMap(walkable);
        danger.Update([new Point(0, 4)]);
        var safety = new SafetyMap(walkable);
        safety.Update(danger);

        Assert.True(safety[new Point(19, 4)] < safety[new Point(5, 4)]);
        Assert.True(safety[new Point(6, 4)] < safety[new Point(5, 4)]);
    }
}
