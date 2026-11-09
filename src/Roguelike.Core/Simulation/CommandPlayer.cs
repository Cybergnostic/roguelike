using Roguelike.Core.Actions;
using Roguelike.Core.Entities;
using Roguelike.Core.Items;
using SadRogue.Primitives;

namespace Roguelike.Core.Simulation;

/// <summary>
/// Turns typed commands ("explore", "go 12 5", "fight 7", "learn steady_aim") into player actions,
/// one at a time, for scripted play: the text front end (tools/Roguelike.Play) and the window's
/// follow mode run the same commands, so a game played in one can be watched in the other.
/// <para>
/// Usage: <c>foreach (var action in player.Run(line)) world.Step(action);</c> The enumerator checks
/// the world between actions, so it must be stepped before asking for the next one. Multi-turn
/// commands stop early when a new enemy comes into view or the player gets hurt
/// (<see cref="StopReason"/> says why).
/// </para>
/// </summary>
public sealed class CommandPlayer(World world)
{
    public const string Help =
        "n s e w ne nw se sw [count] | wait [count] | rest [max] | explore [max] | go X Y | " +
        "attack ID | fire ID | fight ID [max] | pickup | drink | equip ITEM | drop ITEM | raise ATTR | learn SKILL | " +
        "stairs (walk to the known stairs) | descend | ascend | close (an adjacent gate; walk into one to open it) | pay (the toll at a nearby gate) | buy NPC ITEM (walk into someone to talk)";

    /// <summary>Why the last command ended early, or null if it simply finished.</summary>
    public string? StopReason { get; private set; }

    public IEnumerable<GameAction> Run(string line)
    {
        StopReason = null;
        var c = line.Split('#')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (c.Length == 0)
            return [];
        var p = world.Player;
        int Arg(int i, int dflt) => c.Length > i && int.TryParse(c[i], out var v) ? v : dflt;

        switch (c[0])
        {
            case "n" or "s" or "e" or "w" or "ne" or "nw" or "se" or "sw":
            {
                var dir = c[0] switch
                {
                    "n" => Direction.Up, "s" => Direction.Down, "e" => Direction.Right, "w" => Direction.Left,
                    "ne" => Direction.UpRight, "nw" => Direction.UpLeft, "se" => Direction.DownRight, _ => Direction.DownLeft,
                };
                return Repeat(Arg(1, 1), () => new MoveAction(dir), interruptible: Arg(1, 1) > 1);
            }
            case "wait": return Repeat(Arg(1, 1), () => new WaitAction(), interruptible: Arg(1, 1) > 1);
            case "rest": return Repeat(Arg(1, 200), () => p.Hp >= p.MaxHp ? null : new WaitAction(), interruptible: true);
            case "explore": return Repeat(Arg(1, 300), ExploreStep, interruptible: true, stopOnItems: true);
            case "go":
            {
                var goal = new Point(Arg(1, 0), Arg(2, 0));
                return Repeat(200, () => world.StepAroundOthers(p, goal) is { } d ? new MoveAction(d) : null, interruptible: true);
            }
            case "walk":
            {
                var goal = new Point(Arg(1, 0), Arg(2, 0));
                return Repeat(200, () => world.StepAroundOthers(p, goal, knownOnly: true) is { } d ? new MoveAction(d) : null, interruptible: true);
            }
            case "charge": return [new ChargeAction(Arg(1, -1))];
            case "fight": return Fight(Arg(1, -1), Arg(2, 12));
            case "attack": return [new AttackAction(Arg(1, -1))];
            case "fire": return [new FireAction(Arg(1, -1))];
            case "pickup": return [new PickUpAction()];
            case "descend": return [new DescendAction()];
            case "ascend": return [new AscendAction()];
            case "close": return [new CloseGateAction()];
            case "pay": return [new PayTollAction()];
            case "sneak": return [new ToggleSneakAction()];
            case "spare": return [new SpareAction(Arg(1, -1))];
            case "buy" when c.Length > 2: return [new BuyAction(Arg(1, -1), c[2])];
            case "stairs":
                if (world.Map.StairsDown is { } stairs && world.Map.Explored[stairs])
                    return Repeat(300, () => world.StepAroundOthers(p, stairs) is { } d ? new MoveAction(d) : null, interruptible: true);
                StopReason = "you haven't found the stairs";
                return [];
            case "drink":
                if (p.Inventory?.Items.FirstOrDefault(i => i.Def.Category == ItemCategory.Potion) is { } potion)
                    return [new DrinkAction(potion.Id)];
                StopReason = "no potion";
                return [];
            case "equip": return [new EquipAction(Arg(1, -1))];
            case "drop": return [new DropAction(Arg(1, -1))];
            case "raise" when c.Length > 1: return [new RaiseAttributeAction(c[1])];
            case "learn" when c.Length > 1: return [new LearnSkillAction(c[1])];
            default:
                StopReason = $"unknown command '{line.Trim()}' (commands: {Help})";
                return [];
        }
    }

