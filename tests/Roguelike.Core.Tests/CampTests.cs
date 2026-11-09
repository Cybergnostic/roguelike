using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core.Tests;

public class CampTests
{
    private static readonly Point Gate = new(43, 10);
    private static readonly Point OutsideGate = new(43, 14);
    private static readonly Point FarAway = new(3, 20);

    private static World Valley(ulong seed = 1)
    {
        var world = HistoryTests.ValleyHeldBy("tollkeepers", seed);
        world.Player.Hp = 10_000; // these tests are about behaviour, not survival
        return world;
    }

    private static Squad Sentries(World w) => w.Squads[0];
    private static Squad Reserve(World w) => w.Squads[1];
    private static Squad FrontPatrol(World w) => w.Squads[2];
    private static Camp CampOf(World w) => w.Camps[0];

    private static List<GameEvent> Run(World world, int turns)
    {
        var events = new List<GameEvent>();
        for (int t = 0; t < turns; t++)
            events.AddRange(world.Step(new WaitAction()));
        return events;
    }

    [Fact]
    public void GuardsLeaveATravellerOutsideTheirGroundAlone()
    {
        var world = Valley();
        world.Player.Position = OutsideGate;
        Assert.True(Sentries(world).Members.Any(m => world.CanSee(m, world.Player)), "a guard should be able to see the player");
        int hp = world.Player.Hp;

        var events = Run(world, 25);

        Assert.DoesNotContain(events, e => e is SquadSpotted);
        Assert.Equal(hp, world.Player.Hp);
    }

    [Fact]
    public void SteppingInsideTheCampMakesYouAnEnemy()
    {
        var world = Valley();
        world.Player.Position = Gate + Direction.Up;

        var events = world.Step(new WaitAction());

        Assert.Contains(events, e => e is SquadSpotted s && s.SquadId == Sentries(world).Id);
        Assert.Equal(SquadState.Engage, Sentries(world).State);
        Assert.False(CampOf(world).Provoked); // trespass alone isn't remembered
    }

    [Fact]
    public void LeavingTheCampEndsTheFightForAClearReason()
    {
        var world = Valley();
        world.Player.Position = Gate + Direction.Up;
        world.Step(new WaitAction());
        Assert.Equal(SquadState.Engage, Sentries(world).State);

        world.Player.Position = OutsideGate;
        var events = Run(world, 30);

        Assert.Contains(events, e => e is SquadStoodDown { Reason: StandDownReason.IntruderLeft } s && s.SquadId == Sentries(world).Id);
        Assert.DoesNotContain(events, e => e is ActorAttacked a && a.TargetId == world.Player.Id);
        Assert.Equal(SquadState.Idle, Sentries(world).State);
        Assert.Null(Sentries(world).LastKnownTarget);
    }

    [Fact]
    public void GuardsHoldTheirPosts()
    {
        var world = Valley();
        world.Player.Position = FarAway;
        var guards = Sentries(world).Members.Concat(Reserve(world).Members).ToList();
        var posts = guards.Select(g => g.Position).ToList();

        Run(world, 60);

        Assert.Equal(posts, guards.Select(g => g.Position));
    }

    [Fact]
    public void GuardsGoBackToTheirPostsAfterAChase()
    {
        var world = Valley();
        var guards = Sentries(world).Members.ToList();
        var posts = guards.Select(g => g.Position).ToList();
        world.Player.Position = Gate + Direction.Up;
        world.Step(new WaitAction());
        world.Step(new WaitAction());

        world.Player.Position = FarAway;
        Run(world, 40);

        Assert.Equal(posts, guards.Select(g => g.Position));
    }

    [Fact]
    public void ThePatrolWalksItsRouteOverAndOver()
    {
        var world = Valley();
        world.Player.Position = FarAway;
        var patrol = FrontPatrol(world);
        var route = patrol.Route!;
        var stopsReached = new List<int>();

        for (int t = 0; t < 200; t++)
        {
            world.Step(new WaitAction());
            int stop = patrol.RouteStop;
            if (stopsReached.Count == 0 || stopsReached[^1] != stop)
                stopsReached.Add(stop);
        }

        Assert.True(stopsReached.Count >= 4, $"only {stopsReached.Count} legs walked");
        Assert.Equal(3, route.Count);
    }

