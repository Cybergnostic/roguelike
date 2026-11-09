using System.Text;
using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using Roguelike.Core.Items;
using Roguelike.Core.Simulation;
using SadRogue.Primitives;
using SadRogue.Primitives.GridViews;

// A text front end for playing by hand (or by an AI agent) one command batch at a time.
// The game is deterministic, so every run replays the whole command file from the seed and
// then prints what the player can see now. Append commands to the file and run again.
//
//   dotnet run --project tools/Roguelike.Play -- --seed 7 --commands moves.txt [--log 20] [--full] [--surface]
//
// Commands, one per line (# starts a comment):
//   n s e w ne nw se sw [count]   step / bump-attack (count repeats, stops if a new enemy appears)
//   wait [count]  rest [max]      wait; rest until healed (stops when an enemy appears)
//   explore [max]                 walk to the nearest unexplored area until something shows up
//   go X Y                        travel to a cell, stopping if an enemy appears
//   attack ID   fire ID           melee an adjacent monster / shoot one
//   fight ID [max]                shoot (melee when adjacent) until it dies; stops when neither works
// The interpreter is Roguelike.Core.Simulation.CommandPlayer, shared with the game window's
// --follow mode, so you can watch a scripted game in the real UI.
//   pickup   drink   equip ITEM   drop ITEM
//   raise ATTR   learn SKILL      spend XP (free)
//   stairs   descend              walk to the stairs once found / take them (>)
//   pay                           pay the toll at a nearby camp gate

string? file = null;
ulong seed = 1;
int logLines = 20;
bool full = false;
bool surface = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seed": seed = ulong.Parse(args[++i]); break;
        case "--commands": file = args[++i]; break;
        case "--log": logLines = int.Parse(args[++i]); break;
        case "--full": full = true; break;
        case "--surface": surface = true; break;
    }
}

var world = World.Create(seed, depth: surface ? World.SurfaceDepth : 1);
var log = new List<string>();
var names = new Dictionary<int, string>();
var commands = new CommandPlayer(world);
int lineNo = 0;
string? stopReason = null;

var lines = file is not null && File.Exists(file) ? File.ReadAllLines(file) : [];
foreach (var raw in lines)
{
    lineNo++;
    var line = raw.Split('#')[0].Trim();
    if (line.Length == 0)
        continue;
    if (world.IsOver)
    {
        log.Add($"(line {lineNo} ignored: the game is over)");
        break;
    }
    log.Add($"> {line}");
    foreach (var action in commands.Run(line))
        Do(action);
    stopReason = commands.StopReason;
    if (stopReason is not null)
        log.Add($"  [stopped: {stopReason}]");
}

Print();
return 0;

// ---------------------------------------------------------------------------------------

