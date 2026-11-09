using Roguelike.Core;
using Roguelike.Core.Actions;
using Roguelike.Core.AI.Squads;
using Roguelike.Core.Entities;
using Roguelike.Core.Events;
using SadConsole;
using SadConsole.Input;
using SadRogue.Primitives;
using Console = SadConsole.Console;

namespace Roguelike.App;

/// <summary>Root screen: map, sidebar, status line and message log. Owns input; draws the World.</summary>
public sealed partial class GameScreen : ScreenObject
{
    public const int MapWidth = 80;
    public const int MapHeight = 24;
    public const int SidebarWidth = 22;
    public const int LogLines = 5;
    public const int Width = MapWidth + SidebarWidth;
    public const int Height = MapHeight + 1 + LogLines;

    private static readonly Dictionary<Keys, Direction> MoveKeys = new()
    {
        [Keys.Up] = Direction.Up, [Keys.Down] = Direction.Down,
        [Keys.Left] = Direction.Left, [Keys.Right] = Direction.Right,
        [Keys.K] = Direction.Up, [Keys.J] = Direction.Down,
        [Keys.H] = Direction.Left, [Keys.L] = Direction.Right,
        [Keys.Y] = Direction.UpLeft, [Keys.U] = Direction.UpRight,
        [Keys.B] = Direction.DownLeft, [Keys.N] = Direction.DownRight,
        [Keys.NumPad8] = Direction.Up, [Keys.NumPad2] = Direction.Down,
        [Keys.NumPad4] = Direction.Left, [Keys.NumPad6] = Direction.Right,
        [Keys.NumPad7] = Direction.UpLeft, [Keys.NumPad9] = Direction.UpRight,
        [Keys.NumPad1] = Direction.DownLeft, [Keys.NumPad3] = Direction.DownRight,
    };

    private readonly Console _map = new(MapWidth, MapHeight);
    private readonly Console _sidebar = new(SidebarWidth, MapHeight) { Position = new Point(MapWidth, 0) };
    private readonly Console _status = new(Width, 1) { Position = new Point(0, MapHeight) };
    private readonly Console _log = new(Width, LogLines) { Position = new Point(0, MapHeight + 1) };
    private readonly List<(string Text, Color Color)> _messages = [];

    private World _world;

    public GameScreen()
    {
        Children.Add(_map);
        Children.Add(_sidebar);
        Children.Add(_status);
        Children.Add(_log);
        _world = NewWorld();
        ApplyStartOptions();
        Draw();
    }

    /// <summary>Seed for the first game, from --seed; later games are random.</summary>
    public static ulong? StartSeed { get; set; }

    /// <summary>Start at dungeon level 1 instead of the surface (--dungeon, and always when following a command file).</summary>
    public static bool StartInDungeon { get; set; }

    private World NewWorld()
    {
        var seed = StartSeed ?? (ulong)Random.Shared.NextInt64();
        StartSeed = null;
        var world = World.Create(seed, MapWidth, MapHeight, depth: StartInDungeon ? 1 : World.SurfaceDepth);
        _messages.Clear();
        _commands = null; // follow mode drives the new world from here on
        _following = null;
        AddMessage(StartInDungeon ? $"You enter the goblin-infested halls. (seed {seed})" : $"You come down the old road into the valley. (seed {seed})", Theme.Text);
        if (FollowFile is not null)
            AddMessage($"Following commands from {Path.GetFileName(FollowFile)}.", Theme.Intent);
        if (world.Objective is { } errand)
            AddMessage($"Errand: bring the {errand.Item.Def.Name} from the back of the goblin camp to the waystone (far west).", Theme.Warning);
        AddMessage("Move/attack: arrows, numpad, hjklyubn.  Wait: . 5 Space.  Stairs: > <  Close gate: C  Quit: Esc.", Theme.Text);
        AddMessage("Items: g pick up  i inventory  e equip  q drink  d drop  f fire bow   c character  x examine  t toll", Theme.Text);
        AddMessage("Debug: F1 reveal  F2 squads  F3 heatmap  F5 spectator  P autoplay", Theme.Intent);
        return world;
    }

