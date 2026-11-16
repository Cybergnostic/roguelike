using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Items;
using Roguelike.Core.Progression;
using Roguelike.Core.Simulation;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class ClassTests
{
    private static World Started(string classId, ulong seed = 1)
    {
        var world = World.Create(seed, classId: classId);
        foreach (var other in world.Actors.Where(a => a != world.Player))
            other.Hp = 0;
        world.Step(new WaitAction());
        world.Player.Hp = 100_000;
        return world;
    }

    private static Actor FoeAt(World world, int distance, string kind = "goblin_warrior")
    {
        var at = world.Map.Tiles.Positions().First(p => world.IsFree(p) && World.Distance(p, world.Player.Position) == distance && world.HasClearShot(world.Player.Position, p));
        var foe = world.SpawnMonster(kind, at);
        foe.Hp = 100_000;
        return foe;
    }

    private static void Learn(World world, string skill)
    {
        world.GrantXp(XpSource.Kill, "test", 2000);
        world.Step(new LearnSkillAction(skill));
        Assert.True(world.Player.Character!.Rank(skill) > 0, $"couldn't learn {skill}");
    }

    public static TheoryData<string> AllClasses => new() { "warrior", "ranger", "shadow", "mystic" };

    [Theory, MemberData(nameof(AllClasses))]
    public void EachClassStartsWithItsOwnKitAndKnowledge(string id)
    {
        var world = World.Create(1, classId: id);
        var def = world.Content.Classes.Single(c => c.Id == id);
        var player = world.Player;

        Assert.Equal(def.Gold, player.Gold);
        Assert.Equal(def, player.Character!.Class);
        Assert.All(def.Starts, s => Assert.Equal(1, player.Character.Rank(s)));
        foreach (var k in def.Kit.Where(k => k.Equip))
            Assert.Contains(player.Equipment!.All, i => i.Def.Id == k.Item);
        foreach (var a in def.Attributes)
            Assert.Equal(a.Value, player.Character.Score(a.Key));
        Assert.Equal(player.MaxHp, player.Hp);
    }

    [Fact]
    public void TheClassesReallyDiffer()
    {
        var soldier = World.Create(1, classId: "warrior").Player;
        var ranger = World.Create(1, classId: "ranger").Player;
        var rogue = World.Create(1, classId: "shadow").Player;
        var mystic = World.Create(1, classId: "mystic").Player;

        Assert.True(soldier.MaxHp > mystic.MaxHp);
        Assert.True(soldier.Defense > rogue.Defense);
        Assert.NotNull(ranger.Equipment!.Bow);
        Assert.NotNull(rogue.Equipment!.OffHand);
        Assert.Null(mystic.Equipment!.Bow);
        Assert.True(mystic.Bonus(Stat.Magic) > soldier.Bonus(Stat.Magic));
    }

    [Fact]
    public void AClassCantLearnAnotherClassesSkills()
    {
        var world = Started("warrior");
        world.GrantXp(XpSource.Kill, "test", 2000);

        var events = world.Step(new LearnSkillAction("escape_artist"));

        Assert.Contains(events, e => e is ActionFailed { Reason: FailureReason.NotYourClass });
        Assert.Equal(0, world.Player.Character!.Rank("escape_artist"));
    }

    [Fact]
    public void TheClasslessLearnOnlyGeneralSkills()
    {
        var world = World.Create(1);
        world.GrantXp(XpSource.Kill, "test", 2000);
        Assert.Contains(world.Step(new LearnSkillAction("mend")), e => e is ActionFailed { Reason: FailureReason.NotYourClass });
        world.Step(new LearnSkillAction("toughness"));
        Assert.Equal(1, world.Player.Character!.Rank("toughness"));
    }

    [Fact]
    public void YouCantUseWhatYouDontKnow()
    {
        var world = Started("warrior");
        var foe = FoeAt(world, 3);
        Assert.Contains(world.Step(new UseAbilityAction("cinder_bolt", foe.Id)), e => e is ActionFailed { Reason: FailureReason.AbilityUnknown });
    }

    [Fact]
    public void SkillsHaveCooldowns()
    {
        var world = Started("mystic");
        var foe = FoeAt(world, 4);
        var bolt = world.Content.Abilities["cinder_bolt"];

        Assert.Contains(world.Step(new UseAbilityAction("cinder_bolt", foe.Id)), e => e is AbilityUsed);
        Assert.True(world.CooldownLeft(bolt) > 0);
        Assert.Contains(world.Step(new UseAbilityAction("cinder_bolt", foe.Id)), e => e is ActionFailed { Reason: FailureReason.AbilityNotReady });
    }

    [Fact]
    public void AimedSkillsNeedATargetInReach()
    {
        var world = Started("mystic");
        Assert.Contains(world.Step(new UseAbilityAction("cinder_bolt")), e => e is ActionFailed { Reason: FailureReason.NoTarget });
        var at = world.Map.Tiles.Positions().First(p => world.IsFree(p) && World.Distance(p, world.Player.Position) >= 8);
        var far = world.SpawnMonster("goblin_warrior", at);
        Assert.Contains(world.Step(new UseAbilityAction("cinder_bolt", far.Id)), e => e is ActionFailed { Reason: FailureReason.OutOfRange });
    }

    [Fact]
    public void MagicMakesTheBoltStronger()
    {
        var world = Started("mystic");
        var foe = FoeAt(world, 4);
        int before = world.Player.Character!.Rank("cinder_bolt");
        int magic = world.Player.Bonus(Stat.Magic);

        Learn(world, "cinder_bolt");

        Assert.Equal(before + 1, world.Player.Character!.Rank("cinder_bolt"));
        Assert.Equal(magic + 1, world.Player.Bonus(Stat.Magic));
    }

    [Fact]
    public void MendHeals()
    {
        var world = Started("mystic");
        Learn(world, "mend");
        world.Player.Hp = 5;

        world.Step(new UseAbilityAction("mend"));

        Assert.True(world.Player.Hp > 5);
    }

    [Fact]
    public void AVolleyLoosesTwoArrows()
    {
        var world = Started("ranger");
        var foe = FoeAt(world, 4);
        var events = world.Step(new UseAbilityAction("hunters_volley", foe.Id));
        Assert.Equal(2, events.OfType<ActorAttacked>().Count(a => a.AttackerId == world.Player.Id));
    }

    [Fact]
    public void APinningShotAlwaysStaggers()
    {
        var world = Started("ranger");
        Learn(world, "pinning_shot");
        var foe = FoeAt(world, 4);
        Assert.Contains(world.Step(new UseAbilityAction("pinning_shot", foe.Id)), e => e is ActorStaggered s && s.ActorId == foe.Id);
    }

    [Fact]
    public void BackstabDoublesDamageOnTheUnaware()
    {
        var world = Started("shadow");
        var foe = FoeAt(world, 1);
        world.SquadOf(foe)!.State = SquadState.Idle;

        var events = world.Step(new AttackAction(foe.Id));

        Assert.Contains(events, e => e is SpecialUsed { Special: "backstab" });
    }

    [Fact]
    public void NoBackstabOnSomeoneAlreadyFighting()
    {
        var world = Started("shadow");
        var foe = FoeAt(world, 1);
        world.SquadOf(foe)!.State = SquadState.Engage;
        Assert.DoesNotContain(world.Step(new AttackAction(foe.Id)), e => e is SpecialUsed { Special: "backstab" });
    }

    [Fact]
    public void VanishingGetsYouOutOfSight()
    {
        var world = Started("shadow");
        Learn(world, "escape_artist");
        var foe = FoeAt(world, 2);
        var squad = world.SquadOf(foe)!;
        world.Step(new WaitAction());
        Assert.True(squad.State == SquadState.Engage || squad.Surrendered || squad.State == SquadState.Flee);
        var from = world.Player.Position;

        world.Step(new UseAbilityAction("vanish"));

        Assert.NotEqual(from, world.Player.Position);
        Assert.True(world.Player.HiddenUntil > world.Turn - 1);
        if (!world.IsAdjacent(foe.Position, world.Player.Position))
            Assert.False(world.CanSee(foe, world.Player));
    }

    [Fact]
    public void AWarCryShakesNearbySquads()
    {
        var world = Started("warrior");
        world.GrantXp(XpSource.Kill, "test", 2000);
        for (int i = 0; i < 3; i++)
            world.Step(new LearnSkillAction("battle_momentum")); // War Cry comes at rank 3
        var foe = FoeAt(world, 4);
        var squad = world.SquadOf(foe)!;
        int morale = squad.Morale;

        world.Step(new UseAbilityAction("war_cry"));

        Assert.True(squad.Morale < morale);
    }

    [Fact]
    public void AShoveIntoAWallStaggers()
    {
        var world = Started("warrior");
        var player = world.Player;
        // Stand with a wall right behind the foe.
        var (spot, dir) = world.Map.Tiles.Positions().Where(world.IsFree)
            .SelectMany(p => World.EightWay.Select(d => (p, d)))
            .First(x => world.IsFree(x.p + x.d) && world.Map.InBounds(x.p + x.d + x.d) && !world.Map.Walkable[x.p + x.d + x.d]);
        player.Position = spot;
        var foe = world.SpawnMonster("goblin_warrior", spot + dir);
        foe.Hp = 100_000;
        Assert.Contains(world.Step(new UseAbilityAction("shove", foe.Id)), e => e is ActorStaggered s && s.ActorId == foe.Id);
        Assert.Equal(spot + dir, foe.Position);
    }

    [Fact]
    public void AShoveIntoTheOpenPushesThemBack()
    {
        var world = Started("warrior");
        var player = world.Player;
        var (spot, dir) = world.Map.Tiles.Positions().Where(world.IsFree)
            .SelectMany(p => World.EightWay.Select(d => (p, d)))
            .First(x => world.IsFree(x.p + x.d) && world.IsFree(x.p + x.d + x.d) && world.IsFree(x.p + x.d + x.d + x.d));
        player.Position = spot;
        var foe = world.SpawnMonster("goblin_warrior", spot + dir);
        foe.Hp = 100_000;
        var events = world.Step(new UseAbilityAction("shove", foe.Id));
        // (It may come straight back in on its own turn.)
        Assert.Contains(events, e => e is ActorMoved m && m.ActorId == foe.Id && m.From == spot + dir && m.To == spot + dir + dir);
    }

    [Fact]
    public void HoldTheLineBracesInAnyArmour()
    {
        var world = Started("warrior");
        Learn(world, "hold_the_line");
        world.Step(new WaitAction());
        Assert.True(world.Player.BraceBonus >= 1);
        Assert.True(world.Player.CounterChance >= 15);
    }

    [Fact]
    public void SkirmishersIgnoreThePointBlankPenalty()
    {
        var world = Started("ranger");
        var foe = FoeAt(world, 1);
        int before = world.HitChance(world.Player, foe, ranged: true);
        Learn(world, "skirmishing");
        Learn(world, "skirmishing"); // rank 2
        Assert.True(world.HitChance(world.Player, foe, ranged: true) > before);
    }

    [Theory, MemberData(nameof(AllClasses))]
    public void TheBotCanPlayEachClass(string id)
    {
        var results = Enumerable.Range(0, 3).Select(s => Simulator.Run((ulong)s, new TacticalBot(), 2500, classId: id)).ToList();
        Assert.Contains(results, r => r.DepthReached >= 1);
    }

    [Theory, MemberData(nameof(AllClasses))]
    public void AClassPlaysTheSameEveryTime(string id)
    {
        static List<string> Play(string classId)
        {
            var world = World.Create(5, classId: classId);
            var bot = new TacticalBot();
            var log = new List<string>();
            for (int i = 0; i < 300 && !world.IsOver; i++)
                log.AddRange(world.Step(bot.Decide(world)).Select(e => e.ToString()!));
            return log;
        }
        Assert.Equal(Play(id), Play(id));
    }

    [Theory]
    [InlineData("\"trees\": [\"warrior\", \"survival\", \"senses\", \"runes\"]", "\"trees\": [\"warrior\", \"nonsense\", \"senses\", \"runes\"]", "nonsense")]
    [InlineData("\"starts\": [\"breakthrough\"]", "\"starts\": [\"mend\"]", "mend")]
    [InlineData("{ \"item\": \"robes\",", "{ \"item\": \"nothing\",", "nothing")]
    public void ABrokenClassIsAContentError(string find, string replace, string expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rl-content-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json"))
                File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
            var path = Path.Combine(dir, "classes.json");
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
