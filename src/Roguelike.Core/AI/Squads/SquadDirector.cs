using Roguelike.Core.Events;

namespace Roguelike.Core.AI.Squads;

/// <summary>
/// Squad-level thinking, run once per turn before members act: pooling what members
/// see, morale, state changes and dispatching runners. Members then act on the result.
/// </summary>
public static class SquadDirector
{
    private const int DeathMoralePenalty = 25;
    private const int WoundMoralePenalty = 8;
    private const int ForgetEngageAfter = 6;   // turns without sight before Engage -> Alert
    private const int GiveUpSearchAfter = 30;  // turns without sight before Alert -> Idle

    public static void Think(World world, Squad squad)
    {
        squad.CoolDown(world.Turn);
        bool wasRunning = squad.State == SquadState.Flee;
        UpdateMorale(world, squad);
        if (squad.IsWipedOut)
            return;

        // Pool perception: whoever sees an enemy shouts, and the squad hears it if the leader
        // is in earshot. A lone straggler's sighting stays with the straggler.
        Entities.Actor? spotter = null, target = null;
        var leaderAt = squad.Leader!.Position;
        bool seenBeyondReach = false;
        squad.ClearSightings();
        foreach (var member in squad.Members)
        {
            if (World.Distance(member.Position, leaderAt) > Squad.ShoutRange
                || world.NearestVisibleHostile(member) is not { } seen)
                continue;
            if (squad.IsLeashed && !squad.WithinLeash(seen.Position))
            {
                seenBeyondReach = true; // too far out to be worth chasing
                continue;
            }
            squad.ReportSighting(seen.Position);
            if (spotter is null)
                (spotter, target) = (member, seen);
        }
        if (target is not null)
        {
            squad.TargetId = target.Id;
            squad.LastKnownTarget = target.Position;
            squad.LastSeenTurn = world.Turn;
        }
        int sinceSeen = squad.LastSeenTurn < 0 ? int.MaxValue : world.Turn - squad.LastSeenTurn;

        if (squad.Surrendered || squad.Spared)
            return; // out of the fight: its members just wait, or keep clear
        if (target == world.Player)
            SizeUp(world, squad);

        if (squad.IsLeashed && spotter is null && squad.State is SquadState.Engage or SquadState.Alert
            && squad.TargetId == world.Player.Id)
        {
            if (!world.AreHostile(squad.Leader!, world.Player))
                StandDown(world, squad, StandDownReason.IntruderLeft);
            else if (seenBeyondReach || (squad.LastKnownTarget is { } last && !squad.WithinLeash(last)))
                StandDown(world, squad, StandDownReason.TooFarFromHome);
        }

        switch (squad.State)
        {
            case SquadState.Idle or SquadState.Alert when spotter is not null:
                squad.State = SquadState.Engage;
                world.Emit(new SquadSpotted(squad.Id, spotter.Id, target!.Id));
                DispatchRunner(world, squad);
                break;

            case SquadState.Engage when sinceSeen > ForgetEngageAfter:
                squad.State = SquadState.Alert;
                squad.Destination = squad.LastKnownTarget;
                break;

            case SquadState.Alert when sinceSeen > GiveUpSearchAfter && spotter is null:
                squad.State = SquadState.Idle;
                squad.Destination = null;
                squad.HelpTargetSquadId = null; // the fight is over; call the runner back
                break;

            case SquadState.Flee when spotter is null && squad.IsAvoiding(world.Turn):
                break; // keeping well away

            case SquadState.Flee when spotter is null && squad.AvoidingUntil >= 0:
                squad.AvoidingUntil = -1;
                squad.SettleDown(); // the scare has worn off, and nobody's keen to go back
                break;

            case SquadState.Flee when spotter is null && squad.Morale >= Squad.RallyPoint:
                squad.State = SquadState.Alert; // regrouped; go looking again
                squad.Destination = squad.LastKnownTarget;
                break;
        }

        if (!squad.IsFearless && squad.State != SquadState.Flee && squad.Morale < Squad.BreakingPoint)
        {
            squad.State = SquadState.Flee;
            squad.CallOffRaid(); // a broken war band goes home, whatever its orders
            world.Emit(new SquadRouted(squad.Id, squad.Members.Select(m => m.Id).ToList()));
        }

        // Caught while running, a squad that talks may try begging instead.
        const int CaughtWithin = 2;
        if (wasRunning && squad.State == SquadState.Flee && target == world.Player && !squad.IsFearless
            && squad.Members.Min(m => World.Distance(m.Position, world.Player.Position)) <= CaughtWithin
            && ThinksOfBegging(world, squad))
            return;

        // Warriors hold the mouth of a corridor the enemy is standing in, giving archers time.
        squad.HoldTurns = squad.State == SquadState.Engage && spotter is not null
                          && world.IsChokepoint(squad.LastKnownTarget!.Value)
            ? squad.HoldTurns + 1
            : 0;

        // A lair squad won't search beyond its territory: it goes home instead.
        if (squad.State == SquadState.Alert && squad.Destination is { } goal && !squad.WithinLeash(goal))
        {
            squad.State = SquadState.Idle;
            squad.Destination = null;
        }

        if (squad.State == SquadState.Alert)
            SearchNearby(world, squad);

        if (squad.State == SquadState.Idle)
            UpdatePatrol(world, squad);
    }

