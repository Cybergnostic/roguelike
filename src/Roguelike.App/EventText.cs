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
        // Fights the player isn't part of: describe if seen, otherwise it's a distant noise.
        ActorAttacked a when a.AttackerId != world.Player.Id && a.TargetId != world.Player.Id
                             && !isVisible(a.AttackerId) && !isVisible(a.TargetId) => "You hear the clash of weapons.",
        ActorDied d when d.ActorId != world.Player.Id && !isVisible(d.ActorId) => null,

        ActorAttacked { Ranged: true } a when a.Damage == 0 => $"{Cap(Name(a.AttackerId))} {RangedVerb(a.AttackerId)} at {Name(a.TargetId)} and misses.",
        ActorAttacked { Ranged: true } a => $"{Cap(Name(a.AttackerId))} {RangedVerb(a.AttackerId)} {Name(a.TargetId)} for {a.Damage}.",
        ActorAttacked a when a.Damage == 0 => $"{Cap(Name(a.AttackerId))} {Verb(a.AttackerId, "miss", "misses")} {Name(a.TargetId)}.",
        ActorAttacked a => $"{Cap(Name(a.AttackerId))} {Verb(a.AttackerId, "hit", "hits")} {Name(a.TargetId)} for {a.Damage}.",

        ActorHealed h when isVisible(h.HealerId) || isVisible(h.TargetId) =>
            $"{Cap(Name(h.HealerId))} chants; {Name(h.TargetId)} looks healthier.",
        ActorHealed => null,

        ActorDied d when d.ActorId == world.Player.Id => "You die...",
        ActorDied d => $"The {d.Kind.Replace('_', ' ')} dies.",

        CorpseRaised r when isVisible(r.RaisedId) || isVisible(r.CasterId) =>
            $"{Cap(Name(r.CasterId))} raises the {Kind(r.CorpseKind)}'s corpse - it rises as a {Kind(r.RaisedKind)}!",
        CorpseRaised => "You hear a low, rhythmic chanting.",
        MinionCrumbled m when isVisible(m.ActorId) => $"The {Kind(m.Kind)} crumbles to dust, its master gone.",
        MinionCrumbled => null,

        ZoneEntered z => $"You enter {z.ZoneName}.",
        SquadMustered m when world.Squads.FirstOrDefault(s => s.Id == m.SquadId)?.Members.Any(a => isVisible(a.Id)) == true =>
            $"A fresh band sets out from {m.ZoneName}!",
        SquadMustered m => $"Drums beat in {m.ZoneName}.",
        RaidLaunched r => $"War horns sound: {world.Content.Factions[r.FactionId].Name} march on {r.ZoneName}.",

        SquadSpotted s when s.TargetId == world.Player.Id => $"{Cap(Name(s.SpotterId))} spots you and raises the alarm!",
        SquadSpotted s when isVisible(s.SpotterId) || isVisible(s.TargetId) =>
            $"{Cap(Name(s.SpotterId))} spots {Name(s.TargetId)}!",
        SquadSpotted => null,

        SquadRouted r when r.MemberIds.Any(isVisible) => "The goblins' nerve breaks - they flee!",
        SquadRouted => "You hear panicked shrieks in the distance.",

        ReinforcementsCalled c when isVisible(c.RunnerId) => $"{Cap(Name(c.RunnerId))} rallies another war band!",
        ReinforcementsCalled => "War drums sound somewhere in the dungeon.",

        ItemPickedUp p when p.ActorId == world.Player.Id => $"You pick up {ItemText.AName(p.Item)}.",
        ItemDropped d when d.ActorId == world.Player.Id => $"You drop {ItemText.TheName(d.Item)}.",
        LootDropped l when isVisible(l.ActorId) => $"{Cap(Name(l.ActorId))} drops {ItemText.AName(l.Item)}.",
        ItemEquipped q when q.ActorId == world.Player.Id =>
            $"You {EquipVerb(q.Item)} {ItemText.TheName(q.Item)}" +
            (q.Replaced is { } old ? $", putting away {ItemText.TheName(old)}." : ".") +
            (q.Item.Enchantment < 0 ? " It feels wrong in your hands." : ""),
        ItemUnequipped u when u.ActorId == world.Player.Id => $"You take off {ItemText.TheName(u.Item)}.",
        PotionDrunk p when p.ActorId == world.Player.Id =>
            p.Healed > 0 ? $"You drink {ItemText.AName(Single(p.Item))}. You feel better (+{p.Healed} HP)."
                         : $"You drink {ItemText.AName(Single(p.Item))}. Nothing happens; you're unhurt.",
        ActionFailed f when f.ActorId == world.Player.Id => f.Reason switch
        {
            FailureReason.NothingToPickUp => "There is nothing here to pick up.",
            FailureReason.InventoryFull => "Your pack is full.",
            FailureReason.NoBow => "You have no bow readied.",
            FailureReason.OutOfRange => "That's out of range.",
            FailureReason.NoLineOfFire => "Something is in the way.",
            FailureReason.CannotEquip => "You can't equip that.",
            FailureReason.NotDrinkable => "You can't drink that.",
            _ => null,
        },

        _ => null,
    };

    /// <summary>Unseen attackers are "something": you know you were shot, not by whom.</summary>
    private string Name(int id) =>
        id == world.Player.Id ? "you" : isVisible(id) ? nameOf(id) : "something";

    private static string EquipVerb(Roguelike.Core.Items.Item item) => item.Def.Category switch
    {
        Roguelike.Core.Items.ItemCategory.Weapon => "wield",
        Roguelike.Core.Items.ItemCategory.Bow => "ready",
        _ => "put on",
    };

    /// <summary>A one-item view of a stack, for "you drink a potion" phrasing.</summary>
    private static Roguelike.Core.Items.Item Single(Roguelike.Core.Items.Item item) =>
        new(item.Id, item.Def) { Enchantment = item.Enchantment, Count = 1 };

    /// <summary>"shoots" unless looks.json gives the kind its own verb (a caster "blasts").</summary>
    private string RangedVerb(int actorId) =>
        actorId == world.Player.Id ? "shoot"
        : isVisible(actorId) && world.GetActor(actorId) is { } a && Theme.RangedVerb(a.Kind) is { } verb ? verb
        : "shoots";

    private string Verb(int actorId, string you, string them) => actorId == world.Player.Id ? you : them;

    private static string Kind(string kind) => kind.Replace('_', ' ');

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
