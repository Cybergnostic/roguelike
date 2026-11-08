using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Items;
using SadRogue.Primitives;

namespace Roguelike.Core.Simulation;

/// <summary>
/// A bot that plays like a careful human (what Claude did by hand in playtests), for balance
/// simulations across the whole dungeon:
/// <list type="bullet">
/// <item>spends XP along a build plan; wears better gear; picks up potions and upgrades only;</item>
/// <item>in a fight: melee whatever is adjacent (bows are poor point-blank), shoot priority targets
///   (raisers, healers, archers, the wounded) from range, kite slow monsters, fight from corridors
///   when outnumbered;</item>
/// <item>lets other factions fight each other when they aren't after it;</item>
/// <item>drinks potions when it's losing, retreats when hurt and out of potions, rests out of sight;</item>
/// <item>explores the whole level, then takes the stairs.</item>
/// </list>
/// One instance per game: it keeps a little memory (how long it has been watching a fight).
/// </summary>
public sealed class TacticalBot : IPlayerBot
{
    public string Name => "tactical";

    /// <summary>XP purchases in order ("skill:id" or "attr:id"); each is bought as soon as affordable.</summary>
    private static readonly string[] BuildPlan =
    [
        "skill:toughness", "skill:weapon_training", "skill:steady_aim", "skill:field_medicine",
        "skill:toughness", "skill:thick_skin", "skill:weapon_training", "skill:second_wind",
        "skill:steady_aim", "skill:toughness", "skill:cleave", "skill:thick_skin", "skill:weapon_training",
        "skill:eagle_eye", "skill:field_medicine", "skill:second_wind", "skill:steady_aim", "skill:eagle_eye",
    ];
    private static readonly string[] AttributeCycle = ["con", "str", "dex", "con", "str", "wis"];

    private int _watchingTurns;
    private int _ignoreUntil;
    private int _depth, _arrivedTurn, _passiveTurns;
    private int _attributeIndex;

    public GameAction Decide(World world)
    {
        var me = world.Player;
        if (SpendXp(world, me) is { } purchase)
            return purchase;

        var hostiles = world.Actors
            .Where(a => a != me && !a.IsDead && world.AreHostile(me, a) && world.PlayerCanSee(a))
            .ToList();
        var threats = hostiles.Where(a => IsAfterMe(world, a) || World.Distance(a.Position, me.Position) <= 2).ToList();
        double hp = (double)me.Hp / me.MaxHp;

        if (world.Depth != _depth)
            (_depth, _arrivedTurn) = (world.Depth, world.Turn);
        bool overstayed = world.Turn - _arrivedTurn > LevelTurnBudget;

        // 1. Survival.
        if (threats.Count > 0 && hp < 0.5 && Potion(me) is { } potion)
            return new DrinkAction(potion.Id);
        if (threats.Count == 0 && hp < 0.25 && Potion(me) is { } spare)
            return new DrinkAction(spare.Id);

        if (BetterGear(me) is { } upgrade)
            return new EquipAction(upgrade.Id);

        if (threats.Count > 0)
        {
            _watchingTurns = 0;
            // Out of potions and losing: break off to somewhere none of them can see, and recover there.
            if (hp < 0.5 && Potion(me) is null && (threats.Count >= 2 || hp < 0.25)
                && !threats.Any(t => t.Speed > me.Speed)
                && Hideout(world, me, threats) is { } hide && world.StepToward(me, hide) is { } away)
                return new MoveAction(away);
            // Holding a corridor is good until it turns into a stand-off (a leashed guard that can't
            // come, a squad jammed behind a fleeing one): then shoot whatever can be shot, or go in.
            bool patient = _passiveTurns < 6;
            var action = Fight(world, me, threats, allowHold: patient);
            if (action is WaitAction && !patient)
                action = Fight(world, me, hostiles, allowHold: false);
            if (action is WaitAction && _passiveTurns > 12 && world.Turn >= _ignoreUntil)
                _ignoreUntil = world.Turn + 30;
            _passiveTurns = action is WaitAction ? _passiveTurns + 1 : 0;
            if (action is WaitAction && world.Turn < _ignoreUntil)
                return Explore(world, me, overstayed) ?? action;
            return action;
        }
        _passiveTurns = 0;

        // 2. Others are fighting each other and haven't noticed me: let them wear each other down.
        var busy = hostiles.Where(h => world.SquadOf(h) is { State: SquadState.Engage } s && s.TargetId != me.Id).ToList();
        if (busy.Count > 0 && _watchingTurns < 25)
        {
            _watchingTurns++;
            return new WaitAction();
        }
        if (busy.Count == 0)
            _watchingTurns = 0;
        // Then go after whoever is left, unless they can't be reached (a guard behind its own
        // squadmates, say): then get on with the level for a while.
        if (hostiles.Count > 0 && world.Turn >= _ignoreUntil && hp >= 0.7 && !overstayed)
        {
            var attack = Fight(world, me, hostiles);
            if (attack is not WaitAction)
                return attack;
            _ignoreUntil = world.Turn + 30;
        }

        // 3. Quiet: recover, loot, explore, descend.
        if (hp < 0.7)
            return new WaitAction();
        if (world.ItemsAt(me.Position).Any(i => Wanted(me, i)) && me.Inventory is { IsFull: false })
            return new PickUpAction();
        var fov = world.Map.PlayerFov.BooleanResultView;
        var loot = world.FloorItems
            .Where(f => fov[f.Position] && Wanted(me, f.Item))
            .OrderBy(f => World.Distance(f.Position, me.Position))
            .FirstOrDefault();
        if (loot is not null && !overstayed && world.StepToward(me, loot.Position) is { } toLoot)
            return new MoveAction(toLoot);
        return Explore(world, me, overstayed) ?? new WaitAction();
    }