    private const int AvoidTurns = 40;

    /// <summary>Weighs up the player from what the squad can see, and decides how to go about this fight.</summary>
    private static void SizeUp(World world, Squad squad)
    {
        var rules = world.Content.Combat;
        var player = world.Player;
        double hp = squad.Members.Sum(m => m.Hp), attack = squad.Members.Sum(m => m.Attack);

        // Anyone else already at the player's throat makes this a much better moment to join in.
        foreach (var other in world.Actors)
            if (other != player && !other.IsDead && !squad.Members.Contains(other) && world.AreHostile(other, player)
                && World.Distance(other.Position, player.Position) <= 2)
            {
                hp += other.Hp;
                attack += other.Attack;
            }

        attack *= 5.0 / (5 + Math.Max(0, player.Defense));
        double theirs = Math.Clamp(player.Hp, 1, player.MaxHp) * Math.Max(1, Math.Max(player.Attack, player.Equipment?.Bow is null ? 0 : player.RangedAttack));
        theirs *= 1 + player.Menace / 100.0; // big armour and big weapons look dangerous
        double odds = hp * attack / theirs;

        bool hasBow = player.Equipment?.Bow is not null;
        bool mayRun = !squad.IsLeashed && !squad.IsFearless; // nobody abandons what they're guarding
        var previous = squad.Posture;
        squad.Posture = odds >= rules.BoldAbove ? Posture.Bold
            : squad.IsFearless || odds >= rules.WaryBelow ? Posture.Even
            : odds >= rules.TerrifiedBelow ? (hasBow ? Posture.Even : Posture.Wary)
            : Posture.Desperate;

        if (squad.Posture == Posture.Bold && previous != Posture.Bold && !squad.HasTaunted && squad.Faction.Taunts.Count > 0)
        {
            squad.HasTaunted = true;
            if (squad.Members.FirstOrDefault(m => world.CanSee(m, player)) is { } mouth)
                world.Emit(new SquadTaunted(mouth.Id, world.Rng.NextInt(squad.Faction.Taunts.Count)));
        }

        if (squad.Posture != Posture.Desperate)
            return;
        if (squad.Morale >= Squad.BreakingPoint && ThinksOfBegging(world, squad))
            return;
        int nearest = squad.Members.Min(m => World.Distance(m.Position, player.Position));
        if (mayRun && (!hasBow || nearest > rules.AllInWithin))
        {
            squad.Posture = Posture.Even;
            if (squad.State != SquadState.Flee)
                world.Emit(new SquadCowed(squad.Id, squad.Members.Select(m => m.Id).ToList()));
            squad.State = SquadState.Flee;
            squad.AvoidingUntil = world.Turn + AvoidTurns;
            squad.CallOffRaid();
        }
        else if (!mayRun && !squad.IsFearless)
            squad.Posture = Posture.Even; // holds its ground at home, by the usual tactics
    }

    /// <summary>Close to a hopeless fight, a squad that talks may throw itself on the player's mercy, once.</summary>
    private static bool ThinksOfBegging(World world, Squad squad)
    {
        const int WithinEarshot = 5;
        if (squad.HasPleaded || squad.Faction.Pleas.Count == 0
            || squad.Members.FirstOrDefault(m => world.CanSee(m, world.Player) && World.Distance(m.Position, world.Player.Position) <= WithinEarshot) is not { } mouth)
            return false;
        squad.HasPleaded = true;
        var rules = world.Content.Combat;
        if (world.Rng.NextDouble() >= rules.PleaChance)
            return false;
        squad.Surrendered = true;
        squad.State = SquadState.Idle;
        squad.Destination = null;
        DecideRansom(world, squad);
        var lines = squad.Offer switch
        {
            Ransom.Secret => squad.Faction.Offers,
            Ransom.Gold => squad.Faction.Ransoms,
            _ => squad.Faction.Pleas,
        };
        world.Emit(new SquadPleaded(squad.Id, mouth.Id, world.Rng.NextInt(lines.Count), squad.Offer));
        return true;
    }

