# Development history

What was built, and every balance measurement, in the order they were written down. This is a record: **old entries
describe the game as it was then** (ten levels, the tower, older formulas). For the game as it is now, see
[PROJECT.md](PROJECT.md).

**Done**
- Skeleton: map gen, FOV with memory, bump combat, message log, status line.
- **Goblin squad AI**: roles (warrior, archer, shaman, runner); shared squad knowledge; states
  idle (group patrol) → alert (search last known position) → engage → flee; morale (−25 per death,
  −8 per badly wounded member, regen when not fighting, breaks below 30, rallies at 60);
  warriors surround and hold corridor mouths up to 8 turns while archers shoot; archers kite and
  seek clear shots; shamans heal (cooldown 5, range 6); runners fetch the nearest idle squad.
- **Readability**: sidebar with visible monsters' HP and intent; unseen events reported as sounds;
  unseen attackers are "something".
- **Player kit**: items from data with +X enchantment (incl. cursed) and a rarity tier (see the
  rarity entry below); inventory (20 slots, potions stack) and equipment slots; derived stats; loot
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
- **Squad minds, before the follow-up fixes** (2026-11-09), tactical bot, 500 games each (seeds 1000–1499): Soldier won **44.4%**, Ranger **37.0%**, Rogue **26.6%**, Mystic **41.0%**. All but the Rogue are above the 20–30% target: ordinary dungeon squads are now mindless or rabble, so they got easier. Not yet retuned.
- **Trials** (`--trials`, 2026-11-09, 200 games per cell; fights with nothing else on the level). What they show:
  - Thinking matters to characters who get reached. Six wolves, seasoned Soldier: beast 93%, pack 57%. Mystic: 72%, 15%. Goblin force, Soldier: rabble 91%, drilled 74%; Mystic 75%, 50%.
  - Ranger and Rogue win 90–100% whatever the squad's mind. Nothing yet punishes them. Corridors help everyone (pack vs Mystic: 15% in the open, 50% in a corridor).
  - Strength compounds on twin daggers (both hands get the full bonus): against the warlord's host a Rogue with attribute points in Dex alone wins 29%, Str alone 58%, both 78%. Str on a Soldier does nothing comparable.
  - The bot's head-on fight with the necromancer's court is 0–5% for every build, far below its real win rates, so that row is not representative. Reason not established.
  - Caveats: the tactical bot doesn't sneak or use doors; trial gear is the starting kit plus enchantment, so veteran and final characters are under-equipped.
- **Classes** (2026-11-09), tactical bot, 500 games each (seeds 1000–1499): Soldier won **35.2%**, Ranger **23.6%**, Rogue **17.8%**, Mystic **31.4%**. Classless was not re-measured.
  The bot doesn't sneak, so the Rogue's Backstab rarely fires and its figure is a floor. Soldier and Mystic sit a little over the 20–30% target and Rogue a little under.
  Whole bandit den won: Soldier 53%, Ranger 87%, Rogue 63%, Mystic 26%. Run with `--class ID` (Sim and Play both take it, and so does `--encounters`).
- **The big balance pass** (2026-11-09), tactical bot, 1000 games (seeds 1000–1999): won **28.0%**, died 72%, inside the 20–30% target.
  Death rate by depth: 5.4, 9.4, 8.6, 10.6, 11.8, 9.0, 49.7% (the last level is the boss). Monster HP +26% a level, elites 12% + 3% a level, the warlord from level 4, brace only on plate with a 20% counter.
  Valley fights (`--encounters --seeds 300`, bot with the starting kit, alone against one group): wolves won 93%, spiders 87%, boars 95%, bear 67%, den sentries 100%, den crew 50%, whole den 46%.
