# Project handbook

Everything needed to pick this project up cold: vision, decisions, architecture, how to work on it,
current state and roadmap. **Keep it current**: update it whenever a decision, milestone or known
issue changes.

Last updated: 2026-11-09 (classes, squad minds, trials, and the fixes they led to)

---

## 0. Where we left off

Start here in a new chat. Everything below is merged to `main` and pushed. 380 tests pass.

**Last finished**
- Four classes with their own kits and skills.
- Squad minds: mindless, beast, pack, rabble, drilled. A commander lifts its squad to drilled while it lives.
- Trials that pit every class and build against every enemy, stage and mind (`--trials`).
- Follow-ups from the trials: drilled fighters run at shooters, commanded goblin and skeleton squads on levels 3-5, half Strength on the off-hand dagger.

**Open, in the order I would take them**
1. **The crypt is still a cliff.** Levels 1-6 kill at most 14%, level 7 kills 43-72%. Commanders made the middle harder, but the last level did not get gentler.
2. **Class balance.** Rogue is 18%, under the 20-30% target. Soldier 36% and Mystic 33% are over.
3. **Ranger and Rogue walk over most single fights**, even against drilled squads. Enemies still need more ways to punish them.
4. **Human playtests** against [WORLD_DESIGN_VISION.md](WORLD_DESIGN_VISION.md) §12. The user will do these when ready; don't wait for them.
5. **Choose what to build next** from §7 below and [IDEAS.md](IDEAS.md) (its §16 suggests an order). Nothing in IDEAS is a commitment until the user picks it.

**Documents**
| File | What it's for |
|---|---|
| this file | Vision, decisions, architecture, current state, roadmap |
| [HISTORY.md](HISTORY.md) | What was built when, and every past balance measurement |
| [IDEAS.md](IDEAS.md) | The user's idea bank: possibilities, not commitments |
| [WORLD_DESIGN_VISION.md](WORLD_DESIGN_VISION.md) | Exploration and world direction |
| [UI_DESIGN_VISION.md](UI_DESIGN_VISION.md) | Presentation direction |

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
| **XP is spent on attributes and skills** (user) | Skills are data, grouped in trees; a class is the trees it can learn plus its kit. The direction is ToME: more classes and many more skills | Fixed level-ups |
| **Rarity tier and +X are independent** (user) | A +3 white sword and a +0 orange sword are both real choices | Tiers capping +X |
| **One derived-stat vocabulary (`Stat`)** | Attributes, skills and item affixes all add to the same stats, so content can combine freely in data | Per-system bonus fields |
| **Generated items: tier, then affixes, all from data** (`affixes.json`) | Tiers (magic/rare/legendary) roll by depth; affixes are stat bonuses with depth growth; legendaries add a special. Core stores the name *words*; the front end builds "Grim Fang (+1 long sword)" | Hand-written item lists; names built in core |
| **A squad's tactics come from what it is** (`minds.json`; user, 2026-11-09) | Each monster has a mind (mindless, beast, pack, rabble), and one with `commands` lifts its whole squad to that mind while it lives (drilled). Killing the commander drops the squad back and costs it morale. Minds are data: range of shouts, memory, sizing up, surrounding, holding corridors, advancing together, running at shooters, how much losses hurt | One tactics level for everyone; tactics by faction |
| **Classes are data** (`classes.json`; user, 2026-11-09) | Each class sets attributes, gold, starting kit, the skill trees it can learn and the skills it starts with. A skill can grant an active ability with a cooldown (`abilities.json`), so adding a class needs no code. A class can't learn from another class's own trees; a game started with no class (tests, plain tools) only gets the general trees | Classes coded by hand; open skill choice for everyone |
| **Tune with the tactical bot over many seeds** | Balance target (2026-11-08): the tactical bot wins about 20–30% and deaths rise with depth. A human plays differently; the bot measures the curve, not absolute difficulty | Tuning by feel from single games |
| **Own `SafetyMap` (Brogue flee map)** | Per-faction flee maps needed; GoRogue `FleeMap` cost ~1 ms each | GoRogue `FleeMap` |
| **The player starts as a neutral traveler** (user, 2026-11-09) | Warnings, tolls and peaceful passage only make sense if factions don't start out at war with the player | Known enemy; status varying by origin |
| **Permadeath; the world resets on death, and persists fully within a run** (user, 2026-11-09; "for now") | Classic roguelike stakes; no campaign continuity to design yet | Persistent world across runs |
| **A seed fixes the layout, whatever the player does** (user, 2026-11-09) | Players can retry the same world. Each location generates from its own stream derived from world seed + location id, separate from gameplay randomness | One shared stream, which makes later maps depend on how earlier ones were played |
| **The surface is one large scrolling map for now** (user, 2026-11-09) | The user wanted a wider starting area with more in it; the window's view follows the player | A few connected maps (the earlier choice) |
| **Seven dungeon levels, ending in the necromancer's crypt; killing him wins** (user, 2026-11-09) | A boss at the end of a shorter dungeon reads as a little RPG | Ten levels; walking out the bottom to win |
| **Character traits, general skills and crafting are fine-tuning, later** (user, 2026-11-09) | First a complete vertical slice | |