string? Do(GameAction action)
{
    foreach (var a in world.Actors)
        names[a.Id] = a.Name;
    var events = world.Step(action);
    foreach (var e in events)
        if (Describe(e) is { } text)
            log.Add("  " + text);
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
    ActorAttacked { Missed: true } a => $"{Name(a.AttackerId)} {(a.Ranged ? "shoots at" : "swings at")} {Name(a.TargetId)} and misses",
    ActorAttacked a => $"{Name(a.AttackerId)} {(a.Ranged ? "shoots" : "hits")} {Name(a.TargetId)} for {a.Damage}",
    ActorDied d when d.ActorId == world.Player.Id => "YOU DIE",
    ActorDied d => Seen(d.ActorId) || d.KillerId == world.Player.Id ? $"{Name(d.ActorId)} dies" : "(somewhere, something dies)",
    ActorHealed h when Seen(h.TargetId) => $"{Name(h.HealerId)} heals {Name(h.TargetId)} +{h.Amount}",
    CorpseRaised r => Seen(r.CasterId) || Seen(r.RaisedId) ? $"{Name(r.CasterId)} raises a {r.CorpseKind} corpse as {Name(r.RaisedId)}" : "(chanting somewhere)",
    MinionCrumbled m when Seen(m.ActorId) => $"{Name(m.ActorId)} crumbles to dust",
    ZoneEntered z => $"you enter {z.ZoneName}",
    CampAlarmed c => $"*** a horn sounds from {c.ZoneName} ***",
    SquadStoodDown s => $"*** a squad stands down: {s.Reason} ***",
    GateMoved g => g.Open ? "*** a gate opens ***" : "*** a gate closes ***",
    GateChallenged c => $"{Name(c.GuardId)} challenges you: {c.Price} gold for {c.Turns} turns (command: pay)",
    TrespassWarned w => $"{Name(w.GuardId)} warns you: one more step in means a fight",
    TollPaid t => $"*** you pay {t.Price} gold: {t.Turns} turns in {t.ZoneName} ***",
    PassRunningOut r => $"a guard warns you: {r.TurnsLeft} turns of paid time left",
    PassExpired x => x.Inside ? "*** your paid time is over and you are still inside ***" : "*** your paid time is over ***",
    AbilityUsed a when Seen(a.ActorId) => $"{Name(a.ActorId)} uses {a.AbilityId}{(a.TargetId is { } t ? " on " + Name(t) : "")}",
    Talked t => $"{Name(t.NpcId)} says: {t.Line}{(t.HasGoods ? " (buy ID ITEM)" : "")}",
    ItemBought b => $"bought {ItemName(b.Item)} for {b.Price} gold",
    SoundHeard s => $"(you hear {s.Kind} {(s.From is { } d2 ? "to the " + d2.Type : "far off")}{(s.FactionA is { } fa ? $": {fa}{(s.FactionB is { } fb ? " vs " + fb : "")}" : "")})",
    ActorStaggered s when Seen(s.ActorId) => $"{Name(s.ActorId)} staggers",
    ChargedAt c => $"you charge {Name(c.TargetId)}",
    CampThreatened => "you threaten the guards",
    SquadHeardPlayer h when h.MemberIds.Any(Seen) => "a squad hears you and comes to look",
    SneakChanged c => c.Sneaking ? "you start sneaking" : "you stop sneaking",
    SquadCowed => "a squad loses its nerve and runs",
    SquadTaunted t when Seen(t.SpeakerId) => $"{Name(t.SpeakerId)} shouts a taunt",
    SquadPleaded p when Seen(p.SpeakerId) => $"{Name(p.SpeakerId)} surrenders, offering {p.Offer} (walk into it, then: spare ID or attack ID)",
    MercyAsked m => $"{Name(m.ActorId)} waits for your answer (spare {m.ActorId} / attack {m.ActorId})",
    RansomPaid r => $"{Name(r.ActorId)} pays {r.Gold} gold",
    BeggarSpared b => $"{Name(b.ActorId)} runs off",
    SecretTold s => $"secret: {s.Kind} to the {s.From.Type}",
    LoreDiscovered d => $"*** you read a stone about the {d.Era.Kind} ***",
    TheftCommitted t => t.WitnessId is { } w ? $"*** {Name(w)} sees you steal ***" : "you steal it unseen",
    CampAttackedWhileAway a => $"*** while you were gone {a.ZoneName} was attacked: {(a.Held ? "it held" : "it fell")}, {a.DefendersLost} defenders lost ***",
    CampReinforced r => $"*** {r.ZoneName} has {r.Count} fresh guards ***",
    IncursionBegan i => $"*** {i.FactionId} march on {i.ZoneName} ***",
    ObjectiveCompleted o => $"*** errand done: {o.Item.Def.Name} delivered, +{o.Gold} gold, +{o.Xp} XP{(o.Reward is { } r ? ", " + ItemName(r) : "")} ***",
    LevelEntered l => l.GoingUp ? $"*** you climb back up to depth {l.Depth} ***" : $"*** you descend to depth {l.Depth} ***",
    GameWon => "*** YOU ESCAPE THE DUNGEON: YOU WIN ***",
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
        ItemCategory.Coins => "money",
        ItemCategory.Relic => "relic",
        _ => $"heal {i.Def.Heal}",
    };
    foreach (var a in i.Affixes)
        stats += $", {a.Value:+0;-0} {a.Def.Stat}";
    if (i.Special is { } special)
        stats += $", {special}";
    var tier = i.Rarity == Rarity.Common ? "" : i.Rarity.ToString().ToLowerInvariant() + " ";
    var title = i.NameWords is { Count: > 0 } words ? $"\"{string.Join(' ', words)}\" " : "";
    return $"{tier}{title}{n} ({stats})";
}

