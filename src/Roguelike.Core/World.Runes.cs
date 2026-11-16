using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Map;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

namespace Roguelike.Core;

/// <summary>A rune carved on the ground: it lasts until a turn, and goes off a number of times on whoever is hostile to its maker.</summary>
public sealed class Mark(Point at, string runeId, int ownerId, int until, int charges)
{
    public Point At { get; } = at;
    public string RuneId { get; } = runeId;
    public int OwnerId { get; } = ownerId;
    public int Until { get; set; } = until;
    public int Charges { get; set; } = charges;

    /// <summary>Only those who can sense runes see it (Rune Lore 2, Rune-Sight); others find it underfoot.</summary>
    public bool Hidden { get; init; }

    /// <summary>A seal binding a squad of the dead to this level: break it and they crumble.</summary>
    public int? SealOf { get; init; }
}

/// <summary>Runes (COMBAT_AND_MAGIC.md §4): carving, what each does where, how marks go off and fade, learning them from stones.</summary>
public sealed partial class World
{
    public IReadOnlyList<Mark> Marks => _location.Marks;

    public Mark? MarkAt(Point p) => _location.Marks.FirstOrDefault(m => m.At == p);

    /// <summary>How long carving takes, in time (100 = a turn).</summary>
    public static int CarveTime(RuneSurface surface) => surface switch
    {
        RuneSurface.Ground => 600,
        RuneSurface.Body => 100,
        _ => 1500,
    };

    public const int ThurisazWeaponBonus = 2, AlgizArrowGuard = 2;
    private const int SlowedByIsa = 50, IsaTurns = 3, HeldByNaudhiz = 2;

    /// <summary>Marks a carver can keep at once: 2, and one more for each rank of Inscription.</summary>
    public int MaxMarks(Actor actor) => 2 + (actor.Character?.Rank("inscription") ?? 0) + (actor.HasSpecial("strong_marks") ? 1 : 0);

    /// <summary>How long carving takes this carver, in time (100 = a turn): a Mystic's Rune-Sight halves it.</summary>
    public static int CarveTime(Actor actor, RuneSurface surface) =>
        surface == RuneSurface.Body || !actor.HasSpecial("quick_carving") ? CarveTime(surface) : CarveTime(surface) / 2;

    public FailureReason? CarveProblem(Actor actor, string runeId, RuneSurface surface)
    {
        if (!Content.Runes.TryGetValue(runeId, out var rune) || actor.Character?.KnowsRune(runeId) != true)
            return FailureReason.RuneUnknown;
        if (!actor.HasSpecial("carving") || rune.On(surface) is null)
            return FailureReason.CantCarveThere;
        switch (surface)
        {
            case RuneSurface.Ground when Map.Tiles[actor.Position] is not (Tile.Floor or Tile.LairFloor or Tile.Grass or Tile.Road):
                return FailureReason.CantCarveThere;
            case RuneSurface.Weapon or RuneSurface.Armor:
                if (!actor.HasSpecial("gear_carving"))
                    return FailureReason.CantCarveThere;
                if ((surface == RuneSurface.Weapon ? actor.Equipment?.Weapon : actor.Equipment?.Armor) is null)
                    return FailureReason.CantCarveThere;
                if (_actors.Any(a => !a.IsDead && a != actor && AreHostile(a, actor) && PlayerCanSee(a)))
                    return FailureReason.EnemiesNear;
                break;
        }
        return null;
    }

    public void Carve(Actor actor, string runeId, RuneSurface surface)
    {
        var rune = Content.Runes[runeId];
        Emit(new RuneCarved(actor.Id, runeId, surface, actor.Position));
        switch (surface)
        {
            case RuneSurface.Ground:
                if (MarkAt(actor.Position) is { } old)
                    _location.Marks.Remove(old);
                int turns = rune.GroundTurns * (actor.HasSpecial("deep_carving") ? 2 : 1);
                _location.Marks.Add(new Mark(actor.Position, runeId, actor.Id, Turn + turns, Math.Max(1, rune.Charges) + (actor.HasSpecial("strong_marks") ? 1 : 0)));
                var mine = _location.Marks.Where(m => m.OwnerId == actor.Id).ToList();
                foreach (var extra in mine.Take(Math.Max(0, mine.Count - MaxMarks(actor))))
                    FadeMark(extra);
                _paths.Clear();
                break;
            case RuneSurface.Body:
                PaintOn(actor, rune);
                break;
            case RuneSurface.Weapon:
                actor.Equipment!.Weapon!.Rune = runeId;
                break;
            case RuneSurface.Armor:
                actor.Equipment!.Armor!.Rune = runeId;
                break;
        }
    }

