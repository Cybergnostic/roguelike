using Roguelike.Core.AI.Squads;
using Roguelike.Core.Content;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Map;
using SadRogue.Primitives;

namespace Roguelike.Core;

/// <summary>The rules behind the class skills (COMBAT_AND_MAGIC.md §3): what each new ability and special does.</summary>
public sealed partial class World
{
    // ---- Warrior ----

    /// <summary>
    /// Pushes the target straight away from the actor, a cell (two with Breakthrough rank 2). Driven into a
    /// wall or someone else, it reels; with rank 3 whoever it's driven into is knocked back too. It always
    /// spoils a wind-up.
    /// </summary>
    public void Shove(Actor actor, Actor target)
    {
        var dir = Direction.GetDirection(actor.Position, target.Position);
        int cells = actor.HasSpecial("shove_far") ? 2 : 1;
        bool staggered = false;
        if (target.WindUp is { } spoiled)
        {
            target.WindUp = null;
            Emit(new WindUpSpoiled(target.Id, spoiled.AbilityId));
        }
        for (int i = 0; i < cells && !target.IsDead; i++)
        {
            var next = target.Position + dir;
            if (IsFree(next))
            {
                var from = target.Position;
                target.Position = next;
                Emit(new ActorMoved(target.Id, from, next));
                TriggerMark(target); // shoved onto a rune
                continue;
            }
            // Into something: it reels, and so does whoever it hit.
            StaggerIfAlive(target, actor);
            staggered = true;
            if (ActorAt(next) is { } other && other != actor)
            {
                StaggerIfAlive(other, actor);
                if (actor.HasSpecial("shove_through") && IsFree(next + dir))
                {
                    var from = other.Position;
                    other.Position = next + dir;
                    Emit(new ActorMoved(other.Id, from, other.Position));
                }
            }
            break;
        }
        if (!staggered && actor.HasSpecial("shove_far"))
            StaggerIfAlive(target, actor);
    }

    /// <summary>Breakthrough is no use on something too big to push.</summary>
    public static bool CanBeShoved(Actor target) => target.Rank != Rank.Boss;

    /// <summary>The player's kill: Battle Momentum makes the next action quick, and rank 2 emboldens.</summary>
    private void PlayerKilled()
    {
        if (Player.HasSpecial("momentum"))
            _quickNextAction = true;
        if (Player.HasSpecial("bloodlust"))
            AddStatus(Player, StatusKind.Emboldened, 2, 25, Player);
    }

    private bool _quickNextAction;

    /// <summary>Hold the Line rank 2: an enemy slipping from one cell next to you to another takes a blow, once a turn.</summary>
    private void Intercept(Actor mover, Point from)
    {
        if (mover == Player || !Player.HasSpecial("intercept") || Player.IsDead || _interceptedOnTurn == Turn
            || !AreHostile(mover, Player) || !IsAdjacent(from, Player.Position) || !IsAdjacent(mover.Position, Player.Position))
            return;
        _interceptedOnTurn = Turn;
        Emit(new SpecialUsed(Player.Id, "intercept", mover.Id));
        ResolveAttack(Player, mover, ranged: false);
    }

    private int _interceptedOnTurn = -1;

    // ---- Ranger ----

    /// <summary>Suppressive Fire: where the player has a bow trained, and how many more shots it will take.</summary>
    private (Point Center, int Radius, int ShotsLeft, int Until)? _suppressing;

    public void Suppress(Actor actor, Actor target, AbilityDef ability)
    {
        _suppressing = (target.Position, Math.Max(1, ability.Amount), Math.Max(1, ability.Count), Turn + Math.Max(1, ability.Turns));
        Emit(new CoveringFire(actor.Id, target.Position, ability.Turns));
    }

    /// <summary>Anyone hostile stepping into the covered ground is shot, and their squad's nerve suffers.</summary>
    private void SuppressiveFire(Actor mover)
    {
        if (_suppressing is not { } s || mover == Player || Player.IsDead || !AreHostile(mover, Player))
            return;
        if (Turn > s.Until || s.ShotsLeft <= 0)
        {
            _suppressing = null;
            return;
        }
        if (Distance(mover.Position, s.Center) > s.Radius || Distance(mover.Position, Player.Position) > Player.Range
            || Player.Equipment?.Bow is null || !HasClearShot(Player.Position, mover.Position))
            return;
        _suppressing = s with { ShotsLeft = s.ShotsLeft - 1 };
        Emit(new SpecialUsed(Player.Id, "suppressive_fire", mover.Id));
        ResolveAttack(Player, mover, ranged: true);
        if (SquadOf(mover) is { IsFearless: false } squad)
            squad.Morale -= SuppressionMorale;
    }

    private const int SuppressionMorale = 8;

    /// <summary>A creature took a step: covering fire and interceptions react to it.</summary>
    public void AfterStep(Actor mover, Point from)
    {
        TriggerMark(mover);
        if (mover.IsDead)
            return;
        Intercept(mover, from);
        if (!mover.IsDead)
            SuppressiveFire(mover);
    }