    [Fact]
    public void TheFirstBumpWarnsAndTheSecondAttacks()
    {
        var world = Valley();
        var sentry = Sentries(world).Members[0];
        sentry.Position = OutsideGate + Direction.Up;
        world.Player.Position = OutsideGate;
        int turn = world.Turn;

        var first = world.Step(new MoveAction(Direction.Up));

        Assert.Contains(first, e => e is ActionFailed { Reason: FailureReason.NotHostileYet });
        Assert.Equal(turn, world.Turn);
        Assert.False(CampOf(world).Provoked);

        var second = world.Step(new MoveAction(Direction.Up));

        Assert.Equal(turn + 1, world.Turn);
        Assert.Contains(second, e => e is ActorAttacked a && a.AttackerId == world.Player.Id);
        Assert.Contains(second, e => e is CampAlarmed);
        Assert.True(CampOf(world).Provoked);
        Assert.True(world.AreHostile(Reserve(world).Members[0], world.Player)); // the whole camp knows
    }

    [Fact]
    public void WalkingAwayCancelsTheBumpWarning()
    {
        var world = Valley();
        var sentry = Sentries(world).Members[0];
        sentry.Position = OutsideGate + Direction.Up;
        world.Player.Position = OutsideGate;

        world.Step(new MoveAction(Direction.Up));
        world.Step(new WaitAction());
        sentry.Position = OutsideGate + Direction.Up; // back in the way
        var again = world.Step(new MoveAction(Direction.Up));

        Assert.Contains(again, e => e is ActionFailed { Reason: FailureReason.NotHostileYet });
        Assert.False(CampOf(world).Provoked);
    }

    [Fact]
    public void ShootingAGuardProvokesTheCamp()
    {
        var world = Valley();
        var archer = Sentries(world).Members.Single(m => m.Kind == "goblin_archer");
        world.Player.Position = OutsideGate;
        Assert.True(world.HasClearShot(world.Player.Position, archer.Position));

        var events = world.Step(new FireAction(archer.Id));

        Assert.Contains(events, e => e is CampAlarmed);
        Assert.True(CampOf(world).Provoked);
    }

    [Fact]
    public void AnAttackedCampRemembersAfterYouComeBack()
    {
        var world = Valley();
        CampOf(world).Provoked = true;
        world.Player.Position = world.Map.StairsDown!.Value;
        world.Step(new DescendAction());
        world.Player.Position = world.Map.StairsUp!.Value;
        world.Step(new AscendAction());

        world.Player.Position = OutsideGate;
        var events = world.Step(new WaitAction());

        Assert.True(CampOf(world).Provoked);
        Assert.Contains(events, e => e is SquadSpotted s && s.SquadId == Sentries(world).Id);
    }

    [Fact]
    public void AChaseEndsWhenTheEnemyIsBeyondTheirGround()
    {
        var world = Valley();
        CampOf(world).Provoked = true;
        var squad = Sentries(world);
        world.Player.Position = OutsideGate;
        world.Step(new WaitAction());
        Assert.Equal(SquadState.Engage, squad.State);

        // Just past where they'll go, and still in plain sight of the ones who came out.
        var leader = squad.Leader!;
        var beyond = world.Map.Tiles.Positions()
            .Where(p => world.IsFree(p) && !squad.WithinLeash(p) && squad.Members.Any(m => world.CanSee(m, new Probe(p).At(world, m))))
            .OrderBy(p => World.Distance(p, leader.Position)).First();
        world.Player.Position = beyond;
        var events = world.Step(new WaitAction());

        Assert.Contains(events, e => e is SquadStoodDown { Reason: StandDownReason.TooFarFromHome } s && s.SquadId == squad.Id);
        Assert.Equal(SquadState.Idle, squad.State);
    }

    /// <summary>Answers "could this member see that cell" by standing the player there.</summary>
    private sealed record Probe(Point Cell)
    {
        public Actor At(World world, Actor _)
        {
            world.Player.Position = Cell;
            return world.Player;
        }
    }

