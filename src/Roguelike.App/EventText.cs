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
                             && !isVisible(a.AttackerId) && !isVisible(a.TargetId) => null, // reported as a SoundHeard
        SoundHeard s => Heard(s),
        ActorDied d when d.ActorId != world.Player.Id && !isVisible(d.ActorId) => null,

        ActorAttacked { Ranged: true, Missed: true } a => $"{Cap(Name(a.AttackerId))} {RangedVerb(a.AttackerId)} at {Name(a.TargetId)} and {Verb(a.AttackerId, "miss", "misses")}.",
        ActorAttacked { Missed: true } a => $"{Cap(Name(a.AttackerId))} {Verb(a.AttackerId, "miss", "misses")} {Name(a.TargetId)}.",
        ActorAttacked a when a.Damage == 0 => $"{Cap(Name(a.TargetId))}{(a.TargetId == world.Player.Id ? "r" : "'s")} armour turns the blow.",
        ActorAttacked { Ranged: true } a => $"{Cap(Name(a.AttackerId))} {RangedVerb(a.AttackerId)} {Name(a.TargetId)} for {a.Damage}.",
        ActorAttacked a => $"{Cap(Name(a.AttackerId))} {Verb(a.AttackerId, "hit", "hits")} {Name(a.TargetId)} for {a.Damage}.",

        ActorHealed h when isVisible(h.HealerId) || isVisible(h.TargetId) =>
            $"{Cap(Name(h.HealerId))} chants; {Name(h.TargetId)} looks healthier.",
        ActorHealed => null,

        ActorDied d when d.ActorId == world.Player.Id => "You die...",
        ActorDied d => $"The {d.Kind.Replace('_', ' ')} dies.",

        AbilityUsed { TargetId: { } t } a when isVisible(a.ActorId) || t == world.Player.Id => $"{Cap(Name(a.ActorId))} {world.Content.Abilities[a.AbilityId].Verb} {Name(t)}!",
        AbilityUsed a when isVisible(a.ActorId) => $"{Cap(Name(a.ActorId))} {world.Content.Abilities[a.AbilityId].Verb}!",
        AbilityUsed => null,

        CorpseRaised r when isVisible(r.RaisedId) || isVisible(r.CasterId) =>
            $"{Cap(Name(r.CasterId))} raises the {Kind(r.CorpseKind)}'s corpse - it rises as a {Kind(r.RaisedKind)}!",
        CorpseRaised => null, // reported as a SoundHeard
        MinionCrumbled m when isVisible(m.ActorId) => $"The {Kind(m.Kind)} crumbles to dust, its master gone.",
        MinionCrumbled => null,

        ZoneEntered z => $"You enter {z.ZoneName}.",
        LevelEntered { Depth: World.SurfaceDepth } => "You climb out of the cave into the open air.",
        CampAlarmed c => $"A horn sounds from {c.ZoneName}!",
        SquadStoodDown { Reason: StandDownReason.IntruderLeft } s when s.MemberIds.Any(isVisible) => "They watch you go, and turn back to their posts.",
        SquadStoodDown s when s.MemberIds.Any(isVisible) => "Your pursuers break off the chase and head for home.",
        SquadStoodDown => null,
        GateChallenged c => $"{Cap(Name(c.GuardId))} calls out: \"Halt! {c.Price} gold to come in, good for {c.Turns} turns.\" (t to pay)",
        TrespassWarned w => $"{Cap(Name(w.GuardId))} levels a weapon at you: \"Not one step further without paying!\"",
        TollPaid p => $"You hand over {p.Price} gold. The guards stand aside: {p.Turns} turns in {p.ZoneName}.",
        PassRunningOut p => $"A guard snarls: \"{p.TurnsLeft} turns and your time is up. Get out, or pay again at the gate.\"",
        PassExpired { Inside: true } => "\"Time's up!\" Your paid time is over, and you're still inside.",
        PassExpired => "Your paid time at the camp is over.",
        Talked { Line: { } line } t => $"{Cap(Name(t.NpcId))} says: \"{Expand(line)}\"",
        Talked => null,
        ItemBought b => $"You buy {ItemText.AName(b.Item)} for {b.Price} gold.",
        SneakChanged c => c.Sneaking ? "You move carefully, keeping low." : "You stop sneaking.",
        SquadHeardPlayer h when h.MemberIds.Any(isVisible) => "They heard you, and turn to look.",
        SquadHeardPlayer => null,
        SquadCowed c when c.MemberIds.Any(isVisible) => "They take one look at you and turn to run.",
        SquadCowed => null,
        SquadTaunted t when isVisible(t.SpeakerId) => $"{Cap(Name(t.SpeakerId))} shouts: \"{Said(t.SpeakerId, f => f.Taunts, t.Line)}\"",
        SquadTaunted => null,
        SquadPleaded p when isVisible(p.SpeakerId) => $"{Cap(Name(p.SpeakerId))} throws down its weapon: \"{Said(p.SpeakerId, f => p.Offer switch { Roguelike.Core.AI.Squads.Ransom.Secret => f.Offers, Roguelike.Core.AI.Squads.Ransom.Gold => f.Ransoms, _ => f.Pleas }, p.Line)}\" (walk into it to decide)",
        SquadPleaded => null,
        RansomPaid r => $"{Cap(Name(r.ActorId))} hands over what it has and backs away. (+{r.Gold} gold)",
        BeggarSpared b => $"{Cap(Name(b.ActorId))} scrambles away, gabbling its thanks.",
        SecretTold { Kind: SecretKind.Stairs } s => $"\"The way down! It's to the {Wind(s.From)}!\" (marked on your map)",
        SecretTold { Kind: SecretKind.Treasure } s => $"\"There's treasure, to the {Wind(s.From)}!\" (marked on your map)",
        SecretTold s => $"\"There's a place to the {Wind(s.From)} you ought to see.\" (marked on your map)",
        LoreDiscovered => "Old words are carved into this stone. You stop and read them. (m: what the stones tell)",
        XpGained { Source: XpSource.Lore } x => $"You understand this place a little better. (+{x.Amount} XP)",
        TheftCommitted { WitnessId: { } guard } => $"{Cap(Name(guard))} sees you take it!",
        TheftCommitted => "Nobody saw you take it.",
        CampAttackedWhileAway { Held: true } c => $"While you were gone there was fighting at {c.ZoneName}. Its gate hangs broken.",
        CampAttackedWhileAway c => $"While you were gone {c.ZoneName} fell. Its gate is smashed, and something else walks inside.",
        CampReinforced c => $"While you were gone, {c.ZoneName} sent for fresh guards.",
        IncursionBegan i => $"{Cap(world.Content.Factions[i.FactionId].Name)} are marching on {i.ZoneName}!",
        ObjectiveCompleted o => $"You set {ItemText.TheName(o.Item)} down at the waystone. Your errand is done. (+{o.Xp} XP{(o.Reward is { } r ? $", {ItemText.AName(r)}" : "")}{(o.Gold > 0 ? $", +{o.Gold} gold" : "")})",
        GateMoved g when g.ActorId == world.Player.Id => g.Open ? "You push the gate open." : "You pull the gate shut.",
        GateMoved g when isVisible(g.ActorId) => g.Open ? "A gate creaks open." : "A gate slams shut.",
        GateMoved => null,
        LevelEntered { GoingUp: true } l => $"You climb back up to depth {l.Depth}.",
        LevelEntered l when l.Depth >= world.Content.Dungeon.Levels => $"You descend to depth {l.Depth}, the bottom of everything. The air tastes of old graves.",
        LevelEntered l => $"You descend the stairs to depth {l.Depth}.",
        GameWon g => $"{world.History.Necromancer} falls, and the dead lie still. You win, after {g.Turn} turns!",
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
        // Kill XP shows on the status line; first encounters are worth a line.
        XpGained { Source: XpSource.Sighting } x => $"You study the {Kind(x.Subject)} closely. (+{x.Amount} XP)",
        XpGained { Source: XpSource.Zone } x => $"You have found {x.Subject}. (+{x.Amount} XP)",
        XpGained => null,
        AttributeRaised r => $"Your {world.Content.Attributes.First(a => a.Id == r.AttributeId).Name} rises to {r.NewScore}.",
        SkillLearned l when world.Content.Skills[l.SkillId].Ranks == 1 => $"You learn {world.Content.Skills[l.SkillId].Name}.",
        SkillLearned l => $"You learn {world.Content.Skills[l.SkillId].Name} (rank {l.NewRank}/{world.Content.Skills[l.SkillId].Ranks}).",
        SpecialUsed { Special: "cleave" } u => $"Your blow carries on into {Name(u.TargetId)}!",
        SpecialUsed { Special: "lifesteal" } u when u.ActorId == world.Player.Id => "Your weapon drinks; you feel stronger.",
        SpecialUsed { Special: "thorns" } u when u.ActorId == world.Player.Id => $"Thorns bite {Name(u.TargetId)}!",
        SpecialUsed => null,

        ActionFailed f when f.ActorId == world.Player.Id => f.Reason switch
        {
            FailureReason.NotEnoughXp => "You need more experience for that.",
            FailureReason.AttributeAtMax => "That is as high as it goes.",
            FailureReason.SkillMastered => "You have mastered that already.",
            FailureReason.MissingPrerequisite => "You must learn what it builds on first.",
            FailureReason.NothingToPickUp => "There is nothing here to pick up.",
            FailureReason.InventoryFull => "Your pack is full.",
            FailureReason.NoBow => "You have no bow readied.",
            FailureReason.OutOfRange => "That's out of range.",
            FailureReason.NoLineOfFire => "Something is in the way.",
            FailureReason.CannotEquip => "You can't equip that.",
            FailureReason.NotDrinkable => "You can't drink that.",
            FailureReason.NotOnStairs => "There are no stairs here.",
            FailureReason.NoGateToClose => "There's no open gate within reach.",
            FailureReason.NotHostileYet => "They're leaving you alone. Move into them again to attack.",
            FailureReason.NoTollHere => "There's no toll to pay here. Get closer to a guarded gate.",
            FailureReason.TollRefused => "They're not taking your money now.",
            FailureReason.TollAlreadyPaid => "You've already paid.",
            FailureReason.NoOneToPay => "There's nobody watching the gate to pay.",
            FailureReason.NotEnoughGold => "You can't afford the toll.",
            _ => null,
        },

        _ => null,
    };

    private string Said(int speaker, Func<Roguelike.Core.Entities.Faction, IReadOnlyList<string>> lines, int line) =>
        world.GetActor(speaker) is { } a && lines(a.Faction) is { Count: > 0 } said ? said[line % said.Count] : "...";

    private static string Wind(SadRogue.Primitives.Direction direction) => direction.Type switch
    {
        SadRogue.Primitives.Direction.Types.Up => "north",
        SadRogue.Primitives.Direction.Types.UpRight => "north-east",
        SadRogue.Primitives.Direction.Types.Right => "east",
        SadRogue.Primitives.Direction.Types.DownRight => "south-east",
        SadRogue.Primitives.Direction.Types.Down => "south",
        SadRogue.Primitives.Direction.Types.DownLeft => "south-west",
        SadRogue.Primitives.Direction.Types.Left => "west",
        SadRogue.Primitives.Direction.Types.UpLeft => "north-west",
        _ => "somewhere",
    };

    /// <summary>What the player makes of a sound: as much as their ears could tell them.</summary>
    private static string Heard(SoundHeard s)
    {
        var who = s.FactionA is { } a
            ? Theme.FactionSound(a) + (s.FactionB is { } b && b != a ? " and " + Theme.FactionSound(b) : "")
            : null;
        var what = s.Kind switch
        {
            SoundKind.Fight => who ?? "the clash of weapons",
            SoundKind.Death => who is null ? "a scream, cut short" : $"{who}, then silence",
            SoundKind.Chanting => "low, rhythmic chanting",
            SoundKind.Footsteps => "footsteps",
            _ => "a heavy gate moving",
        };
        var where = s.From is { } from ? $"{(s.Near ? "close by to the" : "to the")} {Wind(from)}" : "somewhere far off";
        return $"You hear {what} {where}.";
    }

    /// <summary>Fills in the history's names in something written in the data.</summary>
    private string Expand(string text) => text
        .Replace("{tribe}", world.History.Tribe)
        .Replace("{founder}", world.History.Founder)
        .Replace("{necromancer}", world.History.Necromancer);

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