    public override bool ProcessKeyboard(Keyboard keyboard)
    {
        if (InModalUi)
        {
            if (ModalInput(keyboard) is { } chosen && !_world.IsOver)
                PlayTurn(chosen);
            Draw();
            return true;
        }

        if (keyboard.IsKeyPressed(Keys.Escape))
        {
            Game.Instance.MonoGameInstance.Exit();
            return true;
        }

        if (HandleDebugKeys(keyboard))
        {
            Draw();
            return true;
        }

        if (_world.IsOver)
        {
            bool sameSeed = keyboard.IsKeyPressed(Keys.S);
            if (sameSeed || keyboard.IsKeyPressed(Keys.R))
            {
                if (sameSeed)
                    StartSeed = _world.Seed;
                _world = NewWorld();
                Draw();
            }
            return true;
        }

        if (IsFollowing)
            return true; // the command file is playing; only debug keys and Esc work

        GameAction? action = null;
        foreach (var (key, dir) in MoveKeys)
            if (keyboard.IsKeyPressed(key)) { action = new MoveAction(dir); break; }
        bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        if (keyboard.IsKeyPressed(Keys.OemPeriod) && shift)
            action = new DescendAction(); // '>'
        else if (keyboard.IsKeyPressed(Keys.OemComma) && shift)
            action = new AscendAction(); // '<'
        else if (keyboard.IsKeyPressed(Keys.C) && shift)
            action = new CloseGateAction();
        else if (keyboard.IsKeyPressed(Keys.OemPeriod) || keyboard.IsKeyPressed(Keys.NumPad5)
            || keyboard.IsKeyPressed(Keys.Space))
            action = new WaitAction();
        int messagesBefore = _messageCount;
        action ??= ItemCommand(keyboard);

        if (action is null)
        {
            // An item key may have opened a menu or just printed a message.
            bool changed = InModalUi || _messageCount != messagesBefore;
            if (changed)
                Draw();
            return changed;
        }

        PlayTurn(action);
        Draw();
        return true;
    }

    /// <summary>Runs one turn and logs what happened.</summary>
    private void PlayTurn(GameAction action)
    {
        // Snapshot names and positions before the turn, so actors that die mid-turn can still be described.
        var before = _world.Actors.ToDictionary(a => a.Id, a => (a.Name, a.Position));
        var events = _world.Step(action);

        var fov = _world.Map.PlayerFov.BooleanResultView;
        bool IsVisible(int id) => _reveal ||
            (_world.GetActor(id) is { } a ? fov[a.Position] : before.TryGetValue(id, out var b) && fov[b.Position]);
        var text = new EventText(_world, id => $"the {before.GetValueOrDefault(id).Name ?? "thing"}", IsVisible);
        bool playerMoved = false;

        var said = new HashSet<string>();
        foreach (var e in events)
        {
            playerMoved |= e is ActorMoved m && m.ActorId == _world.Player.Id;
            // One copy of each line per turn ("You hear the clash of weapons." can fire a lot).
            if (text.Describe(e) is not { } line || !said.Add(line))
                continue;
            var color = e switch
            {
                ItemPickedUp { Item.Rarity: > Roguelike.Core.Items.Rarity.Common } p => Theme.ForRarity(p.Item.Rarity),
                LootDropped { Item.Rarity: > Roguelike.Core.Items.Rarity.Common } l => Theme.ForRarity(l.Item.Rarity),
                ItemEquipped { Item.Rarity: > Roguelike.Core.Items.Rarity.Common } q => Theme.ForRarity(q.Item.Rarity),
                ActorDied or SquadRouted => Theme.Good,
                ActorAttacked a when a.TargetId == _world.Player.Id && a.Damage > 0 => Theme.Danger,
                SquadSpotted or ReinforcementsCalled or CorpseRaised or ZoneEntered or RaidLaunched or SquadMustered => Theme.Warning,
                GateChallenged or TrespassWarned or PassRunningOut or PassExpired or CampAlarmed => Theme.Warning,
                LoreDiscovered => Theme.Warning,
                TheftCommitted { WitnessId: not null } => Theme.Danger,
                IncursionBegan => Theme.Warning,
                LevelEntered or GameWon or TollPaid or ObjectiveCompleted => Theme.Good,
                MinionCrumbled or AttributeRaised or SkillLearned or SpecialUsed => Theme.Good,
                XpGained => Theme.Intent,
                _ => Theme.Text,
            };
            if (e is ActorDied d && d.ActorId == _world.Player.Id)
                color = Theme.Danger;
            AddMessage(line, color);
        }
        if (playerMoved)
            DescribeItemsUnderfoot();
        if (playerMoved && _world.Map.StairsDown == _world.Player.Position)
            AddMessage(_world.Depth == World.SurfaceDepth ? "A cave mouth opens here. (> to climb down)"
                : _world.Depth >= _world.Content.Dungeon.Levels
                ? "Stairs lead up and out of the dungeon. (> to leave and win)"
                : "There are stairs down here. (> to descend)", Theme.Warning);
        if (playerMoved && _world.Map.StairsUp == _world.Player.Position)
            AddMessage("There are stairs up here. (< to climb back)", Theme.Warning);
        bool canSpend = CanSpendXp();
        if (canSpend && !_couldSpendXp && !_sheetOpen)
            AddMessage("You feel you could learn something new. (c: character)", Theme.Warning);
        _couldSpendXp = canSpend;
        if (_world.IsOver)
        {
            _autoplay = false;
            AddMessage($"R: new game.  S: retry this seed ({_world.Seed}).  Esc: quit.", _world.IsWon ? Theme.Good : Theme.Danger);
        }
    }

