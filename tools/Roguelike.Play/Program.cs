using System.Text;
using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Items;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

// A text front end for playing by hand (or by an AI agent) one command batch at a time.
// The game is deterministic, so every run replays the whole command file from the seed and
// then prints what the player can see now. Append commands to the file and run again.
//
//   dotnet run --project tools/Roguelike.Play -- --seed 7 --commands moves.txt [--log 20] [--full]
//
// Commands, one per line (# starts a comment):
//   n s e w ne nw se sw [count]   step / bump-attack (count repeats, stops if a new enemy appears)
//   wait [count]  rest [max]      wait; rest until healed (stops when an enemy appears)
//   explore [max]                 walk to the nearest unexplored area until something shows up
//   go X Y                        travel to a cell, stopping if an enemy appears
//   attack ID   fire ID           melee an adjacent monster / shoot one
//   fight ID [max]                shoot (or melee if no shot) until it dies; stops when neither works
//   pickup   drink   equip ITEM   drop ITEM
//   raise ATTR   learn SKILL      spend XP (free)

string? file = null;
ulong seed = 1;
int logLines = 20;
bool full = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seed": seed = ulong.Parse(args[++i]); break;
        case "--commands": file = args[++i]; break;
        case "--log": logLines = int.Parse(args[++i]); break;
        case "--full": full = true; break;
    }
}

var world = World.Create(seed);
var log = new List<string>();
var names = new Dictionary<int, string>();
int lineNo = 0;
string? stopReason = null;

var lines = file is not null && File.Exists(file) ? File.ReadAllLines(file) : [];
foreach (var raw in lines)
{
    lineNo++;
    var line = raw.Split('#')[0].Trim();
    if (line.Length == 0)
        continue;
    if (world.Player.IsDead)
    {
        log.Add($"(line {lineNo} ignored: you are dead)");
        break;
    }
    log.Add($"> {line}");
    stopReason = Run(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    if (stopReason is not null)
        log.Add($"  [stopped: {stopReason}]");
}

Print();
return 0;

// ---------------------------------------------------------------------------------------

string? Run(string[] c)
{
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
        case "rest":
            return Repeat(Arg(1, 200), () => p.Hp >= p.MaxHp ? null : new WaitAction(), interruptible: true);
        case "explore": return Repeat(Arg(1, 300), ExploreStep, interruptible: true, stopOnItems: true);
        case "go":
        {
            var goal = new Point(Arg(1, 0), Arg(2, 0));
            return Repeat(200, () => world.StepToward(p, goal) is { } d ? new MoveAction(d) : null, interruptible: true);
        }
        case "fight":
        {
            int id = Arg(1, -1);
            for (int i = 0; i < Arg(2, 12); i++)
            {
                if (p.IsDead) return "you died";
                if (world.GetActor(id) is not { IsDead: false } t) return null;
                int turn = world.Turn;
                if (p.Equipment?.Bow is not null && World.Distance(p.Position, t.Position) <= p.Range && world.HasClearShot(p.Position, t.Position))
                    Do(new FireAction(id));
                else if (world.IsAdjacent(p.Position, t.Position))
                    Do(new AttackAction(id));
                else
                    return "no shot and not adjacent";
                if (world.Turn == turn) return "blocked";
            }
            return "target still alive";
        }
        case "attack": return Do(new AttackAction(Arg(1, -1)));
        case "fire": return Do(new FireAction(Arg(1, -1)));
        case "pickup": return Do(new PickUpAction());
        case "drink":
            return p.Inventory!.Items.FirstOrDefault(i => i.Def.Category == ItemCategory.Potion) is { } potion
                ? Do(new DrinkAction(potion.Id)) : "no potion";
        case "equip": return Do(new EquipAction(Arg(1, -1)));
        case "drop": return Do(new DropAction(Arg(1, -1)));
        case "raise": return Do(new RaiseAttributeAction(c[1]));
        case "learn": return Do(new LearnSkillAction(c[1]));
        default: return $"unknown command '{c[0]}'";
    }
}