---

## 3. Repository layout

```
Roguelike.slnx
data/                        game content (JSON with comments), copied next to every program
  factions.json              factions + relations (hostile/neutral/allied) + raid orders
  monsters.json              stat blocks: faction, role, hp/attack/defense, range, fearless, speed, rank, abilities,
                             risesAs (corpse → undead), raise (necromancers), xp (kill reward), npc (lines, goods)
  abilities.json             active skills for elites, bosses and the player (kinds in AbilityKind; "power" scales with Magic)
  minds.json                 how squads think: mindless, beast, pack, rabble, drilled
  classes.json               the player classes: attributes, gold, kit, skill trees, starting skills
  elites.json                the chance and kinds of elite squad members (title, stat changes, skills)
  spawns.json                room occupancy, floor item chance, safe start, weighted squad templates,
                             lairs (unique squads placed in the farthest rooms, with a home)
  items.json                 item types (minDepth), +X enchantment weights (minDepth), loot tables
  affixes.json               rarity tiers (chance by depth, affix count, special), affix pool (stat, range,
                             growth per level, slots), legendary specials per slot, rare-name word lists
  dungeon.json               number of levels (7) and how monsters grow per level (HP, XP, attack, accuracy,
                             defence, occupancy)
  combat.json                hit chance (accuracy vs evasion), point-blank penalty, minimum damage, off-hand rules, sizing-up thresholds, ransoms
  player.json                player base stats, starting attributes, XP rules, inventory size, starting kit
  attributes.json            the six attributes: what each modifier point adds to derived stats; raise costs
  skills.json                skill trees: ranks, XP costs, prerequisites, per-rank stat bonuses, specials
  surface.json               the hand-drawn valley: terrain rows + legend, start, zones ("{tribe}" = rolled name) and tolls, squads, loot, errand, incursions
  history.json               how the site's past is rolled: mine depth, kinds of fall, tribes (toll, garrison), heirloom, name lists
  looks.json                 front end only: glyph + colour per kind, item and tile
src/Roguelike.Core/          the simulation (GoRogue only; no SadConsole)
  Content/ContentDatabase.cs loads + validates data/; ContentDatabase.Default reads ./data
  World.cs                   all state; Step(action) -> events; spatial/AI helpers
  Map/GameMap.cs             tiles (change via SetTile), explored, walkable/transparent views, player FOV, rooms
  Map/CellGrid.cs            array-backed bool grid; walkability lives in one so pathfinding reads an array
  Map/MapGenerator.cs        GoRogue rooms-and-mazes, tuned roomy (will be replaced by history-driven gen)
  Map/LairBuilder.cs         rebuilds a room as a lair ("tower": lair floor + pillars); keeps it connected
  Map/PlaceBuilder.cs        turns a hand-drawn place from the content files into a GameMap
  Map/DistanceMap.cs         fast BFS Dijkstra map (IGridView<double?>)
  Map/SafetyMap.cs           Brogue flee map built from a DistanceMap
  Entities/Actor.cs          anything that takes turns; Kind, Faction, stats, Fearless, Brain
  Entities/Faction.cs        Faction, Relation, FactionRelations
  Progression/Stat.cs        the derived stats everything adds to (maxHp, meleeAttack, defense, regen...)
  Progression/Character.cs   attribute scores, skill ranks, XP pool; sums bonuses; spending checks
  Items/Item.cs              ItemDef (data), Item (instance: +X Enchantment, Rarity, Affixes, Special,
                             NameWords, Count), categories
  Items/Affix.cs             AffixDef, AffixRoll, RarityTierDef, name word lists
  Items/Inventory.cs         Inventory (stacking, capacity) and Equipment (weapon/bow/armor slots)
  Actions/                   GameAction records: Move (bump-attack, ally swap), Attack, Wait, RangedAttack,
                             Heal, CallForHelp, RaiseDead, PickUp, Drop, Equip, Drink, Fire,
                             RaiseAttribute, LearnSkill (free actions: Perform returns false)
  Events/GameEvent.cs        structured events emitted during a turn
  AI/IBrain.cs               per-actor decision interface
  AI/Squads/                 Squad (blackboard), SquadDirector (squad-level think), SquadMemberBrain (roles)
  AI/Strategy/FactionPlanner strategic layer, once per turn: lair musters and faction raids
  History/SiteHistory.cs     the site's past as facts, rolled once per seed from its own stream; World shapes levels from it
  Simulation/                bots (AggressiveBot, IdleBot, TacticalBot), Simulator (headless games + stats),
                             CommandPlayer (scripted play), Exploration (auto-explore), ItemValue (bots' gear worth),
                             Trials (class, build, stage and mind matchups)
src/Roguelike.App/           SadConsole front end
  Program.cs                 startup, --seed N, --spectate
  GameScreen.cs              map/sidebar/status/log, input, turn loop
  GameScreen.Debug.cs        debug views (F1/F2/F3/F5/P), autoplay
  GameScreen.Items.cs        item commands, inventory menus, bow targeting
  GameScreen.Character.cs    character sheet (c): attributes, skill trees, derived stats; letters spend XP
  GameScreen.Examine.cs      examine mode (x) and the toll prompt (t); shows only what the player knows
  GameScreen.Chronicle.cs    what the stones tell (m): words the site's history for the player
  GameScreen.Shop.cs         buying from people in the valley (opens after talking to anyone with goods)
  GameScreen.Popups.cs       what floats over the map (speech, grunts, noises, tooltip) and the mouse; cosmetic, uses no game randomness
  ItemText.cs                item names ("+1 short sword", "keen dagger", "Grim Fang (+1 long sword)") and stat lines
  Theme.cs                   ALL visuals: glyphs, colours, the lighting formula (swap for tiles later)
  GameScreen.Panels.cs       everything around the map: place bar, in sight, this place, status strip, log, key guide
  Fonts/                     bitmap fonts built by tools/scripts/make_font.py: 16x32 atlases, CP437 then bold copies at glyph + 256
  EventText.cs               events -> log messages (unseen things become sounds)
tools/Roguelike.Sim/         headless simulation report CLI (TrialReport.cs prints --trials)
tools/Roguelike.Play/        text front end for playing by hand or by an AI agent: replays a command file
                             from a seed, prints the view, monsters (ids, hit chances), stats and log.
                             Commands are interpreted by Core's Simulation/CommandPlayer
tools/scripts/make_font.py   rebuilds the bitmap fonts from JetBrains Mono (needs Pillow)
tools/scripts/sendkey.py     send keys to the game window (UI checks without stealing focus)
tests/Roguelike.Core.Tests/  xUnit: determinism, map connectivity, squad behaviour, observer mode
docs/                        this file, HISTORY, IDEAS and the two vision documents (see §0)
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
   nobody's enemy; a squad with a `Camp` tolerates the player unless they're past its gate without
   paid time, or have attacked it). `world.CouldFight(a, b)` is the faction table alone. `world.CanSee(observer, target)`: monster sight, Euclidean radius ~8.5 plus
   Bresenham line of sight. `world.PlayerCanSee(actor)`: the player's FOV (UI, bots only).
   AI never asks "where is the player"; it asks `NearestVisibleHostile` / `AdjacentHostile`.
6. **Dijkstra maps and knowledge:** AI moves on `Squad.TowardKnownThreats` / `AwayFromKnownThreats`:
   enemies some member in earshot of the leader (`Squad.ShoutRange`) sees now, otherwise the last
   known position; rebuilt lazily after the squad thinks (stale within a turn, which is acceptable).
   Being attacked reveals the attacker's position. **AI must never path on true positions of unseen
   actors.** `TowardEnemiesOf(faction)` / `AwayFromEnemiesOf(faction)` are omniscient and kept for the
   F3 debug heatmaps only, like `TowardPlayer`. `StepDownhill` prefers less crowded cells, which
   makes groups surround instead of queue.
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
10. **Turn order** in `World.Step`: player action (its `TimeCost` goes into `_timeOwed`; the world below runs once per 100 owed, so a
   quick action can pass with no reply and a slow one can give two) → update FOV and maps → each squad thinks → each
   non-player actor gains `Speed` energy and acts once per 100 (zombies at 50 act every other turn;
   actors created this turn wait) → remove dead → turn++ → regen → update FOV.
11b. **Builds:** `Stat.Speed` shortens everything the player does (Fleet Foot skill); armour's `moveTime` slows steps only. `arrowGuard` adds armour against missiles,
   `brace` adds armour and a counter-attack when the wearer didn't step last turn (`Actor.StoodStill`), and `menace` on any gear makes squads sizing up the player less confident.
11a. **Squads size up the player** (`SquadDirector.SizeUp`, thresholds in combat.json) from what they can see: their HP x attack against
   the player's apparent health (capped at max HP), best attack and armour. Others already at the player's throat count on their side.
   `Squad.Posture`: Bold (surround), Even, Wary (no bow: keep out of reach), Desperate (run, or charge if a bow has them close). Leashed
   squads and the fearless never run from it. Factions with `pleas` may surrender (`Squad.Surrendered`). What they offer for their lives depends on the squad's highest rank (`combat.json` ransom): nothing, gold, or a secret if the level still has one.
11. **Corpses** are `World.Corpses` records (kind, position, turn), left by every non-player death
   except raised dead ("the dead rise only once"). Raised actors carry `MasterId`; `World.Damage`
   crumbles them when the master dies. Spawn monsters at runtime with `World.SpawnMonster`.
12. **Locations:** all per-map state lives in `World.Location` and is kept for the whole game; add new
   per-map state there, not on `World`. While a map is built, `world.Rng` is that map's own stream
   (from world seed + location), so a seed gives the same maps however the game is played. Play
   randomness is a separate stream. The surface is `World.SurfaceDepth` (0); its cave leads to level 1.
   Time spent away is applied on return by `World.CatchUp`, in outline. Scaling and loot treat it as level 1 (`DangerDepth`). Walkable and see-through are separate grids
   (brush is one, not the other). Change gates with `World.SetGate`. AI routes use `Map.Passable`,
   which counts closed gates: a planned step into one opens it.

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
dotnet test                                     # ~25 s
dotnet run --project src/Roguelike.App          # play, from the valley (--dungeon starts at level 1)
dotnet run --project src/Roguelike.App -- --seed 640 --spectate
dotnet run --project tools/Roguelike.Sim -c Release -- --seeds 50 [--turns 1000] [--bot aggressive|idle] [--observer] [--games]
dotnet run --project tools/Roguelike.Sim -c Release -- --map --start 7   # print seed 7's level as text
dotnet run --project tools/Roguelike.Sim -c Release -- --encounters --seeds 300   # each valley fight, bot with the starting kit
dotnet run --project tools/Roguelike.Sim -c Release -- --trials --seeds 200   # every class, build, stage and enemy, and each squad mind compared (~2 min)
dotnet run --project tools/Roguelike.Play -c Release -- --seed 42 --commands moves.txt [--full] [--log 20]
```
**Watching an agent play:** start the window with
`dotnet run --project src/Roguelike.App -- --seed 42 --follow moves.txt [--surface] [--pace 0.25]`; it plays each command
appended to the file (same interpreter), ~11 turns/s unless `--pace` says how many seconds a turn is shown. The agent decides with `Roguelike.Play` on the
same seed and file. `Roguelike.Play` is for playtesting by an agent: append commands to the file (`explore`, `go X Y`,
`n`/`se`... `fight ID`, `fire ID`, `pickup`, `equip ITEM`, `drink`, `rest`, `learn SKILL`, `raise ATTR`;
see the header of its Program.cs) and run again. Multi-turn commands stop when a new enemy appears.
The report gives: death rate, survivors' lowest HP and close calls (<25% HP), share of floor
inside rooms, monsters per level, kills split by "player" vs "others" per faction, events, squad states.

