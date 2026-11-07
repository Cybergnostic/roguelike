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

        // Pool perception: the first member who sees an enemy tells the others.
        Entities.Actor? spotter = null, target = null;
        foreach (var member in squad.Members)
        {
            target = world.NearestVisibleHostile(member);
            if (target is not null)
            {
                spotter = member;
                break;
            }
        }
        if (target is not null)
        {
            squad.TargetId = target.Id;
            squad.LastKnownTarget = target.Position;
            squad.LastSeenTurn = world.Turn;
        }
        int sinceSeen = squad.LastSeenTurn < 0 ? int.MaxValue : world.Turn - squad.LastSeenTurn;

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
            world.Emit(new SquadRouted(squad.Id, squad.Members.Select(m => m.Id).ToList()));
        }

        // Warriors hold the mouth of a corridor the enemy is standing in, giving archers time.
        squad.HoldTurns = squad.State == SquadState.Engage && spotter is not null
                          && world.IsChokepoint(squad.LastKnownTarget!.Value)
            ? squad.HoldTurns + 1
            : 0;

        if (squad.State == SquadState.Idle)
            UpdatePatrol(world, squad);
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

    private static void DispatchRunner(World world, Squad squad)
    {
        if (squad.HelpCalled || squad.HelpTargetSquadId is not null || !squad.WithRole(SquadRole.Runner).Any())
            return;

        var leader = squad.Leader!;
        var nearest = world.Squads
            .Where(s => s != squad && s.Faction == squad.Faction && !s.IsWipedOut
                        && s.State is SquadState.Idle or SquadState.Alert)
            .OrderBy(s => SadRogue.Primitives.Distance.Chebyshev.Calculate(leader.Position, s.Leader!.Position))
            .FirstOrDefault();

        squad.HelpTargetSquadId = nearest?.Id;
    }

    private static void UpdatePatrol(World world, Squad squad)
    {
        var leader = squad.Leader!;
        if (squad.Destination is { } d && SadRogue.Primitives.Distance.Chebyshev.Calculate(leader.Position, d) > 1)
            return;
        var rooms = world.Map.Rooms;
        squad.Destination = rooms[world.Rng.NextInt(rooms.Count)].Center;
    }
}