    [Fact]
    public void ABrokenSquadFallsBackToItsRallyPoint()
    {
        var world = Valley();
        CampOf(world).Provoked = true;
        var squad = Sentries(world);
        world.Player.Position = OutsideGate;
        world.Step(new WaitAction());
        squad.Morale = 0;
        world.Step(new WaitAction());
        Assert.Equal(SquadState.Flee, squad.State);
        squad.Morale = 0;

        for (int t = 0; t < 12; t++)
        {
            squad.Morale = 0;
            world.Step(new WaitAction());
        }

        Assert.All(squad.Members, m => Assert.True(World.Distance(m.Position, squad.Rally!.Value) <= 3, $"{m.Kind} at {m.Position}"));
    }

    [Fact]
    public void GoblinsOpenAClosedGateToGetWhereTheyreGoing()
    {
        var world = Valley();
        world.Player.Position = FarAway;
        world.SetGate(Gate, open: false, world.Player);
        var squad = Sentries(world);
        squad.State = SquadState.Alert;
        squad.LastKnownTarget = squad.Destination = OutsideGate;
        squad.LastSeenTurn = world.Turn;

        var events = Run(world, 12);

        Assert.Contains(events, e => e is GateMoved { Open: true } g && g.ActorId != world.Player.Id);
    }

    [Fact]
    public void AnAttackedCampShutsItsGate()
    {
        var world = Valley();
        world.Player.Position = FarAway;
        CampOf(world).Provoked = true;

        var events = Run(world, 20);

        Assert.Equal(Roguelike.Core.Map.Tile.GateClosed, world.Map.Tiles[Gate]);
        Assert.Contains(events, e => e is GateMoved { Open: false } g && g.ActorId != world.Player.Id);
    }

    [Fact]
    public void AnUnprovokedCampLeavesItsGateOpen()
    {
        var world = Valley();
        world.Player.Position = FarAway;
        Run(world, 20);
        Assert.Equal(Roguelike.Core.Map.Tile.GateOpen, world.Map.Tiles[Gate]);
    }

    [Fact]
    public void SearchersLookAroundWhereTheyLastKnewTheEnemyWas()
    {
        Squad? squad = null;
        World? world = null;
        for (ulong seed = 0; seed < 200 && squad is null; seed++)
        {
            world = World.Create(seed);
            squad = world.Squads.FirstOrDefault(s => !s.IsLeashed && s.Members.All(m => World.Distance(m.Position, world.Player.Position) > 12));
        }
        world!.Player.Hp = 10_000;
        var spot = squad!.Leader!.Position;
        squad.State = SquadState.Alert;
        squad.LastKnownTarget = squad.Destination = spot;
        squad.LastSeenTurn = world.Turn;

        var visited = new HashSet<Point>();
        for (int t = 0; t < 20; t++)
        {
            world.Step(new WaitAction());
            if (squad.State == SquadState.Alert && squad.Destination is { } d)
                visited.Add(d);
        }

        Assert.True(visited.Count >= 2, $"searched only {visited.Count} places");
        Assert.All(visited, p => Assert.True(World.Distance(p, spot) <= 5));
    }

    // ---- The gate: challenge, last warning, toll ----

    private static readonly Point JustOutsideGate = Gate + Direction.Down;
    private static readonly Point JustInsideGate = Gate + Direction.Up;

    private static void KillSquads(World w)
    {
        foreach (var member in w.Squads.SelectMany(s => s.Members))
            member.Hp = 0;
    }

    /// <summary>Puts the player by the gate and pays, returning that turn's events.</summary>
    private static IReadOnlyList<GameEvent> PayAtGate(World world)
    {
        world.Player.Position = JustOutsideGate;
        return world.Step(new PayTollAction());
    }

    [Fact]
    public void AGuardCallsOutTheTermsOnceAsYouApproach()
    {
        var world = Valley();
        world.Player.Position = OutsideGate;

        var events = Run(world, 10);

        var challenge = Assert.Single(events.OfType<GateChallenged>());
        Assert.Equal((20, 150), (challenge.Price, challenge.Turns));
        Assert.True(CampOf(world).TermsKnown);
        Assert.DoesNotContain(events, e => e is ActorAttacked a && a.TargetId == world.Player.Id);
    }