string? Do(GameAction action)
{
    foreach (var a in world.Actors)
        names[a.Id] = a.Name;
    int hpBefore = world.Player.Hp;
    var events = world.Step(action);
    foreach (var e in events)
        if (Describe(e) is { } text)
            log.Add("  " + text);
    return null;
}

/// <summary>Repeats an action; interruptible runs stop when a new enemy comes into view or you get hurt.</summary>
string? Repeat(int count, Func<GameAction?> next, bool interruptible, bool stopOnItems = false)
{
    var seen = VisibleHostiles().Select(a => a.Id).ToHashSet();
    var itemsSeen = VisibleItems().Select(f => f.Item.Id).ToHashSet();
    for (int i = 0; i < count; i++)
    {
        if (world.Player.IsDead)
            return "you died";
        var action = next();
        if (action is null)
            return i == 0 ? "nothing to do" : null;
        int hp = world.Player.Hp;
        int turn = world.Turn;
        Do(action);
        if (!interruptible)
            continue;
        if (world.Player.Hp < hp)
            return "you were hurt";
        if (VisibleHostiles().FirstOrDefault(a => !seen.Contains(a.Id)) is { } enemy)
            return $"{enemy.Name} [{enemy.Id}] comes into view";
        if (stopOnItems && VisibleItems().FirstOrDefault(f => !itemsSeen.Contains(f.Item.Id)) is { } item)
            return $"you spot {ItemName(item.Item)} at {item.Position.X},{item.Position.Y}";
        if (world.Turn == turn)
            return "blocked";
    }
    return null;
}

GameAction? ExploreStep()
{
    // Nearest reachable walkable cell next to unexplored, walkable-unknown territory.
    var map = world.Map;
    var start = world.Player.Position;
    var dist = new Dictionary<Point, int> { [start] = 0 };
    var queue = new Queue<Point>();
    queue.Enqueue(start);
    while (queue.Count > 0)
    {
        var cur = queue.Dequeue();
        if (cur != start && World.EightWay.Any(d => map.InBounds(cur + d) && !map.Explored[cur + d]))
            return world.StepToward(world.Player, cur) is { } dir ? new MoveAction(dir) : null;
        foreach (var d in World.EightWay)
        {
            var nb = cur + d;
            if (map.InBounds(nb) && map.Walkable[nb] && map.Explored[nb] && !dist.ContainsKey(nb))
            {
                dist[nb] = dist[cur] + 1;
                queue.Enqueue(nb);
            }
        }
    }
    return null;
}

IEnumerable<Actor> VisibleHostiles() =>
    world.Actors.Where(a => a != world.Player && !a.IsDead && world.PlayerCanSee(a) && world.AreHostile(world.Player, a));

IEnumerable<World.FloorItem> VisibleItems() =>
    world.FloorItems.Where(f => world.Map.PlayerFov.BooleanResultView[f.Position]);

string Name(int id) => id == world.Player.Id ? "you"
    : world.GetActor(id) is { } a ? $"{a.Name}[{id}]"
    : names.TryGetValue(id, out var n) ? $"{n}[{id}]" : $"#{id}";

bool Seen(int id) => id == world.Player.Id
    || world.GetActor(id) is { } a && world.Map.PlayerFov.BooleanResultView[a.Position];

