# Project handbook

Everything needed to pick this project up cold: vision, decisions, architecture, how to work on it,
current state and roadmap. **Keep it current**: update it whenever a decision, milestone or known
issue changes.

Last updated: 2026-11-08 (character system, step 5 part 1)

---

## 1. Vision

A **classical roguelike RPG** (turn-based, grid, permadeath, procedural, tactical) set in a
**living dungeon**. It's an adventure, not a whole-world simulation like Dwarf Fortress: the game
simulates **one site in depth** (a valley, a ruined fortress, the caves beneath) and the player's
`@` arrives **in the middle of its story**.

Touchstones: Brogue (systems, readability, Dijkstra-map AI), Sil (squad tactics), Caves of Qud
(history-driven content), Cogmind (UI/visual polish), Dwarf Fortress (simulation), Diablo-style
loot (rarity tiers, +X gear).

### Design pillars
1. **Generate history first, then the map.** The site is played forward through eras (dwarves dig
   a mine → something breaks through from below → goblins move in → a necromancer takes the tower).
   The map, factions and artifacts are the *result* of that history.
2. **Factions with goals, on two levels of detail.** A cheap abstract strategic simulation off-screen
   (raids, expansion, alliances); full tactical squad AI near the player. Switch between them cleanly.
3. **The world leaves traces.** Nothing important happens off-screen without evidence: tracks,
   corpses, sounds ("war drums"), rumours, changed rooms. Off-screen events must read as a story.
4. **Readability over hidden depth.** Show enemy intent (sidebar), report unseen events as sounds.
   Deaths should feel like the player's fault.
5. **Player levers on factions.** Lead skeletons into the goblin camp, sell the necromancer's
   location, return a stolen idol. Using or causing fights between factions is core play.
6. **Loot with provenance and flavour.** Lots of loot, rarity tiers and +X gear (the user likes
   these), with artifacts tied to history.
7. **Crafting from the world.** Materials come from places and creatures; stations are places you
   take and hold (the dwarven forge is in goblin territory).
8. **Everything modular.** Simulation separate from presentation; content in data; the look can go
   from ASCII to Cogmind/Brogue/Qud style without touching rules.

### Where AI/LLMs fit (decided)
- Game AI is hand-written (utility/state machines, Dijkstra maps, squads), not machine learning.
- LLMs, if used at all: narrow and optional, e.g. turning free text into a fixed set of intents
  (threaten, bribe, ask about X) when talking to captives; the engine decides outcomes. Never in
  the rules path. Determinism must be preserved.
- AI-assisted development: bots and headless simulations for balance (already started).

---

## 2. Key decisions (and why)