    [Fact]
    public void GuardsAskAgainAfterYouHaveGoneAway()
    {
        var world = Valley();
        world.Player.Position = OutsideGate;
        Run(world, 2);
        world.Player.Position = FarAway;
        Run(world, 2);
        world.Player.Position = OutsideGate;

        Assert.Single(Run(world, 2).OfType<GateChallenged>());
    }

    [Fact]
    public void NobodyChallengesYouWhenNoGuardCanSeeYou()
    {
        var world = Valley();
        KillSquads(world);
        world.Player.Position = OutsideGate;

        Assert.DoesNotContain(Run(world, 3), e => e is GateChallenged);
        Assert.False(CampOf(world).TermsKnown);
    }

    [Fact]
    public void StandingInTheGateIsTheLastWarning()
    {
        var world = Valley();
        world.Player.Position = Gate;

        var events = Run(world, 5);

        Assert.Single(events.OfType<TrespassWarned>());
        Assert.DoesNotContain(events, e => e is SquadSpotted);
        Assert.DoesNotContain(events, e => e is ActorAttacked a && a.TargetId == world.Player.Id);

        var inside = world.Step(new MoveAction(Direction.Up));
        Assert.Contains(inside, e => e is SquadSpotted s && s.TargetId == world.Player.Id);
    }

    [Fact]
    public void PayingBuysSafeTimeInside()
    {
        var world = Valley();
        int gold = world.Player.Gold;
        var paid = PayAtGate(world);

        Assert.Contains(paid, e => e is TollPaid { Price: 20, Turns: 150 });
        Assert.Equal(gold - 20, world.Player.Gold);

        world.Player.Position = JustInsideGate;
        var events = Run(world, 100);

        Assert.DoesNotContain(events, e => e is SquadSpotted);
        Assert.DoesNotContain(events, e => e is ActorAttacked a && a.TargetId == world.Player.Id);
    }

    [Fact]
    public void TheGuardsWarnYouBeforeYourTimeRunsOutAndThenAttack()
    {
        var world = Valley();
        PayAtGate(world);
        world.Player.Position = JustInsideGate;

        var waiting = Run(world, 140);
        Assert.Single(waiting.OfType<PassRunningOut>());
        Assert.DoesNotContain(waiting, e => e is PassExpired);

        var after = Run(world, 15);
        Assert.Contains(after, e => e is PassExpired { Inside: true });
        Assert.Contains(after, e => e is SquadSpotted s && s.TargetId == world.Player.Id);
    }

    [Fact]
    public void TimeRunningOutOutsideTheCampIsHarmless()
    {
        var world = Valley();
        PayAtGate(world);
        world.Player.Position = FarAway;

        var events = Run(world, 160);

        Assert.Contains(events, e => e is PassExpired { Inside: false });
        Assert.DoesNotContain(events, e => e is PassRunningOut);
        Assert.DoesNotContain(events, e => e is SquadSpotted);
    }

    [Fact]
    public void YouCanPayAgainOnlyWhenYourTimeIsNearlyUp()
    {
        var world = Valley();
        world.Player.Gold = 100;
        PayAtGate(world);

        var tooSoon = world.Step(new PayTollAction());
        Assert.Contains(tooSoon, e => e is ActionFailed { Reason: FailureReason.TollAlreadyPaid });
        Assert.Equal(80, world.Player.Gold);

        Run(world, 135);
        var renewed = world.Step(new PayTollAction());
        Assert.Contains(renewed, e => e is TollPaid);
        Assert.Equal(60, world.Player.Gold);
        Assert.True(CampOf(world).PassTurnsLeft(world.Turn) > 140);
    }

    [Fact]
    public void YouCantPayWithoutTheMoney()
    {
        var world = Valley();
        world.Player.Gold = 19;
        world.Player.Position = JustOutsideGate;
        int turn = world.Turn;

        var events = world.Step(new PayTollAction());

        Assert.Contains(events, e => e is ActionFailed { Reason: FailureReason.NotEnoughGold });
        Assert.Equal(turn, world.Turn);
        Assert.Equal(19, world.Player.Gold);
    }