string? Describe(GameEvent e) => e switch
{
    ActorAttacked a when !Seen(a.AttackerId) && !Seen(a.TargetId) => null,
    ActorAttacked a => $"{Name(a.AttackerId)} {(a.Ranged ? "shoots" : "hits")} {Name(a.TargetId)} for {a.Damage}",
    ActorDied d when d.ActorId == world.Player.Id => "YOU DIE",
    ActorDied d => Seen(d.ActorId) || d.KillerId == world.Player.Id ? $"{Name(d.ActorId)} dies" : "(somewhere, something dies)",
    ActorHealed h when Seen(h.TargetId) => $"{Name(h.HealerId)} heals {Name(h.TargetId)} +{h.Amount}",
    CorpseRaised r => Seen(r.CasterId) || Seen(r.RaisedId) ? $"{Name(r.CasterId)} raises a {r.CorpseKind} corpse as {Name(r.RaisedId)}" : "(chanting somewhere)",
    MinionCrumbled m when Seen(m.ActorId) => $"{Name(m.ActorId)} crumbles to dust",
    ZoneEntered z => $"you enter {z.ZoneName}",
    SquadMustered m => $"(drums: a band musters in {m.ZoneName})",
    RaidLaunched r => $"(war horns: {r.FactionId} raid {r.ZoneName})",
    SquadSpotted s when s.TargetId == world.Player.Id => $"{Name(s.SpotterId)} spots you!",
    SquadRouted r when r.MemberIds.Any(Seen) => "a squad routs and flees",
    ReinforcementsCalled c => Seen(c.RunnerId) ? $"{Name(c.RunnerId)} brings reinforcements" : "(war drums)",
    ItemPickedUp p => $"picked up {ItemName(p.Item)} (item {p.Item.Id})",
    LootDropped l when Seen(l.ActorId) => $"{Name(l.ActorId)} drops {ItemName(l.Item)}",
    ItemEquipped q => $"equipped {ItemName(q.Item)}",
    PotionDrunk p => $"drank a potion, +{p.Healed} HP",
    XpGained x => $"+{x.Amount} XP ({x.Source} {x.Subject})",
    AttributeRaised r => $"{r.AttributeId} -> {r.NewScore}",
    SkillLearned l => $"learned {l.SkillId} rank {l.NewRank}",
    SpecialUsed u => $"{u.Special} into {Name(u.TargetId)}",
    ActionFailed f => $"failed: {f.Reason}",
    _ => null,
};

string ItemName(Item i)
{
    var n = i.Count > 1 ? $"{i.Count}x {i.Def.Name}" : i.Def.Name;
    if (i.Def.IsEquipment && i.Enchantment != 0)
        n = $"{i.Enchantment:+0;-0} {n}";
    var stats = i.Def.Category switch
    {
        ItemCategory.Weapon => $"atk {i.AttackBonus}",
        ItemCategory.Bow => $"atk {i.AttackBonus} r{i.Def.Range}",
        ItemCategory.Armor => $"def {i.DefenseBonus}",
        _ => $"heal {i.Def.Heal}",
    };
    return $"{n} ({stats})";
}

static char Letter(string kind) => kind switch
{
    "goblin_warrior" => 'w', "goblin_archer" => 'a', "goblin_shaman" => 's', "goblin_runner" => 'r',
    "skeleton_warrior" => 'W', "skeleton_archer" => 'A', "goblin_zombie" => 'Z', "necromancer" => 'N',
    _ => '?',
};

string Intent(Actor a)
{
    if (world.SquadOf(a) is not { } sq)
        return "";
    var role = sq.RoleOf(a);
    var target = sq.TargetId == world.Player.Id ? "you" : sq.TargetId is { } t ? Name(t) : "?";
    return sq.State switch
    {
        SquadState.Flee => "fleeing",
        SquadState.Idle when sq.Home is not null => "guarding",
        SquadState.Idle when sq.IsRaiding => "raiding",
        SquadState.Idle => "patrolling",
        SquadState.Alert => $"searching ({target})",
        SquadState.Engage => $"{role.ToString().ToLowerInvariant()} engaging {target}",
        _ => "",
    } + $" morale {sq.Morale:0}";
}

