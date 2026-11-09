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
            if (squad.Camp is not null && !squad.WithinLeash(seen.Position))
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

        if (squad.Camp is not null && spotter is null && squad.State is SquadState.Engage or SquadState.Alert
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