    [Fact]
    public void YouCantPayFromFarAway()
    {
        var world = Valley();
        world.Player.Position = FarAway;
        Assert.Contains(world.Step(new PayTollAction()), e => e is ActionFailed { Reason: FailureReason.NoTollHere });
    }

    [Fact]
    public void YouCantPayWithNobodyThereToTakeIt()
    {
        var world = Valley();
        KillSquads(world);
        world.Player.Position = JustOutsideGate;
        Assert.Contains(world.Step(new PayTollAction()), e => e is ActionFailed { Reason: FailureReason.NoOneToPay });
    }

    [Fact]
    public void AnAttackedCampWontTakeYourMoney()
    {
        var world = Valley();
        CampOf(world).Provoked = true;
        world.Player.Position = JustOutsideGate;
        Assert.Contains(world.Step(new PayTollAction()), e => e is ActionFailed { Reason: FailureReason.TollRefused });
    }

    [Fact]
    public void YouCantBuyYourWayOutOfAFightInside()
    {
        var world = Valley();
        world.Player.Position = JustInsideGate;
        world.Step(new WaitAction());
        Assert.Contains(world.Step(new PayTollAction()), e => e is ActionFailed { Reason: FailureReason.TollRefused });
    }

    [Fact]
    public void AttackingAGuardVoidsWhatYouPaid()
    {
        var world = Valley();
        PayAtGate(world);
        var guard = Sentries(world).Members[0];
        world.Player.Position = JustInsideGate;

        world.Step(new AttackAction(guard.Id));

        Assert.False(CampOf(world).HasPass(world.Turn));
        Assert.True(world.AreHostile(guard, world.Player));
    }

    [Fact]
    public void CoinsGoInThePurseEvenWithAFullPack()
    {
        var world = World.Create(1);
        var player = world.Player;
        while (!player.Inventory!.IsFull)
            player.Inventory.Add(world.CreateItem(world.Content.Items["dagger"], enchantment: 0));
        var coins = world.CreateItem(world.Content.Items["gold"], count: 7);
        world.PlaceOnFloor(coins, player.Position);
        int gold = player.Gold;

        world.Step(new PickUpAction());

        Assert.Equal(gold + 7, player.Gold);
        Assert.Empty(world.ItemsAt(player.Position));
        Assert.DoesNotContain(player.Inventory!.Items, i => i == coins);
    }

    [Fact]
    public void GoblinsSometimesDropAHandfulOfCoins()
    {
        var world = World.Create(1);
        var rolled = Enumerable.Range(0, 400).Select(_ => world.RollLoot("goblin")).Where(i => i?.Def.Id == "gold").ToList();

        Assert.NotEmpty(rolled);
        Assert.All(rolled, i => Assert.InRange(i!.Count, 2, 6));
    }

    [Fact]
    public void PayingPlaysTheSameEveryTime()
    {
        static List<string> Play()
        {
            var world = Valley(7);
            var log = PayAtGate(world).Select(e => e.ToString()!).ToList();
            world.Player.Position = JustInsideGate;
            for (int t = 0; t < 170; t++) log.AddRange(world.Step(new WaitAction()).Select(e => e.ToString()!));
            log.Add(world.Player.Position.ToString());
            return log;
        }

        Assert.Equal(Play(), Play());
    }

    // ---- The errand, theft, and a third party ----

    private static readonly Point Relic = new(55, 3);
    private static readonly Point Waystone = new(1, 20);
    private static readonly Point StoreLoot = new(50, 4);

    private static Squad Keeper(World w) => w.Squads[3];

    /// <summary>Walks the player to a cell one real step at a time, collecting what happens.</summary>
    private static List<GameEvent> WalkTo(World world, Point goal, int maxTurns = 200)
    {
        var events = new List<GameEvent>();
        for (int t = 0; t < maxTurns && world.Player.Position != goal; t++)
        {
            var step = world.StepAroundOthers(world.Player, goal);
            Assert.True(step is not null, $"no step from {world.Player.Position} to {goal}");
            events.AddRange(world.Step(new MoveAction(step!.Value)));
        }
        Assert.Equal(goal, world.Player.Position);
        return events;
    }

