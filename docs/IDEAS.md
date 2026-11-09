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

## 20. Materials, weather, digging, and a reactive environment

**Status: [Explore] - a potentially major design pillar, not an agreed implementation plan.** Inspired *very loosely* by Noita's systemic material interactions, but adapted to a **discrete-turn, grid-based, tactical classical roguelike**. Do not attempt to recreate Noita's per-pixel falling-sand simulation.

### The opportunity

A third complementary pillar may be **a reactive physical environment**, alongside intelligent inhabitants and persistent exploration. The environment should give the player choices beyond walking around walls and exchanging attacks. Terrain, materials, weather and time can change line of sight, movement, stealth, route planning, fort defense, enemy behavior, and the history a place leaves behind.

**Central rule:** Materials behave consistently, in ways the player can learn and exploit; creatures must respond believably to those rules. Systemic depth is valuable only when its consequences are visible and understandable.

### Material vocabulary and possible properties

Propose representing each terrain cell with a **base material** and selected **environmental state/overlays**, rather than multiplying special tile enums. Candidate properties: hardness/diggability, opacity, permeability, flammability, structural support, movement time, noise, cover, slipperiness, and whether it records tracks. Not every material requires every property.

| Material / condition | Possible behavior and tactical use |
|---|---|
| Loose soil | Diggable; excavation is noisy and leaves disturbed ground |
| Hard stone / bedrock | Cannot easily be dug; anchors important map constraints |
| Masonry / old brick | Breakable at high cost; weaker ruins may give alternate entrances |
| Timber / wooden supports | Flammable and breakable; can brace tunnels or form gates/barricades |
| Sand | Slower footing; prints may be visible; loose banks can collapse |
| Snow | Accumulates, changes travel and sound, preserves and then obscures tracks |
| Ice | Slippery or breakable, especially over water; footing risk |
| Water / mud | Alters travel and sound; can impede armored characters or redirect access |
| Grass / brush | Hides actors but may burn; the season/weather can change conditions |
| Rubble | Can be cleared, stacked as cover, or used to block an entrance |
| Fire / smoke | Active overlays: damage, illumination, occlusion, alarm, spreading to appropriate fuel |

These are **design possibilities**, not claims about currently implemented rules. Start small, with deterministic material definitions and a limited number of state changes.

### Diggable, breakable and buildable terrain

Digging is potentially a strong form of **player-authored route creation**, especially in a game with horizontal wilderness and vertical dungeons.

- Pickaxes or abilities can excavate **soft earth**, weak masonry, or damaged mine walls; solid bedrock stays resistant or unbreakable.
- Digging consumes time, makes noise, risks tool wear or exhaustion, and exposes the player to pursuit.
- The resulting passages persist through the run, becoming shortcuts, escapes, bypasses, or vulnerabilities.
- Barricades, doors and rubble can change traffic and defensive positions, including during raids.
- Structural support can matter *selectively*: loosened ground or damaged timber may cause an understandable, telegraphed collapse. Avoid unpredictable instant-death cave-ins.
- AI must adapt: guards investigate repeated mining sounds, smart squads reroute or block holes, workers clear rubble, and selected burrowers dig their own passages.

**Critical guardrail:** unrestricted digging can invalidate forts, quests, monster navigation and dungeon pacing. Keep meaningful hard boundaries, expensive excavation, support requirements, warnings, and authored exceptions grounded in material rules. A blocked route should feel like a physical constraint, not an unexplained invisible wall.

**Engineering consequences:** Map mutation must invalidate cached paths, update walkability/transparency and FOV, preserve map changes on revisit, and revalidate generated layouts and accessible exits.

### Day/night and real illumination

The existing renderer has visual light and firelight, but a proposed next step is a **simulation-backed world light model** distinct from cosmetic glow (§19).

- Sunlight intensity and angle vary by time; caves have little or no daylight.
- Torches, braziers, burning objects and magic can create genuine local light sources.
- Darkness, cover and illumination influence player visibility **and** monster perception consistently.
- Routines respond to time: guard shifts, nocturnal animals, traders traveling by day, riskier night routes.
- Fire, moonlight, fog and storm conditions can make one route favorable at a particular time.
- The UI must make *what is actually illuminated* clear, without revealing hidden actors or enemy knowledge.