    /// <summary>Explore the level (unless it's time to leave), then take the stairs.</summary>
    private static GameAction? Explore(World world, Actor me, bool overstayed)
    {
        if (!overstayed && Exploration.NextStep(world) is { } explore)
            return new MoveAction(explore);
        if (world.Map.StairsDown is { } stairs && world.Map.Explored[stairs])
        {
            if (me.Position == stairs)
                return new DescendAction();
            if (world.StepToward(me, stairs) is { } toStairs)
                return new MoveAction(toStairs);
        }
        if (Exploration.NextStep(world) is { } search)
            return new MoveAction(search); // stairs not found yet: keep looking
        return null;
    }

    /// <summary>Turns on one level before the bot stops picking fights and heads for the stairs.</summary>
    private const int LevelTurnBudget = 1500;

    /// <summary>The nearest reachable cell (within 12 steps) that none of the threats can see.</summary>
    private static Point? Hideout(World world, Actor me, List<Actor> threats)
    {
        var visited = new HashSet<Point> { me.Position };
        var queue = new Queue<(Point P, int D)>();
        queue.Enqueue((me.Position, 0));
        while (queue.Count > 0)
        {
            var (cur, d) = queue.Dequeue();
            if (d > 0 && threats.All(t => !world.HasLineOfSight(t.Position, cur) && World.Distance(t.Position, cur) > 1))
                return cur;
            if (d >= 12)
                continue;
            foreach (var dir in World.EightWay)
            {
                var nb = cur + dir;
                if (world.IsFree(nb) && visited.Add(nb))
                    queue.Enqueue((nb, d + 1));
            }
        }
        return null;
    }

    private static GameAction Fight(World world, Actor me, List<Actor> enemies, bool allowHold = true)
    {
        // Something next to me: hit the most valuable adjacent target with whichever weapon does more.
        var adjacent = enemies.Where(e => world.IsAdjacent(e.Position, me.Position)).ToList();
        if (adjacent.Count > 0)
        {
            var slowOnly = adjacent.All(e => e.Speed < me.Speed);
            if (slowOnly && adjacent.Count == 1 && me.Equipment?.Bow is not null
                && KiteStep(world, me, adjacent) is { } kite)
                return new MoveAction(kite); // step back from the shambler, shoot it on the way in
            var target = adjacent.OrderByDescending(e => Priority(world, e, me)).First();
            double melee = Expected(world, me, target, ranged: false);
            double bow = me.Equipment?.Bow is not null && world.HasClearShot(me.Position, target.Position)
                ? Expected(world, me, target, ranged: true) : 0;
            return bow > melee ? new FireAction(target.Id) : new AttackAction(target.Id);
        }

        // Outnumbered in the open: back into a corridor so they come one at a time.
        if (enemies.Count(e => IsAfterMe(world, e)) >= 3 && !world.IsChokepoint(me.Position)
            && NearbyChokepoint(world, me, enemies) is { } choke && world.StepToward(me, choke) is { } toChoke)
            return new MoveAction(toChoke);

        // Shoot the best target in range.
        if (me.Equipment?.Bow is not null)
        {
            var shot = enemies
                .Where(e => World.Distance(e.Position, me.Position) <= me.Range && world.HasClearShot(me.Position, e.Position))
                .OrderByDescending(e => Priority(world, e, me))
                .FirstOrDefault();
            if (shot is not null)
                return new FireAction(shot.Id);
        }

        // Hold a chokepoint and let them come; otherwise close in on the most important target.
        if (allowHold && world.IsChokepoint(me.Position) && enemies.Any(e => IsAfterMe(world, e)))
            return new WaitAction();
        var goal = enemies.OrderByDescending(e => Priority(world, e, me)).First();
        return world.StepToward(me, goal.Position) is { } step ? new MoveAction(step) : new WaitAction();
    }

    /// <summary>Who to hit first: raisers and healers, then shooters, then the wounded and the near.</summary>
    private static double Priority(World world, Actor e, Actor me)
    {
        var role = world.SquadOf(e)?.RoleOf(e) ?? SquadRole.Warrior;
        double score = role switch
        {
            SquadRole.Necromancer => 50,
            SquadRole.Shaman => 30,
            SquadRole.Runner when world.SquadOf(e)?.HelpTargetSquadId is not null => 35,
            SquadRole.Archer => 20,
            _ => 10,
        };
        score += 15 * (1 - (double)e.Hp / e.MaxHp);
        score -= 2 * World.Distance(e.Position, me.Position);
        return score;
    }