void Print()
{
    var p = world.Player;
    var map = world.Map;
    var fov = map.PlayerFov.BooleanResultView;
    var c = p.Character!;
    var sb = new StringBuilder();

    foreach (var l in log.TakeLast(logLines))
        sb.AppendLine(l);
    sb.AppendLine();

    int x0 = full ? 0 : Math.Clamp(p.Position.X - 25, 0, map.Width - 51);
    int x1 = full ? map.Width : x0 + 51;
    int y0 = full ? 0 : Math.Clamp(p.Position.Y - 9, 0, Math.Max(0, map.Height - 19));
    int y1 = full ? map.Height : Math.Min(map.Height, y0 + 19);
    sb.AppendLine($"     x={x0}..{x1 - 1}  (tens: {string.Concat(Enumerable.Range(x0, x1 - x0).Select(x => x % 10 == 0 ? (char)('0' + x / 10 % 10) : ' '))})");
    for (int y = y0; y < y1; y++)
    {
        sb.Append($"{y,3}  ");
        for (int x = x0; x < x1; x++)
        {
            var pt = new Point(x, y);
            char ch;
            if (!map.Explored[pt]) ch = ' ';
            else if (pt == p.Position) ch = '@';
            else if (fov[pt] && world.ActorAt(pt) is { } a) ch = Letter(a.Kind);
            else if (fov[pt] && world.ItemsAt(pt).Any()) ch = '!';
            else if (fov[pt] && world.Corpses.Any(k => k.Position == pt)) ch = '%';
            else ch = map.Tiles[pt].ToString() switch
            {
                "Wall" => '#', "Pillar" => 'O', "LairFloor" => fov[pt] ? ',' : ';', _ => fov[pt] ? '.' : ':',
            };
            sb.Append(ch);
        }
        sb.AppendLine();
    }
    sb.AppendLine("  legend: . lit floor  : remembered floor  , lair floor  O pillar  ! item  % corpse");
    sb.AppendLine("          w/a/s/r goblin warrior/archer/shaman/runner  W/A skeleton warrior/archer  Z zombie  N necromancer");
    sb.AppendLine();

    var zone = map.ZoneAt(p.Position)?.Name ?? "open dungeon";
    sb.AppendLine($"Turn {world.Turn}  at {p.Position.X},{p.Position.Y} ({zone})  HP {p.Hp}/{p.MaxHp}  Melee {p.Attack}  Bow {p.RangedAttack} r{p.Range}  Def {p.Defense}");
    sb.AppendLine($"XP {c.Xp} to spend ({c.TotalXp} total).  Attr point costs {c.RaiseCost}.  " +
        $"Attrs: {string.Join(" ", world.Content.Attributes.Select(a => $"{a.Id} {c.Score(a.Id)}"))}");
    var skills = world.Content.Skills.Values.Select(s => $"{s.Id} {c.Rank(s.Id)}/{s.Ranks}" +
        (c.NextRankCost(s.Id) is { } cost ? $" ({cost}{(c.CheckLearn(s.Id) is null ? ", can buy" : "")})" : ""));
    sb.AppendLine("Skills: " + string.Join("; ", skills));
    sb.AppendLine("Inventory: " + string.Join("; ", p.Inventory!.Items.Select(i =>
        $"[{i.Id}] {ItemName(i)}{(p.Equipment!.IsEquipped(i) ? " *equipped*" : "")}")));
    var here = world.ItemsAt(p.Position).ToList();
    if (here.Count > 0)
        sb.AppendLine("Underfoot: " + string.Join(", ", here.Select(ItemName)));
    var items = VisibleItems().Where(f => f.Position != p.Position).ToList();
    if (items.Count > 0)
        sb.AppendLine("Items in view: " + string.Join("; ", items.Select(f => $"{ItemName(f.Item)} at {f.Position.X},{f.Position.Y}")));
    var visible = world.Actors.Where(a => a != p && !a.IsDead && world.PlayerCanSee(a))
        .OrderBy(a => World.Distance(a.Position, p.Position)).ToList();
    sb.AppendLine(visible.Count == 0 ? "No monsters in view." : "Monsters in view:");
    foreach (var a in visible)
        sb.AppendLine($"  [{a.Id}] {Letter(a.Kind)} {a.Name} at {a.Position.X},{a.Position.Y} dist {World.Distance(a.Position, p.Position)}" +
                      $"  HP {a.Hp}/{a.MaxHp} atk {a.Attack} def {a.Defense}{(a.Range > 0 ? $" range {a.Range}" : "")}" +
                      $"  {Intent(a)}{(world.HasClearShot(p.Position, a.Position) && World.Distance(p.Position, a.Position) <= p.Range ? "  (clear shot)" : "")}");
    if (p.IsDead)
        sb.AppendLine("*** YOU ARE DEAD ***");
    Console.Write(sb);
}
