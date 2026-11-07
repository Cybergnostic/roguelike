using Roguelike.Core.Actions;
using Roguelike.Core.Entities;

namespace Roguelike.Core.AI;

/// <summary>Decides what a non-player actor does each turn. Squad and faction AI plug in here.</summary>
public interface IBrain
{
    GameAction Decide(World world, Actor self);
}