Night should not be merely a blanket visibility penalty or color filter. It should offer distinct opportunities: stealth and hidden movement alongside navigation and detection risks.

### Weather, climate, and surface conditions

Environmental weather should matter mechanically without requiring continuous particle-level simulation.

- **Rain:** dampens fires, muddies some terrain, may mask footsteps, reduces distant sight, creates wet surfaces.
- **Snow:** builds up over time, records tracks, obscures old tracks during snowfall, slows travel in deep drifts.
- **Fog:** reduces distant visibility for all parties, including archers and patrols.
- **Wind:** can influence smoke movement, fire spread, scent/track interpretation where modeled.
- **Sandstorms:** reduce sight, cover footprints, change route risk and create visibility windows in arid regions.
- **Thunderstorms:** short lightning flashes can create dramatic but rule-consistent visibility events.

**Footprint example:** A scout notices wolf tracks in snow and follows them to a den. Later, new snowfall gradually erases the trail. Conversely, an enemy sentry can discover the player's tracks and investigate - without instantly knowing the player's precise location.

Weather and day/night should affect NPC routines, hostility decisions and pursuit, not merely alter the player's appearance.

### A few high-value interactions rather than all possible physics

- **Dry brush + fire + wind:** a watch post catches fire; goblins hear the commotion and dispatch defenders, creating a tactical opening.
- **Torch + smoke:** illumination improves while smoke eventually obstructs a narrow corridor.
- **Water + cold:** a route freezes and becomes passable but slippery; thawing changes it again.
- **Soft ground + digging:** a cave connects behind a fort, offering an alternate approach at the cost of time and noise.
- **Timber supports + damage:** a mine passage collapses; goblins lose access until workers clear or bypass the obstruction.
- **Snow + patrols:** tracks reveal a route and may betray a hidden infiltrator; active snowfall changes the evidence over time.
- **Mud + armor:** a heavily armored fighter's route choices differ from a lightly equipped scout's; terrain supplements build identity without dictating it.

### The Noita-inspired distinction: simulate rules, not every particle

Keep three separate layers:

1. **Material definition:** stable, data-driven terrain properties (soil, stone, wood, etc.).
2. **Environmental simulation:** discrete cell-level quantities/states (burning, wet, snow depth, smoke density, tracks, disturbed ground, support damage) updated on **game turns**.
3. **Visual-only particles:** drifting snowflakes, dust, sparks, embers, smoke wisps, rain and magical effects; these need not be individually physical entities or alter world state.

A fire spreading from one cell to another is a **simulation** event. Rendering twenty sparks above the fire is **presentation**. Neither the number nor the position of cosmetic sparks should influence deterministic outcomes.

For offscreen locations, use sparse scheduled events or aggregate environmental updates rather than detailed simulation of every snowflake and smoky tile each turn. Store significant persistent changes (opened tunnel, burned fortification, flooded room, tracks when relevant).

### Integration with existing systems

| Existing system | Useful connection |
|---|---|
| Squad AI and orders | Use cover, avoid or exploit hazards, investigate sounds, guard new breaches, clear obstacles |
| Perception and stealth | Light, shadows, fog, smoke, footprints, disturbed terrain and sound transmission |
| Character builds and equipment | Movement time across snow/mud/sand; heavy armor, climbing/digging gear; no universal best build |
| Factions and territory | Fires, collapsed gates, repairs, blocked roads and new passages change strategic objectives |
| Procedural history | Past cave-ins, old masonry, timber supports, drainage and dig sites explain map geography |
| Exploration and vertical topology | Excavation, frozen crossings, floods and opened shafts create interconnected routes |
| Persistence | Terrain alterations and major environmental events remain when the player returns |
| Enhanced ASCII visuals | Warm/cool daylight palette, shadows, rain/snow overlays, steam/smoke, debris and particles - always secondary to readability |

### Recommended implementation sequence - only after testing fit