    /// <summary>A rune on the skin: quick, and short-lived.</summary>
    private void PaintOn(Actor actor, RuneDef rune)
    {
        switch (rune.Id)
        {
            case "algiz":
                AddStatus(actor, StatusKind.Shielded, rune.BodyTurns, rune.Power, actor);
                break;
            case "thurisaz":
                AddStatus(actor, StatusKind.Thorned, rune.BodyTurns, 0, actor);
                break;
            case "isa":
                AddStatus(actor, StatusKind.Steadfast, rune.BodyTurns, 0, actor);
                break;
            case "berkano":
                int healed = Math.Min(actor.MaxHp - actor.Hp, rune.Power);
                actor.Hp += healed;
                Emit(new ActorHealed(actor.Id, actor.Id, healed));
                break;
        }
    }

    private void FadeMark(Mark mark)
    {
        _location.Marks.Remove(mark);
        _paths.Clear();
        Emit(new RuneFaded(mark.RuneId, mark.At));
    }

    /// <summary>Marks that ran out of time wear away.</summary>
    private void WearMarks()
    {
        foreach (var mark in _location.Marks.Where(m => m.Until < Turn).ToList())
            FadeMark(mark);
    }

    private static bool IsUndead(Actor actor) => actor.Faction.Id == "undead";

    /// <summary>Runes that hurt or hinder whoever steps on them; the canny step round these.</summary>
    private static bool Harmful(string runeId) => runeId is "thurisaz" or "isa" or "naudhiz";

    /// <summary>
    /// Whether a mark keeps this actor off a cell: the dead can't cross Algiz at all, and squads whose mind
    /// avoids marks won't step on a harmful one. Only marks made by its enemies count.
    /// </summary>
    public bool MarkBars(Actor actor, Point p)
    {
        if (MarkAt(p) is not { } mark || GetActor(mark.OwnerId) is not { } owner || !AreHostile(owner, actor))
            return false;
        if (mark.RuneId == "algiz" && IsUndead(actor))
            return true;
        return Harmful(mark.RuneId) && SquadOf(actor)?.Mind.AvoidsMarks == true;
    }

    /// <summary>Someone stepped onto a cell: a mark there made by their enemy goes off.</summary>
    private void TriggerMark(Actor mover)
    {
        if (MarkAt(mover.Position) is not { } mark || GetActor(mark.OwnerId) is not { } owner || !AreHostile(owner, mover))
            return;
        var rune = Content.Runes[mark.RuneId];
        bool strong = owner.HasSpecial("strong_marks");
        switch (mark.RuneId)
        {
            case "algiz": // the living break a ward by walking through it
                FadeMark(mark);
                return;
            case "thurisaz":
                Emit(new RuneTriggered(mark.RuneId, mark.At, mover.Id));
                Damage(owner, mover, strong ? rune.Power * 3 / 2 : rune.Power, ranged: false);
                break;
            case "isa":
                Emit(new RuneTriggered(mark.RuneId, mark.At, mover.Id));
                AddStatus(mover, StatusKind.Slowed, IsaTurns + (strong ? 1 : 0), SlowedByIsa, owner);
                break;
            case "naudhiz":
                Emit(new RuneTriggered(mark.RuneId, mark.At, mover.Id));
                AddStatus(mover, StatusKind.Held, HeldByNaudhiz + (strong ? 1 : 0), 0, owner);
                break;
            default:
                return;
        }
        if (--mark.Charges <= 0)
            FadeMark(mark);
    }

    /// <summary>Corpses lying under a binding rune can't be raised.</summary>
    public bool IsBound(Point p) => MarkAt(p) is { RuneId: "naudhiz" };

    /// <summary>Within the light of one of the player's Kenaz marks: the player sees whoever is there.</summary>
    public bool InPlayersLight(Point p) =>
        _location.Marks.Any(m => m.RuneId == "kenaz" && m.OwnerId == Player.Id
                                 && Distance(m.At, p) <= Content.Runes["kenaz"].Radius && HasLineOfSight(m.At, p));

    /// <summary>Resting beside one of the player's Berkano marks heals twice as fast.</summary>
    private bool RestingByBerkano() =>
        _location.Marks.Any(m => m.RuneId == "berkano" && m.OwnerId == Player.Id && Distance(m.At, Player.Position) <= Content.Runes["berkano"].Radius);

    /// <summary>
    /// Stones teach runes to anyone who can read them, one rune a stone, in an order fixed by the seed.
    /// (A separate discovery from the stone's history, so learning Rune Lore later still lets you read it.)
    /// </summary>
    private void LearnRuneFromStone()
    {
        if (Map.Tiles[Player.Position] != Tile.Inscription || Player.Character is not { } c || !Player.HasSpecial("rune_reading")
            || !c.Discover($"runestone:{Depth}:{Player.Position.X},{Player.Position.Y}"))
            return;
        var next = Content.RuneList.OrderBy(r => StableHash($"{Seed}:{r.Id}")).FirstOrDefault(r => !c.KnowsRune(r.Id));
        if (next is not null && c.LearnRune(next.Id))
            Emit(new RuneLearned(next.Id));
    }

    // ---- The crypt: seals and the necromancer's traps ----