    [Fact]
    public void TheErrandCanBeFinishedByPayingWithoutAFight()
    {
        var world = Valley();
        int startGold = world.Player.Gold;
        world.Player.Position = JustOutsideGate;
        var events = world.Step(new PayTollAction()).ToList();

        events.AddRange(WalkTo(world, Relic));
        events.AddRange(world.Step(new PickUpAction()));
        Assert.NotNull(world.Player.Inventory!.Find(world.Objective!.Item.Id));
        events.AddRange(WalkTo(world, Waystone));

        Assert.Contains(events, e => e is ObjectiveCompleted);
        Assert.True(world.Objective!.Completed);
        Assert.Null(world.Player.Inventory!.Find(world.Objective.Item.Id));
        Assert.Equal(startGold - 20, world.Player.Gold);
        Assert.Equal(5, world.Player.Inventory!.Items.Single(i => i.Def.Id == "healing_potion").Count);
        Assert.DoesNotContain(events, e => e is ActorDied);
        Assert.DoesNotContain(events, e => e is ActorAttacked a && a.TargetId == world.Player.Id);
        Assert.DoesNotContain(events, e => e is CampAlarmed);
    }

    [Fact]
    public void TheErrandCanBeFinishedBySneakingInWhileThePatrolIsAway()
    {
        var world = Valley();
        var behindTheCamp = new Point(57, 1);
        world.Player.Position = behindTheCamp;
        var patrol = FrontPatrol(world);
        var events = new List<GameEvent>();

        // Watch from cover until the patrol has walked off to the far end of the front.
        for (int t = 0; t < 120 && !(patrol.RouteStop == 0 && patrol.Leader!.Position.X < 45); t++)
            events.AddRange(world.Step(new WaitAction()));
        Assert.True(patrol.Leader!.Position.X < 45, "the patrol never left");

        events.AddRange(WalkTo(world, Relic));
        events.AddRange(world.Step(new PickUpAction()));
        events.AddRange(WalkTo(world, behindTheCamp));
        Assert.Contains(events, e => e is ItemPickedUp p && p.Item == world.Objective!.Item);
        events.AddRange(WalkTo(world, Waystone, maxTurns: 300));

        Assert.Contains(events, e => e is ObjectiveCompleted);
        Assert.DoesNotContain(events, e => e is SquadSpotted);
        Assert.DoesNotContain(events, e => e is ActorDied);
        Assert.Equal(30, world.Player.Gold); // nothing paid
    }

    [Fact]
    public void ThePatrolCatchesSomeoneAtTheBackFence()
    {
        var world = Valley();
        var patrol = FrontPatrol(world);
        var inTheGap = new Point(56, 4);
        world.Player.Position = inTheGap;

        var events = new List<GameEvent>();
        for (int t = 0; t < 80 && !events.Any(e => e is SquadSpotted); t++)
            events.AddRange(world.Step(new WaitAction()));

        Assert.Contains(events, e => e is SquadSpotted s && s.SquadId == patrol.Id);
    }

    [Fact]
    public void KillTwoGuardsEscapeAndComeBack()
    {
        var world = Valley();
        var sentries = Sentries(world);
        var victims = sentries.Members.Where(m => m.Kind == "goblin_warrior").Take(2).ToList();
        var survivor = sentries.Members.Single(m => !victims.Contains(m));
        world.Player.Position = JustOutsideGate;
        var events = new List<GameEvent>();

        foreach (var victim in victims)
        {
            victim.Hp = 1;
            victim.Position = JustOutsideGate + Direction.Up + Direction.Left; // within reach of the blow
            while (!victim.IsDead)
                events.AddRange(world.Step(new AttackAction(victim.Id)));
        }

        Assert.True(CampOf(world).Provoked);
        Assert.Contains(events, e => e is CampAlarmed);
        int corpses = world.Corpses.Count;
        Assert.True(corpses >= 2);

        // Run for the cave. Some of them give chase; none of them follow all the way.
        events.AddRange(WalkTo(world, world.Map.StairsDown!.Value, maxTurns: 200));
        events.AddRange(Run(world, 40));
        Assert.Contains(events, e => e is SquadStoodDown);
        Assert.DoesNotContain(world.Squads, s => s.Camp is not null && s.State == SquadState.Engage);

        world.Step(new DescendAction());
        world.Player.Position = world.Map.StairsUp!.Value;
        world.Step(new AscendAction());

        Assert.True(CampOf(world).Provoked); // it remembers
        Assert.All(victims, v => Assert.Null(world.GetActor(v.Id)));
        Assert.Equal(corpses, world.Corpses.Count);
        Assert.NotNull(world.GetActor(survivor.Id));
    }

