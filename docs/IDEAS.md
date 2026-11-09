# Design Ideas - Living Roguelike

> **Status:** Ongoing idea bank, started 2026-11-09. These are design possibilities from our discussions, **not** a feature commitment, implementation claim, or replacement roadmap.
>
> **Purpose:** Keep ideas in one place as we discuss the game. Add new ideas here before deciding whether and when to implement them. Preserve rejected and postponed ideas with the reason, rather than silently losing them.
>
> **Sources of truth:** [PROJECT.md](PROJECT.md) for current implementation, confirmed decisions, architecture, and test results; [WORLD_DESIGN_VISION.md](WORLD_DESIGN_VISION.md) for the overall exploration/world direction; [UI_DESIGN_VISION.md](UI_DESIGN_VISION.md) for presentation. If an idea conflicts with a confirmed decision, **the project handbook wins until we explicitly change it**.

## The north star

Build a **classical, turn-based, permadeath roguelike** with:
- **Horizontal exploration** inspired by the freedom, landmarks, curiosity, and meaningful detours of *Morrowind/Skyrim*, rather than their scale or quest-marker structure.
- **Vertical exploration** through interconnected, persistent caves, mines, crypts, forts, and underground routes. Depth changes ecology and decisions, not merely enemy stats.
- **Living places**, not randomly placed combat encounters. Camps, patrols, factions, traders, and monsters have reasons to be where they are.
- **Tactical intelligence** as a signature feature: boars, wolves, undisciplined goblins, and commanded squads behave *qualitatively differently*.
- **Stable, discoverable rules**. Smart player decisions can overcome greater power. The game must be readable, fair, and reproducible.

**Design thesis:** A player should be able to solve problems by *understanding the place and its inhabitants*, not just by gaining damage and armor. The best player stories should arise from the interaction of geography, information, objectives, time, equipment, and AI.

**Scope guardrail:** Prefer one coherent valley, fort, and underground network over a giant empty map. The current documented direction is one scrolling surface and seven dungeon levels, with a necromancer crypt as the ending. The world persists **within a run**, resets on death, and layouts stay stable for a given seed. Do not accidentally turn proposals here into a plan for a permanent campaign world.

## How to use this file

- **[Existing]** Already in the game to some degree. Verify against current code before relying on this label.
- **[Explore]** Worth prototyping or testing; not yet approved.
- **[Later]** Plausible, but should not delay a playable vertical slice.
- **[Concern]** Risk or open design question.
- For each major idea, consider: **player decision**, **world rule**, **how the player learns it**, **AI response**, **implementation cost**, and **a reproducible test**.
- When we decide to build something, move its actionable tasks into the project roadmap/issues while keeping the reasoning here.
- This file collects themes from the conversations; it does not imply that every proposed mechanic should coexist in the finished game.

---

## 1. Speed, action economy, and different combat identities

### Current foundation [Existing]
- Turns use time/energy; speed changes how frequently actors can act.
- Weapon attacks have different time costs (e.g., fast daggers vs. slower heavy weapons).
- Armor may slow **movement**, while providing defense, arrow protection, menace, and bracing.
- Standing still in heavy armor can grant defense and a counterattack; there is also a charge/closing-distance concept.

### Design direction [Explore]
**Slow builds control space; fast builds control time.** A 1.25x-speed actor has roughly five actions per four ordinary actions when action costs are equal. Making slow builds viable by simply adding damage and armor can create a stationary dominant strategy. Instead, offer different *situations in which a build excels*.

- **Light/mobile:** scout, disengage, reposition, circle, outpace pursuers, and choose engagement distance; vulnerable if trapped.
- **Heavy/anchored:** hold a gate or doorway, absorb missiles, resist displacement, intercept someone moving past, shelter allies, and withstand charges; vulnerable to being bypassed, baited into open ground, or outmaneuvered.
- **Reach fighter:** threaten approach squares, punish careless advances, and control distance; less effective when an opponent closes inside weapon reach or attacks from another angle.
- **Skirmisher:** exploit cover, feints, range, and hit-and-run behavior; avoid being forced into stand-up fights.

**Potential mechanics to test one at a time:**
- Interception/zone of control: a braced defender can stop or punish *one* opponent trying to pass.
- Reach and minimum-range disadvantages (especially for spears).
- Momentum and commitment on charges: extra impact in exchange for telegraphing, recovery time, or vulnerability to interruption.
- A bounded **reaction budget** (perhaps one reaction per standard time window) shared by counterattack, interception, and defensive response.
- Context-specific counterplay: flanking, ranged attacks, smoke, terrain, bypasses, breaking line of sight, and forcing a defender to move.