    private const int CryptSeals = 2, CryptTraps = 4;
    private const int SmashSealTime = 500;

    /// <summary>
    /// On the last level the necromancer has bound some of his dead with seals, set elsewhere on the level, and
    /// hidden thorn traps in the passages round his court. Uses the level's own generation stream.
    /// </summary>
    private void LayCryptSeals()
    {
        if (Depth != Content.Dungeon.Levels || _actors.FirstOrDefault(a => a.Kind == "necromancer") is not { } necromancer)
            return;
        var court = SquadOf(necromancer);
        var bound = _squads.Where(s => s != court && s.Faction.Id == "undead" && s.Members.Count > 0).ToList();
        var rooms = Map.Rooms.Skip(1).ToList();
        for (int i = 0; i < CryptSeals && bound.Count > 0 && rooms.Count > 0; i++)
        {
            var squad = bound[Rng.NextInt(bound.Count)];
            bound.Remove(squad);
            var leader = squad.Members[0].Position;
            // In a room of its own, away from the squad it binds.
            var room = rooms.Where(r => !r.Contains(leader)).OrderByDescending(r => Distance(r.Center, leader)).Take(3).ToList();
            if (room.Count == 0)
                break;
            var chosen = room[Rng.NextInt(room.Count)];
            rooms.Remove(chosen);
            var at = chosen.Positions().Where(p => IsFree(p) && MarkAt(p) is null).OrderBy(p => Distance(p, chosen.Center)).Cast<Point?>().FirstOrDefault();
            if (at is { } spot)
                _location.Marks.Add(new Mark(spot, "naudhiz", necromancer.Id, int.MaxValue, 99) { SealOf = squad.Id });
        }
        var heart = Map.Zones.FirstOrDefault(z => z.Area.Contains(necromancer.Position))?.Heart ?? necromancer.Position;
        var passages = Map.Tiles.Positions().Where(p => IsFree(p) && IsChokepoint(p) && MarkAt(p) is null
                                                        && Distance(p, heart) is >= 6 and <= 16).ToList();
        for (int i = 0; i < CryptTraps && passages.Count > 0; i++)
        {
            var p = passages[Rng.NextInt(passages.Count)];
            passages.Remove(p);
            _location.Marks.Add(new Mark(p, "thurisaz", necromancer.Id, int.MaxValue, 2) { Hidden = true });
        }
    }

    /// <summary>Whether the player can see this mark: their own, or anyone's unless hidden from those who can't sense runes.</summary>
    public bool PlayerSeesMark(Mark mark) =>
        mark.OwnerId == Player.Id || !mark.Hidden || Player.HasSpecial("rune_sense");

    /// <summary>
    /// Why the player can't unmake the mark at or next to them, or null. Their own marks they can always
    /// scratch out; a seal anyone can smash (slowly, loudly); other marks need Rune Lore 3.
    /// </summary>
    public FailureReason? BreakProblem(Actor actor, out Mark? mark)
    {
        mark = new[] { actor.Position }.Concat(EightWay.Select(d => actor.Position + d))
            .Select(MarkAt).FirstOrDefault(m => m is not null && PlayerSeesMark(m));
        if (mark is null)
            return FailureReason.NoTarget;
        if (mark.OwnerId == actor.Id || mark.SealOf is not null || actor.HasSpecial("rune_breaking"))
            return null;
        return FailureReason.CantCarveThere;
    }

    /// <summary>How long unmaking a mark takes: smashing a seal by hand is slow and loud; with Rune Lore 3, or your own, a moment.</summary>
    public int BreakTime(Actor actor) =>
        BreakProblem(actor, out var mark) is null && mark!.SealOf is not null && !actor.HasSpecial("rune_breaking") ? SmashSealTime : 100;

    public void BreakMark(Actor actor)
    {
        if (BreakProblem(actor, out var mark) is not null)
            return;
        _location.Marks.Remove(mark!);
        _paths.Clear();
        int crumbled = 0;
        if (mark!.SealOf is { } squadId && _squads.FirstOrDefault(s => s.Id == squadId) is { } bound)
            foreach (var dead in bound.Members.Where(m => !m.IsDead).ToList())
            {
                dead.Hp = 0;
                Emit(new MinionCrumbled(dead.Id, dead.Kind, actor.Id));
                crumbled++;
            }
        Emit(new MarkBroken(mark.RuneId, mark.At, crumbled));
        if (mark.SealOf is not null && !actor.HasSpecial("rune_breaking"))
            Noise(mark.At, SoundKind.Fight, actor);
    }

    /// <summary>FNV-1a: the same on every machine and run, unlike string.GetHashCode.</summary>
    private static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char ch in text)
            hash = (hash ^ ch) * 16777619;
        return hash;
    }

    /// <summary>For tests and tools: teach the player a rune outright.</summary>
    public bool TeachRune(string runeId) => Player.Character?.LearnRune(runeId) == true;
}