1. **Terrain material metadata + one dig action** in a contained cave/fort scenario. Make a soft-earth wall diggable while bedrock remains solid; test path/FOV updates, time cost, noise, and persistence.
2. **Simulation-backed lighting and day/night** for the valley and dungeon. Test guard detection, movement and atmosphere under day/torch/night conditions.
3. **One weather loop (rain or snow) plus tracks**, including a human- and AI-readable investigation scenario.
4. **Bounded fire and smoke**, with strict spread limits, clear telegraphs and predictable AI responses.
5. **Later experiments:** sand, ice, water flow, flooding, structural collapse, burrowers, barricades, seasonal/biome differences. Add only if they create more meaningful decisions than maintenance cost.

### Quality, performance and fairness constraints

- Turn-based environment rules are deterministic from seed and input. Cosmetic animation and particle randomness stay independent.
- Environmental effects must have bounded CPU/update costs and avoid processing the whole inactive overworld each turn.
- Use event-driven or dirty-region updates where possible; avoid expensive flood fill, dynamic FOV and path recalculations unless terrain actually changes.
- Hazards should be **discoverable and telegraphed**. Permadeath combined with arbitrary collapse or invisible fire spread would feel unfair.
- Keep exits and required objectives accessible by at least one viable route; enforce world-generation validation.
- Give players readable cues about unstable ground, combustible supports, smoke, visibility and local weather.
- Weather particles must not obscure important glyphs; supply reduced-motion and reduced-effects options.
- Do not make crafting a prerequisite for using the environment. Simple tools and obvious affordances are better for the first slice.

### Focused validation scenario

**Snowy mine approach:** follow goblin tracks to a disused tunnel, dig through a weak wall (making noise), and emerge near the fort. A goblin guard can discover the disturbance or tracks; defenders respond using their normal knowledge and orders. A damaged timber support can optionally close one route with clear prior warning.

This single scenario tests **material properties + digging + weather + evidence + NPC investigation + map persistence** without a universal physics simulator.

### Big open decision

Should environmental manipulation be a **primary gameplay pillar** (players frequently dig, burn, flood, build and reshape places) or a **supporting layer** (occasional meaningful interactions that deepen exploration and squad tactics)? This decision affects world generation, equipment design, AI, performance, UI, and development scope.

**Current recommendation:** begin as a supporting layer, prove a few surprising systemic encounters, then promote it to a central pillar only if testing shows that terrain manipulation is genuinely fun and distinct.

---

## 21. Further systemic additions - physical interaction, senses, magic, recurring characters

> Discussion: 2026-11-09. **User feedback recorded**. Brainstorming, not a locked roadmap. The first ideas are particularly worth exploring. Do not treat any proposal below as implemented.

### Priority and user response

| Idea | Current disposition |
|---|---|
| Small-scale physical object interaction | **Strong interest.** Keep scope limited; do not build a full physics or construction simulator. |
| Different creature senses | **Strong interest.** A particularly good fit for species differences, AI, stealth, weather, and terrain. |
| Distinctive systemic magic | **Major future pillar.** Magic has not been designed yet; it should be a huge and distinctive part of the game, **not merely another ranged damage build**. Explore thoroughly later rather than specifying its system prematurely. |
| Recurring enemies and characters | **Strong interest.** Angband-style memorable named uniques (user mentioned Grishnákh and Gollum as examples), potentially complemented by persistent emergent individuals. |
| Social interaction and deception | **Keep as an open idea.** Player is unsure what form it should take; avoid presuming complex disguises/dialogue. |
| Expedition preparation | **Keep as an open idea.** Interesting, but implementation and emphasis undecided. |
| Environmental mysteries and mechanisms | **Keep as an open idea.** Explore when location/history/physical systems mature. |

### Physical object interaction - small, tactical, systemic [Explore - strong interest]

A small action vocabulary could cover pushing/pulling objects, dragging bodies, throwing portable items, moving obstacles, using a rope, or overturning a cart for cover. Focus on a few test objects and consistent rules rather than trying to simulate every object.

Examples:
- Push a crate in front of a gate, obstructing a pursuer.
- Move a corpse out of the necromancer's reach; moving it costs time and may leave traces.
- Tip a cart as temporary cover; enemies can reposition around it or move it if capable.
- Move a heavy object with Strength and action-time costs; weight/material and terrain are relevant.