    /// <summary>Shoot when there's a clear shot in range, otherwise melee if adjacent, until the target dies.</summary>
    private IEnumerable<GameAction> Fight(int id, int max)
    {
        var p = world.Player;
        for (int i = 0; i < max; i++)
        {
            if (p.IsDead) { StopReason = "you died"; yield break; }
            if (world.GetActor(id) is not { IsDead: false } target) yield break;
            int taken = world.ActionsTaken;
            if (p.Equipment?.Bow is not null && World.Distance(p.Position, target.Position) <= p.Range
                && world.HasClearShot(p.Position, target.Position)
                && !world.IsAdjacent(p.Position, target.Position))
                yield return new FireAction(id);
            else if (world.IsAdjacent(p.Position, target.Position))
                yield return new AttackAction(id);
            else { StopReason = "no shot and not adjacent"; yield break; }
            if (world.ActionsTaken == taken) { StopReason = "blocked"; yield break; }
        }
        if (world.GetActor(id) is { IsDead: false })
            StopReason = "target still alive";
    }

    private IEnumerable<GameAction> Repeat(int count, Func<GameAction?> next, bool interruptible, bool stopOnItems = false)
    {
        var seen = VisibleHostiles().Select(a => a.Id).ToHashSet();
        var itemsSeen = VisibleItems().Select(f => f.Item.Id).ToHashSet();
        for (int i = 0; i < count; i++)
        {
            if (world.IsOver) { StopReason = world.IsWon ? "you won" : "you died"; yield break; }
            var action = next();
            if (action is null)
            {
                if (i == 0) StopReason = "nothing to do";
                yield break;
            }
            int hp = world.Player.Hp, taken = world.ActionsTaken;
            yield return action;
            if (!interruptible)
                continue;
            if (world.Player.Hp < hp) { StopReason = "you were hurt"; yield break; }
            if (VisibleHostiles().FirstOrDefault(a => !seen.Contains(a.Id)) is { } enemy)
            {
                StopReason = $"{enemy.Name} [{enemy.Id}] comes into view";
                yield break;
            }
            if (stopOnItems && VisibleItems().FirstOrDefault(f => !itemsSeen.Contains(f.Item.Id)) is { } item)
            {
                StopReason = $"you spot an item at {item.Position.X},{item.Position.Y}";
                yield break;
            }
            if (world.ActionsTaken == taken) { StopReason = "blocked"; yield break; }
        }
    }

    private GameAction? ExploreStep() =>
        Exploration.NextStep(world) is { } dir ? new MoveAction(dir) : null;

    private IEnumerable<Actor> VisibleHostiles() =>
        world.Actors.Where(a => a != world.Player && !a.IsDead && world.PlayerCanSee(a) && world.AreHostile(world.Player, a));

    private IEnumerable<World.FloorItem> VisibleItems() =>
        world.FloorItems.Where(f => world.Map.PlayerFov.BooleanResultView[f.Position]);
}