### In-game keys
Move/attack: arrows, numpad, `hjklyubn`. Wait: `.` `5` `Space`. Stairs: `>` `<`. Walk into someone in the hamlet to talk (and buy from the peddler). Gates: walk into one to open it, `C`
closes an adjacent one. Moving into someone who's leaving you alone warns you; doing it again attacks. Sneak: `z`. Examine: `x` (Tab next thing, no time passes). What the stones tell: `m`. Pay a toll near a gate: `t`. After a game: `R` new seed, `S` retry this seed. Quit: `Esc`.
Items: `g`/`,` pick up, `i` inventory (pick an item to use it), `e` equip/remove, `q` drink, `d` drop,
`f` fire bow (Tab next target, `f`/Enter fire, Esc cancel). Skills: `a` (pick one; aimed ones use Tab, `a`/Enter, Esc). Character sheet: `c` (letters spend XP).
A new game opens on the class picker (letters choose, Esc quits); `--class ID` skips it and `S` retries the same seed and class.
Plain look without lighting: `F4`.
Debug: `F1` reveal map + all actors, `F2` squad overlay (tints, tinted ring = chase limit, `o` posts, `r` patrol stops, `R` rally,
`x` destinations, `?` last known target position, squad list with orders and morale, camp state), `F3` cycle heatmaps (toward player, then per faction
"toward enemies" and "flee"),
`F5` spectator mode (invisible, invulnerable), `P` autoplay (playing: the aggressive bot plays for
you, a quick way to see progression; spectating: time passes).