    private int _messageCount;
    private bool _couldSpendXp;

    private void AddMessage(string text, Color color)
    {
        _messageCount++;
        _messages.Add((text, color));
        if (_messages.Count > LogLines)
            _messages.RemoveAt(0);
    }

    private void Draw()
    {
        DrawMap();
        DrawSidebar();
        DrawStatus();
        DrawModalUi();

        _log.Clear();
        for (int i = 0; i < _messages.Count; i++)
            _log.Print(1, i, _messages[i].Text, _messages[i].Color);
    }

    private void DrawMap()
    {
        var map = _world.Map;
        var fov = map.PlayerFov.BooleanResultView;
        var heat = HeatmapView();

        _map.Clear();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var p = new Point(x, y);
            if (!map.Explored[p] && !_reveal)
                continue;
            var look = Theme.ForTile(map.Tiles[p]);
            var fg = fov[p] ? look.Foreground
                : map.Explored[p] ? Theme.Remembered(look.Foreground)
                : Theme.Revealed(look.Foreground);
            _map.SetGlyph(x, y, look.Glyph, fg, heat?.Invoke(p) ?? Theme.Background);
        }

        foreach (var corpse in _world.Corpses)
        {
            if (!fov[corpse.Position] && !_reveal)
                continue;
            var look = Theme.ForCorpse(corpse.Kind);
            _map.SetGlyph(corpse.Position.X, corpse.Position.Y, look.Glyph, look.Foreground, _map.Surface[corpse.Position.X, corpse.Position.Y].Background);
        }
        DrawFloorItems();
        if (_showSquads)
            DrawSquadMarkers();