static char Letter(string kind) => kind switch
{
    "goblin_warrior" => 'w', "goblin_archer" => 'a', "goblin_shaman" => 's', "goblin_runner" => 'r',
    "skeleton_warrior" => 'W', "skeleton_archer" => 'A', "goblin_zombie" => 'Z', "necromancer" => 'N', "acolyte" => 'n', "bone_knight" => 'K', "wraith" => 'V', "wolf" => 'l', "boar" => 'b', "giant_spider" => 'x', "bear" => 'E', "bandit" => 'r', "bandit_archer" => 'q', "bandit_chief" => 'R', "peddler" => 'p', "hermit" => 'h', "villager" => 'v', "goblin_brute" => 'B', "goblin_warlord" => 'L',
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
                "Wall" => '#', "Pillar" => 'O', "StairsDown" => '>', "StairsUp" => '<', "CaveMouth" => '>', "Road" => '=', "Brush" => ';', "Tree" => 'T', "Palisade" => '#', "GateOpen" => '/', "GateClosed" => '+', "Waystone" => 'W', "Inscription" => '"', "Rubble" => 'o', "Water" => '~', "Fire" => '*', "GateBroken" => '\'', "LairFloor" => fov[pt] ? ',' : ';', _ => fov[pt] ? '.' : ':',
            };
            sb.Append(ch);
        }
        sb.AppendLine();
    }
    sb.AppendLine("  legend: . lit floor  : remembered floor  , lair floor  O pillar  ! item  % corpse");
    sb.AppendLine("          w/a/s/r goblin warrior/archer/shaman/runner  W/A skeleton warrior/archer  Z zombie  N necromancer");
    sb.AppendLine();

    var zone = map.ZoneAt(p.Position)?.Name ?? "open dungeon";
    sb.AppendLine($"Depth {world.Depth}/{world.Content.Dungeon.Levels}{(map.StairsDown is { } st && map.Explored[st] ? $" (stairs at {st.X},{st.Y})" : "")}  Turn {world.Turn}  at {p.Position.X},{p.Position.Y} ({zone})  HP {p.Hp}/{p.MaxHp}  Melee {p.Attack}  Bow {p.RangedAttack} r{p.Range}  Def {p.Defense}  Acc {p.Accuracy}  Eva {p.Evasion}");
    var pass = world.Camps.FirstOrDefault(k => k.HasPass(world.Turn));
    sb.AppendLine($"Gold {p.Gold}{(pass is null ? "" : $"  Paid time left at {pass.Zone.Name}: {pass.PassTurnsLeft(world.Turn)} turns")}");
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
                      $"  {Intent(a)}{(world.HasClearShot(p.Position, a.Position) && World.Distance(p.Position, a.Position) <= p.Range ? "  (clear shot)" : "")}" +
                      $"  [you hit: melee {world.HitChance(p, a, false)}% bow {world.HitChance(p, a, true)}%; it hits you {world.HitChance(a, p, a.Range > 0 && !world.IsAdjacent(a.Position, p.Position))}%]");
    if (p.IsDead)
        sb.AppendLine("*** YOU ARE DEAD ***");
    if (world.IsWon)
        sb.AppendLine("*** YOU WON ***");
    Console.Write(sb);
}