### Testing the window without touching the user's screen
Run the app on a private display: `Xvfb :77 -screen 0 1500x900x24 +extension GLX`, then `DISPLAY=:77 LIBGL_ALWAYS_SOFTWARE=1` for the app, xdotool and `import -window root`.
It runs slowly there: hold keys and buttons about 0.2 s (xdotool keydown/mousedown, wait, keyup/mouseup) or the game misses them.

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

**What exists today**
- **The valley**: a 150x46 scrolling surface with a hamlet (peddler, hermit, villagers), the Thornwood (wolves, spiders), boars, a bear and a bandit den.
- **The goblin camp**: palisade, working gate, warnings, toll, an errand, theft from the storehouse, a gap in the fence and an undead incursion. Which tribe holds it comes from the site's history.
- **The dungeon**: seven stored, revisitable levels ending in the necromancer's crypt. Killing him wins. Lairs include the goblin warren and the acolyte's shrine.
- **A site history** rolled per seed: founder, mine depth, how it fell, heirloom, inscribed stones. Places left behind catch up in outline when you return, and corpses rot.
- **Characters**: Soldier, Ranger, Rogue and Mystic, each with its own kit, skill tree and active skills. Six attributes, XP spending, items with +X, rarity tiers and affixes.
- **Combat feel**: weapon speeds, twin daggers, stagger, the charge, heavy armour (slower steps, arrow guard, brace, menace), sneaking, noise and hearing.
- **Squads**: each thinks according to its mind (`data/minds.json`), sizes up the player, routs, surrenders for a ransom, keeps to a leash, fetches help. Factions raid and muster.
- **Front end**: enhanced ASCII look, panels, popups, mouse support, class picker, skill menu, examine, chronicle, shop, gate talk.
- **Tools**: `Roguelike.Sim` (whole games, `--encounters`, `--trials`), `Roguelike.Play`, debug overlays, spectator mode.