    private static double Expected(World world, Actor me, Actor target, bool ranged)
    {
        int power = ranged ? me.RangedAttack : me.Attack;
        double avgHit = Math.Max(1, (power + 1) / 2.0 - target.Defense / 2.0);
        return world.HitChance(me, target, ranged) / 100.0 * avgHit;
    }

    /// <summary>
    /// Whether this enemy is coming for me: its squad is hunting me, or it's fighting and I'm the
    /// nearest enemy it can see (archers shoot whoever is nearest, whatever their squad is after).
    /// </summary>
    private static bool IsAfterMe(World world, Actor e) =>
        world.SquadOf(e) is { } s
        && (s.TargetId == world.Player.Id && s.State is SquadState.Engage or SquadState.Alert
            || s.State == SquadState.Engage && world.NearestVisibleHostile(e) == world.Player);

    /// <summary>A free cell next to me, out of the enemies' reach, from which I can still shoot them all.</summary>
    private static Direction? KiteStep(World world, Actor me, List<Actor> enemies) =>
        World.EightWay
            .Where(d => world.IsFree(me.Position + d)
                        && enemies.All(e => World.Distance(e.Position, me.Position + d) >= 2
                                            && World.Distance(e.Position, me.Position + d) <= me.Range
                                            && world.HasClearShot(me.Position + d, e.Position)))
            .Select(d => (Dir: (Direction?)d, Score: enemies.Sum(e => World.Distance(e.Position, me.Position + d))))
            .OrderByDescending(x => x.Score)
            .Select(x => x.Dir)
            .FirstOrDefault();

    /// <summary>The closest chokepoint within a few steps that isn't nearer the enemy than I am.</summary>
    private static Point? NearbyChokepoint(World world, Actor me, List<Actor> enemies)
    {
        int nearestEnemy = enemies.Min(e => World.Distance(e.Position, me.Position));
        Point? best = null;
        int bestDist = int.MaxValue;
        for (int dy = -5; dy <= 5; dy++)
        for (int dx = -5; dx <= 5; dx++)
        {
            var p = me.Position + new Point(dx, dy);
            if (!world.Map.InBounds(p) || !world.IsFree(p) || !world.IsChokepoint(p))
                continue;
            int toEnemy = enemies.Min(e => World.Distance(e.Position, p));
            int dist = World.Distance(p, me.Position);
            if (toEnemy >= nearestEnemy && toEnemy >= 2 && dist < bestDist)
                (best, bestDist) = (p, dist);
        }
        return best;
    }

    private GameAction? SpendXp(World world, Actor me)
    {
        if (me.Character is not { } c)
            return null;
        for (int i = 0; i < BuildPlan.Length; i++)
        {
            var step = BuildPlan[i];
            var id = step[(step.IndexOf(':') + 1)..];
            if (!world.Content.Skills.ContainsKey(id))
                continue;
            int wanted = BuildPlan.Take(i + 1).Count(s => s == step); // this entry is that skill's Nth rank
            if (c.Rank(id) >= wanted)
                continue;
            return c.CheckLearn(id) is null ? new LearnSkillAction(id) : null; // save up for it
        }
        // Plan done: attributes in rotation.
        for (int i = 0; i < AttributeCycle.Length; i++)
        {
            var attr = AttributeCycle[(_attributeIndex + i) % AttributeCycle.Length];
            if (c.CheckRaise(attr) is null)
            {
                _attributeIndex = (_attributeIndex + i + 1) % AttributeCycle.Length;
                return new RaiseAttributeAction(attr);
            }
            if (c.CheckRaise(attr) == Events.FailureReason.NotEnoughXp)
                return null;
        }
        return null;
    }

    private static Item? Potion(Actor me) =>
        me.Inventory?.Items.FirstOrDefault(i => i.Def.Category == ItemCategory.Potion);

    private static double Score(Item? i) => ItemValue.Of(i);

    private static Item? Slot(Actor me, ItemCategory c) => c switch
    {
        ItemCategory.Weapon => me.Equipment?.Weapon,
        ItemCategory.Bow => me.Equipment?.Bow,
        _ => me.Equipment?.Armor,
    };

    /// <summary>Potions always; equipment only if it beats what's worn.</summary>
    private static bool Wanted(Actor me, Item item) =>
        item.Def.Category == ItemCategory.Potion
        || item.Def.IsEquipment && Score(item) > Score(Slot(me, item.Def.Category));

    private static Item? BetterGear(Actor me) =>
        me.Inventory?.Items.FirstOrDefault(i => i.Def.IsEquipment && me.Equipment?.IsEquipped(i) == false
                                                && Score(i) > Score(Slot(me, i.Def.Category)));
}