- **Heavy armour and speed** (2026-11-09), tactical bot, 1000 games (seeds 1000–1999): won **57.5%**, died 42%, well above the 20–30% target.
  Death rate by depth: 5.3, 14.6, 3.7, 1.7, 1.3, 2.1, 21.6%. The same build with `brace` removed from items.json won 32.0%, so bracing (extra armour and a free counter each turn) is what's too strong, at least for a bot that often holds still in corridors. Not retuned: the user will balance later.
- **Sneaking, hearing and rarer secrets** (2026-11-09), tactical bot, 1000 games (seeds 1000–1999): won **29.1%**, died 70%, inside the 20–30% target.
  Death rate by depth: 5.9, 18.0, 9.1, 6.6, 5.5, 9.1, 47.3%. The bot never sneaks, and now also learns nothing from Stealth.
- **Squads size up the player** (2026-11-09), tactical bot, 1000 games (seeds 1000–1999): won **25.7%**, died 74%, inside the 20–30% target.
  Death rate by depth: 4.3, 17.2, 11.4, 6.4, 7.0, 8.4, 53.2%. The bot kills anyone begging next to it.
- **Weapon speeds and elastic leashes** (2026-11-09), tactical bot, 1000 games (seeds 1000–1999): won **21.9%**, died 77%, inside the 20–30% target.
  Death rate by depth: 6.1, 19.4, 12.2, 10.7, 9.5, 8.4, 54.0%.