The AI must understand blocked paths; world persistence must remember meaningful object changes. Avoid cumbersome commands and excessive inventory micro-management.

### Creature-specific senses [Explore - strong interest]

Perception should not be a single universal sight-radius stat. Build an extensible perception vocabulary and start with a few archetypes:

- Humans and goblins: sight and hearing, impacted by light, smoke, cover and noise.
- Wolves: smell and scent trails, influenced by wind, rain, time and proximity; not omniscient tracking.
- Spiders: vibrations or disturbed webs; crossing a web quietly may matter more than light.
- Bats: echolocation under meaningful constraints.
- Special undead: possibly detect life or magic, with clear, learnable limits.

**Gameplay contract:** The player needs clues to understand why they were detected; monster senses must respect uncertainty and stale information. Different species should pose different *problems*, not just have different numerical awareness. Do not implement every proposed sense at once; start with one meaningful nonvisual sense.

### Magic as a major, unusual pillar [Explore later - strong interest, specifics undecided]

User wants magic to be **fundamentally more than a ranged build**. Do not lock in spell schools, resource bars or a fixed spell system yet. Candidate design questions:

- Magic that changes rules or situations: darkness, silence, illusions, light, ice, weather, matter, minds and ritual effects.
- Magic that interacts with actual materials, corpses, terrain, sound, AI knowledge and faction behavior.
- Rituals that need time, preparation, places, materials or risk, rather than functioning as instant combat attacks.
- Enemies use magic intelligently and its effects should be legible and predictable enough for tactical choices.
- The player could become a magician with a meaningfully different relationship to the world, rather than an archer with mana.

These are **possibilities, not user-approved specifications**. Magic merits its own later design discussion.

### Memorable named uniques and persistent individuals [Explore - strong interest]

User explicitly referenced the Angband tradition of named recurring figures and uniques, e.g. Grishnákh and Gollum. Distinguish two complementary models:
1. **Authored / generated world uniques:** memorable named creatures or characters, recognizable across runs, with distinct capabilities, habitats, drops and behavior; their circumstances may differ per world seed.
2. **Emergent persistent individuals:** a commander survives an encounter, remembers what it actually witnessed, changes orders or equipment, and can reappear **later in the same run**.

Do not imply that a procedurally surviving enemy literally remembers a previous permadeath run unless the game deliberately introduces cross-run continuity. A named unique can recur across new runs without carrying its previous-run memories. As always, no omniscient knowledge transfer.

### Social interaction and deception [Keep open]

Possible elements: faction recognition, trust, warnings, tolls, reputation, permissions, bluffing, forged credentials or disguise. But the user has **not decided** the extent or nature of this system. Start with established camp interactions; avoid assuming a deep dialogue tree or social-simulation overhaul.

### Expedition preparation [Keep open]

Possible trade-offs between carried tools, ropes, ammunition, healing, light and capacity; temporary camps or caches. A larger interconnected overworld makes this more relevant. Avoid hunger/thirst chores and constant maintenance just to justify supplies.

### Environmental mysteries and mechanisms [Keep open]

History-linked devices such as old pumps, water controls, supports, sealed passages and working machinery could reward observation and material interaction. Prefer multiple systemic outcomes over one scripted solution; connect discoveries to prior builders and factions. Avoid elaborate bespoke puzzle production before core interactions are stable.

### Other related suggestions - not explicitly selected by user

- A readable run chronicle/postmortem to expose encounters, discoveries, consequences and causes of death.
- Optional late-game tactical convenience (automation or intention previews) is only brainstorming; preserve player agency and avoid omniscient forecasting.

### Suggested first small prototypes - not a commitment

1. A crate that either side can push to obstruct a doorway, with pathing and persistence.
2. Wolves that track an aging scent trail that rainfall or wind can disrupt.
3. A named goblin commander unique who may survive and recur within a run.

**Working principle:** add more meaningful *verbs* and kinds of knowledge, not only more numbers. Connect new systems to existing AI, history, weather, physical terrain and player agency. User has selected areas of interest, **not requested implementation**.
