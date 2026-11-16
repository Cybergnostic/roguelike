using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Progression;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

/// <summary>The Mystic's five skills (COMBAT_AND_MAGIC.md §3).</summary>
public class MysticTests
{
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

    private static void Learn(World world, string skill, int rank)
    {
        while (world.Player.Character!.Rank(skill) < rank)
            Assert.Contains(world.Step(new LearnSkillAction(skill)), e => e is SkillLearned);
    }

    private static Actor Foe(World world, int distance, string kind = "goblin_warrior")
    {
        var at = world.Map.Tiles.Positions().First(p => world.IsFree(p) && World.Distance(p, world.Player.Position) == distance
                                                        && world.HasClearShot(world.Player.Position, p));
        var foe = world.SpawnMonster(kind, at);
        foe.Hp = 100_000;
        var squad = world.SquadOf(foe)!;
        squad.State = SquadState.Engage;
        squad.TargetId = world.Player.Id;
        return foe;
    }

    private static int Dealt(World world, string ability, Actor foe)
    {
        foe.StaggeredUntil = world.Turn + 1; // can't miss
        int hp = foe.Hp;
        world.Step(new UseAbilityAction(ability, foe.Id));
        return hp - foe.Hp;
    }

    [Fact]
    public void TheWordGoesThroughArmour()
    {
        var world = Mystic();
        var knight = Foe(world, 4, "bone_knight");
        var word = world.Content.Abilities["word_of_power"];
        int power = word.Power + world.Player.Bonus(Stat.Magic);
        int dealt = Dealt(world, "word_of_power", knight);
        // Staggered (x1.5), +-20%, and no armour taken off.
        Assert.InRange(dealt, (int)(power * 0.8 * 1.5) - 1, (int)(power * 1.2 * 1.5) + 1);
    }

    [Fact]
    public void AtRankThreeTheWordHitsTheDeadTwiceAsHard()
    {
        var world = Mystic();
        Learn(world, "word_of_power", 3);
        var skeleton = Foe(world, 4, "skeleton_warrior");
        int power = world.Content.Abilities["word_of_power"].Power + world.Player.Bonus(Stat.Magic);
        int dealt = Dealt(world, "word_of_power", skeleton);
        Assert.True(dealt >= (int)(power * 2 * 0.8 * 1.5) - 1, $"{dealt} vs power {power}");
    }

    [Fact]
    public void AHexedFoeTakesMoreFromEveryone()
    {
        var world = Mystic();
        Learn(world, "hex", 1);
        var foe = Foe(world, 3);
        world.Step(new UseAbilityAction("hex", foe.Id));
        Assert.Equal(25, foe.StatusOf(StatusKind.Hexed)!.Amount);
    }

    [Fact]
    public void AtRankTwoTheHexLeapsWhenItsVictimDies()
    {
        var world = Mystic();
        Learn(world, "hex", 2);
        var foe = Foe(world, 3);
        var friend = world.SpawnMonster("goblin_warrior", World.EightWay.Select(d => foe.Position + d).First(world.IsFree), world.SquadOf(foe));
        world.Step(new UseAbilityAction("hex", foe.Id));
        world.Damage(world.Player, foe, foe.Hp, ranged: false);
        Assert.True(friend.Has(StatusKind.Hexed));
    }

    [Fact]
    public void TheSpiritBladeStrikesHarderAndThroughArmour()
    {
        var world = Mystic();
        Learn(world, "spirit_blade", 1);
        world.Step(new UseAbilityAction("spirit_blade"));
        Assert.Equal(3, world.Player.StatusOf(StatusKind.SpiritBlade)!.Amount);
    }

    [Fact]
    public void AtRankTwoAegisTurnsArrows()
    {
        var world = Mystic();
        Learn(world, "aegis", 2);
        world.Step(new UseAbilityAction("aegis"));
        var archer = Foe(world, 3, "goblin_archer");
        int hp = world.Player.Hp;
        world.ResolveAttack(archer, world.Player, ranged: true);
        Assert.Equal(hp, world.Player.Hp);
        Assert.Equal(10 + world.Player.Bonus(Stat.Magic), world.Player.StatusOf(StatusKind.Shielded)!.Amount);
    }

    [Fact]
    public void RuneSightReadsRunesAndCarvesFast()
    {
        var player = World.Create(1, classId: "mystic").Player;
        Assert.True(player.HasSpecial("rune_reading"));
        Assert.Equal(300, World.CarveTime(player, RuneSurface.Ground));
    }

    [Fact]
    public void AtRankThreeTheWordCanBeSpokenThroughAMark()
    {
        var world = Mystic();
        Learn(world, "rune_sight", 3);
        var player = world.Player;
        world.Step(new CarveAction("algiz", RuneSurface.Ground));
        var mark = player.Position;
        // Walk away from the mark, keeping it in sight, then aim at someone near the mark but out of the word's reach.
        var word = world.Content.Abilities["word_of_power"];
        var fov = world.Map.PlayerFov.BooleanResultView;
        var spot = world.Map.Tiles.Positions().Cast<Point?>().FirstOrDefault(p => world.IsFree(p!.Value) && World.Distance(p.Value, mark) is >= 3 and <= 5 && world.HasLineOfSight(p.Value, mark));
        if (spot is not { } stand)
            return;
        player.Position = stand;
        world.Step(new WaitAction());
        var target = world.Map.Tiles.Positions().Cast<Point?>().FirstOrDefault(p => world.IsFree(p!.Value) && World.Distance(p.Value, player.Position) > word.Range
                                                                  && World.Distance(p.Value, mark) <= word.Range && world.HasClearShot(mark, p.Value));
        if (target is not { } far)
            return;
        var foe = world.SpawnMonster("goblin_warrior", far);
        Assert.Equal(mark, world.WordOrigin(player, foe, word));
    }
}