- **Seven levels with the crypt boss and elites** (2026-11-09), tactical bot, 1000 games (seeds 1000–1999): won **30.9%**, died 68%.
  Death rate by depth: 9.7, 17.8, 7.2, 4.8, 5.0, 5.0, 47.1% (the warlord's warren makes level 2 hard; the last level is the boss).
- **The finished camp and time passing** (2026-11-09), same setup: won **55.7%**, died 42%.
  About 59000 turns/s. The bot rarely goes back up, so this barely tests time passing.
- **The history generator with tribes and plague** (2026-11-09), same setup:
  - Won **55.1%**, died 41%: far above the 20–30% target. Probably the guaranteed heirloom (a +2 unique on level 2–5,
    not measured apart from the rest). Left as it is: the user will balance later.
  - Death rate by depth: 9.5, 7.9, 3.1, 1.6, 1.9, 2.9, 4.7, 4.2, 6.9, 9.5%.
  - The bot only plays the dungeon; these numbers say nothing about the camp.
- **Coins in the loot tables** (2026-11-09), same setup: won 35.4%, died 61%. Coins replace "nothing" rolls and the
  bot ignores them, so this is noise.
- **Squads search where they lost someone** (2026-11-09), tactical bot, 1000 games (seeds 1000–1999):
  - Won **36.7%**, died 59%. Death rate by depth: 9.8, 8.7, 6.8, 3.4, 4.9, 4.5, 8.1, 10.8, 13.9, 17.7%.
- **Honest enemy knowledge and stored levels** (2026-11-09), tactical bot, 1000 games (seeds 1000–1999),
  20000-turn cap, about 90 s on this 12-thread machine (71000 turns/s):
  - Won **39.6%**, died 57%. Before stored levels, same seeds: won 40.2%, died 57%, so that refactor is
    balance-neutral. Honest knowledge took it from 34.7% (the affix baseline below) to about 40%.
  - Above the 20–30% target; deliberately not retuned yet, as orders, search and camps will move it again.
  - Death rate by depth: 8.5, 6.4, 6.2, 4.3, 4.6, 5.3, 9.4, 8.4, 12.8, 17.0%.
  - 200 games can mislead: the honest-knowledge build won 47% on seeds 1000–1199 but 40% over 1000.
- **With rarity tiers and affixes** (2026-11-08, commit 24c231b), tactical bot, 1000 games (seeds 1000–1999):
  - Won **34.7%**, died 62%, timed out 4%: items add about 14 points over the item-less baseline
    below. A bit above the 20–30% target (the 200-game sample said 29%); retune when the content
    changes, not before.
  - Death rate by depth: 9.3, 9.0, 7.1, 4.2, 5.1, 6.5, 7.7, 11.1, 15.8, 18.6%.
  - Per game: 8.8 magic, 2.6 rare and 0.6 legendary drops; lifesteal fires 15 times, thorns 8.6, cleave 7.5.
  - On arrival at depth 10 the character averages 68 HP (61 without affixes), accuracy 4.3, evasion 4.7.
  - About 3900 turns/s for this run (1000 games in 25 minutes).
- **After the difficulty pass** (2026-11-08, before affixes), tactical bot, 1000 games (seeds 1000–1999):
  - Won **21.1%**, died 75%, timed out 4%. Average turn of death about 3800.
  - Death rate by depth: 9.3, 9.6, 5.4, 4.7, 7.5, 7.2, 11.8, 18.7, 23.8, **34.6%**: rising, as intended.
  - On arrival at depth 10 the character averages 61 HP, melee 16, defence 6.9, 3.5 potions.
  - Killers: goblin archer 239, goblin warrior 159, skeleton archer 126, necromancer 115, skeleton warrior 88.
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

- **Faster simulation** (2026-11-08), found by profiling with `dotnet-trace`:
  - Walkability is a flat array (`CellGrid`), kept in sync by `GameMap.SetTile`.
  - `StepToward` reuses each actor's A* path while it stays on it and the goal is unchanged.
  - `SafetyMap` merges two sorted streams instead of a priority queue (same values; a test pins it).
  - 24 tactical games: 2584 → 4822 turns/s on this desktop.
- **Difficulty pass** (2026-11-08): deaths now rise with depth instead of peaking on level 1.
  - Monsters: +20% HP per level (was 12%), +1 attack every 2 levels (was 3), +1 accuracy every 3
    (was 4), and +1 defence every 4 (new). Rooms 3% more likely occupied per level.
  - A gentler first level: goblin scouts (1–3 warriors, maybe an archer, no runner) instead of war
    bands, no skeleton patrols, a young warren that musters scouts every 150 turns (max 2), and a
    small court (necromancer, one skeleton warrior, one corpse). Lairs come in level ranges for this.
  - Fewer potions: goblins 3 and floor 6 by weight (was 10 and 15).
  - A runner whose target band dies goes to the next nearest band instead of giving up.
  - The sim prints the character's average stats on arriving at each level (`LevelArrival`).
- **Rarity tiers and affixes** (step 5 part 2, 2026-11-08):
  - Each generated weapon, bow or armour rolls +X, then a tier: magic 10% (+0.5% per level), rare 2%
    from depth 2 (+0.4%), legendary 0.3% from depth 4 (+0.15%), else common.
  - Magic: 1 affix, named by it ("keen dagger", "leather armor of the bear"). Rare: 2–3 affixes and
    a name ("Raven Sting (long bow)"). Legendary: 3–4 affixes, a name and a signature special.
  - Twelve affixes add to the shared `Stat`s (accuracy, melee/ranged attack, range, defence,
    evasion, max HP, regen, potion healing, XP), some growing with depth. Worn items add through
    `Actor.Bonus`. Max HP from gear brings no HP (else swapping heals), and taking it off clamps HP.
  - Specials: cleave and lifesteal (weapons), piercing (bows: arrows meet half the armour), thorns
    (armour: melee attackers take 1–3). `Actor.HasSpecial` covers skills and gear.
  - Front end: tier colours on the floor glyph, in menus and on pickup/loot lines (white, green,
    orange, red; gold and violet reserved for uniques and artifacts); stat lines list affixes.
  - Bots value gear with `ItemValue` (affixes weighted against attack). Sim counts loot and
    equipped items by tier.
  - Balance: with affixes the bot won 49.5%; rarer tiers, a weaker HP affix (2–4 + 0.5/level) and
    monster HP growth 17% → 20% brought it to 29% (200 games).

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
