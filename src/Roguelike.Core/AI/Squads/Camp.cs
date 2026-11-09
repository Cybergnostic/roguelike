using Roguelike.Core.Map;
using SadRogue.Primitives;

namespace Roguelike.Core.AI.Squads;

/// <summary>
/// A place its squads defend. They leave a passing traveller alone: only someone inside the zone,
/// or anyone once the camp has been attacked (it remembers), is treated as an enemy.
/// </summary>
public sealed class Camp(Zone zone)
{
    public Zone Zone { get; } = zone;

    /// <summary>Someone has attacked the camp. From now on its squads fight the player on sight.</summary>
    public bool Provoked { get; set; }

    public bool Tolerates(Point playerAt) => !Provoked && !Zone.Area.Contains(playerAt);
}