        foreach (var actor in _world.Actors)
        {
            if (actor.IsDead || !(fov[actor.Position] || _reveal))
                continue;
            var look = Theme.ForKind(actor.Kind);
            var squad = _world.SquadOf(actor);
            var bg = squad?.State == SquadState.Flee ? Theme.FleeingBackground
                : _showSquads && squad is not null ? Theme.SquadTint(squad.Id)
                : _map.Surface[actor.Position.X, actor.Position.Y].Background;
            var fg = actor == _world.Player && _world.ObserverMode ? Theme.Intent : look.Foreground;
            _map.SetGlyph(actor.Position.X, actor.Position.Y, look.Glyph, fg, bg);
        }
    }

    /// <summary>Brogue-style list of visible monsters, with health and what they're up to.</summary>
    private void DrawSidebar()
    {
        _sidebar.Clear();
        _sidebar.Fill(Theme.Text, Theme.SidebarBackground, 0);
        if (_showSquads)
        {
            DrawSquadList();
            return;
        }

        var player = _world.Player;
        var visible = _world.Actors
            .Where(a => a != player && !a.IsDead && (_reveal || _world.PlayerCanSee(a)))
            .OrderBy(a => World.Distance(a.Position, player.Position))
            .ToList();

        int y = 0;
        _sidebar.Print(1, y++, visible.Count == 0 ? "Nothing in sight" : "In sight", Theme.Text, Theme.SidebarBackground);
        y++;
        foreach (var actor in visible)
        {
            if (y + 2 >= MapHeight)
            {
                _sidebar.Print(1, y, $"...and {visible.Count - visible.IndexOf(actor)} more", Theme.Text, Theme.SidebarBackground);
                break;
            }
            var look = Theme.ForKind(actor.Kind);
            _sidebar.SetGlyph(1, y, look.Glyph, look.Foreground, Theme.SidebarBackground);
            _sidebar.Print(3, y++, Truncate(actor.Name, SidebarWidth - 4), Theme.Text, Theme.SidebarBackground);
            DrawBar(1, y++, SidebarWidth - 2, actor.Hp, actor.MaxHp);
            _sidebar.Print(1, y++, Truncate(Intent(actor), SidebarWidth - 2), Theme.Intent, Theme.SidebarBackground);
        }
    }

    private void DrawBar(int x, int y, int width, int value, int max)
    {
        int filled = max <= 0 ? 0 : (int)Math.Ceiling(width * Math.Clamp(value, 0, max) / (double)max);
        var color = value * 3 <= max ? Theme.Danger : value * 3 <= max * 2 ? Theme.Warning : Theme.Good;
        for (int i = 0; i < width; i++)
            _sidebar.SetGlyph(x + i, y, ' ', Color.Black, i < filled ? color.GetDarker() : Theme.BarEmpty);
        var label = $"{Math.Max(0, value)}/{max}";
        _sidebar.Print(x + (width - label.Length) / 2, y, label, Color.White, color.GetDarker());
        for (int i = 0; i < label.Length; i++)
        {
            int cx = x + (width - label.Length) / 2 + i;
            _sidebar.SetBackground(cx, y, cx - x < filled ? color.GetDarker() : Theme.BarEmpty);
        }
    }

    /// <summary>Show intent, so the player can plan around it.</summary>
    private string Intent(Actor actor)
    {
        if (_world.SquadOf(actor) is not { } squad)
            return "";
        var role = squad.RoleOf(actor);
        return squad.State switch
        {
            SquadState.Flee when squad.Rally is not null => "falling back",
            SquadState.Flee => "fleeing in panic",
            SquadState.Idle when squad.Camp is not null && !_world.AreHostile(actor, _world.Player) && _world.CanSee(actor, _world.Player) => "watching you",
            SquadState.Idle when squad.Route is not null => "on patrol",
            SquadState.Idle when role == SquadRole.Necromancer => "brooding",
            SquadState.Idle when squad.Home is not null => "guarding",
            SquadState.Idle when squad.IsRaiding => "marching to raid",
            SquadState.Idle => "patrolling",
            SquadState.Alert => squad.TargetId == _world.Player.Id ? "searching for you" : "searching",
            SquadState.Engage => role switch
            {
                SquadRole.Runner when squad.HelpTargetSquadId is not null => "running for help!",
                SquadRole.Archer => $"shooting at {TargetName(squad)}",
                SquadRole.Necromancer => $"cursing {TargetName(squad)}",
                SquadRole.Shaman => "supporting allies",
                _ when squad.IsHoldingPassage && _world.AdjacentHostile(actor) is null
                    => "holding the passage",
                _ => $"attacking {TargetName(squad)}",
            },
            _ => "",
        };
    }

    private string TargetName(Squad squad) =>
        squad.TargetId == _world.Player.Id ? "you"
        : squad.TargetId is { } id && _world.GetActor(id) is { } t ? t.Name.Split(' ')[0] + "s"
        : "enemies";

    private void DrawStatus()
    {
        var player = _world.Player;
        _status.Clear();
        _status.Print(1, 0, $"HP {Math.Max(0, player.Hp)}/{player.MaxHp}",
            player.Hp * 3 <= player.MaxHp ? Theme.Danger : Theme.Good);
        var bow = player.Equipment?.Bow is null ? "no bow" : $"Bow {player.RangedAttack} r{player.Range}";
        _status.Print(14, 0, $"Atk {player.Attack}  Def {player.Defense}  {bow}", Theme.Text);
        _status.Print(46, 0, $"{(_world.Depth == World.SurfaceDepth ? "Valley" : $"D{_world.Depth}")} T{_world.Turn}", Theme.Text);
        if (player.Character is { } c)
            _status.Print(58, 0, CanSpendXp() ? $"XP {c.Xp} (c)" : $"XP {c.Xp}", CanSpendXp() ? Theme.Warning : Theme.Text);
        _status.Print(72, 0, $"Gold {player.Gold}", Theme.Text);
        if (_world.Camps.FirstOrDefault(camp => camp.HasPass(_world.Turn)) is { } paid)
            _status.Print(83, 0, $"Pass {paid.PassTurnsLeft(_world.Turn)}", Theme.Good);

        var flags = new List<string>();
        if (_world.ObserverMode) flags.Add(_autoplay ? "SPECTATING (auto)" : "SPECTATING");
        if (_reveal) flags.Add("REVEAL");
        if (_showSquads) flags.Add("SQUADS");
        if (_heatmap != 0) flags.Add($"HEAT: {HeatmapName}");
        var text = string.Join("  ", flags);
        _status.Print(Width - text.Length - 1, 0, text, Theme.Warning);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