Older build notes and every past measurement are in [HISTORY.md](HISTORY.md).

**Current balance** (2026-11-09, commit "Drilled squads run at shooters"). Tactical bot, 500 games per class, seeds 1000-1499:

| Class | Won | Died | Death rate on levels 1-7 (%) |
|---|---|---|---|
| Soldier | 36.2% | 63% | 6.4, 6.7, 6.2, 5.4, 6.7, 9.7, 43.4 |
| Ranger | 26.6% | 73% | 0.2, 1.4, 2.8, 4.8, 7.3, 12.6, 63.7 |
| Rogue | 18.4% | 81% | 0.0, 2.4, 3.5, 7.9, 13.4, 10.4, 72.3 |
| Mystic | 32.6% | 67% | 6.2, 2.8, 3.7, 8.2, 10.7, 8.3, 49.7 |

Target: 20-30% wins, deaths rising with depth. Ranger is on target, Rogue just under, Soldier and Mystic over.

**What the trials say** (`--trials --seeds 200 --start 1000`; one fight, nothing else on the level):
- Thinking matters to characters who get reached. Six wolves in a room, seasoned: Soldier beast 96%, pack 56%; Mystic 71%, 15%. Goblin force: Soldier rabble 90%, drilled 75%; Mystic 74%, 52%.
- Corridors help (pack against Mystic: 15% in the open, 48% in a corridor). Fearless skeletons barely change with mind, because the mind differences are mostly about breaking.
- Ranger and Rogue still win most single fights. Running at shooters cost the Ranger little there: drilled wolves 93% to 84%, drilled goblins 99% to 96%. Its whole-game rate fell further, 37% to 27%, with the new commanded squads in the mix.
- Tension by class: Mystic fights are 65% real and 30% lethal with 4% walkovers. Ranger walks over 57-61% and Rogue 70-74%.
- By stage: fresh 88%, seasoned 92%, veteran 65%, final 13%. The stage rosters are fixed, so these don't see `spawns.json` changes.
- Zombies are always a walkover. The bear (81% real) and bandits with their chief (75% real) are the best-shaped fights.
- Caveats: the bot doesn't sneak or use doors; trial gear is the starting kit plus enchantment, so veteran and final characters are under-equipped; the court trial (0-1%) is far below real win rates, so it is not representative, and the reason isn't established.