    /// <summary>What a surrendering squad has to offer: elites usually something good, ordinary monsters rarely much.</summary>
    private static void DecideRansom(World world, Squad squad)
    {
        var rank = squad.Members.Max(m => m.Rank);
        if (world.Content.Combat.Ransom?.GetValueOrDefault(rank) is not { } terms || world.Rng.NextDouble() >= terms.OfferChance)
            return;
        bool canTell = squad.Faction.Offers.Count > 0 && world.HasSecretToTell();
        if (canTell && world.Rng.NextDouble() < terms.SecretShare)
        {
            squad.Offer = Ransom.Secret;
            return;
        }
        if (squad.Faction.Ransoms.Count > 0)
        {
            squad.Offer = Ransom.Gold;
            squad.OfferedGold = world.Rng.NextInt(terms.Gold[0], terms.Gold[1] + 1);
        }
    }

    /// <summary>The squad has a reason to stop: it forgets the chase and goes back to its orders.</summary>
    private static void StandDown(World world, Squad squad, StandDownReason reason)
    {
        squad.State = SquadState.Idle;
        squad.Destination = null;
        squad.LastKnownTarget = null;
        squad.TargetId = null;
        squad.LastSeenTurn = -1;
        squad.HelpTargetSquadId = null;
        world.Emit(new SquadStoodDown(squad.Id, squad.Members.Select(m => m.Id).ToList(), reason));
    }

    /// <summary>On reaching the place they were looking for, searchers try somewhere close by.</summary>
    private static void SearchNearby(World world, Squad squad)
    {
        const int SearchRadius = 5;
        const int MaxWalk = 7;
        if (squad.Destination is not { } here || squad.LastKnownTarget is not { } center
            || SadRogue.Primitives.Distance.Chebyshev.Calculate(squad.Leader!.Position, here) > 1)
            return;
        var reach = squad.TowardKnownThreats(world.Map.Passable);
        for (int tries = 0; tries < 10; tries++)
        {
            var p = center + new SadRogue.Primitives.Point(
                world.Rng.NextInt(-SearchRadius, SearchRadius + 1), world.Rng.NextInt(-SearchRadius, SearchRadius + 1));
            if (p != here && world.Map.InBounds(p) && reach[p] <= MaxWalk && squad.WithinLeash(p))
            {
                squad.Destination = p;
                return;
            }
        }
    }

    private static void UpdateMorale(World world, Squad squad)
    {
        int deaths = squad.Members.RemoveAll(m => m.IsDead);
        squad.Morale -= deaths * DeathMoralePenalty;

        foreach (var m in squad.Members)
        {
            if (m.Hp * 2 < m.MaxHp && squad.CountedWounded.Add(m.Id))
                squad.Morale -= WoundMoralePenalty;
        }

        // Recover slowly when not in the thick of it.
        if (squad.State is SquadState.Flee or SquadState.Idle or SquadState.Alert)
            squad.Morale = Math.Min(Squad.MaxMorale, squad.Morale + (squad.State == SquadState.Flee ? 3 : 1));
    }

    internal static void DispatchRunner(World world, Squad squad)
    {
        if (squad.HelpCalled || squad.HelpTargetSquadId is not null || !squad.WithRole(SquadRole.Runner).Any())
            return;

        var leader = squad.Leader!;
        var nearest = world.Squads
            .Where(s => s != squad && s.Faction == squad.Faction && !s.IsWipedOut && s.Leader is not null
                        && s.State is SquadState.Idle or SquadState.Alert)
            .OrderBy(s => SadRogue.Primitives.Distance.Chebyshev.Calculate(leader.Position, s.Leader!.Position))
            .FirstOrDefault();

        squad.HelpTargetSquadId = nearest?.Id;
    }

    private static void UpdatePatrol(World world, Squad squad)
    {
        if (squad.Route is { Count: > 0 } route)
        {
            if (SadRogue.Primitives.Distance.Chebyshev.Calculate(squad.Leader!.Position, route[squad.RouteStop]) <= 1)
                squad.RouteStop = (squad.RouteStop + 1) % route.Count;
            squad.Destination = route[squad.RouteStop];
            return;
        }
        if (squad.Home is { } home)
        {
            squad.Destination = home;
            return;
        }
        var leader = squad.Leader!;
        if (squad.RaidTarget is { } target)
        {
            // Marching on the target; once there with nothing to fight, the raid is over.
            if (SadRogue.Primitives.Distance.Chebyshev.Calculate(leader.Position, target) <= 2)
                squad.CallOffRaid();
            else
            {
                squad.Destination = target;
                return;
            }
        }
        if (squad.Destination is { } d && SadRogue.Primitives.Distance.Chebyshev.Calculate(leader.Position, d) > 1)
            return;
        var rooms = world.Map.Rooms;
        squad.Destination = rooms[world.Rng.NextInt(rooms.Count)].Center;
    }
}