### Important balance warning [Concern]
The project handbook records a heavy-armor simulation where the tactical bot won **57.5% with bracing** and **32% without it**. These are scenario-specific bot results, *not* human win rates, but enough to flag a possible dominant corridor strategy. Test bracing in corridors **and** open terrain, against archers, intelligent flanking, and retreating enemies. Avoid solving this only by tuning a flat defense number.

**Success test:** Two different builds should solve the same fort/cave encounter with genuinely different plans, not just different kill speeds.

## 2. Different kinds of intelligence, not just smarter pathfinding

A creature's intelligence should determine which *concepts* it can use, what it knows, and how well it coordinates.

- **Boar:** immediate threat response, charge, panic, retreat; no sophisticated feints or commander-dependent tactics.
- **Wolves:** pack spacing, circling, separation of weak targets, threat assessment, casualty-based disengagement; no omniscient hive mind.
- **Disorganized goblin band:** loosely follows nearby allies, opportunistic aggression, morale collapses when isolated; poor timing and discipline.
- **Commanded goblin squad:** assign roles, hold reserves, flank, protect archers, defend gates, execute orders, disengage as a group, change behavior if the commander is killed.
- **Undead:** potentially fearless and persistent but rigid; a necromancer or controller can be a meaningful strategic weakness.

**Implementation approach [Explore]:** Compose a small number of capabilities or tactical doctrines, rather than writing a different complete AI for every monster kind. Use goals, local perception, role, group orders, training/discipline, and morale. Let leader loss remove coordination rather than artificially nerfing HP.

**Fairness rule:** More intelligent opponents can plan better, but cannot see through fog of war or react to events they never learned about.

## 3. Information, memory, belief, and alarm propagation

This could be as important as combat AI.

- Distinguish **world truth** from **what each actor/squad believes**.
- Information arrives by sight, sound, discovery, speaking/shouting, signals, patrol reports, and messengers.
- A guard seeing movement in bushes has a *suspicion*, not the player's exact identity and coordinates.
- A sentry killed silently does not instantly alert the whole fort; a replacement later notices the empty post or corpse.
- Memories have provenance, time, and confidence: last seen position, heard disturbance, unidentified tracks, confirmed attacker, rumor.
- Groups can search likely places, post extra guards, close gates, lose confidence, stand down, or misinterpret evidence.
- Enemy coordination should improve when they can communicate, and degrade if a messenger or leader is intercepted.

**Gameplay outcomes:** Distraction, scouting, sabotage, stealth, deception, pursuit breaks, and escaping pursuit all become meaningful without requiring an LLM.

**Implementation hint:** Reuse the existing perception/event system. Add explicit knowledge/belief records where necessary; share knowledge through concrete communications, not invisible faction-wide updates. Ensure the UI never leaks hidden, currently unknown camp states.

## 4. Places should have purposes, rights, and vulnerabilities

A place is not merely a combat arena. It can have territory, entrances, food, workers, guards, political importance, and something its inhabitants are trying to protect.

**Example: goblin roadside fort**
- Controls a bridge or mine approach; guards collect tolls.
- Road passes near rather than obligatorily through the fort; a slower safe bypass exists.
- Sentries watch gates, a reserve stays inside, a patrol follows a route, a leader decides escalation.
- The player can observe, pay, bargain, sneak, lure, distract, attack, retreat, or ignore it.
- Casualties, damage, theft, alarm, agreements, and commander death affect the next visit.
- Pursuers have a purpose and stopping rule: defend territory, recover goods, drive off a threat, or capture an attacker. They may cease chasing when the cost exceeds the benefit.
- If outmatched, defenders may retreat behind walls, close the gate, request help, or negotiate.

**Open design question:** Should all forts support nonviolent approaches? Probably not; the important thing is that restrictions follow understandable local circumstances instead of universal kill-on-sight logic.

## 5. Exploration with landmarks and curiosity

The *Morrowind* lesson is freedom of discovery, not kilometers of terrain.

- From a ridge, a player notices smoke, a ruin, a bridge, or a watchtower and chooses to investigate.
- Roads are fast and legible but exposed; forests offer concealment and uncertain routes; rivers create crossings and obstacles.
- A few hand-designed, memorable site archetypes can be placed by a seeded generator in a coherent geography.
- Exploration yields useful alternatives: shortcuts, safe camps, information, resources, factions to contact, mechanisms, and alternate entrances.
- Every region should reward occasional detours even without a quest.
- Avoid long empty walks. Travel should create decisions, observations, and changes in risk.

