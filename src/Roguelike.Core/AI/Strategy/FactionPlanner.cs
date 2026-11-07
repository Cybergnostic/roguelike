using Roguelike.Core.AI.Squads;
using Roguelike.Core.Events;

namespace Roguelike.Core.AI.Strategy;

/// <summary>
/// The strategic layer, run once per turn before squads think:
///   musters: standing lairs send out fresh squads, keeping their faction alive;
///   raids: every so often a faction sends one idle squad against a standing enemy lair.
/// Squads then fight whatever they meet on the way, the player included.
/// </summary>
public static class FactionPlanner
{
    /// <summary>A raiding party needs at least this many members.</summary>
    private const int MinRaiders = 2;

    public static void Think(World world)
    {
        if (world.Turn == 0)
            return;
        Musters(world);
        Raids(world);
    }

    private static void Musters(World world)
    {
        foreach (var lair in world.Lairs)
        {
            if (lair.Template.Muster is not { } muster || world.Turn % muster.Every != 0 || lair.HasFallen)
                continue;
            var faction = lair.Squad!.Faction;
            int roaming = world.Squads.Count(s => s.Faction == faction && s.Home is null && !s.IsWipedOut);
            if (roaming >= muster.Max)
                continue;
            var template = world.Content.SquadTemplates.First(t => t.Name == muster.Squad);
            if (world.Muster(lair, template) is { } squad)
                world.Emit(new SquadMustered(squad.Id, faction.Id, lair.Zone.Name));
        }
    }

    private static void Raids(World world)
    {
        foreach (var faction in world.Content.Factions.Values)
        {
            if (faction.Raid is not { } plan || world.Turn % plan.Every != 0)
                continue;
            var target = world.Lairs.FirstOrDefault(l => l.Zone.FactionId == plan.Against && !l.HasFallen)?.Zone;
            if (target is null)
                continue;

            // The idle, unattached squad closest to the target goes.
            var raiders = world.Squads
                .Where(s => s.Faction == faction && s.State == SquadState.Idle && s.Home is null
                            && !s.IsRaiding && s.Members.Count >= MinRaiders)
                .OrderBy(s => World.Distance(s.Leader!.Position, target.Heart))
                .FirstOrDefault();
            if (raiders is null)
                continue;

            raiders.RaidTarget = target.Heart;
            raiders.RaidZone = target.Name;
            raiders.Destination = target.Heart;
            world.Emit(new RaidLaunched(raiders.Id, faction.Id, target.Name));
        }
    }
}
