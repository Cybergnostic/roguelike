using Roguelike.Core;
using Roguelike.Core.Events;

namespace Roguelike.App;

/// <summary>
/// Turns the simulation's structured events into log messages. Things the player can't
/// see are reported as sounds, so off-screen events still leave a readable trace.
/// </summary>
public sealed class EventText(World world, Func<int, string> nameOf, Func<int, bool> isVisible)
{
    public string? Describe(GameEvent e) => e switch
    {
        ActorAttacked { Ranged: true } a when a.Damage == 0 => $"{Cap(Name(a.AttackerId))} {Verb(a.AttackerId, "shoot", "shoots")} at {Name(a.TargetId)} and misses.",
        ActorAttacked { Ranged: true } a => $"{Cap(Name(a.AttackerId))} {Verb(a.AttackerId, "shoot", "shoots")} {Name(a.TargetId)} for {a.Damage}.",
        ActorAttacked a when a.Damage == 0 => $"{Cap(Name(a.AttackerId))} {Verb(a.AttackerId, "miss", "misses")} {Name(a.TargetId)}.",
        ActorAttacked a => $"{Cap(Name(a.AttackerId))} {Verb(a.AttackerId, "hit", "hits")} {Name(a.TargetId)} for {a.Damage}.",

        ActorHealed h when isVisible(h.HealerId) || isVisible(h.TargetId) =>
            $"{Cap(Name(h.HealerId))} chants; {Name(h.TargetId)} looks healthier.",
        ActorHealed => null,

        ActorDied d when d.ActorId == world.Player.Id => "You die...",
        ActorDied d => $"The {d.Kind.Replace('_', ' ')} dies.",

        SquadSpotted s => $"{Cap(Name(s.SpotterId))} spots you and shrieks an alarm!",

        SquadRouted r when r.MemberIds.Any(isVisible) => "The goblins' nerve breaks - they flee!",
        SquadRouted => "You hear panicked shrieks in the distance.",

        ReinforcementsCalled c when isVisible(c.RunnerId) => $"{Cap(Name(c.RunnerId))} rallies another war band!",
        ReinforcementsCalled => "War drums sound somewhere in the dungeon.",

        _ => null,
    };

    /// <summary>Unseen attackers are "something": you know you were shot, not by whom.</summary>
    private string Name(int id) =>
        id == world.Player.Id ? "you" : isVisible(id) ? nameOf(id) : "something";

    private string Verb(int actorId, string you, string them) => actorId == world.Player.Id ? you : them;

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
