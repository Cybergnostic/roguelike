using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.Content;
using Roguelike.Core.Events;
using Roguelike.Core.Map;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class SurfaceTests
{
    private static readonly Point Gate = new(43, 10);
    private static readonly Point Cave = new(72, 7);
    private static readonly Point InsideCamp = new(43, 6);

    private static World Valley(ulong seed = 1) => World.Create(seed, depth: World.SurfaceDepth);

    /// <summary>Walking distances from a cell, treating some cells as if they were blocked.</summary>
    private static DistanceMap WalkFrom(World world, Point from, Func<Point, bool>? blocked = null)
    {
        var open = new ArrayView<bool>(world.Map.Width, world.Map.Height);
        foreach (var p in open.Positions())
            open[p] = world.Map.Walkable[p] && blocked?.Invoke(p) != true;
        var map = new DistanceMap(open);
        map.Update([from]);
        return map;
    }

    private static bool InCamp(World world, Point p) => world.Map.ZoneAt(p) is not null;

    [Fact]
    public void TheValleyFitsTheScreenAndHasItsParts()
    {
        var world = Valley();
        Assert.Equal((80, 24), (world.Map.Width, world.Map.Height));
        Assert.Equal(World.SurfaceDepth, world.Depth);
        Assert.Equal(Tile.Road, world.Map.Tiles[world.Player.Position]);
        Assert.Equal(Tile.GateOpen, world.Map.Tiles[Gate]);
        Assert.Equal(Cave, world.Map.StairsDown);
        Assert.Equal(Tile.CaveMouth, world.Map.Tiles[Cave]);
        Assert.Equal(9, world.Actors.Count(a => a != world.Player));
        Assert.Equal(4, world.Squads.Count);
    }

    [Fact]
    public void TheCaveCanBeReachedWithoutEnteringTheCamp()
    {
        var world = Valley();
        var walk = WalkFrom(world, world.Player.Position, p => InCamp(world, p));
        Assert.NotNull(walk[Cave]);
    }

    [Fact]
    public void WithTheGateShutTheBreachStillLeadsIntoTheCamp()
    {
        var world = Valley();
        world.SetGate(Gate, open: false, world.Player);
        Assert.NotNull(WalkFrom(world, world.Player.Position)[InsideCamp]);

        // Those are the only other ways in: the breach in the west wall and the gap in the back fence.
        var breach = new Point(30, 6);
        var backGap = new Point(56, 4);
        Assert.NotNull(WalkFrom(world, world.Player.Position, p => p == breach)[InsideCamp]);
        Assert.NotNull(WalkFrom(world, world.Player.Position, p => p == backGap)[InsideCamp]);
        Assert.Null(WalkFrom(world, world.Player.Position, p => p == breach || p == backGap)[InsideCamp]);
    }

    [Fact]
    public void WalkingTheRoadPastTheCampGoesUnnoticed()
    {
        var world = Valley();
        void Walk(Direction dir, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                var events = world.Step(new MoveAction(dir));
                Assert.DoesNotContain(events, e => e is SquadSpotted);
                Assert.Equal(Tile.Road, world.Map.Tiles[world.Player.Position]);
            }
        }

        Walk(Direction.Right, 63); // along the south road to the corner
        Walk(Direction.Up, 13);    // north up the east side
        Walk(Direction.Right, 5);  // to the rocks
        world.Step(new MoveAction(Direction.Right));
        Assert.Equal(Cave, world.Player.Position);

        world.Step(new DescendAction());
        Assert.Equal(1, world.Depth);
    }

    [Fact]
    public void TheCaveJoinsTheDungeonAndTheValleyRemembers()
    {
        var world = Valley();
        var valley = world.Map;
        var sentry = world.Squads[0].Members[0];
        world.Damage(world.Player, sentry, 1000, ranged: false);
        world.SetGate(Gate, open: false, world.Player);
        int monsters = world.Actors.Count(a => !a.IsDead && a != world.Player);

        world.Player.Position = Cave;
        world.Step(new DescendAction());
        Assert.Equal(1, world.Depth);
        Assert.NotNull(world.Map.StairsUp);

        world.Player.Position = world.Map.StairsUp!.Value;
        var events = world.Step(new AscendAction());

        Assert.Contains(events, e => e is LevelEntered { Depth: World.SurfaceDepth, GoingUp: true });
        Assert.Same(valley, world.Map);
        Assert.Equal(Cave, world.Player.Position);
        Assert.True(sentry.IsDead);
        Assert.Equal(monsters, world.Actors.Count(a => !a.IsDead && a != world.Player));
        Assert.Equal(Tile.GateClosed, world.Map.Tiles[Gate]);
    }

    [Fact]
    public void TheSameSeedGivesTheSameValley()
    {
        static List<string> Loot(World w) => w.FloorItems.Select(f => $"{f.Item.Def.Id} {f.Item.Enchantment} {f.Item.Rarity} {f.Position}").ToList();
        Assert.NotEmpty(Enumerable.Range(0, 20).SelectMany(s => Loot(Valley((ulong)s))));
        Assert.Equal(Loot(Valley(5)), Loot(Valley(5)));
    }

    [Fact]
    public void BrushCanBeWalkedThroughButNotSeenThrough()
    {
        var world = Valley();
        var brush = new Point(25, 6);
        Assert.Equal(Tile.Brush, world.Map.Tiles[brush]);
        Assert.True(world.Map.Walkable[brush]);
        Assert.False(world.Map.Transparent[brush]);
        Assert.False(world.HasLineOfSight(brush + Direction.Left, brush + Direction.Right));
    }

    [Fact]
    public void AGuardCantSeeAnIntruderBehindBrush()
    {
        var world = Valley();
        world.Player.Hp = 10_000;
        var guard = world.Squads[0].Members[0];

        // Brush at x=36..40 on row 14: one on each side of it.
        guard.Position = new Point(41, 14);
        world.Player.Position = new Point(35, 14);
        Assert.False(world.CanSee(guard, world.Player));

        world.Player.Position = new Point(41, 18); // nothing in the way on open ground
        Assert.True(world.CanSee(guard, world.Player));
    }

    [Fact]
    public void WalkingIntoAClosedGateOpensItAndUsesTheTurn()
    {
        var world = Valley();
        world.Player.Hp = 10_000;
        world.SetGate(Gate, open: false, world.Player);
        world.Player.Position = Gate + Direction.Down;
        int turn = world.Turn;

        var events = world.Step(new MoveAction(Direction.Up));

        Assert.Equal(Tile.GateOpen, world.Map.Tiles[Gate]);
        Assert.Equal(Gate + Direction.Down, world.Player.Position);
        Assert.Equal(turn + 1, world.Turn);
        Assert.Contains(events, e => e is GateMoved { Open: true });
    }

    [Fact]
    public void AClosedGateBlocksMovementAndSight()
    {
        var world = Valley();
        var (above, below) = (Gate + Direction.Up, Gate + Direction.Down);
        Assert.True(world.HasLineOfSight(above, below));

        world.Player.Position = below;
        world.Step(new CloseGateAction());

        Assert.Equal(Tile.GateClosed, world.Map.Tiles[Gate]);
        Assert.False(world.Map.Walkable[Gate]);
        Assert.False(world.HasLineOfSight(above, below));
    }

    [Fact]
    public void AGateWithSomeoneInItWontClose()
    {
        var world = Valley();
        world.Squads[0].Members[0].Position = Gate;
        world.Player.Position = Gate + Direction.Down;

        var events = world.Step(new CloseGateAction());

        Assert.Equal(Tile.GateOpen, world.Map.Tiles[Gate]);
        Assert.Contains(events, e => e is ActionFailed { Reason: FailureReason.NoGateToClose });
    }

    [Fact]
    public void SurfaceMonstersAreAsToughAsTheFirstLevel()
    {
        var content = ContentDatabase.Default;
        var goblin = Valley().Squads[0].Members[0];
        Assert.Equal(content.Dungeon.HpFor(content.Monsters["goblin_warrior"].MaxHp, 1), goblin.MaxHp);
    }

    [Theory]
    [InlineData("\"start\": [3, 20]", "\"start\": [0, 0]", "start")]
    [InlineData("\"monster\": \"goblin_shaman\"", "\"monster\": \"goblin_shamen\"", "goblin_shamen")]
    [InlineData("\"table\": \"floor\", \"at\": [50, 4]", "\"table\": \"flor\", \"at\": [50, 4]", "flor")]
    [InlineData("\">\": \"cavemouth\"", "\">\": \"road\"", "cave mouth")]
    public void ABrokenSurfaceFileIsAContentError(string find, string replace, string expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rl-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
            var path = Path.Combine(dir, "surface.json");
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
