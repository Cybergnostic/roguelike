# Combat, classes and runes: proposal

**Status: being built on branch `combat-and-runes` (user, 2026-11-16: "let's try and do this"); the user will
test it when it's all in.** Step 1 done; see §5 and §6. Built from the user's notes (lethal combat; a powerful player
against weak individuals but dangerous groups; four classes of 3-5 strong skills; magic as runes, wards and
sigils rather than elemental damage) and Claude's suggestions. Mark each part accepted, changed or rejected
before it is built; then move what's decided into [PROJECT.md](PROJECT.md).

Numbers below are **targets** to tune with the simulator, not final values.

---

## 1. Lethality

### Hits to kill (level 1, starting kit)

| | Player kills it in | It kills the player in |
|---|---|---|
| Ordinary (goblin, skeleton, wolf) | 2 hits; 1 with an opportunity | Ranger/Shadow/Mystic 8-10 hits, Warrior ~15 |
| Elite (bone knight, brute, chief) | 4-6 hits | 4-6 hits, its big blow wound up a turn ahead |
| Boss | many | few; a fight with a shape, not a slugfest |

Group danger comes from the arithmetic: one goblin is harmless, four around a Ranger kill it in under two turns.

### Rules
1. **Damage is nearly fixed:** weapon damage x 0.8-1.2, minus armour (flat, no roll), at least 1. Today it is a
   uniform roll from 1 to attack, which turns the same blow into 1 or 9. Hit chance stays a roll.
2. **No random critical hits. Big hits come from opportunities** anyone can see:
   - *unaware*: the target's squad isn't fighting, or it can't see the attacker: x2 damage
   - *staggered*: the target is reeling: x1.5 damage, and the blow can't miss
   Backstab today is a Rogue skill; this makes it a rule for everyone, and Shadow's skills sharpen it.
3. **Overwhelmed:** for each enemy adjacent to a target beyond the first, attacks on it get +1 accuracy and
   +20% damage (at most +3 / +60%). Applies to the player and monsters alike. This is what makes armour
   matter less when surrounded, and what makes getting surrounded the thing to avoid.
4. **Telegraphs:** an elite's heavy blow, a charge, a commander's order are announced a turn before they land
   (shown on the map and in the log). Stepping away, shoving or staggering the attacker spoils it.