    /// <summary>A step back from the target that still leaves a clear shot at it, for Disengage.</summary>
    public Point? DisengageLanding(Actor actor, Actor target) =>
        EightWay.Select(d => actor.Position + d)
            .Where(p => IsFree(p) && Distance(p, target.Position) > Distance(actor.Position, target.Position) && HasClearShotFrom(actor, p, target.Position))
            .OrderByDescending(p => _actors.Where(a => !a.IsDead && AreHostile(a, actor)).Select(a => Distance(a.Position, p)).DefaultIfEmpty(9).Min())
            .Cast<Point?>().FirstOrDefault();

    /// <summary>The next creature beyond the target along the line of a shot, within reach: Pinning Shot rank 3.</summary>
    public Actor? BehindInLine(Actor shooter, Actor target)
    {
        var dir = Direction.GetDirection(shooter.Position, target.Position);
        var p = target.Position;
        for (int i = 0; i < 3; i++)
        {
            p += dir;
            if (!Map.InBounds(p) || !Map.Transparent[p])
                return null;
            if (ActorAt(p) is { IsDead: false } next && AreHostile(next, shooter))
                return next;
        }
        return null;
    }

    // ---- Shadow ----

    /// <summary>Tumble: the cell on the far side of the target, if it is free.</summary>
    public Point? TumbleLanding(Actor actor, Actor target)
    {
        var beyond = target.Position + (target.Position - actor.Position);
        return IsFree(beyond) ? beyond : null;
    }

    /// <summary>Where a thrown stone lands: up to Amount cells past the target, along the line from the thrower.</summary>
    public Point? StoneLanding(Actor actor, Actor target, AbilityDef ability)
    {
        var dir = Direction.GetDirection(actor.Position, target.Position);
        Point? landing = null;
        var p = target.Position;
        for (int i = 0; i < Math.Max(1, ability.Amount); i++)
        {
            p += dir;
            if (!Map.InBounds(p) || !Map.Walkable[p])
                break;
            landing = p;
        }
        return landing;
    }

    /// <summary>
    /// A clatter at a spot: squads not fighting who are near enough to hear go to look. With Misdirection rank 3,
    /// squads fighting the player who can't see them right now are fooled too.
    /// </summary>
    public void Distract(Actor actor, Point at)
    {
        Emit(new StoneThrown(actor.Id, at));
        foreach (var squad in _squads)
        {
            if (squad.Leader is not { } leader || Distance(leader.Position, at) > DistractHearing || squad.Surrendered || squad.Spared)
                continue;
            bool calm = squad.State is SquadState.Idle or SquadState.Alert;
            bool fooled = squad.State == SquadState.Engage && actor.HasSpecial("decoy") && !squad.Members.Any(m => CanSee(m, actor));
            if (!calm && !fooled)
                continue;
            squad.State = SquadState.Alert;
            squad.Destination = at;
            squad.LastKnownTarget = at;
        }
    }

    private const int DistractHearing = 12;

    /// <summary>A cloud of smoke around a spot for some turns: nobody sees through it.</summary>
    public void MakeSmoke(Point center, int radius, int turns)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            var p = center + new Point(dx, dy);
            if (!Map.InBounds(p) || Map.Tiles[p] is not (Tile.Floor or Tile.LairFloor or Tile.Smoke))
                continue;
            int existing = _location.Smoke.FindIndex(s => s.At == p);
            if (existing >= 0)
                _location.Smoke[existing] = _location.Smoke[existing] with { Until = Turn + turns };
            else
            {
                _location.Smoke.Add((p, Map.Tiles[p], Turn + turns));
                Map.SetTile(p, Tile.Smoke);
            }
        }
        _paths.Clear();
        UpdateFov();
    }

    private void ClearSmoke()
    {
        if (_location.Smoke.Count == 0)
            return;
        bool cleared = false;
        foreach (var s in _location.Smoke.Where(s => s.Until < Turn).ToList())
        {
            Map.SetTile(s.At, s.Was);
            _location.Smoke.Remove(s);
            cleared = true;
        }
        if (cleared)
        {
            _paths.Clear();
            UpdateFov();
        }
    }

    /// <summary>Whether a blow from this attacker finds the target unready: Shadow's Opportunist and From the Shadows widen it.</summary>
    public bool CaughtOffGuard(Actor attacker, Actor target)
    {
        if (IsUnaware(target))
            return true;
        if (attacker.HasSpecial("from_shadows") && attacker.HiddenUntil >= Turn)
            return true;
        if (attacker.HasSpecial("cutthroat") && target != Player
            && (SquadOf(target) is { State: SquadState.Flee } || !_actors.Any(a => a != target && !a.IsDead && !AreHostile(a, target)
                                                                                   && a != attacker && Distance(a.Position, target.Position) <= 2)))
            return true;
        return false;
    }
}