    [Fact]
    public void AFleeingGoblinAndAFighterTradePlacesInACorridor()
    {
        World? world = null;
        List<Point>? corridor = null;
        for (ulong seed = 0; seed < 200 && corridor is null; seed++)
        {
            world = World.Create(seed);
            corridor = Runs(world).FirstOrDefault();
        }
        Assert.NotNull(corridor);
        foreach (var other in world!.Actors.Where(a => a != world.Player && a.Faction.Id != "goblins"))
            other.Hp = 0;
        var goblins = world.Squads.Where(s => s.Faction.Id == "goblins" && !s.IsFearless).Take(2).ToList();
        foreach (var extra in world.Squads.Where(s => !goblins.Contains(s)).SelectMany(s => s.Members))
            extra.Hp = 0;
        var fleeing = goblins[0].Members[0];
        var fighter = goblins[1].Members[0];
        foreach (var m in goblins.SelectMany(s => s.Members).Where(m => m != fleeing && m != fighter))
            m.Hp = 0;
        world.Player.Hp = 100_000;
        world.Player.Position = corridor![0];
        fleeing.Position = corridor[2];
        fighter.Position = corridor[3];
        goblins[0].State = SquadState.Flee;
        goblins[0].LastKnownTarget = corridor[0];

        bool swapped = false;
        for (int t = 0; t < 6 && !swapped; t++)
        {
            goblins[0].Morale = 0;
            world.Step(new WaitAction());
            swapped = World.Distance(fighter.Position, corridor[0]) < World.Distance(fleeing.Position, corridor[0]);
        }

        Assert.True(swapped, "the fleeing goblin should let the fighter past");

        static IEnumerable<List<Point>> Runs(World w)
        {
            foreach (var p in w.Map.Tiles.Positions())
            {
                var run = Enumerable.Range(0, 6).Select(i => p + new Point(i, 0)).ToList();
                if (run.All(c => w.Map.InBounds(c) && w.IsChokepoint(c)) && run.All(w.IsFree))
                    yield return run;
            }
        }
    }

    [Fact]
    public void TheWaystoneDoesNothingWithoutTheRelic()
    {
        var world = Valley();
        world.Player.Position = Waystone + Direction.Right;

        var events = world.Step(new MoveAction(Direction.Left));

        Assert.DoesNotContain(events, e => e is ObjectiveCompleted);
        Assert.False(world.Objective!.Completed);
    }

    [Fact]
    public void TakingTheRelicIsNotTheft()
    {
        var world = Valley();
        PayAtGate(world);
        world.Player.Position = Relic;

        var events = world.Step(new PickUpAction());

        Assert.DoesNotContain(events, e => e is TheftCommitted);
        Assert.False(CampOf(world).Provoked);
    }

    [Fact]
    public void BeingSeenStealingTurnsTheCampAgainstYouEvenIfYouPaid()
    {
        var world = Valley();
        PayAtGate(world);
        world.Player.Position = StoreLoot;
        Assert.True(Keeper(world).Members.Any(m => world.CanSee(m, world.Player)));

        var events = world.Step(new PickUpAction());

        Assert.Contains(events, e => e is TheftCommitted { WitnessId: not null });
        Assert.Contains(events, e => e is CampAlarmed);
        Assert.True(CampOf(world).Provoked);
        Assert.Contains(events, e => e is SquadSpotted s && s.SquadId == Keeper(world).Id);
    }

    [Fact]
    public void StealingUnseenRaisesNoAlarm()
    {
        var world = Valley();
        PayAtGate(world);
        foreach (var member in Keeper(world).Members)
            member.Hp = 0;
        world.Step(new WaitAction());
        world.Player.Position = StoreLoot;

        var events = world.Step(new PickUpAction());

        Assert.Contains(events, e => e is TheftCommitted { WitnessId: null });
        Assert.False(CampOf(world).Provoked);
    }