**Known issues / tech debt**
- Places the player has left catch up in outline when they return (`World.CatchUp`), not turn by turn: only corpses,
  healing, calming down, replacing guards and a due incursion. Raids and musters don't run while away.
- The crypt is a wall: 43-72% of those who reach level 7 die there, against 14% at most on any other level.
- Monsters don't follow on the stairs, so stairs are a perfect escape for now.
- Dungeon goblins are still hostile on sight: only squads with a `Camp` tolerate the player.
- A theft nobody saw is never discovered: guards don't notice missing goods.
- The wider valley is only tested by the rules, not by bots: the balance sim never plays the surface.
- The camp only reacts to the undead attack by fighting: it doesn't shut its gate or sound a horn for it.
- `World.StepToward` gives up when its next step is blocked and no neighbour is strictly closer (goblins can jam
  like this). Scripted player commands use `StepAroundOthers` instead.
- Someone seen coming over the breach gets no warning, by design: warnings are for the gate.
- Camp squads don't answer arrows from beyond their leash, so a long bow can pick them off from outside.
- Sight on the surface is the same radius 8 as underground, so landmarks can't be seen from afar yet.
- Squad leashes stretch by `Squad.PursuitStretch` for a while after someone hurts the squad; past that, it goes home.
- 500 tactical games per class, plus the valley fights, take about 2-3 minutes on this 12-thread machine. The largest costs were per-faction Dijkstra and flee maps, FOV and map generation.
- A runner whose only exit is blocked (e.g. by the player standing in it) keeps trying forever;
  it should look for another route or another squad.
