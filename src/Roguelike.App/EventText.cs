using Roguelike.Core;
using Roguelike.Core.Events;

namespace Roguelike.App;

/// <summary>Turns the simulation's structured events into log messages.</summary>
public static class EventText
{
    public static string? Describe(World world, GameEvent e, Func<int, string> nameOf) => e switch
    {
        ActorAttacked a when a.Damage == 0 => $"{Cap(nameOf(a.AttackerId))} {Verb(world, a.AttackerId, "miss", "misses")} {nameOf(a.TargetId)}.",
        ActorAttacked a => $"{Cap(nameOf(a.AttackerId))} {Verb(world, a.AttackerId, "hit", "hits")} {nameOf(a.TargetId)} for {a.Damage}.",
        ActorDied d when d.ActorId == world.Player.Id => "You die...",
        ActorDied d => $"The {d.Kind} dies.",
        _ => null,
    };

    private static string Verb(World world, int actorId, string you, string them) =>
        actorId == world.Player.Id ? you : them;

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
