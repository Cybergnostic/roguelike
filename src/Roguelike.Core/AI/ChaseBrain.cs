using Roguelike.Core.Actions;
using Roguelike.Core.Entities;
using SadRogue.Primitives;

namespace Roguelike.Core.AI;

/// <summary>
/// Placeholder monster AI: if it can see the player, it follows the shared
/// "toward player" Dijkstra map (Brogue-style); otherwise it wanders.
/// The goblin squad AI will replace this.
/// </summary>
public sealed class ChaseBrain : IBrain
{
    private static readonly Direction[] Directions = [.. AdjacencyRule.EightWay.DirectionsOfNeighbors()];

    public GameAction Decide(World world, Actor self)
    {
        var player = world.Player;
        bool seesPlayer = !player.IsDead && world.Map.PlayerFov.BooleanResultView[self.Position];

        if (seesPlayer)
        {
            if (self.Position.IsAdjacentTo(player.Position))
                return new AttackAction(player.Id);

            var dir = world.TowardPlayer.GetDirectionOfMinValue(self.Position);
            if (dir != Direction.None && world.ActorAt(self.Position + dir) is null)
                return new MoveAction(dir);
            return new WaitAction();
        }

        // Wander: one random step, or stay put.
        if (world.Rng.NextInt(3) == 0)
            return new WaitAction();
        var wander = Directions[world.Rng.NextInt(Directions.Length)];
        var next = self.Position + wander;
        return world.Map.Walkable[next] && world.ActorAt(next) is null
            ? new MoveAction(wander)
            : new WaitAction();
    }
}

internal static class PointExtensions
{
    public static bool IsAdjacentTo(this Point a, Point b) =>
        a != b && Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1;
}