5. **Monsters shoot only from where the player could see them** (lit, in the player's FOV rules). No arrows
   out of the dark.
6. **Rest between fights:** with no enemy known nearby for a while, the player recovers quickly to full.
   Attrition comes from what a fight costs (potions, cooldowns, marks), not from HP carried across a level.
7. **Depth changes who you meet, not their numbers.** Monster HP/attack/accuracy/defence growth per level
   (`dungeon.json`) goes to zero. Instead depth raises: elite chance (`elites.json` variants), how many squads
   are drilled or commanded, mixed compositions, and new kinds of creature per depth band. Ordinary monsters
   stay the same, so a grown character really does cut through them.

### Answers to the corridor
A doorway shouldn't always win. Enemies get: shooters holding the corridor mouth (exists), routes around
(levels with loops), spears reaching 2 cells, smoke or fear to flush the player out, and runes (§4).

---

## 2. Character structure

| Layer | What | How it's gained |
|---|---|---|
| Attributes | Str, Dex, Con, Int, Wis, Cha (to be revisited) | XP |
| Class skills | 5 per class, 3 ranks each, useful from rank 1 | XP; only your class's |
| General skills | Perception, Stealth, Athletics, Endurance | XP; anyone |
| Runes | Rune Lore, Inscription (general skills) + runes themselves | skills by XP; runes found in the world |

A winning run should afford about 15 ranks: two or three class skills maxed and a few others, never everything.
Classes are renamed **Warrior, Ranger, Shadow, Mystic**. Current skills fold into the new ones as noted.

---

## 3. Class skills

### Warrior: holds ground, breaks lines
| Skill | Kind | Rank 1 | Rank 2 | Rank 3 |
|---|---|---|---|---|
| Weapon Mastery | passive | +damage, +accuracy in melee | heavy weapons stagger more often | heavy weapons strike 15% faster |
| Combat Stances | toggle (switching takes a turn) | Aggressive (+30% damage, -2 armour) or Guarded (+2 armour, hit back the first attacker each turn) | stances stronger | switching is free once per turn |
| Hold the Line | passive | standing still: +armour, can't be staggered or shoved | an enemy slipping past you (adjacent to adjacent) takes a free blow | Overwhelmed doesn't apply to you |
| Breakthrough | active | Shove: push an enemy 1 cell; into a wall or another creature, both are staggered | Charge (the current `v`) | shove 2 cells, and it carries through a line |
| Battle Momentum | passive | a kill makes your next action quick | a kill gives +damage for 2 turns | War Cry (exists): squads around lose nerve |

### Ranger: kills at range, shapes the approach
| Skill | Kind | Rank 1 | Rank 2 | Rank 3 |
|---|---|---|---|---|
| Marksmanship | passive | +bow damage and accuracy | +range | shots ignore half of armour |
| Shooting Techniques | actives | Rapid Shot (Volley, exists) | Careful Aim: a slower shot, +accuracy, +50% damage | Suppressive Fire: cover an area; anyone entering it is shot, squads lose nerve |
| Skirmishing | passive + active | Disengage: step back and shoot in one action | no point-blank penalty (Skirmisher, exists) | +evasion on a turn you moved |
| Pinning Shot | active (exists) | staggers, and slows for 2 turns | pins: the target can't move for a turn | passes through to a second target |
| Hunter's Instinct | passive + free action | Mark Prey: +damage against it, and you always know where it is | squads that have seen you are shown | your first hit on marked prey is an opportunity |

### Shadow: works the enemy's awareness
| Skill | Kind | Rank 1 | Rank 2 | Rank 3 |
|---|---|---|---|---|
| Shadowcraft | passive | sneaking is quieter and only slightly slower | standing still in shadow or brush: unseen beyond 2 cells | move through cover at full speed while sneaking |
| Opportunist | passive | opportunity hits x3 instead of x2 (Backstab, exists) | isolated (no ally within 2) and fleeing targets count as opportunities | opportunity hits can't miss |
| Evasion | passive + active | +2 evasion against each adjacent enemy beyond the first | Tumble: slip past an adjacent enemy to the cell behind it | Overwhelmed doesn't apply to you |
| Misdirection | active | throw a sound: squads that hear it go to look (uses existing hearing) | smoke: blocks sight in a small area | leave a false trail: squads' last-known position becomes wrong |
| Escape Artist | active | Vanish (exists) | breaking line of sight makes pursuers search the wrong way | Shadow Strike (exists) from hiding counts as an opportunity |

### Mystic: power over the spirit, struck at range
Classical combat skills with an occult flavour: damage, curses, a shield, a weapon empowered. No fire, ice
or chaos damage. The Mystic does not copy the rune skills; one skill makes it better with them.

| Skill | Kind | Rank 1 | Rank 2 | Rank 3 |
|---|---|---|---|---|
| Word of Power | active, short cooldown | a spoken word strikes at range; ignores armour | staggers | double damage against the bound and the dead |
| Hex | active | marks an enemy: it takes +25% damage from everyone, hits weaker | the hex spreads to a nearby enemy when the target dies | hexed enemies' squads lose nerve |
| Spirit Blade | toggle or active | your melee weapon strikes the spirit: +damage, ignores armour | each hit drains a little life (Life Drain, folded in) | a kill with it heals you |
| Aegis | active | a ward on yourself absorbs the next few points of damage | stops arrows entirely while it lasts | when it breaks, adjacent enemies are shoved back |
| Rune-Sight | passive | carve marks twice as fast; read runes without Rune Lore | your marks are a step stronger and you keep one more active | cast Word of Power through any of your marks in sight |

Mend (self-heal) becomes a rune (Berkano, §4), so the Mystic heals through the system everyone has, faster.

---

## 4. Runes (the universal layer)

Magic is **written into the world**: runes carved on gear, on doorways and floors, on stone, on corpses,
painted on the body. A rune is a *meaning*, not a damage type. Runes are **found** (inscriptions, tablets,
carved items, the site's history), not bought; skills decide how well they're used.

### Rules
- **Carving takes time** and makes noise: 6 turns on a floor or door, 1 turn painted on the body (weaker, short).
  Mystics carve twice as fast. Gear is carved while resting.
- **Material matters:** stone holds longer than wood; ash on skin fades in a few turns.
- **Limit:** 2 marks active at once (+1 per Inscription rank, +1 Rune-Sight); marks fade.
- **Enemies treat marks by mind:** the mindless walk into them; beasts flinch once hurt; rabble avoid marks
  they've seen; drilled squads go around and shoot the carver. Undead can be bound by them.
- Marks show on the map as runic glyphs (the font can carry the Futhark).

### First runes (Elder Futhark meanings)
| Rune | Meaning | On a place | On gear | On a creature or corpse |
|---|---|---|---|---|
| Algiz | protection | a ward: a kind you choose (the dead, beasts) can't cross | armour turns arrows | - |
| Thurisaz | thorn | wounds whoever crosses | weapon: +damage | - |
| Isa | ice, stillness | slows whoever crosses | armour: can't be staggered | holds it still a turn |
| Naudhiz | need, constraint | - | - | binds: a corpse can't rise; the bound are held |
| Kenaz | torch | light: reveals and lights an area (hurts Stealth nearby) | - | - |
| Berkano | birch, healing | rest there heals faster | - | heals (the old Mend) |

Later: Hagalaz (breaking: shatter marks, seals, doors), Raidho (journey: step between two marks), bind-runes
(two runes in one mark, combined by rule rather than by list), rituals.

### General rune skills
| Skill | Rank 1 | Rank 2 | Rank 3 |
|---|---|---|---|
| Rune Lore | read inscriptions and learn their runes; see marks and what they do | identify marked items; read enemy seals | break enemy marks and seals |
| Inscription | carve runes on places and the body | carve on gear; marks last longer | one more mark active; bind-runes |

### Where it meets the world
The crypt's dead are **bound by seals**: break or read the seal and they crumble or wake. The necromancer's
wards guard his rooms. Goblin shamans paint crude sigils. The dwarves' inscriptions (already in `SiteHistory`)
teach runes. That gives the crypt a fix made of knowledge and preparation rather than bigger numbers.

---

## 5. Building it

Each step leaves the game playable, is measured with the simulator, and gets a short human playtest where
marked.

| Step | Contents | Playtest |
|---|---|---|
| 1. Lethality | fixed damage, opportunities, Overwhelmed, flat depth scaling + composition, fast rest, retuned numbers | yes |
| 2. Foundations | a small status system (slowed, held, hexed, shielded, marked), telegraphed wind-ups, stances | - |
| 3. Warrior, Ranger, Shadow | the 15 skills; renamed classes; the bot learns to use them | yes |
| 4. Runes | marks on the map, carving, the six runes, Rune Lore and Inscription, inscriptions that teach | yes |
| 5. Mystic | the five skills on top of runes | yes |
| 6. The crypt | seals and bound dead, then the full rebalance and trials | yes |

### Risks
- **The bot can't judge fun, and undervalues skills it doesn't use.** Every skill needs bot support or a
  scripted trial, or the numbers lie. Human playtests decide; the simulator measures curves and catches bugs.
- **UI cost:** aiming at cells (marks, smoke, suppression), stance and status display. Each needs screenshots.
- **Scope:** about 25 new mechanics. Staged as above it is large but tractable; done all at once it isn't.
- **Preparation magic can turn tedious** (warding every door). The mark limit, fading, noise and enemies that
  notice marks are there to stop it; watch for it in playtests.

---

## 6. Progress log

### Step 1, lethality (2026-11-16)
Built: steady damage (±20%), unaware ×2 / backstab ×3 / staggered ×1.5 and can't miss, overwhelmed (+1
accuracy, +20% damage per extra enemy, up to 3), monsters shoot the player only from where it could see
them, quick rest after 15 turns with no enemy in sight (4% of max HP a turn), no stat growth with depth,
flat XP, elites 10% + 7% a level from level 2, rooms 5% fuller a level. `Roguelike.Sim --lethality` prints
hits to kill each way. Starting weapons retuned to the targets: short bow 5, long bow 7, dagger 3,
Cinder Bolt 5.

Changed from the proposal, found while tuning:
- **Armour stops at most half a blow.** Armour in full made the player's growing armour shrug off
  everything (Soldier 98% wins). Now heavy armour halves blows but never ignores them.
- **Deep levels need tougher kinds, not just arrangement.** With stats flat, the level-1 roster can't
  threaten a grown character whatever the composition (rest made no difference: same win rates without it).
  Added goblin veterans and marksmen (level 5 bands, war parties) and barrow guards and archers (cohorts,
  skeleton files). A goblin warrior is still a goblin warrior everywhere.

Measured (tactical bot, 300 games per class, seeds 1000-1299, `--turns 30000`): Soldier 88%, Ranger 96%,
Rogue 84%, Mystic 64%. Was 30 / 18 / 16 / 26 with depth scaling. The crypt cliff is gone; deaths spread over
levels 4-7. Too easy overall: left for the rebalance after the class skills, as agreed.


### Step 2, foundations (2026-11-16)
- **Statuses** (`Entities/Status.cs`, `World.AddStatus`): slowed (percent less energy, or more time for the
  player), held (can't step, can still strike), hexed (+percent damage taken, -percent dealt), shielded (soaks
  points of damage, `ShieldAbsorbed`), marked (+percent damage from whoever marked it). They expire at the end
  of their last turn (`StatusEnded`).
- **Wind-ups:** abilities with `"windUp": true` are announced (`WindingUp`, a "!!" callout, the intent line says
  "about to charge you") and delivered on the monster's next action only if still possible; stepping out of
  reach or a stagger spoils them (`WindUpSpoiled`). The monster charge is now wound up, and a new **heavy
  blow** (+4, always staggers) went to the bone knight, goblin brute, warlord and bandit chief.
- **Stances:** `StanceAction`; Aggressive (+30% damage, -2 armour), Guarded (+2 armour, the first melee
  attacker each turn is struck back). Needs the `stances` special, which the Warrior's skill will grant.
- Not yet: the tactical bot doesn't react to wind-ups or use stances (step 3, bots that play their class).

### Step 3, Warrior, Ranger and Shadow (2026-11-16)
- The 15 skills of §3 as data (`skills.json` trees `warrior`, `ranger`, `shadow`), with per-rank `grants` and
  `rankText` (shown on the character sheet as "next: ..."). New abilities: Shove, Careful Aim, Suppressive
  Fire, Disengage, Mark Prey (takes no time), Tumble, Throw Stone, Smoke. Rules for them in `World.Skills.cs`.
- Classes renamed (ids too): Warrior, Ranger, Shadow, Mystic. Each learns its tree plus the general Senses and
  Survival trees; Toughness moved to Survival. The Mystic is unchanged until step 5.
- Differences from §3, for simplicity: every ability aims at a creature or at yourself (no cell targeting yet):
  Throw Stone lands beyond the creature aimed at, Smoke bursts around you, Suppressive Fire covers the ground
  around a target. Pinning Shot rank 1 slows (50%, 2 turns) as well as staggering. Mark Prey rank 3 raises the
  mark to +60% rather than "first hit is an opportunity".
- Screen: `w` cycles stances; the status line shows stance and conditions; marked prey (and, with Watchful,
  whoever is hunting you) stay drawn out of sight.
- **The bot plays its class:** a build order per class; dodges announced heavy blows (or shoves the brute);
  Warrior shoves into walls and switches stance; Ranger marks, pins, disengages, covers ground, aims; Shadow
  sneaks up on the unaware to backstab, tumbles out of rings, smokes and vanishes when losing.

Measured (tactical bot, 300 games per class, seeds 1000-1299): **Warrior 84%, Ranger 99.7%, Shadow 93%,
Mystic 83%**. Every class is too strong, the Ranger most of all (no deaths below level 1). Left for the
rebalance (step 6). Warriors die mostly on level 1 (11%), before their skills arrive.

### Step 4, runes (2026-11-16)
- `data/runes.json`: Algiz, Thurisaz, Isa, Naudhiz, Kenaz, Berkano, each with what it does on the ground, the
  skin, a weapon and armour (where it can go), and its numbers. Rules in `World.Runes.cs`.
- Known, not bought: the Mystic starts with Algiz and Berkano (and Rune Lore 1, Inscription 1); anyone with Rune
  Lore learns one rune from each inscribed stone they stand on, in an order fixed by the seed.
- General tree `runes`: Rune Lore (read stones; ranks 2-3 for enemy marks later) and Inscription (ground and
  skin; gear at rank 2; longer marks at rank 3; 2 + rank marks at once). Every class can learn them.
- Ground marks go off on enemies of their maker who step on them (Thurisaz wounds, Isa slows, Naudhiz holds),
  spend charges and fade with time. Algiz: the undead can't cross it; the living break it by crossing. Kenaz:
  the player sees whoever is in its light. Berkano: resting beside it heals twice as fast. Naudhiz keeps corpses
  from rising. Minds with `avoidsMarks` (pack, rabble, drilled) step round harmful marks; the mindless and beasts
  walk in.
- Skin: Algiz ward (soaks 8), Thurisaz thorns, Isa steadfast, Berkano heal. Gear: Thurisaz weapon +2, Algiz armour
  +2 against missiles, Isa armour can't be staggered. Gear needs quiet (no enemy in sight) and 15 turns.
- Screen: `r` opens the rune menu (rune, then g/b/w/a, with each surface's effect or why not); marks drawn with
  CP437 look-alikes (¥ ► │ ╪ « ß) until the font carries the Futhark; examine and tooltip describe them; gear
  shows "marked Thurisaz".
- The bot learns Rune Lore and Inscription, walks to stones it can read, paints Algiz as enemies close (not in
  melee), Berkano when hurt with no potion, and cuts runes into its gear when it's quiet.

Measured (300 games per class): Warrior 82%, Ranger 93%, Shadow 89%, Mystic 74%. (A first bot version spent its
turns painting wards and healing runes in melee instead of fighting or drinking: the Mystic fell to 52%.)

### Step 5, the Mystic (2026-11-16)
- The Old Rites tree (Cinder Bolt, Mend, Life Drain) is gone. New `mystic` tree: Word of Power (ranged, through
  armour; staggers; double on the dead and the bound), Hex (+25% taken, -25% dealt; leaps on death; shakes the
  squad), Spirit Blade (+3 melee through armour for 8 turns; drains; heals on a kill), Aegis (soaks 10 + Magic;
  stops arrows; throws back the adjacent when it breaks), Rune-Sight (reads runes, carves twice as fast; marks
  stronger and one more; Word of Power spoken through your own marks).
- The Mystic starts with Word of Power, Rune-Sight and Inscription, knowing Algiz and Berkano. Healing now comes
  from Berkano (skin, and resting beside it), potions and rest.
- The bot: Aegis as enemies close, Spirit Blade in melee, Hex on elites or groups, Word of Power on the most
  important target.

Measured (300 games): Mystic 88% (Warrior 82%, Ranger 93%, Shadow 89% from step 4).

### Step 6, the crypt and the rebalance (2026-11-16)
- **Crypt** (step 6a): the necromancer binds up to two undead squads on the last level with seals (Naudhiz
  marks of his, set in other rooms). Smash a seal (5 loud turns; 1 quiet turn with Rune Lore 3) and its dead
  crumble. Up to four hidden Thurisaz traps lie in the passages round his court, seen only with Rune Lore 2 or
  Rune-Sight. `R` unmakes a mark at or beside you. The bot breaks seals it sees.
- **What the first measurement showed:** by level 5 every class had bought all 15 of its class ranks and
  carried 6-20 potions. The XP economy, not the monsters, was the main reason everything was a walkover.
- **Changes:** class skill ranks cost 100/200/300 (general skills about 1.5x); deep kinds tougher (goblin
  veteran 22 HP / attack 11 / armour 3, marksman 14 / 9, barrow guard 30 / 12 / 4, barrow archer 16 / 9) and in
  bigger numbers; brutal elites +4 attack, veteran archers +2; elites 10% + 10% a level; rooms 9% fuller a level;
  rest slower (after 25 quiet turns, 2% a turn). Per class: short bow shoots in a full turn, long bow 6, Rapid
  Shot cooldown 7, Careful Aim +40%, Pinning Shot cooldown 10 and slows 30%, Disengage cooldown 6, Mark Prey
  +20% / +40%, Ranger Dex 15 / Con 11 / 2 potions; Marksmanship and Weapon Mastery give damage only; Evasion
  adds +1 evasion a rank, Shadow Con 14; Word of Power 4, Aegis 8, Spirit Blade +2, Hex 20%, Mystic Int 15 /
  3 potions.

**Measured (tactical bot, 500 games per class, seeds 1000-1499, `--turns 30000`): Warrior 32%, Ranger 36%,
Shadow 28%, Mystic 30%.** Deaths by level (1-7):
Warrior 15, 11, 2, 9, 22, 26, 17; Ranger 2, 1, 3, 10, 13, 32, 27; Shadow 2, 5, 4, 13, 27, 35, 23;
Mystic 10, 5, 7, 15, 24, 23, 22. Deaths now rise with depth from level 3; the old crypt cliff is gone (level 6
is the hardest). Ranger and Warrior are a little over the 20-30% target. Warriors still die early (15% on
level 1). Fuzz: 3000 fights, no rule broken.