**Test:** Remove combat from one stretch of the overworld. Is there still a reason to explore it?

## 6. Verticality as an interconnected network

Avoid treating deeper dungeon floors as the same rooms with larger numbers.

- Surface fort, old mine, cave mouth, collapsed shaft, abandoned watchtower cellar, and crypt can be linked.
- Different depth bands have different history, ecology, routes, visibility, resources, and problems.
- A hidden underground route might bypass a defended gate; a cave may exit on the far side of a river.
- Unlocking a lift, lowering a bridge, repairing a ladder, or draining a chamber changes traversal permanently for the run.
- Shortcuts and multiple entrances reward map knowledge and make retreats strategically interesting.
- Give the descent a story and destination; the currently planned necromancer crypt can anchor it.

**First small prototype:** One surface fort, one shallow cave, two entrances, one blocked passage that can be opened, and an observable consequence above ground.

## 7. The environment as a consistent system

Favor a **small reusable ruleset** over hundreds of unique scripted interactions.

- Light, darkness, torches, and extinguishable light sources.
- Sound propagation: heavy armor and running make noise; terrain and barriers affect hearing.
- Doors, gates, barricades, bridges, obstacles, and destructible/repairable passages.
- Fire + combustible objects; smoke + visibility; water + mobility, and potentially electricity.
- High ground, chokepoints, cover, soft ground, and narrow paths matter to player and enemy alike.
- Animals and squads respond differently to hazards based on nature/training.

**Order of operations:** Start with *doors/gates, lighting, sound, and terrain cover*, as these directly strengthen existing stealth/camp/combat systems. Add fluid/fire interactions only if they produce consistent decisions and remain understandable.

**Rule:** Do not add a spectacular interaction that the player cannot predict or the enemies cannot respond to believably.

## 8. Routines, time, and observing the world

The player should sometimes gain more by **watching** than by attacking.

- Guards rotate at dusk; patrols change routes or return to camp.
- Hunters, wolves, foragers, and caravans have destinations and rough schedules.
- A camp may be understrength when a raiding party is away.
- Night changes visibility and behavior; rest and waiting have risks and opportunities.
- NPCs have only as much simulated routine as gameplay requires.

**Tech principle:** Near the player, run detailed actors; for distant sites, advance compact state/events. Preserve consequential changes and leave evidence (tracks, sounds, reports, missing guards, damaged gates) so offscreen simulation does not feel like arbitrary randomization.

## 9. Knowledge as progression

A character can gain options without increasing numerical stats.

- Tracks reveal routes, numbers, or creature types.
- Watching patrols reveals habits and shift changes.
- Books, inscriptions, witnesses, and ruins reveal site history and hidden connections.
- Conversations expose rumors; rumors may be wrong or incomplete.
- An exploration map records **seen**, **remembered**, **reported**, and **inferred** information separately.
- Learning that wolves avoid a path, goblins have a weak gate, or a cave connects to the river can change a plan.

**Critical distinction:** Stable *general rules* remain learnable across runs; each generated world's *facts* may vary by seed. Do not make every run require memorizing random, untelegraphed exceptions.

## 10. Situational quests and noncombat goals

Use a few clear authored objectives embedded in an independent world.

- A village needs medicine, but the road is held by bandits: fight, pay, sneak, find a side route, or exploit a rival's attack.
- A lost heirloom has historical provenance and several plausible recovery routes.
- Reveal why goblins control a mine; sell information, negotiate passage, or alter the balance between factions.
- A faction's goal may change while the player is elsewhere; the same objective now needs a different solution.

Rewards need not all be XP from kills. Information, access, safe travel, an ally, a repaired shortcut, supplies, or a lasting change to a place may be more interesting.

**Important:** Systemic quests still need clear motivation, readable stakes, and a reasonable way to know when an objective is complete.

## 11. Faction ecology and world consequences

- Factions need limited objectives and resources, not full-fledged simulated national economies.
- Raids, replacements, recovering casualties, patrol shortages, ownership, and supply needs produce visible changes.
- Killing a commander may weaken coordination; killing guards might make a gate easier to approach, but raise security later.
- Predators may move if prey disappears; undead attacks can draw defenders away from a gate.
- Alliances, neutrality, trespass, suspicion, and an individual squad's immediate threat response should be distinct.

**Avoid:** A huge global simulation where events happen continuously but rarely change anything the player can notice or influence.