- Levels are generic: the same factions and lairs on every level, only stronger. Deep levels need
  their own content (new monsters, places) rather than ever bigger numbers.
- Arrows are infinite; monsters don't pick up or use items; floor items aren't remembered once
  out of sight; potions are used even at full HP.
- Squads are still idle about 77% of the time, because patrols are random. Raids and musters keep
  the dungeon going, but there's no other purpose for idle squads yet (foraging, guarding routes).
- Undead don't raid or forage. Possible next step: the necromancer sends servants to drag corpses
  back to the tower.
- The warren has no architecture of its own yet (it's a plain room with a name).
- The squad's state is shared, so a member out of earshot follows the squad's state even though it
  couldn't have heard (rare: followers stay close).
- Test suite takes ~25 s in Debug (the multi-seed simulations dominate).
- `ActorAt` is a linear scan; switch to a spatial index (GoRogue `SpatialMap`) when actor counts grow.
- Terrain changes must go through `GameMap.SetTile` (walkability cache). Cached A* paths assume walls
  don't change during a level; clear `World._paths` when digging arrives.
- Items aren't identified: every property is visible at once (identify-by-use is part 3).
- Monster sight is radius + Bresenham line; the player's FOV is shadowcasting. They can disagree
  slightly at edges. Noise is modelled; light and facing are not.

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
   - ~~Part 2: rarity tiers and affixes, item names, loot generation by tier~~ (done 2026-11-08;
     preceded by a sim speed-up and a difficulty pass)
   - Part 3 (parked until the camp slice works): random uniques, identify-by-use, scrolls of enchanting (+X)
   - ~~Classes, class skill trees, active skills with cooldowns~~ (done 2026-11-09). Still later (ToME direction, user):
     a use for Charisma/Intelligence/Wisdom beyond the passives
6. **Camp vertical slice** (built; human playtests still to come): one surface map, one goblin camp, one underground map, with
   several ways through (see [WORLD_DESIGN_VISION.md](WORLD_DESIGN_VISION.md) §12 for acceptance criteria)
   - ~~a. Honest enemy knowledge: squads act only on sight, shouts and last-known position; no pathing
     to true positions of unseen enemies; a test pins it~~ (done 2026-11-09)
   - ~~b. Stored locations: per-level state moves out of `World` so maps can be left and revisited;
     each location generates from its own seed-derived stream; the seed can be retried after death~~
     (done 2026-11-09; locations are keyed by depth for now, surface maps will need a real id)
   - ~~c. A hand-built camp from a text map: road, palisade, working gate, cover, cave down. No
     generator until the camp is fun. Doors and gates must clear cached paths and update sight~~
     (done 2026-11-09: `data/surface.json`; the road passes out of the guards' sight)
   - ~~d. Place-based squad orders: guard, patrol, investigate, pursue with a reason to stop, withdraw
     to a rally point. Separate faction relation from access policy (the player starts neutral)~~
     (done 2026-11-09: `Camp`, orders in `data/surface.json`; goblins open and shut the gate)
   - ~~e. Warning and toll, plus examine mode~~ (done 2026-11-09: terms called out within 4 tiles of the
     gate, the gate tile is the last warning, 20 gold buys 150 turns; `x` examines, `t` pays)
   - ~~f. Something to do there: the errand (relic behind the camp, back to the waystone), theft from the
     storehouse, a gap in the back fence timed against the patrol, undead coming out of the cave on turn 300~~
     (done 2026-11-09; the errand's story is a placeholder)
   - ~~g. F2 overlay: chase limits, posts, patrol stops, rally points, orders, camp state~~ (done 2026-11-09)
   - h. Human playtests against the §12 list, and balancing. **The user will do these later (2026-11-09):
     don't stop development to wait for them.**
7. **History generator**: eras → map features → factions → artifacts
   - ~~a. First version: `data/history.json` + `SiteHistory`, rolled per seed from its own stream: founder and clan,
     mine depth, how and where it fell (war: the dead and skeleton patrols; collapse: fallen rock and left gear),
     timber props on mine levels, the founder's unique heirloom, inscribed stones read by stepping on them, `m` chronicle~~
     (done 2026-11-09)
   - ~~b. The valley follows the history: which tribe holds the camp (its name, toll and garrison, weighted by how the
     mine fell), and plague as a third kind of fall~~ (done 2026-11-09)
   - c. Later: more kinds of fall and tribe, per-level identity below the mine, factions that want the heirloom,
     the incursion depending on the history (it's the same every run).
8. ~~**Time passing in places the player has left, first version**~~ (done 2026-11-09): corpses wither and rot away
   (user's request; the ancient dead don't), and on return monsters have healed and calmed, camps have replaced fallen
   guards, and an attack that came due was fought without the player, leaving a broken gate, bodies, or a fallen camp.
   Later: raids and musters while away, repairs, supplies, pursuit up the stairs.
9. ~~**A little vertical slice of an RPG**~~ (done 2026-11-09): seven levels ending in the necromancer's crypt (boss;
   killing him wins), elites and skills, a 150x46 scrolling valley with a hamlet (peddler, hermit, villagers), the
   Thornwood (wolves, spiders), boars, a bear and a bandit den. Character traits, general skills and crafting come later.
9a. ~~**Enhanced ASCII look**~~ (done 2026-11-09, from the user's exported prototype): palette, JetBrains Mono bitmap fonts,
   18px square map cells with a scrolling view, firelight and torchlight, darker memory, panel layout (in sight, this place,
   inspect, HP strip, log, key guide). `F4` switches to a plain look. Still from the prototype: mouse (click to inspect, walk
   here), gate talk with choices in the panel, a journal, proportional panel fonts, smoke, screen scaling.
10. ~~**Better sneaking**~~ (done 2026-11-09): `z` sneaks (slower, quieter, spotted from closer). Footstep noise = 3 + armour noise - Stealth, halved
    sneaking; calm squads that hear it go to look (`SquadHeardPlayer`). `Stat.Perception` (Wisdom, Perception skill) and `Stat.Stealth` (Dexterity, Stealth skill)
    drive what the player hears (`World.Noise`, `SoundHeard`) and how quiet they are. Athletics and other secondary skills later.
11. **Dungeon identity**: levels below the mine get their own monsters and places.
11a. ~~**Heavy builds and speed**~~ (done 2026-11-09): slower steps, arrow guard, brace, menace, stagger, the charge (`v`), Fleet Foot. Still to come: a speed affix.
11b. ~~**Popups, mouse, gate talk**~~ (done 2026-11-09): speech, grunts and noises over the map; hover tooltip and Inspect, left click pins, right click walks; the guards' challenge as a choice in the side panel (1-3 or click).
11c. ~~**Squad minds and trials**~~ (done 2026-11-09): see §0 and §6.
12. **Character depth**: identify-by-use, enchant scrolls. (Classes and active skills are done.)
13. **Wider region**: more sites, rethink the surface map.
14. **Crafting**: material tags + recipes + stations as places. Postponed (user, 2026-11-09): not important yet
Later: more sites and a wider region (rethink the surface layout then), a campaign chaining sites,
fuller stealth, LLM-driven interrogation (optional), tiles/visual upgrade toward Cogmind/Qud.

Re-run the sim and compare with the current balance (§6) after every content or AI change.

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
- **Decided**: tiers don't gate +X (independent). First affix pool and drop rates are in
  `affixes.json` (2026-11-08); materials and origins are not modelled yet.

### Character
- Now: six D&D attributes and skills bought with XP (Sil-style spending; XP for kills, first
  sightings and new places). Implemented in step 5 part 1.
- Four classes exist (Soldier, Ranger, Rogue, Mystic), each with its own tree and active skills.
- Direction (user): toward **Tales of Maj'Eyal**: more classes and many more skills. The user will
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