    [Fact]
    public void TheUndeadAttackTheCampWithoutThePlayer()
    {
        var world = Valley();
        world.Player.Position = FarAway;
        int goblins = world.Actors.Count(a => a.Faction.Id == "goblins");

        var events = Run(world, 299);
        Assert.DoesNotContain(events, e => e is IncursionBegan);

        events = Run(world, 150);

        Assert.Single(events.OfType<IncursionBegan>());
        Assert.Contains(events, e => e is ActorDied);
        Assert.True(world.Actors.Count(a => a.Faction.Id == "goblins" && !a.IsDead) < goblins
                    || world.Squads.All(s => s.Faction.Id != "undead"), "the fight changed nothing");
        Assert.False(CampOf(world).Provoked);
        Assert.DoesNotContain(events, e => e is ActorAttacked a && a.TargetId == world.Player.Id);
    }

    [Fact]
    public void AnIncursionWaitsUntilThePlayerIsBack()
    {
        var world = Valley();
        world.Player.Position = world.Map.StairsDown!.Value;
        world.Step(new DescendAction());
        world.Player.Hp = 100_000;
        world.ObserverMode = true;
        Run(world, 320);
        world.ObserverMode = false;
        world.Player.Position = world.Map.StairsUp!.Value;
        world.Step(new AscendAction());

        Assert.Single(world.Step(new WaitAction()).OfType<IncursionBegan>());
    }

    [Fact]
    public void AnUnopposedIncursionReachesTheCampAndEnds()
    {
        var world = Valley();
        foreach (var member in world.Squads.SelectMany(s => s.Members))
            member.Hp = 0;
        world.Player.Position = FarAway;

        Run(world, 420); // nobody opposes them, so they arrive and have nowhere further to go

        var undead = Assert.Single(world.Squads);
        Assert.Equal(SquadState.Idle, undead.State);
        Assert.False(undead.IsRaiding);
    }

    [Fact]
    public void DungeonGoblinsAreStillHostile()
    {
        var world = World.Create(3);
        var goblin = world.Actors.First(a => a.Faction.Id == "goblins");
        Assert.True(world.AreHostile(goblin, world.Player));
    }

    [Fact]
    public void TheCampPlaysTheSameEveryTime()
    {
        static List<string> Play()
        {
            var world = Valley(7);
            var log = new List<string>();
            world.Player.Position = OutsideGate;
            for (int t = 0; t < 20; t++) log.AddRange(world.Step(new WaitAction()).Select(e => e.ToString()!));
            world.Player.Position = Gate + Direction.Up;
            for (int t = 0; t < 40; t++) log.AddRange(world.Step(new WaitAction()).Select(e => e.ToString()!));
            return log;
        }

        Assert.Equal(Play(), Play());
    }

    [Theory]
    [InlineData("\"order\": \"patrol\"", "\"order\": \"dance\"", "dance")]
    [InlineData("\"territory\": \"the {tribe} camp\", \"rally\": [40, 6]", "\"territory\": \"nowhere\", \"rally\": [40, 6]", "nowhere")]
    [InlineData("\"gate\": [43, 10]", "\"gate\": [43, 12]", "gate")]
    [InlineData("\"rally\": [40, 6]", "\"rally\": [0, 0]", "rally")]
    [InlineData("\"toll\": { \"gate\": [43, 10]", "\"toll\": { \"gate\": [44, 10]", "toll gate")]
    [InlineData("\"price\": 20", "\"price\": 0", "toll price")]
    [InlineData("\"item\": \"reliquary\"", "\"item\": \"dagger\"", "relic")]
    [InlineData("\"owner\": \"the {tribe} camp\" },\n    { \"table\": \"floor\", \"at\": [51", "\"owner\": \"nobody\" },\n    { \"table\": \"floor\", \"at\": [51", "owner")]
    [InlineData("\"target\": \"the {tribe} camp\"", "\"target\": \"nowhere\"", "nowhere")]
    public void ABrokenCampIsAContentError(string find, string replace, string expected)
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