## 12. Procedural generation with causality

Generate or select a coherent **history and geography**, then materialize places and clues.

- A mine existed because of a resource; its collapse explains blocked shafts and abandoned equipment.
- A fort controls an actual route; inhabitants occupy strategically useful positions.
- A necromancer's arrival explains local undead, missing villagers, and unusual activity.
- Artifacts belong to events and places, not just random drop tables.
- Maps should be internally consistent and traversable; the same seed should preserve each location's layout independent of visit order.

**Possible future use of generative AI [Later]:** Offline authoring of lore drafts, plausible accounts, object descriptions, and encounter candidates. Validate and compile into structured content. The simulation's truth and combat rules remain deterministic.

## 13. UI, sound, and modern presentation

See also [§19: Character glyphs, auras, and visual effects](#19-character-glyphs-auras-and-visual-effects) for a detailed proposal.

Keep the readability of Brogue, the information design of Cogmind, and the atmosphere of Caves of Qud.

- Enhanced ASCII as a default; classic ASCII remains usable; optional hybrid tiles can wait.
- Map-first composition and scalable UI; good keyboard control, inspection, contextual actions, and inventory.
- Telegraph intentions: a guard raises a horn, an archer aims, a squad withdraws, a gate closes.
- Differentiate what is *visible now*, last remembered, rumored, and unknown.
- Sound cues, log entries, small animations, lighting shifts, and restrained effects should reinforce the simulation, not obscure it.
- Have an inspection mode that explains mechanics and stakes without exposing hidden AI state.
- Navigation and auto-explore should remove repetitive input, not make decisions for the player.

**UX test:** Can a new player understand *why* the goblins threatened, chased, then stopped chasing them?

## 14. AI that is useful without taking over the game

**Runtime tactical AI [Primary]:** deterministic, testable utility/goal-oriented or state-driven decisions, group roles/orders, perception and memory. Use language models **only optionally**, not to resolve combat or invent world facts.

**Optional NPC language [Later]:** Convert a natural-language request into a validated intent (ask, threaten, bribe, negotiate) or express only grounded knowledge from a character's memory. Avoid mandatory latency/cloud dependency.

**Development-time AI [Useful]:** propose content variants, write scenario tests, search for exploits, and help generate authored data. Keep human review, validation, and balance tests.

## 15. Testing gameplay quality, not just correctness

Existing seeded simulation and scenario testing is a strong asset. Extend it to test *player experience hypotheses*.

**Scenario matrix:**
- Heavy vs. light builds in corridor, open terrain, arrow pressure, chase, and ambush.
- Feral boar vs. wolves vs. unled goblins vs. commanded goblins.
- Sentry killed silently vs. sentry escaping to report.
- Player pays toll, sneaks around, steals, attacks, flees, returns.
- Commander killed while the rest of the squad is alive.
- Camp raided while player is underground, then revisited.

**Measure:** Fairness/readability, action economy, success by approach, escapes, observed vs. hidden knowledge leaks, and whether viable strategies vary by place. Headless bot win rate alone is insufficient. Watch humans play.

**The key success signal:** Players recount specific events and alternative decisions, not just loot statistics.

## 16. Suggested order to explore ideas (not a locked roadmap)

1. **Protect the existing slice.** Playtest valley -> goblin camp -> cave -> crypt -> return; fix unclear rules, major UI issues, and surprising balance exploits.
2. **One intelligent-place experiment.** A fort with watches, gate, alarm, patrol, reserve, and leader; clear pursuit/retreat conditions; outcomes persist.
3. **Honest knowledge and communication.** Suspicion, last-known sightings, corpse discovery, alarm transmission, and believable searches.
4. **Three character approaches to one place.** Fast, heavy, stealth; verify each meaningfully changes decisions and enemy response.
5. **A discovery network.** Visible landmark, clue, alternate cave entrance, shortcut, one interactable environment feature.
6. **Polish and external testing.** Excellent feedback, responsive UI, reliable saves, varied content, and a shareable build.
7. **Only then grow outward.** More sites, ecology, dynamic objectives, deeper procedural history, additional spells/material systems, broader faction operations.

### What not to chase too early

- Giant seamless continents with little to do.
- Full settlement economies and individual lives for every NPC.
- Unrestricted LLM-controlled combat, quest truth, or procedural rules.
- Huge item/monster catalogs before the central loop is enjoyable.
- Sophisticated crafting, many classes, or broad meta-progression before exploration works.
- Perfect global simulation that has no visible consequences.

---

## 17. Open questions for future conversations

- How should interception, reactions, reach, and bracing interact with the time/energy system?
- How can the player read a squad's intentions without seeing private state?
- Which three types of monster intelligence are most important for the first slice?
- What is the minimum memory/information model that makes ambushes, stealth, and alarms believable?
- What rewards make peaceful or stealthy completion competitive with killing everyone?
- Which surface discoveries genuinely change underground decisions, and vice versa?
- How many meaningful locations can a compact valley support without feeling crowded?
- What signals would show that the alpha is *fun* rather than merely technically ambitious?

## 18. Running discussion log

### 2026-11-09 - Foundational brainstorming
- Reaffirmed that **different builds should solve different tactical problems**. Speed is powerful; slow armor builds can control space, intercept, and brace, but need counterplay.
- Proposed intelligence tiers for boars, wolves, disorganized goblins, and commanded squads, with meaningful effects from leader loss.
- Proposed treating **knowledge, alarm propagation, perception, and belief** as gameplay systems.
- Expanded the world vision: living camps, landmarks, vertical shortcuts, environment interactions, routines, observation, situation-driven quests, and knowledge-based progression.
- Agreed to keep these in an independent **ideas document**, without confusing ideas with confirmed decisions or the development roadmap.

### Template for the next idea
```markdown
### YYYY-MM-DD - Idea title
- **Idea:** What could happen in the game?
- **Player decision:** What new choice does it create?
- **World/AI response:** What rules and reactions make it meaningful?
- **Feedback:** How will the player understand it?
- **Status:** Explore / Later / Existing / Rejected
- **First experiment:** Smallest reproducible scenario that could validate it.
```
## 19. Character glyphs, auras, and visual effects

**Status: [Explore] — design discussion, not an implementation commitment.** This elaborates on the UI direction in §13 and [UI_DESIGN_VISION.md](UI_DESIGN_VISION.md).

### Overall aesthetic

Aim for **Cogmind's dynamic feedback with Brogue's restraint**: a crisp Enhanced ASCII map with selective glow, animated highlights, meaningful aura effects, and brief particles. Do not replace clear ASCII with constant neon or heavy post-processing. **The glyph remains readable even when its effect is removed.**

The player asked about effects **around the @ and enemies**, not just tinting the characters themselves: halos, surrounding-cell illumination, weight/boldness, outlines, pulses, and effects that make characters feel alive.

### Character visual hierarchy

| Subject | Default treatment | When to intensify |
|---|---|---|
| Player `@` | Bright, stable, strongly identifiable; subtle warm outline or soft shadow | Dangerous status, a major ability, selected/targeted, damage |
| Ordinary goblin `g` or wildlife | Clean, readable colored glyph; minimal persistent effect | Attack windup, alarm, poison, burning, damage, pursuit cue |
| Elite/commander `G` | Heavier glyph or distinctive outline; subdued rank/background marking | Orders, rallying, special move; brief stronger pulse |
| Necromancer `N`, wraith, major supernatural actor | Distinct supernatural color; restrained soft halo that may extend beyond the cell | Casting, corpse raising, shielding, life-drain, major event |
| Corpse, summoned unit, object | Normally quiet; no constant glow unless it has a mechanical reason | Raised corpse briefly flickers; active magical item pulses |

**Important implementation finding:** `Theme.Bold(glyph)` already maps glyphs to heavier font copies, and `GameScreen.DrawMap()` currently calls it for **every actor**. Thus boldness is not yet a rank/importance signal. Consider normal glyphs for ordinary creatures and stronger visual weight reserved for the player, elite/commander, and major supernatural enemies; confirm legibility at actual 18x18 map-cell size before changing it.

### Three categories of effects

1. **Identity effects:** stable but restrained visual hierarchy (player outline, elite typographic weight, supernatural baseline aura). These help identify what the character *is*.
2. **Status effects:** visible, temporary gameplay conditions (bracing, poison, burning, shielding, enchantment, fear). These indicate what currently affects them.
3. **Action effects:** short, event-driven telegraphs and impacts (archer drawing an arrow, charging fighter, commander shouting orders, necromancer raising corpses, shield absorbing a hit). These explain what the character *does*.

Avoid using the same visual treatment indiscriminately for rank, hostility, item rarity, player selection, and threat: they convey different information.

### Specific candidates to prototype

- **Braced / feet set:** faint ground ring, firm underline, or edge markers around the actor's cell; no continuous bright halo.
- **Arrow protection / shield:** a cool outline or transient flash **when a missile is absorbed**, ideally distinct from a magical barrier.
- **Magical shield:** thin blue-white aura; brief burst on an absorbed impact.
- **Poison:** a few intermittent muted green motes; the text/status panel preserves clarity.
- **Burning:** tiny embers and, only if fire emits actual light in game rules, legitimate illumination.
- **Necromancy:** faint violet halo around the caster; pulse when spell is performed; eligible visible corpses flicker before rising.
- **Wraith:** subtle trailing afterimage on movement (very short and optional).
- **Elite leadership:** short rally pulse or shared response cue when a commander gives an observable order, without drawing hidden squad links.
- **Aiming / charging:** momentary directional cue and windup, giving the player an opportunity to understand the threat *if the mechanics really grant that opportunity*.
- **Damage / miss / stagger:** crisp one-beat hit feedback rather than long animation that blocks input.

### Effects should sometimes extend beyond one cell

At the current approximately **18×18 px map-cell** size, a soft halo need not be restricted to the actor cell. An independent transparent render layer can add restrained radial glow, particles, afterimages, directional indicators, or tint to adjacent cells while the glyph itself stays sharp.

**Two visually different effects to preserve:**
- **Decorative aura:** a small visible halo that does **not** imply additional in-game detection or illumination.
- **Actual light source:** a simulation-backed radius and intensity that changes visibility, terrain lighting, stealth, and AI perception consistently.

Do not let a purely decorative aura accidentally look like a mechanically significant light radius. Neither type should expose enemies outside actual player perception or explored knowledge.

### Layered technical approach (no engine rewrite)

Existing relevant code: [`Theme.cs`](../src/Roguelike.App/Theme.cs) (palette, `Bold`, `Lit`, rank backgrounds), [`GameScreen.cs`](../src/Roguelike.App/GameScreen.cs) (map/actor rendering), [`GameScreen.Popups.cs`](../src/Roguelike.App/GameScreen.Popups.cs) (existing floating overlays), [`data/looks.json`](../data/looks.json) (glyph/color definitions).

- **Phase A - cheap:** normal versus bold glyph rules; brighter/dimmer foreground; rank/status background tints; clear selected and braced markers. Verify appearance in crowding/darkness.
- **Phase B - cell-based:** bounded neighboring-cell aura, slow pulses, short hit/guard/command telegraphs. Add a cosmetic effect queue separate from Core's deterministic rules and RNG.
- **Phase C - animated overlays:** 1–3 particles, brief trails, visible cast effects and shielding impacts. Avoid masking map glyphs or terrain affordances.
- **Phase D - optional smooth rendering:** explore transparent MonoGame sprites or a separate overlay for feathered radial glows/additive effects. Shader/bloom support should be *verified by prototype*, not assumed. No mandatory move from SadConsole.

**Effect data model suggestion:** visual effect descriptor = kind, source actor or known cell, semantic role/color, start time, duration, radius/intensity, and player-visibility constraints. The UI consumes structured world events where available; simulation remains fully deterministic and presentation animation never consumes simulation randomness.

### Animation policy

- **Persistent:** player prominence and very faint supernatural baseline only.
- **Occasional:** low-frequency glow/pulse for significant magic, no constant flashing in standard combat.
- **Immediate:** short, skippable feedback for hits, barriers, alerts, windups, spells, and orders.
- **Accessibility:** reduced-motion option, no mandatory flashing, color-independent shape/text cues, readable classic profile.
- **Priority:** only the most important aura/status is visually prominent on a character at once; other states are available through inspection and the HUD. Don't create rainbow stacks.
- **No omniscience:** effects from unseen actors, unheard signals, or hidden orders do not appear in the normal player view.

### First focused visual experiment

Render one tiny encounter scene in four states: **player `@`, ordinary goblin `g`, elite commander `G`, necromancer `N`**. Compare the view during ordinary movement, in torchlit darkness, during a crowded squad battle, and when each actor performs its defining action. Test at current font/cell dimensions and in Classic mode.

**Acceptance questions:** Can the player instantly find `@`? Can they distinguish ordinary from exceptional enemies *without* inspection? Are active status effects legible? Does a command, spell, or charge read as an action instead of random spectacle? Can all visual effects be disabled while preserving the necessary gameplay information?

**Open aesthetic choice:** Keep a mostly Brogue-like quiet surface with occasional spectacle, or use more frequent Cogmind-like event feedback? Current recommendation: **dynamic on meaningful actions, restrained at rest**.
