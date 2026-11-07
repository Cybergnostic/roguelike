using Roguelike.Core.Actions;
using Roguelike.Core.Entities;
using SadRogue.Primitives;

namespace Roguelike.Core.Simulation;

/// <summary>Plays the player's side automatically, for tests and headless simulation reports.</summary>
public interface IPlayerBot
{
    string Name { get; }
    GameAction Decide(World world);
}

/// <summary>Charges the nearest visible enemy; otherwise heads for unexplored rooms. Never retreats.</summary>
public sealed class AggressiveBot : IPlayerBot
{
    public string Name => "aggressive";

    public GameAction Decide(World world)
    {
        var player = world.Player;
        var target = world.Actors
            .Where(a => world.AreHostile(player, a) && world.PlayerCanSee(a))
            .OrderBy(a => World.Distance(a.Position, player.Position))
            .FirstOrDefault();

        if (target is not null && world.IsAdjacent(player.Position, target.Position))
            return new AttackAction(target.Id);

        Point goal;
        if (target is not null)
            goal = target.Position;
        else
        {
            var room = world.Map.Rooms
                .OrderBy(r => world.Map.Explored[r.Center] ? 1 : 0)
                .ThenBy(r => World.Distance(r.Center, player.Position))
                .First();
            goal = room.Center == player.Position
                ? world.Map.Rooms[world.Turn % world.Map.Rooms.Count].Center
                : room.Center;
        }
        return world.StepToward(player, goal) is { } dir ? new MoveAction(dir) : new WaitAction();
    }
}

/// <summary>Does nothing. Combined with observer mode, the dungeon runs on its own.</summary>
public sealed class IdleBot : IPlayerBot
{
    public string Name => "idle";
    public GameAction Decide(World world) => new WaitAction();
}
