using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.Content;
using Roguelike.Core.Events;
using Roguelike.Core.History;
using Roguelike.Core.Items;
using Roguelike.Core.Map;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class HistoryTests
{
    /// <summary>The valley on the first seed from <paramref name="from"/> where this tribe holds the camp.</summary>
    internal static World ValleyHeldBy(string tribe, ulong from = 0)
    {
        for (ulong seed = from; seed < from + 500; seed++)
        {
            var world = World.Create(seed, depth: World.SurfaceDepth);
            if (world.History.Holders.Id == tribe)
                return world;
        }
        throw new InvalidOperationException($"no seed where {tribe} hold the camp");
    }

    private static IEnumerable<ulong> Seeds => Enumerable.Range(0, 40).Select(s => (ulong)s);

    private static World Level(ulong seed, Func<SiteHistory, int> depth)
    {
        var history = World.Create(seed).History;
        return World.Create(seed, depth: depth(history));
    }

    private static World FirstWith(FallKind fall)
    {
        var seed = Seeds.First(s => World.Create(s).History.Fall == fall);
        return Level(seed, h => h.FallDepth);
    }

    private static IEnumerable<Point> CellsOf(World w, Tile tile) => w.Map.Tiles.Positions().Where(p => w.Map.Tiles[p] == tile);

    [Fact]
    public void TheSameSeedTellsTheSameHistory()
    {
        Assert.Equal(World.Create(5).History.Founder, World.Create(5).History.Founder);
        Assert.Equal(World.Create(5).History.Eras, World.Create(5).History.Eras);
        Assert.Equal(World.Create(5).History.FallDepth, World.Create(5, depth: World.SurfaceDepth).History.FallDepth);
    }

    [Fact]
    public void DifferentSeedsTellDifferentHistories()
    {
        var histories = Seeds.Select(s => World.Create(s).History).ToList();
        Assert.True(histories.Select(h => h.Founder).Distinct().Count() >= 10);
        Assert.Equal(3, histories.Select(h => h.Fall).Distinct().Count());
        Assert.True(histories.Select(h => h.FallDepth).Distinct().Count() >= 3);
    }

    [Fact]
    public void TheMineFallsSomewhereInsideTheMine()
    {
        foreach (var h in Seeds.Select(s => World.Create(s).History))
        {
            Assert.InRange(h.MineDepth, 3, 5);
            Assert.InRange(h.FallDepth, 2, h.MineDepth);
        }
    }

    [Fact]
    public void EveryChapterHasAStoneOnItsLevel()
    {
        foreach (var seed in Seeds.Take(15))
            foreach (var era in World.Create(seed).History.Eras)
            {
                var world = World.Create(seed, depth: era.Depth);
                Assert.Single(CellsOf(world, Tile.Inscription));
            }
    }

    [Fact]
    public void ReadingAStoneTeachesItOnce()
    {
        var world = World.Create(3);
        world.Player.Hp = 10_000;
        var stone = CellsOf(world, Tile.Inscription).Single();
        var era = world.History.Eras.Single(e => e.Kind == EraKind.Founding);
        Assert.False(world.KnowsOf(era));
        int xp = world.Player.Character!.TotalXp;

        world.Player.Position = stone;
        var first = world.Step(new WaitAction());

        Assert.Contains(first, e => e is LoreDiscovered d && d.Era == era);
        Assert.True(world.KnowsOf(era));
        Assert.True(world.Player.Character!.TotalXp >= xp + world.Content.History.LoreXp);

        Assert.DoesNotContain(world.Step(new WaitAction()), e => e is LoreDiscovered);
    }

    [Fact]
    public void TheHeirloomLiesWhereTheMineFell()
    {
        foreach (var seed in Seeds.Take(15))
        {
            var world = Level(seed, h => h.FallDepth);
            var heirloom = Assert.Single(world.FloorItems, f => f.Item.Rarity == Rarity.Unique);
            Assert.Equal(world.History.HeirloomItem, heirloom.Item.Def.Id);
            Assert.Equal(world.History.HeirloomWords, heirloom.Item.NameWords);
            Assert.Equal(world.Content.History.Heirloom.Enchantment, heirloom.Item.Enchantment);
            Assert.NotEmpty(heirloom.Item.Affixes);
        }
    }

    [Fact]
    public void NoHeirloomAnywhereElse()
    {
        var history = World.Create(9).History;
        for (int depth = 1; depth <= 7; depth++)
            if (depth != history.FallDepth)
                Assert.DoesNotContain(World.Create(9, depth: depth).FloorItems, f => f.Item.Rarity == Rarity.Unique);
    }

    [Fact]
    public void AWarLeavesTheDeadAndTheWalkingDead()
    {
        var world = FirstWith(FallKind.War);
        Assert.True(world.Corpses.Count >= 8, $"only {world.Corpses.Count} corpses");
        Assert.True(world.Squads.Count(s => s.Faction.Id == "undead") >= 2);
        Assert.Empty(CellsOf(world, Tile.Rubble));
    }

    [Fact]
    public void ACollapseLeavesRockAndWhatTheMinersLeft()
    {
        var world = FirstWith(FallKind.Collapse);
        Assert.NotEmpty(CellsOf(world, Tile.Rubble));
        Assert.True(world.FloorItems.Count >= world.Content.History.Collapse.Items);
    }

    [Fact]
    public void APlagueLeavesTheSickWalking()
    {
        var world = FirstWith(FallKind.Plague);
        Assert.True(world.Actors.Count(a => a.Kind == "goblin_zombie") >= 3);
        Assert.Contains(world.Corpses, c => c.Kind == "goblin_zombie");
        Assert.Empty(CellsOf(world, Tile.Rubble));
    }

    [Fact]
    public void TheCampIsNamedAfterTheTribeThatHoldsIt()
    {
        var world = World.Create(4, depth: World.SurfaceDepth);
        Assert.Equal($"the {world.History.Tribe} camp", world.Camps[0].Zone.Name);
        Assert.DoesNotContain("{", world.Map.Zones[0].Name);
    }

    [Fact]
    public void EveryKindOfTribeTurnsUp()
    {
        var tribes = Enumerable.Range(0, 200).Select(s => World.Create((ulong)s).History.Holders.Id).Distinct();
        Assert.Equal(["remnant", "tollkeepers", "warband"], tribes.Order());
    }

    [Fact]
    public void HowTheMineFellDecidesWhoCanHoldTheCamp()
    {
        var histories = Enumerable.Range(0, 300).Select(s => World.Create((ulong)s).History).ToList();
        Assert.DoesNotContain(histories, h => h.Fall == FallKind.War && h.Holders.Id == "remnant");
        double Share(FallKind fall) =>
            histories.Where(h => h.Fall == fall).Average(h => h.Holders.Id == "warband" ? 1.0 : 0.0);
        Assert.True(Share(FallKind.War) > Share(FallKind.Collapse), "a war should leave warbands behind more often");
    }

    private static int Goblins(World w) => w.Actors.Count(a => a.Faction.Id == "goblins");

    [Fact]
    public void AWarbandCampIsBiggerAndDearer()
    {
        var keepers = ValleyHeldBy("tollkeepers");
        var warband = ValleyHeldBy("warband");

        Assert.Equal(Goblins(keepers) + 3, Goblins(warband));
        Assert.Equal((40, 80), (warband.Camps[0].Toll!.Price, warband.Camps[0].Toll!.Turns));
    }

    [Fact]
    public void AWarbandsPriceIsMoreThanYouStartWith()
    {
        var world = ValleyHeldBy("warband");
        world.Player.Hp = 10_000;
        world.Player.Position = new Point(43, 11);
        Assert.Contains(world.Step(new PayTollAction()), e => e is ActionFailed { Reason: FailureReason.NotEnoughGold });
    }

    [Fact]
    public void ARemnantIsSmallerAndCheaper()
    {
        var keepers = ValleyHeldBy("tollkeepers");
        var remnant = ValleyHeldBy("remnant");

        Assert.Equal(Goblins(keepers) - 2, Goblins(remnant));
        Assert.DoesNotContain(remnant.Actors, a => a.Kind == "goblin_shaman");
        Assert.Equal((10, 250), (remnant.Camps[0].Toll!.Price, remnant.Camps[0].Toll!.Turns));
    }

    [Theory]
    [InlineData("tollkeepers")]
    [InlineData("warband")]
    [InlineData("remnant")]
    public void EveryTribesCampWorksAtTheGate(string tribe)
    {
        var world = ValleyHeldBy(tribe);
        world.Player.Hp = 10_000;
        world.Player.Gold = 100;
        world.Player.Position = new Point(43, 11);

        Assert.Contains(world.Step(new PayTollAction()), e => e is TollPaid);
        world.Player.Position = new Point(43, 9);
        for (int t = 0; t < 10; t++)
            Assert.DoesNotContain(world.Step(new WaitAction()), e => e is ActorAttacked a && a.TargetId == world.Player.Id);
    }

    [Fact]
    public void MineLevelsHaveTimberPropsAndDeeperOnesDoNot()
    {
        var world = World.Create(11, depth: 1);
        var bottom = World.Create(11, depth: world.History.MineDepth + 3);

        Assert.NotEmpty(CellsOf(world, Tile.Pillar));
        // Only the tower has pillars below the mine, and only on floor it has rebuilt.
        Assert.All(CellsOf(bottom, Tile.Pillar), p => Assert.NotNull(bottom.Map.ZoneAt(p)));
    }

    [Fact]
    public void NoLevelIsCutInTwoByItsPast()
    {
        foreach (var seed in Seeds)
        {
            var history = World.Create(seed).History;
            foreach (int depth in new[] { 1, history.FallDepth, history.MineDepth })
            {
                var world = World.Create(seed, depth: depth);
                var walk = new DistanceMap(world.Map.Walkable);
                walk.Update([world.Player.Position]);
                Assert.All(world.Map.Tiles.Positions().Where(p => world.Map.Walkable[p]),
                    p => Assert.True(walk[p] is not null, $"seed {seed} depth {depth}: {p} is cut off"));
                Assert.All(world.Actors, a => Assert.True(world.Map.Walkable[a.Position]));
            }
        }
    }

    [Fact]
    public void TheLayoutOfALevelDoesNotDependOnThePlay()
    {
        static List<Tile> Tiles(World w) => w.Map.Tiles.Positions().Select(p => w.Map.Tiles[p]).ToList();
        static World DescendAfter(int turns)
        {
            var world = World.Create(13);
            world.Player.Hp = 10_000;
            for (int t = 0; t < turns; t++)
                world.Step(new WaitAction());
            world.Player.Position = world.Map.StairsDown!.Value;
            world.Step(new DescendAction());
            return world;
        }

        Assert.Equal(Tiles(DescendAfter(0)), Tiles(DescendAfter(25)));
    }

    [Fact]
    public void AFallNoTribeCanFollowIsAContentError()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rl-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
            var path = Path.Combine(dir, "history.json");
            File.WriteAllText(path, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), "\"war\": \\d", "\"war\": 0"));

            var e = Assert.Throws<InvalidDataException>(() => ContentDatabase.Load(dir));
            Assert.Contains("War", e.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("\"mineDepth\": [3, 5]", "\"mineDepth\": [3, 10]", "mineDepth")]
    [InlineData("\"squad\": \"skeleton patrol\"", "\"squad\": \"nobody\"", "nobody")]
    [InlineData("{ \"group\": \"camp reserve\", \"monster\": \"goblin_shaman\" }", "{ \"group\": \"camp reserve\", \"monster\": \"goblin_runner\" }", "goblin_runner")]
    [InlineData("\"group\": \"front patrol\",  \"monster\": \"goblin_archer\",  \"at\": [41, 11]", "\"group\": \"nobody\",  \"monster\": \"goblin_archer\",  \"at\": [41, 11]", "nobody")]
    [InlineData("\"at\": [41, 11]", "\"at\": [42, 11]", "free walkable")]
    [InlineData("{ \"item\": \"long_bow\",   \"weight\": 1 }", "{ \"item\": \"healing_potion\", \"weight\": 1 }", "healing_potion")]
    public void ABrokenHistoryIsAContentError(string find, string replace, string expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rl-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
            var path = Path.Combine(dir, "history.json");
            var text = File.ReadAllText(path);
            Assert.Contains(find, text);
            File.WriteAllText(path, text.Replace(find, replace));

            var e = Assert.Throws<InvalidDataException>(() => ContentDatabase.Load(dir));
            Assert.Contains(expected, e.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
