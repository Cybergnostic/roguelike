using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Progression;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class CharacterTests
{
    private static Character CharacterOf(World world) => world.Player.Character!;

    /// <summary>Puts a monster next to the player, out of everyone else's way.</summary>
    private static Actor Adjacent(World world, string kind)
    {
        var at = World.EightWay.Select(d => world.Player.Position + d).First(world.IsFree);
        return world.SpawnMonster(kind, at);
    }

    [Theory]
    [InlineData(9, -1)]
    [InlineData(10, 0)]
    [InlineData(11, 0)]
    [InlineData(12, 1)]
    [InlineData(17, 3)]
    [InlineData(7, -2)]
    public void ModifiersFollowTheDandDTable(int score, int modifier) =>
        Assert.Equal(modifier, Character.Modifier(score));

    [Fact]
    public void StartingAttributesFeedTheDerivedStats()
    {
        var world = World.Create(1);
        var player = world.Player;
        var def = world.Content.Player;
        var c = CharacterOf(world);

        Assert.Equal(12, c.Score("str"));
        Assert.Equal(def.MaxHp + 3 * c.ModifierOf("con"), player.MaxHp);
        Assert.Equal(player.MaxHp, player.Hp);
        Assert.Equal(def.Attack + player.Equipment!.Weapon!.AttackBonus + c.ModifierOf("str"), player.Attack);
        Assert.Equal(def.Attack + player.Equipment.Bow!.AttackBonus + player.Bonus(Stat.RangedAttack), player.RangedAttack);
        Assert.Equal(c.ModifierOf("dex"), player.Bonus(Stat.Accuracy));
    }

    [Fact]
    public void KillingAMonsterEarnsItsXp()
    {
        var world = World.Create(1);
        var goblin = Adjacent(world, "goblin_warrior");
        int before = CharacterOf(world).Xp;

        world.Damage(world.Player, goblin, 1000, ranged: false);

        Assert.Equal(before + world.Content.Monsters["goblin_warrior"].Xp, CharacterOf(world).Xp);
    }

    [Fact]
    public void OthersKillsEarnNothing()
    {
        var world = World.Create(1);
        var goblin = Adjacent(world, "goblin_warrior");
        var skeleton = world.Actors.First(a => a.Kind == "skeleton_warrior");
        int before = CharacterOf(world).Xp;

        world.Damage(skeleton, goblin, 1000, ranged: false);

        Assert.Equal(before, CharacterOf(world).Xp);
    }

    [Fact]
    public void SeeingANewKindOfMonsterTeachesYouOnce()
    {
        var world = World.Create(1);
        world.Player.Hp = 10_000;
        var first = Adjacent(world, "goblin_shaman");
        var events = world.Step(new WaitAction());
        var sighting = Assert.Single(events.OfType<XpGained>(), x => x.Source == XpSource.Sighting && x.Subject == "goblin_shaman");
        Assert.Equal((int)(world.Content.Monsters["goblin_shaman"].Xp * world.Content.Xp.SightFraction), sighting.Amount);

        world.Damage(world.Player, first, 1000, ranged: false);
        Adjacent(world, "goblin_shaman");
        Assert.DoesNotContain(world.Step(new WaitAction()), e => e is XpGained { Source: XpSource.Sighting, Subject: "goblin_shaman" });
    }

    [Fact]
    public void RaisingConstitutionAddsHitPointsWithoutHealingWounds()
    {
        var world = World.Create(1);
        var player = world.Player;
        var c = CharacterOf(world);
        c.Gain(1000);
        player.Hp = 10;
        int maxBefore = player.MaxHp, turn = world.Turn;

        // 12 -> 13 keeps the modifier; 13 -> 14 raises it.
        world.Step(new RaiseAttributeAction("con"));
        Assert.Equal(maxBefore, player.MaxHp);
        var events = world.Step(new RaiseAttributeAction("con"));

        Assert.Contains(events, e => e is AttributeRaised { AttributeId: "con", NewScore: 14 });
        Assert.Equal(maxBefore + 3, player.MaxHp);
        Assert.Equal(13, player.Hp);
        Assert.Equal(turn, world.Turn); // spending XP is free
    }

    [Fact]
    public void EachAttributePointCostsMoreThanTheLast()
    {
        var world = World.Create(1);
        var c = CharacterOf(world);
        var rules = world.Content.AttributeRaise;
        c.Gain(rules.BaseCost + rules.BaseCost + rules.CostPerRaise - 1);

        Assert.Equal(rules.BaseCost, c.RaiseCost);
        world.Step(new RaiseAttributeAction("str"));
        Assert.Equal(rules.BaseCost + rules.CostPerRaise, c.RaiseCost);

        var events = world.Step(new RaiseAttributeAction("str"));
        Assert.Contains(events, e => e is ActionFailed { Reason: FailureReason.NotEnoughXp });
        Assert.Equal(13, c.Score("str"));
    }

    [Fact]
    public void SkillsNeedTheirPrerequisitesAndStopAtMaxRank()
    {
        var world = World.Create(1);
        var c = CharacterOf(world);
        c.Gain(10_000);

        Assert.Contains(world.Step(new LearnSkillAction("cleave")), e => e is ActionFailed { Reason: FailureReason.MissingPrerequisite });
        int attack = world.Player.Attack;
        for (int i = 0; i < 3; i++)
            Assert.Contains(world.Step(new LearnSkillAction("weapon_training")), e => e is SkillLearned { NewRank: var r } && r == i + 1);
        Assert.Equal(attack + 3, world.Player.Attack);
        Assert.Contains(world.Step(new LearnSkillAction("weapon_training")), e => e is ActionFailed { Reason: FailureReason.SkillMastered });
        Assert.Contains(world.Step(new LearnSkillAction("cleave")), e => e is SkillLearned { SkillId: "cleave" });
    }

    [Fact]
    public void FractionalBonusesAddUpBeforeRounding()
    {
        var world = World.Create(1);
        var c = CharacterOf(world);
        c.Gain(10_000);
        int ranged = world.Player.RangedAttack; // Dex 12: +1 modifier, 0.5 ranged attack, rounds to 0
        world.Step(new RaiseAttributeAction("dex"));
        world.Step(new RaiseAttributeAction("dex")); // Dex 14: +2, 1 ranged attack
        Assert.Equal(ranged + 1, world.Player.RangedAttack);
    }

    [Fact]
    public void IntelligenceMeansMoreXp()
    {
        var world = World.Create(1);
        var c = CharacterOf(world);
        c.Gain(10_000);
        for (int i = 0; i < 4; i++)
            world.Step(new RaiseAttributeAction("int")); // 14: +2 modifier, +20%
        int before = c.Xp;
        world.GrantXp(XpSource.Kill, "test", 50);
        Assert.Equal(before + 60, c.Xp);
    }

    [Fact]
    public void CleaveCarriesAKillingBlowIntoTheNextFoe()
    {
        var world = World.Create(1);
        world.Player.Character!.Gain(10_000);
        world.Step(new LearnSkillAction("weapon_training"));
        world.Step(new LearnSkillAction("cleave"));
        var spots = World.EightWay.Select(d => world.Player.Position + d).Where(world.IsFree).Take(2).ToList();
        var doomed = world.SpawnMonster("goblin_runner", spots[0]);
        var next = world.SpawnMonster("goblin_warrior", spots[1]);
        doomed.Hp = 1;
        next.Hp = 1_000;
        world.Player.Hp = 10_000;

        // The runner has no defence and 1 HP: any hit kills, so the blow carries on once one lands.
        var events = new List<Roguelike.Core.Events.GameEvent>();
        for (int swing = 0; swing < 100 && !doomed.IsDead; swing++)
            events.AddRange(world.Step(new AttackAction(doomed.Id)));

        Assert.True(doomed.IsDead);
        Assert.Contains(events, e => e is SpecialUsed { Special: "cleave" } u && u.TargetId == next.Id);
        Assert.Contains(events, e => e is ActorAttacked a && a.AttackerId == world.Player.Id && a.TargetId == next.Id);
    }

    [Fact]
    public void ASkillGrantingAnUnknownSpecialIsAContentError() =>
        AssertContentError("skills.json", "\"special\": \"cleave\"", "\"special\": \"whirlwind\"", "whirlwind");

    [Fact]
    public void AnUnknownStatIsAContentError() =>
        AssertContentError("attributes.json", "\"meleeAttack\": 1", "\"meleeAtack\": 1", "meleeAtack");

    [Fact]
    public void ASkillRequiringAnUnknownSkillIsAContentError() =>
        AssertContentError("skills.json", "\"requires\": [\"steady_aim\"]", "\"requires\": [\"steady_aim2\"]", "steady_aim2");

    private static void AssertContentError(string file, string find, string replace, string expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rl-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
            var path = Path.Combine(dir, file);
            var text = File.ReadAllText(path);
            Assert.Contains(find, text);
            File.WriteAllText(path, text.Replace(find, replace));

            var e = Assert.Throws<InvalidDataException>(() => ContentDatabase.Load(dir));
            Assert.Contains(expected, e.Message);
            Assert.Contains(file, e.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