| Decision | Why | Rejected |
|---|---|---|
| **C# / .NET 10** | Most complete maintained roguelike toolkit (GoRogue + SadConsole); good perf; ONNX/ML.NET if ever needed; Caves of Qud is C# | **Rust**: bracket-lib dormant (0.8.7, 3+ years), thin roguelike ecosystem, Bevy churns. **Python/tcod**: too slow for faction sim, weak visual upgrade path |
| **GoRogue 3.0.0-beta11** for algorithms | FOV, pathing, map gen, goal/flee maps, RNG. Display-agnostic | v3 is a long-running beta; expect API surprises |
| **SadConsole 10.10.1 (MonoGame host)** for display | Active, ASCII+tiles, shaders/effects possible; can move to Godot (C#) later if needed | |
| **Simulation core has zero rendering/input/text** | Modularity; headless tests, bots, sims; swappable front ends | |
| **Deterministic from seed + inputs** | Replays, bug repro, seeded dailies, balance sims | |
| **Own `DistanceMap` (BFS) for chase maps** | ~0.09 ms vs ~1.1 ms for GoRogue `GoalMap` + `FleeMap` per update on 80x24, with identical results for uniform-cost Chebyshev; needed for per-faction maps | GoRogue `GoalMap` (kept only as the flee map source) |
| **Monsters are data ids (`Kind`)** | Front end maps kind to glyph/colour (`data/looks.json` via `Theme.cs`); core never knows what things look like | |
| **Content in JSON (`data/`)** | Tuning without recompiling; modding later. Validated at load with file-specific errors | Hard-coded C# templates |
| **Factions are data, relations table** | Adding a faction = data, not code. Unlisted pairs neutral, same faction allied | Enum of factions |
| **Six D&D attributes** (user, 2026-11-08; to be revisited) | Familiar; CHA/INT/WIS get real uses as talking, magic and factions arrive. Their effects live in `attributes.json`, so revisiting is a data edit | 4 Sil-style attributes; none (Brogue) |
| **XP is spent on attributes and skills now; ToME-style classes and skill trees later** (user) | Skills are data, grouped in trees, so a class can later just be "which trees you start with" (plus actives with cooldowns) | Fixed level-ups |
| **Rarity tier and +X are independent** (user) | A +3 white sword and a +0 orange sword are both real choices | Tiers capping +X |
| **One derived-stat vocabulary (`Stat`)** | Attributes, skills and (next) item affixes all add to the same stats, so content can combine freely in data | Per-system bonus fields |
| **Own `SafetyMap` (Brogue flee map)** | Per-faction flee maps needed; GoRogue `FleeMap` cost ~1 ms each | GoRogue `FleeMap` |

---

## 3. Repository layout

```
Roguelike.slnx
data/                        game content (JSON with comments), copied next to every program
  factions.json              factions + relations (hostile/neutral/allied) + raid orders
  monsters.json              stat blocks: faction, role, hp/attack/defense, range, fearless, speed,
                             risesAs (corpse → undead), raise (necromancers), xp (kill reward)
  spawns.json                room occupancy, floor item chance, safe start, weighted squad templates,
                             lairs (unique squads placed in the farthest rooms, with a home)
  items.json                 item types (minDepth), +X enchantment weights (minDepth), loot tables
  dungeon.json               number of levels (10) and how monsters grow per level (HP, XP, attack, accuracy, occupancy)
  combat.json                hit chance (accuracy vs evasion), point-blank penalty, minimum damage
  player.json                player base stats, starting attributes, XP rules, inventory size, starting kit
  attributes.json            the six attributes: what each modifier point adds to derived stats; raise costs
  skills.json                skill trees: ranks, XP costs, prerequisites, per-rank stat bonuses, specials
  looks.json                 front end only: glyph + colour per kind, item and tile
src/Roguelike.Core/          the simulation (GoRogue only; no SadConsole)
  Content/ContentDatabase.cs loads + validates data/; ContentDatabase.Default reads ./data
  World.cs                   all state; Step(action) -> events; spatial/AI helpers
  Map/GameMap.cs             tiles, explored, walkable/transparent views, player FOV, rooms
  Map/MapGenerator.cs        GoRogue rooms-and-mazes, tuned roomy (will be replaced by history-driven gen)
  Map/LairBuilder.cs         rebuilds a room as a lair ("tower": lair floor + pillars); keeps it connected
  Map/DistanceMap.cs         fast BFS Dijkstra map (IGridView<double?>)
  Map/SafetyMap.cs           Brogue flee map built from a DistanceMap
  Entities/Actor.cs          anything that takes turns; Kind, Faction, stats, Fearless, Brain
  Entities/Faction.cs        Faction, Relation, FactionRelations
  Progression/Stat.cs        the derived stats everything adds to (maxHp, meleeAttack, defense, regen...)
  Progression/Character.cs   attribute scores, skill ranks, XP pool; sums bonuses; spending checks
  Items/Item.cs              ItemDef (data), Item (instance: +X Enchantment, Rarity, Count), categories
  Items/Inventory.cs         Inventory (stacking, capacity) and Equipment (weapon/bow/armor slots)
  Actions/                   GameAction records: Move (bump-attack, ally swap), Attack, Wait, RangedAttack,
                             Heal, CallForHelp, RaiseDead, PickUp, Drop, Equip, Drink, Fire,
                             RaiseAttribute, LearnSkill (free actions: Perform returns false)
  Events/GameEvent.cs        structured events emitted during a turn
  AI/IBrain.cs               per-actor decision interface
  AI/Squads/                 Squad (blackboard), SquadDirector (squad-level think), SquadMemberBrain (roles)
  AI/Strategy/FactionPlanner strategic layer, once per turn: lair musters and faction raids
  Simulation/                bots (AggressiveBot, IdleBot, TacticalBot), Simulator (headless games + stats),
                             CommandPlayer (scripted play), Exploration (auto-explore)
src/Roguelike.App/           SadConsole front end
  Program.cs                 startup, --seed N, --spectate
  GameScreen.cs              map/sidebar/status/log, input, turn loop
  GameScreen.Debug.cs        debug views (F1/F2/F3/F5/P), autoplay
  GameScreen.Items.cs        item commands, inventory menus, bow targeting
  GameScreen.Character.cs    character sheet (c): attributes, skill trees, derived stats; letters spend XP
  ItemText.cs                item names ("+1 short sword", "2 potions of healing")
  Theme.cs                   ALL visuals: glyphs, colours (swap for tiles later)
  EventText.cs               events -> log messages (unseen things become sounds)
tools/Roguelike.Sim/         headless simulation report CLI
tools/Roguelike.Play/        text front end for playing by hand or by an AI agent: replays a command file
                             from a seed, prints the view, monsters (ids, hit chances), stats and log.
                             Commands are interpreted by Core's Simulation/CommandPlayer
tools/scripts/sendkey.py     send keys to the game window (UI checks without stealing focus)
tests/Roguelike.Core.Tests/  xUnit: determinism, map connectivity, squad behaviour, observer mode
docs/PROJECT.md              this file
```

---

## 4. Architecture rules (follow these)

1. **Core never references SadConsole**, never produces display strings or colours. It emits
   `GameEvent`s; the front end describes and draws them.
2. **Everything an actor does is a `GameAction`.** Input, AI, bots and future replays all produce
   actions. Rules live in `Perform`. `Perform` returns false if the turn wasn't used.
3. **All randomness goes through `world.Rng`** (ShaiRandom `MizuchiRandom`, seeded). Never
   `System.Random` or `Random.Shared` in Core. Keep iteration orders deterministic (lists, not
   hash sets, when order affects outcomes). Tests verify same seed + inputs = same game.
4. **AI layering:** `SquadDirector.Think` runs once per squad per turn (pooled perception,
   morale, state transitions, runner dispatch), then each actor's `IBrain.Decide` returns an action.
5. **Hostility and perception:** `world.AreHostile(a, b)` (faction table; the observer-mode player is
   nobody's enemy). `world.CanSee(observer, target)`: monster sight, Euclidean radius ~8.5 plus
   Bresenham line of sight. `world.PlayerCanSee(actor)`: the player's FOV (UI, bots only).
   AI never asks "where is the player"; it asks `NearestVisibleHostile` / `AdjacentHostile`.
6. **Dijkstra maps:** per faction, `TowardEnemiesOf(faction)` (DistanceMap) and
   `AwayFromEnemiesOf(faction)` (SafetyMap), rebuilt lazily once per turn at the start of the
   monsters' phase (stale within a turn, which is acceptable). `TowardPlayer` is kept for the debug
   heatmap and tests. `StepDownhill` prefers less crowded cells, which makes groups surround
   instead of queue.
7. **Attacks go through `world.ResolveAttack`**: hit roll (75% + 5% × (accuracy − evasion), clamped
   5–95; −4 accuracy for shooting an adjacent target), then damage 1..attack minus a random 0..defence,
   at least 1 (combat.json). Misses emit `ActorAttacked { Missed: true }`. `world.Damage` applies HP
   loss, death, XP and loot. Loot is rolled at `world.Depth` (+ the victim's `lootDepthBonus`): items
   and +X values deeper than that are left out. Failed player actions emit
   `ActionFailed(reason)` and return false (no turn used); the front end words the reason.
8. **Stats are derived:** `Attack`/`Defense`/`RangedAttack`/`Range`/`MaxHp` = base + equipment (+X
   included) + `actor.Bonus(Stat)` from the `Character` (attributes per modifier point, skills per
   rank; fractions summed, then rounded down). New sources of bonuses (item affixes) should feed the
   same `Stat` list. Spending XP never uses a turn.
9. **Content** comes from `data/*.json` through `ContentDatabase`; `World.Create(seed, w, h, content?)`
   defaults to `ContentDatabase.Default`. Reference everything by id; validate new fields in `Load`.
10. **Turn order** in `World.Step`: player action → update FOV and maps → each squad thinks → each
   non-player actor gains `Speed` energy and acts once per 100 (zombies at 50 act every other turn;
   actors created this turn wait) → remove dead → turn++ → regen → update FOV.
11. **Corpses** are `World.Corpses` records (kind, position, turn), left by every non-player death
   except raised dead ("the dead rise only once"). Raised actors carry `MasterId`; `World.Damage`
   crumbles them when the master dies. Spawn monsters at runtime with `World.SpawnMonster`.

---

## 5. Working on it

### Environment
- .NET 10 SDK is installed **per-user at `~/.dotnet`** (via Microsoft's `dotnet-install.sh`).
  It may not be on PATH. Use:
  `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1`
- Linux (LMDE 7), X11 display `:0`. Screenshots of the game window work with `import -window <id>`.
  The user may have their own game window open with the same title; pick the newest window id.
  To drive the UI, `tools/scripts/sendkey.py <id> <keys>` sends X events straight to that window
  (python-xlib; no focus change, so keys can't land in the user's other windows). Wrap
  `import` and the script in `timeout`; `import` on a vanished window can hang.
- `--seed N` makes a run reproducible. Level generation consumes RNG, so adding content that
  spawns at generation (e.g. floor items) changes every seed's level; re-find demo seeds then.

### Commands
```sh
dotnet build Roguelike.slnx
dotnet test                                     # ~15 s
dotnet run --project src/Roguelike.App          # play
dotnet run --project src/Roguelike.App -- --seed 640 --spectate
dotnet run --project tools/Roguelike.Sim -c Release -- --seeds 50 [--turns 1000] [--bot aggressive|idle] [--observer] [--games]
dotnet run --project tools/Roguelike.Sim -c Release -- --map --start 7   # print seed 7's level as text
dotnet run --project tools/Roguelike.Play -c Release -- --seed 42 --commands moves.txt [--full] [--log 20]
```
**Watching an agent play:** start the window with
`dotnet run --project src/Roguelike.App -- --seed 42 --follow moves.txt`; it plays each command
appended to the file (same interpreter), ~11 turns/s. The agent decides with `Roguelike.Play` on the
same seed and file. `Roguelike.Play` is for playtesting by an agent: append commands to the file (`explore`, `go X Y`,
`n`/`se`... `fight ID`, `fire ID`, `pickup`, `equip ITEM`, `drink`, `rest`, `learn SKILL`, `raise ATTR`;
see the header of its Program.cs) and run again. Multi-turn commands stop when a new enemy appears.
The report gives: death rate, survivors' lowest HP and close calls (<25% HP), share of floor
inside rooms, monsters per level, kills split by "player" vs "others" per faction, events, squad states.

### In-game keys
Move/attack: arrows, numpad, `hjklyubn`. Wait: `.` `5` `Space`. Restart after death: `R`. Quit: `Esc`.
Items: `g`/`,` pick up, `i` inventory (pick an item to use it), `e` equip/remove, `q` drink, `d` drop,
`f` fire bow (Tab next target, `f`/Enter fire, Esc cancel). Character sheet: `c` (letters spend XP).
Debug: `F1` reveal map + all actors, `F2` squad overlay (tints, `x` destinations, `?` last known
target position, squad list with morale), `F3` cycle heatmaps (toward player, then per faction
"toward enemies" and "flee"),
`F5` spectator mode (invisible, invulnerable), `P` autoplay (playing: the aggressive bot plays for
you, a quick way to see progression; spectating: time passes).

### API gotchas found so far
- `World.Distance(a, b)` (static helper) clashes with `SadRogue.Primitives.Distance` inside `World`;
  qualify the type there.
- GoRogue map gen: wall/floor grid is `context.GetFirst<ISettableGridView<bool>>("WallFloor")`
  (true = floor); rooms are `ItemList<Rectangle>` tagged `"Rooms"`.
- `AdjacencyRule.EightWay.DirectionsOfNeighbors()` is an `IEnumerable`, not indexable.
- SadConsole MonoGame host needs `MonoGame.Framework.DesktopGL` referenced explicitly.
  `SetScreenSize` is obsolete; use `SetWindowSizeInCells(w, h, zoom: true)`.
- `ArrayView<T>.Positions()` needs `using SadRogue.Primitives.GridViews;`.

### Conventions
- Commit at milestones (feature works + tests pass) without asking; message ends with the
  co-author line. Don't push or rewrite history without asking. Git identity is repo-local.
- New behaviour gets a headless test. Balance changes get measured with `Roguelike.Sim` before/after.
- Keep this document updated.

---

## 6. Current state

**Done**
- Skeleton: map gen, FOV with memory, bump combat, message log, status line.
- **Goblin squad AI**: roles (warrior, archer, shaman, runner); shared squad knowledge; states
  idle (group patrol) → alert (search last known position) → engage → flee; morale (−25 per death,
  −8 per badly wounded member, regen when not fighting, breaks below 30, rallies at 60);
  warriors surround and hold corridor mouths up to 8 turns while archers shoot; archers kite and
  seek clear shots; shamans heal (cooldown 5, range 6); runners fetch the nearest idle squad.
- **Readability**: sidebar with visible monsters' HP and intent; unseen events reported as sounds;
  unseen attackers are "something".
- **Player kit**: items from data with +X enchantment (incl. cursed) and a rarity field (all
  Common so far); inventory (20 slots, potions stack) and equipment slots; derived stats; loot
  tables per monster and floor items; pick up/drop/equip/drink/fire; inventory menus and bow
  targeting UI; the bot uses the kit (potions, bow, better gear, walks to loot).
- **AI fixes found via a failing test**: runners keep their mission while the squad flees and call
  from shouting range (6 + line of sight); allies swap places, which cured corridor deadlocks
  that had frozen many patrols (idle time 86% → 71%).
- **Observation tools**: debug overlays, spectator mode, autoplay, headless `Roguelike.Sim` report.
- **Balance pass** (2026-11-07), measured with the sim:
  - Map gen: 10–16 wider rooms, all dead ends trimmed. Floor inside rooms went from 29% to 57%.
  - Quiet start: no squad spawns in a room within `safeStartDistance` (15) walking steps.
  - Regen is now `regenInterval` in player.json (20 turns per HP, was 8).
  - Monsters hit harder: warriors attack 6, archers 4. Skeleton warriors' defence went from 2 to 1,
    which ended their 5:1 dominance over goblins (now about 1.5:1).
  - Bigger goblin bands (2–4 warriors); 60% room occupancy.
  - Fewer potions: 15 on the floor table, 10 on the goblin table.
  - Death events carry `KillerId`.
  - The sim report shows close calls and room share, and can print a level with `--map`.
- **Necromancer, part 1** (2026-11-07):
  - Speed/energy system (`speed` in monsters.json).
  - Corpses left by deaths, drawn as `%` in a dim version of the dead kind's colour.
  - `risesAs` per monster: goblins become slow, tough **goblin zombies**; skeletons reassemble.
  - The **necromancer** (`N`) is a unique lair squad (necromancer + 2 skeleton warriors + 1 archer),
    placed in the room farthest from the start. He waits there ("guarding"/"brooding").
    He raises the nearest corpse in reach and sight (range 7, cooldown 8, max 6 servants),
    otherwise fights like an archer with bolts. Raised dead join his squad and the undead faction.
  - His servants crumble to dust when he dies, and raised dead leave no corpse.
  - Sim counts "corpses raised", "minions crumbled" and "player killed by: <kind>".
- **Necromancer, part 3: a living dungeon** (2026-11-07):
  - `World.Lairs`: a lair is a zone, its recipe and the squad holding it; it falls when that squad
    is wiped out. Lairs are placed as far as possible from the start and from each other.
  - **The goblin warren**: a second lair (shaman, warrior, archer; leash 10) that **musters** a
    fresh war band every 100 turns while it stands, if fewer than 3 goblin bands roam.
  - **Raids** (`raid` in factions.json): every 100 turns the goblins send their nearest idle band
    against the necromancer's tower while it stands. Raiders march (no ambling), fight whatever they
    meet, call off the raid when routed, and finish on arrival. Dead raiders feed the necromancer.
  - `FactionPlanner` (AI/Strategy) runs musters and raids once per turn, before squads think.
  - Fix: squadmates following the same leader used to swap places back and forth forever in
    single file (looked frozen). Followers now queue behind each other; only the leader swaps
    through its own squad. A raid leader waits for stragglers at most 10 turns.
  - Events `SquadMustered`, `RaidLaunched`; sidebar intents "marching to raid", squad list "raiding".
  - looks.json kinds may set `rangedVerb` (the necromancer "blasts").
  - Sim reports "raids launched", "squads mustered" and "player died in: <zone>".
- **Necromancer, part 2: the tower** (2026-11-07):
  - Lairs in spawns.json can have `zone` (a name announced on entry), `architecture` (a
    `LairBuilder` style), `leash` and starting `corpses`.
  - The necromancer's room becomes "the necromancer's tower": `LairFloor` tiles and free-standing
    `Pillar`s that block sight (cover), with 3 old skeletons' bones lying about.
  - His court is now the necromancer, 1 skeleton warrior and 1 archer. He raises only when roused
    (Engage/Alert), so the bones get up when you arrive.
  - Leash 12: lair squads never step farther (walking) from home, holding the border instead;
    searches beyond it turn into going home.
- **Character system** (step 5 part 1, 2026-11-08):
  - Six attributes (str, dex, con, int, wis, cha), D&D modifiers. Effects from attributes.json:
    Str melee, Dex bow + ½ defence, Con 3 HP, Int +10% XP, Wis regen + potion healing, Cha nothing yet.
  - Starting scores 12/12/12/10/10/10 with base stats lowered to match, so the opening character is
    exactly as before (30 HP, attack 2).
  - XP: kills by the player (`xp` per monster), half that for the first sighting of each kind, 25 for
    first entering a named zone. Int scales all of it.
  - Spending: +1 to any attribute (30 XP, then 15 more each time, max 20) or skill ranks from three
    trees (Warfare: Weapon Training, Toughness, Cleave; Archery: Steady Aim, Eagle Eye; Survival:
    Thick Skin, Field Medicine, Second Wind). Prerequisites and max ranks enforced.
  - Cleave is the first "special": a killing melee blow carries on into another adjacent foe.
  - Character sheet (`c`), XP on the status line ("(c)" when you can afford something), a log hint
    the first time you can, messages for sightings, zones, raises and skills.
  - The bot buys the cheapest affordable skill rank or Str/Dex/Con point; UI autoplay uses the bot.
- **Multi-faction foundation**: content in `data/`; factions + relations; per-monster line-of-sight
  perception of any hostile; per-faction toward/flee maps; squads engage other factions.
  Placeholder **undead** faction (skeleton warriors/archers, fearless) so goblins and skeletons
  fight on their own. Unseen fights are reported as "You hear the clash of weapons."

**Sim baselines** (Release build):
- Aggressive bot, 50 seeds × 1000 turns (before content/factions): player died 76%;
  ~19 goblins per level; squads 95% idle; ~1200 turns/s.
- Observer + idle bot, 30 seeds × 800 turns (after factions): ~12 monsters per level;
  goblins lose 126 dead vs undead 35; 27 routs; squads 92% idle; ~1600 turns/s.
- Aggressive bot with the kit, 50 seeds × 1000 turns (after player kit + ally swap): player died
  **2%** (far too easy); squads 71% idle, 18% engaged; 29 of 610 monsters survive; ~2700 turns/s.
- After the balance pass, aggressive bot, 100 seeds × 1000 turns:
  - Player died **23%**, mostly in the first 100–150 turns.
  - Survivors' lowest HP averages 35%; 14 of 77 had a close call.
  - About 21 monsters per level; the player kills about 11 and the factions kill about 7 of each other.
  - Squads are 70% idle, 17% engaged; about 2500 turns/s.
  - Target for this single level: the reckless bot (it never retreats) dies 20–35% of the time,
    and survivors get real close calls. Deeper content should push from there.
- **Tactical bot, full dungeon** (2026-11-08), 1000 games (seeds 1000–1999), 20000-turn cap, ~48 min:
  - Won **67%**, died 29%, timed out 4%. A won game takes about 7900 turns and earns about 5900 XP.
  - Deaths by depth: D1 **19.6%**, D2 4.1%, then about 1% per level down to D10 (1.5%).
    **The difficulty curve is upside down**: the character outgrows the monster scaling after level 1.
  - Killers: goblin archer 87, goblin warrior 70, skeleton archer 60, necromancer 43, skeleton warrior 29.
  - 11.4 potions drunk per game: potions are plentiful.
- Combat pass, aggressive bot, 100 seeds × 1000 turns: player died **33%**; average
  turn of death about 245; 13 of 67 survivors had a close call; about 320 XP per game.
  Killers: skeleton archer 13, skeleton warrior 7, necromancer 6, goblin archer 5, goblin warrior 2.
- Character system, aggressive bot, 100 seeds × 1000 turns:
  - Player died **25%** (was 36% before XP); average turn of death about 150, before much XP comes in.
  - XP earned about 320 per game (survivors 370): about 7 skill ranks and 1 attribute point.
  - 13 of 75 survivors had a close call. Within the 20–35% target, so no retune. The XP economy is
    sized for one level; revisit costs when there are deeper levels.
- Necromancer part 3 (warren, musters, raids), aggressive bot, 100 seeds × 1000 turns:
  - Player died **36%**; 11 of 64 survivors had a close call; average turn of death about 160.
  - Nearly all deaths are in the open dungeon, not inside the lairs (`player died in:` in the report).
  - Per game: about 2 raids and 2 musters with the bot playing.
  - Retuned for a self-replenishing dungeon: goblin warriors attack 5, regen 12, 3 starting potions,
    and the warren's guards are a shaman, a warrior and an archer. Before retuning it was 55%.
  - Observer mode (no player): about 4 raids, 3 musters and **11.5 corpses raised** per game (was 6);
    the necromancer sometimes falls (about 2 servants crumble per game).
- Necromancer part 2: player died 37%; 12 of 63 survivors had a close call.
  With 2 guards it was 47%; the lightly guarded tower whose bones rise is both fairer and creepier.
- Necromancer part 1, aggressive bot, 100 seeds × 1000 turns:
  - Player died **39%**; average turn of death about 170. The lair is now the boss fight.
  - Killers: skeleton warrior 17, necromancer 8, goblin warrior 6, skeleton archer 5, goblin archer 3.
  - About 2 corpses raised per game.
  - With unlimited reassembling skeletons it was 53%. "The dead rise only once" plus a weaker
    bolt (4) and slower raising (cooldown 8) brought it down.
  - The bot attacks whatever is nearest; a human who goes for the necromancer first should do
    better.

- **Combat pass** (2026-11-08, after the playtest below):
  - Hit rolls: accuracy vs evasion (Dex gives both; zombies are easy to hit, runners hard). Armour
    absorbs a random 0..defence, and every hit does at least 1: no more immunity.
  - Shooting point-blank costs 4 accuracy (−20%).
  - Gear by depth: long sword and long bow from depth 2, chain mail from depth 3, +2 from depth 2,
    +3 from depth 4. The single level is depth 1. Skeletons drop as if 1 level deeper, the
    necromancer 2 (the boss reward).
  - Archers and the necromancer no longer step into melee reach while closing in.
  - Retuned: player base HP 33 (36 at start) and natural evasion 1. Bot deaths 33% (was 45% right
    after the change); average death turn about 245, so danger lasts the whole level.
  - The play tool's commands moved to Core (`CommandPlayer`); the window can `--follow` a command file.

- **Ten levels and a tactical bot** (2026-11-08):
  - Stairs down (`>`) in the room farthest from where you arrive; `World.Descend` rebuilds the level
    around the same player (keeps character, kit, turn count). Taking the stairs on level 10 wins.
  - dungeon.json: monsters get +12% HP and +15% XP per level, +1 attack every 3 levels, +1 accuracy
    every 4; rooms 2% more likely occupied per level. Squads and lairs have minDepth/maxDepth: goblin
    war parties from depth 4, skeleton cohorts from 6. Both lairs appear on every level (generic).
  - `TacticalBot`: plays like the playtests (see its doc comment). Breaks corridor stand-offs,
    retreats out of sight to rest, leaves a level after 1500 turns (the warren musters forever).
  - Sim runs games in parallel (`--bot tactical`), reports wins and deaths per depth; `--trace` follows
    one game (TRACE_EVERY env var sets the interval) and prints the cells around the player at the end.

**Playtest by Claude, seed 42 (2026-11-08)**, played turn by turn with `Roguelike.Play`:
- Cleared the level by turn ~366 (warren fallen, necromancer dead), ending at full HP. The only real
  danger was the first fight (lost 10 of 30 HP); it was good: a warrior corking a corridor with
  archers stuck behind him, the shaman healing, the band routing at low morale.
- **Defence makes the player immune (critical).** Damage is roll(1..attack) − defence, and no
  monster has attack above 6. A +3 chain mail (def 6) found at turn 130 meant every later hit did 0,
  including the necromancer's court. Reachable without luck: chain mail 3 + Thick Skin 2 + Dex 14.
  Fix the combat formula before item affixes add more defence.
- **The bow dominates.** Shooting an adjacent enemy has no penalty and a good bow out-damages the
  sword, so melee and its skills are pointless once you find one.
- **The necromancer stepped adjacent to the player** (into a dead zombie's spot) instead of keeping
  his distance behind his guards.
- XP spent mid-fight on Toughness/Constitution heals instantly (free action); minor.
- Attribute points are poor value next to skills (+1 score is half a modifier for 30+ XP); ended
  with 175 unspent XP and nothing worth buying.
- 100 quiet turns at the start; the living dungeon mostly reaches the player as off-screen noises.
- Ended with 5 unused healing potions.

**Known issues / tech debt**
- Monster AI jam: a routed goblin wedged in a corridor in front of an engaged squad blocks it
  forever (neither side passes). A leashed guard at the edge of its leash also stands still forever.
- Simulation speed is about 2000 turns/s on this desktop (4 cores, busy; parallelism helps little).
  A 10-level game with the tactical bot is about 7000 turns. Profile before running 10k games.
- A runner whose only exit is blocked (e.g. by the player standing in it) keeps trying forever;
  it should look for another route or another squad.
- Balance is tuned for one level only. There's no depth curve yet. Once the bot clears a level,
  nothing else happens.
- Arrows are infinite; monsters don't pick up or use items; floor items aren't remembered once
  out of sight; potions are used even at full HP.
- Squads are still idle about 77% of the time, because patrols are random. Raids and musters keep
  the dungeon going, but there's no other purpose for idle squads yet (foraging, guarding routes).
- Undead don't raid or forage. Possible next step: the necromancer sends servants to drag corpses
  back to the tower.
- The warren has no architecture of its own yet (it's a plain room with a name).
- The start is safe and every faction's numbers are tuned for a single level only.
- Engaged squads follow `TowardEnemiesOf`, which leads to the nearest enemy even if unseen
  (omniscient chase). Fine while engaged; revisit with stealth.
- Test suite takes ~30 s in Debug (the multi-seed simulations dominate).
- Remaining per-turn cost is mostly A* for patrolling squads (each follower re-plans each turn).
  Options: cache paths, or per-squad distance maps.
- `ActorAt` is a linear scan; switch to a spatial index (GoRogue `SpatialMap`) when actor counts grow.
- `GameMap.Walkable` is a lambda view; make it an array when terrain becomes mutable (doors, digging).
- Monster sight is radius + Bresenham line; the player's FOV is shadowcasting. They can disagree
  slightly at edges. Stealth will need a proper model (noise, light, facing).

---

## 7. Roadmap

1. ~~Observation tools~~ (done 2026-11-07)
2. ~~Multi-faction foundation~~ (done 2026-11-07)
3. ~~Minimal player kit~~ (done 2026-11-07): inventory, bow, healing potions, armour, so playtests and balance mean something.
4. ~~**Necromancer faction**~~ (done 2026-11-07)
   - ~~Part 1: speed, corpses, the necromancer, raising the dead, servants crumble~~
   - ~~Part 2: the tower: lair architecture, zone, starting bones, leash~~
   - ~~Part 3: a living dungeon: lairs, the goblin warren, musters, raids~~
   - Follow-ups: undead corpse-harvesting parties, warren architecture, bot that focuses priority targets.
5. **Full character system and procedural items** (see §8)
   - Inserted first: ten generic levels, stairs, depth scaling, the tactical bot, mass simulation.
   - ~~Part 1: attributes, XP, skills, character sheet~~ (done 2026-11-08)
   - Part 2: rarity tiers and affixes (affixes add to the same `Stat`s; tiers coloured white/green/
     orange/red), item names, loot generation by tier
   - Part 3: random uniques, identify-by-use, scrolls of enchanting (+X)
   - Later (ToME direction, user): classes that grant skill trees, active skills with cooldowns,
     a use for Charisma/Intelligence/Wisdom beyond the passives
6. **History generator**: eras → map features → factions → artifacts
7. **Crafting**: material tags + recipes + stations as places
Later: multiple levels/sites, a campaign chaining sites, stealth, LLM-driven interrogation (optional),
tiles/visual upgrade toward Cogmind/Qud.

Re-run the sim and compare with the current baseline (§6) after every content or AI change.

---

## 8. Design notes for future systems

### Items (user preferences: rarity colours and +X gear are wanted)
The user wants Diablo-style tiers **and** classic +X items. Proposed structure:

| Tier | Colour | Contents |
|---|---|---|
| Common | white | base item, maybe +X |
| Magic | green | 1 random property |
| Rare | orange | 2–3 properties, generated name |
| Legendary | red | 3–4 properties including one strong "signature" property |
| Unique (random) | gold | procedurally generated named item **with a backstory from the history generator**, e.g. "Grishak's Bonecleaver" carried by a goblin chief |
| Artifact (unique-unique) | violet | hand-authored, at most once per game, tied to the site's history (the dwarf king's axe) and wanted by factions |

- **+X enchantment is orthogonal to tier**: any item can be +0..+N (or cursed, negative). Raised by
  scrolls and the forge. A +3 white sword and a +0 rare sword are different, both valid choices.
- An item = **base type × material × origin × tier properties × +X**. Materials (dwarven steel, bone,
  chitin, iron) come from the site and carry properties; origin (goblin-made, dwarven, necromantic)
  affects quality and quirks. Every attribute is a tag so crafting can reuse it.
- Brogue-style identify-by-use for potions, scrolls and runes.
- **Decided**: tiers don't gate +X (independent). **Not decided yet**: exact affix pools, drop rates.

### Character
- Now: six D&D attributes and skills bought with XP (Sil-style spending; XP for kills, first
  sightings and new places). Implemented in step 5 part 1.
- Direction (user): toward **Tales of Maj'Eyal**: classes, many skills (talent trees), actives with
  cooldowns. Classes should be data that grant trees and starting attributes/kit. The user will
  revisit the attribute list.

### Factions planned
- **Goblins**: numerous, fast, morale-driven, squads with roles. (Implemented)
- **Necromancer + undead**: slow, no morale, growing army from corpses; lair in the tower.
- **Something from the deep**: rare, dangerous ambush threat.
- Later: dwarven remnants (tombs, ghosts), traders, lizardfolk.

### Crafting
Material tags + recipe requirements ("blade + hard material + forge"). Chitin gives acid
resistance, necromantic bone gives life drain. Recipes found as books or rumours. Stations are
locations that must be taken and held.

---

## 9. Background reading
- Brogue CE source (AGPL: study, don't copy): Dijkstra maps, cellular caves, "machine" rooms.
- Grid Sage Games blog (Kyzrati, Cogmind): ASCII UI design, animation, map gen.
- RogueBasin; r/roguelikedev FAQ Friday archive.
- Bob Nystrom, "Rooms and Mazes" (what GoRogue's generator implements).
- Unexplored's cyclic dungeon generation (for the history-driven generator).
