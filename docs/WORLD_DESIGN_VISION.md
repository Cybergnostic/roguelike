# World Design Vision - Living Region, Interactive Places, Deep Dungeons

> Status: **Design direction / working proposal**, 2026-11-08. Not an implementation claim or a locked production plan.
>
> Read alongside [PROJECT.md](PROJECT.md), which remains the source of truth for current code, architecture, and measured results. This document describes **the game we want to make**, why it should work, and the next design questions. Update it as decisions are tested in play. A deeper code review and research report were requested separately; do not treat their pending findings as already incorporated here.

## 1. The goal

Build a **classical roguelike RPG** with the freedom and sense of discovery of *Morrowind* and *Skyrim*, expressed through a turn-based, grid-based, systemic game.

There are two complementary directions:

- **Horizontal exploration:** travel across a coherent, relatively compact surface region. Follow roads or leave them. See a fort on a hill, smoke in the distance, a cave entrance, a ruined watchtower, tracks, a river crossing. Decide where to go, what to avoid, whom to approach, and when to return.
- **Vertical exploration:** descend into interconnected caves, ruins, mines, crypts, and deeper dungeon strata. Depth brings different ecology, factions, resources, threats, and discoveries - not simply larger monster numbers.

**The world is a place, not a sequence of combat rooms.** A camp exists because its inhabitants live there. A patrol comes from somewhere and is going somewhere. A dungeon entrance connects to something below. Actions change the situation, including when the player leaves and returns.

This is **not** a plan to reproduce a giant 3D open world or simulate an entire civilization. The initial scope is **one connected region simulated deeply**, with a handful of meaningful surface locations and a substantial underground network. Expand the region only when its existing places support interesting decisions.

### The central promise

The player should be able to say:

> "I saw a goblin camp beside the road. I could have gone around it, paid them, sneaked through at night, or attacked. I killed two guards, escaped, and returned later. The camp remembered. Its remaining guards had withdrawn behind the palisade, and an undead raid had damaged the outer gate."

That story should arise from **terrain, rules, AI, faction goals, time, and memory**, not a bespoke quest script.

### The intended feel

- **Morrowind/Skyrim:** landmarks, curiosity, route choice, environmental storytelling, hidden places, factions, memorable returns, freedom to approach a location from different directions.
- **Brogue/Sil/NetHack:** discrete turns, legible danger, consistent interactions, tactical choices, meaningful consequences, and permadeath.
- **Caves of Qud/Dwarf Fortress:** history and factions explaining why places and objects exist, but within a deliberately limited simulation budget.
- **Cogmind/Unexplored:** clear presentation, interconnected spaces, and level design that rewards observation.

The player is an adventurer in a living place, not the automatic center of every creature's attention.

## 2. Non-negotiable design pillars

1. **Places before encounters.** Build camps, roads, ruins, lairs, and routes; then populate them with inhabitants and purposes. Do not scatter squads merely to fill empty space.
2. **Multiple viable approaches.** A location should usually support at least three of: avoid, observe, talk, trade/bribe, sneak, distract, lure enemies, fight, retreat, return later. Not every creature needs to be negotiable (undead may not be).
3. **Believable, limited knowledge.** Enemies react to what they see, hear, are told, or remember. They do not know the player's location through walls because a faction-wide path map knows it.
4. **Consequences that persist.** Casualties, damaged gates, stolen supplies, raised alarms, territorial control, faction relations, and discovered routes matter when revisiting.
5. **Readable cause and effect.** Show warnings, posture, sounds, tracks, gates closing, archers taking positions, pursuit breaking off. The player should understand *why* something happened.
6. **Depth through interacting simple rules.** Prefer a small set of reusable systems to dozens of special-case scripts or an unrestricted simulation.
7. **Deterministic, testable core.** Preserve the C# / .NET, GoRogue, SadConsole, data-driven, action/event architecture. Keep all rule decisions deterministic from seed and player actions. No LLM in the runtime rules path.

**Design test:** If removing all combat from a generated surface area leaves nothing worth exploring or doing, that area is not finished.

## 3. What the repository already provides

As documented and inspected on 2026-11-08, the game already has a substantial foundation. **Extend it rather than starting again.**

| Existing system | What it gives us | What the vision still needs |
|---|---|---|
| \`Squad\`, \`SquadDirector\`, \`SquadMemberBrain\` | Roles, pooled sight, morale, idle/alert/engage/flee, help runners | Orders tied to places, realistic search/pursuit, defend/withdraw/rally, noncombat reactions |
| \`Home\`, \`Leash\`, \`HomeDistance\` | Territorial boundary for lair defenders | Meaningful perimeter and patrol routes, pursuit break-off, home-defense positions, safe retreat |
| \`FactionPlanner\` | Musters and raids between goblins and undead | Purposeful jobs, population limits, place state, faction reactions and evidence |
| \`World.Lairs\`, \`Zone\`, \`LairBuilder\` | Named occupied places; tower pillars and cover | Fort/camp architecture, multiple entrances, gates, restricted areas, site ownership |
| \`data/factions.json\` | Faction relationships | Separate **political relation** from **current behavior toward the player** (trespass, warning, toll, attack) |
| \`MapGenerator\`, \`World.Descend\` | Procedural rooms, ten levels and stairs | Surface geography, linked locations, persistent revisitable floors and multiple vertical connections |
| \`World.Step\`, events, seeded RNG, headless sim/tests | Reproducible simulation and measurement | Perception/alert instrumentation, scenario tests, save/replay snapshots, offscreen abstraction |

**Important current constraints:**

- \`data/factions.json\` currently makes both goblins and undead hostile to the player. That is incompatible with meaningful peaceful passage until encounter policy is separated from blanket hostility.
- \`SquadDirector\` records last seen position, but engaged warriors and archers can use \`TowardEnemiesOf\`, which may point toward unseen enemies. Fix this before claiming stealth works.
- Lair defenders currently idle at home; \`LairBuilder\` supports a tower style, not a palisade camp. A named room is not yet a functioning fort.
- \`World.Descend\` rebuilds the next level. Persistent return travel will require a world of stored locations, not just a single current map.
- \`GameMap.SetTile\` maintains the walkability cache; dynamic doors/gates also need path-cache invalidation and correct visibility updates.
- The handbook already identifies jammed corridors, weak idle patrol purpose, generic depth content, and eventual need for spatial indexing. These are directly relevant to this vision.

This is a **design extension**, not a request to replace the engine or add "AI" for its own sake.

## 4. The world structure: horizontal and vertical

### 4.1 One region, many connected places

The long-term structure should be a **persistent graph of playable locations**, each with a map and links to other locations.

A practical mental model:

- **Surface:** woodland, road, river, bridge, goblin camp, abandoned farm, old fort, mine entrance, ruined shrine.
- **Shallow underground:** mine workings, goblin tunnels, cellars, natural caves, crypt.
- **Deep underground:** older ruins, necromantic chambers, collapsed passages, dangerous ecosystems.
- **Connections:** stairs, slopes, cave mouths, ladders, shafts, gates, hidden passages. Some connect horizontally; some connect vertically. Multiple entrances make route planning matter.

Conceptual coordinate: \`(siteId, levelId, x, y)\`. A \`siteId\` might be a surface region, camp interior, cave system, or fortress; a level may have a vertical depth. The exact representation is a future engineering decision.

**Do not immediately build a seamless enormous overworld.** A small, connected set of maps is sufficient if transitions are consistent, travel takes time, and locations remember changes.

### 4.2 Horizontal exploration must create choices

A surface map should have:

- Landmarks visible or hinted at before arrival: walls, towers, smoke, unusual terrain, light, sounds.
- Roads, trails, waterways, ridges, woods, clearings, and cover that affect travel and detection.
- Different approaches to the same destination: road gate, wooded flank, cliff path, tunnel, ruined breach.
- Reasons to deviate from the main route: supplies, rumors, a shortcut, a forge, a hidden entrance, signs of faction conflict.
- Travel with risk and information, **not** long stretches of empty key presses.

The Skyrim/Morrowind lesson is **player-directed curiosity**, not copying quest markers, giant distances, or generic random encounters. The player should spot a place, form a plan, and test it.

### 4.3 Vertical exploration must be more than stat scaling

Going down should change **what the player does**, not only what they fight:

- Upper levels: roads, politics, light, visibility, patrols, trade, fortifications.
- Mid levels: competing inhabitants, shortcuts, darkness, resource control, environmental hazards, alternate entrances.
- Deep levels: strange ecology, powerful inhabitants, older history, rare resources, limited escape routes.

Make levels **connected and revisitable**. A deep exit might lead back to the surface elsewhere. A cave can bypass a guarded fort. A shaft may be one-way until a rope or ladder is secured.

The existing ten-floor progression can remain as a combat/balance testbed while this new topology is prototyped. Do not attempt to redesign all ten floors at once.

## 5. Places with inhabitants: the primary gameplay system

A **Place** is more than a room name. It has:

- **Identity:** name, type (camp, tower, mine, shrine), history, controlling faction.
- **Footprint:** walls, approach area, interior, gates, doors, cover, observation points, escape routes.
- **Territory:** public/roadside edge, watched perimeter, restricted interior, secure heart.
- **Population:** named or persistent squads, guards, leader, reserves, workers, optional visitors.
- **Purpose:** protect food, mine ore, collect tolls, raid rivals, recover corpses, defend a relic.
- **Resources:** supplies, valuables, equipment, corpses, alarm mechanisms, gates.
- **State:** alert level, casualties, damage, supplies, ownership, remembered incidents, current orders.
- **Connections:** road, patrol route, supply route, dungeon entrance, rival location.

Not all of these need full simulation at first. A place can start with a handful of state variables and a few credible behaviors.

### 5.1 First example: goblin roadside camp

**Build this before a complex overworld.**

Geometry should support genuine alternatives:

- A road passes **near** the camp rather than through its only doorway.
- A palisade or wall gives defenders an advantage; at least one gate and one less obvious approach.
- A watch post can see the road; bushes/rocks/terrain offer an indirect route.
- Inside: leader/supplies, guard positions, rally point, exit toward a cave or deeper site.
- A safe bypass route exists, but costs distance/time or misses an opportunity.
- The camp has a **defensible interior**, not a maze of one-tile choke points that trap its own squads.

Inhabitants should have a reason to be where they are:

- Sentries hold positions and challenge intruders.
- Patrols walk a short meaningful route and return.
- A reserve responds to alarms, but not instantaneously.
- A leader may accept payment or decide to defend the inner camp.
- A foraging or raiding group may leave and return later.

A camp is successful when the player can understand its operation just by watching for a few turns.

### 5.2 Territorial behavior, not universal aggression

Distinguish these concepts:

1. **Faction relation:** political alignment between groups.
2. **Local access policy:** who may use the road, enter the perimeter, or approach the storehouse.
3. **Current incident:** trespassing, theft, attack, accepted toll, discovered disguise, alarm.
4. **Personal or group memory:** this is the adventurer who killed our guards.
5. **Immediate threat:** visible weapons, distance, injuries, numbers, escape direction.

Possible responses:

- Ignore a distant traveler.
- Watch and hold position.
- Warn the traveler to stay away.
- Demand a toll or offer passage.
- Escort or shadow someone suspicious.
- Investigate a sound or missing supply.
- Raise an alarm and block the gate.
- Pursue a fleeing attacker **for a limited reason and distance**.
- Break off pursuit and return to defend the camp.
- Retreat behind the wall when the squad loses people.
- Surrender or bargain when trapped (later; not required for the first slice).

**Important:** "Goblins are hostile" should not force every goblin to attack the player on sight at any distance. A territorial camp can be dangerous without making passage impossible.

### 5.3 Pursuit, casualties, and retreat

A chase should have a clear purpose: drive an intruder out, recover stolen goods, catch a dangerous attacker, or protect a threatened route.

Break-off conditions can include:

- The intruder is beyond the defended boundary and is no longer an immediate threat.
- Contact has been lost long enough; search confidence decays.
- Pursuers are wounded or have lost members.
- A gate or leader needs defending.
- A stronger rival faction attacks the camp.
- The squad has exceeded its permitted pursuit budget (distance/time), with a justified exception for exceptional threats.

**Avoid a hard, visible "leash wall" exploit.** Guards should not instantly freeze at an arbitrary tile. They should slow, warn, take cover, fall back, or continue a short distance if already committed. Make the behavior understandable and hard to farm.

A fleeing defender should try to reach **a safe rally point behind the wall**, not always run in a mathematically optimal direction away from the nearest hostile. If that route is blocked, choose cover, a secondary exit, or last-stand behavior. Keep gates usable by allies.

## 6. One camp, many stories

These are **required design scenarios**, not eight separately scripted quests.

| Player choice / event | Expected world response | What the player can read |
|---|---|---|
| Walk along the road outside the restricted zone | Guards watch but need not attack; passage remains possible | Sentry looks toward the road; no alarm |
| Approach the gate openly | Challenge, warning, toll or refusal based on policy and prior actions | Spoken warning, posture, visible guards |
| Pay a toll or bribe | Access for a limited purpose/time; resources transfer; record the agreement | Clear terms and confirmation; guards stand aside |
| Sneak through the wooded flank | Detection depends on actual sight/noise/cover; discovery raises suspicion | Footsteps, torchlight, search calls, alarm |
| Enter after being warned away | Escalation from trespass to pursuit or combat; enough time to withdraw | "Leave now" warning before force where appropriate |
| Kill two guards, then escape | Morale drops; survivors may fall back; pursuit ends for a reason; casualties persist | Horn, retreat order, defenders closing the gate |
| Lure undead toward the camp | Goblins fight or withdraw according to threat; the player can exploit the clash | Distinct faction sounds, movement, casualties and traces |
| Return later | Remaining population, damaged defenses, supplies, suspicion, and faction pressure reflect what happened | Empty watch post, repairs, graves/corpses, new sentries |

Further variations should emerge naturally: the gate was left open after a raid; a patrol is absent; the player steals supplies but is not identified; a corpse becomes a necromancer's servant; the camp loses its leader and its toll policy changes.

**Fairness rule:** Every escalation needs a discoverable cause. Never silently convert a peaceful interaction into a surprise full-faction aggro event.

## 7. AI design: goals, perception, memory, orders

Use **deterministic, layered decision-making** rather than a single giant behavior tree or an LLM.

### Strategic layer - factions and places

Extend \`FactionPlanner\` to make low-frequency decisions:

- Defend this camp, send patrol to this road, forage, repair, muster, raid, recover corpses, retreat after losses.
- Choose from available squads/resources and known threats.
- Generate **orders with destinations and reasons**, not arbitrary teleporting enemies.

A lightweight **PlaceDirector** can own camp alert level, defense assignments, rally positions, and alarm propagation. Do not make every individual actor run strategic planning.

### Squad layer - shared intent

Extend \`SquadDirector\` and \`Squad\` with a small set of orders and memories:

- **Orders:** GuardPost, PatrolRoute, Challenge, Investigate, Intercept, Pursue, DefendGate, WithdrawToRally, Raid, ReturnHome.
- **Shared knowledge:** last observed hostile position, observation turn, source (seen/heard/report), confidence, casualties, current objective.
- **State:** calm, suspicious, alarmed, engaged, searching, withdrawing, rallying. These can be modeled as a combination of *order* and *alert state* rather than an explosion of enum values.
- **Constraints:** home territory, safe rally point, time/distance pursuit budget, morale, leader availability.

An order describes **what and why**; a tactical state describes **how the squad is currently reacting**. Keep those separate to avoid fragile state transitions.

### Individual layer - roles and tactical actions

\`SquadMemberBrain\` keeps tactical execution:

- Sentry observes/challenges and holds a post.
- Warrior screens archers and protects an entrance.
- Archer seeks cover and clear shots, avoids reckless melee.
- Runner physically carries a warning or calls for help within real communication range.
- Wounded defender retreats to a rally point.
- Other roles can be added only when a place genuinely needs them.

### Perception must be authoritative

**No target location from a global enemy map unless the actor has a justified observation or order.**

Suggested perception record (concept, not current code):

\`\`\`csharp
public record Contact(
    int? ActorId,
    Point LastKnownPosition,
    int ObservedTurn,
    ContactSource Source,    // Sight, Sound, AllyReport, Tracks
    int Confidence);         // Decays without new evidence
\`\`\`

- Sight requires real line of sight, range, and later light/visibility.
- Sounds report an **approximate area**, not an exact identity or coordinate.
- Ally communication requires shouting range, line of sight, a runner, or a physical alarm.
- Last known position stays fixed until new information arrives.
- Searching covers nearby plausible cells and stops after a budget.
- A faction-level map may path toward a **known reported position**, but never secretly substitute the true current position.

Start with sight, shout/alarm, and last-known-position decay. Add light, hearing gradients, facing, disguise, and advanced stealth **only after** the basic rules are fair.

### Deterministic decision order

Use simple priorities first, with utility scores only when priorities conflict:

\`\`\`text
If directly threatened -> defend / flee / rally
Else if urgent alarm and assigned to respond -> investigate / reinforce
Else if trespasser seen -> warn / demand toll / block / pursue by policy
Else if on an order -> execute that order
Else -> hold post / patrol / rest
\`\`\`

The actual choice depends on squad role, morale, current orders, and knowledge. Fixed tie-breaking or seeded RNG preserves replayability.

**Do not build full GOAP for every NPC yet.** A small hierarchy with explicit orders and memory will be easier to debug and tune.

## 8. Interaction systems worth building

### First wave - high leverage

- **Doors, gates, and alarms:** open/close, blocked line of sight, clear sound/visual feedback, defensible positions.
- **Trespass and warning:** distinguish travel near a camp from entering a restricted zone.
- **Simple negotiation:** a deterministic "pay toll / offer bribe / refuse / leave" interaction with explicit prices and consequences.
- **Stealth foundation:** sightlines, cover, last-known position, investigation, lost-contact search. No elaborate stealth skill tree required.
- **Retreat and pursuit:** clear stop conditions, rally positions, real casualty response.
- **World evidence:** corpses, tracks, disturbed gate, alarm sound, stolen supply, changed guard count.

### Second wave - after the first camp is fun

- Light and noise propagation, decoys, traps, disguises, disguisable faction status.
- Destructible or repairable defenses, fire/smoke/water interactions.
- Reputation, rumors, informants, surrender, captives, faction bargains.
- Dynamic jobs such as foraging, corpse recovery, supply caravans, and repairs.
- Environmental crafting stations tied to place control.

Every system must work with at least **two other systems**. Otherwise it is probably isolated feature creep.

## 9. World time, persistence, and offscreen simulation

A living region needs continuity, but not full per-actor AI everywhere every turn.

### Three simulation levels

1. **Near the player:** full tactical turns, FOV, movement, attacks, doors, alarms, squad AI.
2. **In the same region but out of sight:** coarse squad travel and place events; simulate routes, time, population and likely outcomes, then materialize tactical actors when relevant.
3. **Far away / inactive locations:** scheduled strategic updates: supplies, patrol departure/return, repairs, raids, casualties, ownership changes.

Offscreen outcomes should use **the same world rules at a sensible abstraction level**, not invent arbitrary new armies. Track where people and resources come from. Make significant changes visible through traces and reports.

### State that must survive a visit

- Map edits (opened gate, destroyed barricade, dug passage).
- Killed/wounded inhabitants, squad membership, corpses and loot.
- Camp alert level, casualties, current orders, faction control and supply.
- Player reputation/incidents and whether the player was identified.
- Discovered areas, explored map, entrances and shortcuts.
- Scheduled events and deterministic RNG state.

The current \`World.Descend\` regenerates a level. That is fine for the present dungeon prototype but **not sufficient for this vision**. Eventually introduce a persistent \`RegionWorld\` / location store with explicit transitions, and snapshot/load support. Avoid rewriting everything until the camp slice has proved the gameplay.

### Performance and correctness

- Simulate expensive perception/pathfinding only for active tactical actors.
- Schedule strategic updates at intervals or when meaningful events occur, not by scanning all locations every turn.
- Keep per-location spatial indexes once actor counts justify them; the current \`ActorAt\` linear scan is a known future bottleneck.
- Invalidate cached paths when doors or terrain change.
- Make ordering deterministic. Save seed, player action log, world clock, event queue and relevant RNG state for replay/debugging.
- Measure both **turns per second** and **meaningful player decisions per minute**. More simulation is not automatically more fun.

## 10. World generation: geography and history with gameplay consequences

The handbook's "history first, then map" direction is worth keeping, but start with **authored procedural grammars**, not a universal history simulator.

Proposed order:

1. Generate a **small regional premise**: old mine, ruined fort, goblin encampment, necromancer in the deep.
2. Lay out major geography and a graph of places: roads, ridges, water, entrances, links between depths.
3. Assign territory, faction needs, rivalries and important resources.
4. Generate each place's architecture from its purpose and terrain.
5. Place inhabitants in jobs and patrol routes, then place loot/traces consistent with the site's history.
6. Validate connectivity, alternate routes, sightlines, cover, escape options and difficulty.
7. Run a short seeded prehistory only where it creates meaningful consequences.

**History must change gameplay:** an old collapsed tunnel creates a shortcut; a goblin toll post controls a road; a necromancer needs corpses; a dwarven forge is valuable because factions can use it.

For the first camp, hand-author a **layout grammar** with variable gate position, watchtower, cover, paths, and reserve room. Procedural variety should preserve the same meaningful tactical choices. Do not randomize away the bypass or trap everyone behind one doorway.

## 11. Progression and reasons not to kill everything

The game already rewards first sightings and discovering named zones, alongside kills. Build on that.

Potential rewards:

- Discovering an entrance, shortcut, secret, landmark or piece of history.
- Getting through a dangerous place without a fight.
- Trading for access or information.
- Stealing a needed item and escaping.
- Solving a faction problem through a third party.
- Recovering a resource or taking control of a station.
- Returning with knowledge that makes a previously dangerous route manageable.

**Avoid rewarding peaceful and violent solutions twice for the same objective.** Prefer objective- or discovery-based XP with bounded bonuses, so the optimal strategy is not "pay the toll, then murder every guard for extra XP."

Equipment, materials, artifacts and skill growth should support different approaches (mobility, perception, negotiation, stealth, defense, magic), not merely damage scaling.

Keep survival and combat relevant: fighting is sometimes the right answer, but **not the only profitable answer**.

## 12. The first vertical slice - prove the vision

Build a **small, repeatable scenario** before scaling up:

**Map:** one outdoor area with a road, cover, a goblin palisade camp, and a cave entrance leading to one underground map.

**Player goal:** reach a useful object or destination beyond/under the camp and return, with no requirement to exterminate the inhabitants.

**Actors:** one guard pair, one small patrol, one reserve/leader group; optional undead squad arriving from a known route. Use existing goblin roles and combat.

**Systems in scope:**

- Camp footprint, restricted area, gate, cover, rally point.
- Sentry/patrol orders and a clear warning.
- Peaceful bypass and one deterministic toll/bribe interaction.
- Sight-based detection and last-known-position search.
- Pursuit with break-off; retreat behind the gate after losses.
- Casualty persistence while the player leaves and returns.
- One surface-to-underground connection and return path.
- Debug overlay for orders, detection, last-known target and pursuit boundary.

**Acceptance criteria (tests + human play):**

1. A seeded player can **complete the objective without killing** a goblin.
2. A seeded player can **pay**, **sneak**, or **fight/retreat**; each produces a different understandable outcome.
3. A guard does not attack from beyond justified detection; a warning precedes escalation when policy allows.
4. A squad that loses sight does **not** path to the player's true hidden location.
5. After the player kills two guards and escapes, surviving defenders can withdraw, the chase ends for a clear reason, and the dead guards stay dead on return.
6. The player can enter and exit the underground area without regenerating the surface camp.
7. A third-party undead attack can alter the camp without forcing the player into the fight.
8. The same seed and action sequence produces the same state/events; no blocked-gate or corridor deadlocks.
9. At least three playtesters (or repeated blinded playthroughs) can identify **why** a guard chased, warned, or stopped.

**Measure:** forced-combat rate, noncombat completion rate, chase duration, unfair detection incidents, blocked actors, player deaths by scenario, and CPU time per turn. Do not tune only for bot win rate.

## 13. Implementation direction (proposed, not committed)

The following are likely extension points. Exact types and interfaces should be chosen during planning, after reviewing tests and call sites.

| Area | Existing home | Likely addition |
|---|---|---|
| Places and access | \`GameMap\`, \`Zone\`, \`World.Lairs\` | Place/territory state, perimeter vs restricted areas, rally/guard points |
| Architecture | \`LairBuilder\`, \`MapGenerator\` | Palisade-camp layout grammar, gates/doors, approach routes, generation validators |
| Encounter policy | \`FactionRelations\`, \`World.AreHostile\` | Access/trespass policy, incident memory, negotiation and aggression decisions |
| Perception | \`World.CanSee\`, \`SquadDirector\` | Contact reports, search confidence, alarm propagation, known-position pathing |
| Squad behavior | \`Squad\`, \`SquadMemberBrain\` | Guard/patrol/pursue/withdraw orders and safe rally behavior |
| Strategy | \`FactionPlanner\` | PlaceDirector, faction goals, bounded population/jobs, event scheduling |
| World topology | \`World.Descend\`, \`GameMap\` | Persistent location graph, travel transitions, revisits and saves |
| Data and tests | \`ContentDatabase\`, \`data/*.json\`, Core tests, \`Roguelike.Sim\` | Validated place templates, scenario tests, new simulation metrics |

Example **proposed** content shape (not valid with the current loader):

\`\`\`json
{
  "id": "roadside_goblin_camp",
  "kind": "palisade_camp",
  "faction": "goblins",
  "territory": {
    "watchRadius": 12,
    "restricted": ["inside_palisade", "storehouse"],
    "roadPolicy": "allow_passage",
    "gatePolicy": "warn_then_toll"
  },
  "layout": {
    "gateCount": 1,
    "watchPosts": 2,
    "rallyPoint": "inner_courtyard",
    "alternateApproach": "wooded_flank"
  },
  "groups": [
    { "squad": "camp_sentries", "order": "guard_gate" },
    { "squad": "goblin_scouts", "order": "patrol_route" },
    { "squad": "camp_reserve", "order": "defend_home" }
  ],
  "pursuit": {
    "maxTurnsWithoutContact": 6,
    "returnWhenOutsideTerritory": true,
    "withdrawBelowMorale": 30
  }
}
\`\`\`

Prefer storing **rules and templates in data**, while the engine owns their deterministic interpretation. Do not over-generalize this schema before one real camp works.

### Suggested implementation sequence for the next planning session

1. **Fix AI knowledge and visibility** so hidden targets are genuinely hidden; add diagnostic traces.
2. **Build a defensible camp** with a road bypass, gate, guard posts and rally point.
3. **Give squads place-based orders** and realistic pursuit/withdrawal.
4. **Add warnings and one noncombat interaction**, then test multiple solutions to the same goal.
5. **Connect one surface area to one underground area** with revisits and persistent state.
6. **Add offscreen events and more places** only after the first location is fun.

This order is deliberate: **make one camp convincing before building a large map full of unconvincing camps**. Effort estimates, file-by-file changes, and milestone ownership belong in the next planning pass.

## 14. Debugging and validation are design tools

Add inspectable state to existing F2/F3 overlays and headless reports:

- Place owner, territory rings, guard posts, gates, rally points.
- Squad order, objective, morale, pursuit budget, retreat destination.
- Contact source, last-known position, last-seen turn, confidence.
- Alert propagation path (who saw whom, who shouted, who heard).
- Strategic event timeline: patrol departed, raid arrived, gate repaired, camp lost two guards.
- Scenario replay with seed and action log; map and world-state diff on failure.

Test invariants:

- No faction-wide omniscient target tracking.
- Every place has reachable entrances and at least one valid escape route.
- Gates/doors update pathing and sight correctly.
- Actors do not repeatedly swap, block, or wait forever in corridors.
- Offscreen casualties do not exceed available population; events have plausible routes/times.
- Travel out and back preserves state.
- Deterministic replay survives save/load once implemented.

Human playtests should answer: **Did the player notice the choice? Did they understand the response? Was retreat actually possible? Did returning feel different?**

## 15. Scope control - what not to build yet

- **No giant seamless overworld.** Start with a small surface region and one deep connection.
- **No fully simulated economy, agriculture, or daily routine for every NPC.** Use bounded jobs, supplies, and scheduled events.
- **No runtime LLM deciding dialogue truth, tactics, prices, or combat rules.** A deterministic interaction menu is better for the first slice. AI tools can help with development, test generation, and optional flavor later.
- **No elaborate reputation network before basic trespass works.** Start with a few remembered incidents and explicit faction policy.
- **No unrestricted destructible terrain or physical simulation.** Begin with gates, doors, cover and a few authored interactions.
- **No quest-marker-driven checklist replacing exploration.** Use landmarks, rumors, map memory and environmental evidence.
- **No permanent meta-progression to compensate for weak runs.** Make knowledge, routes, and in-run choices the primary progression.
- **No ten-floor redesign before one camp has multiple viable solutions.**
- **No feature merely because a modern game has it.** It must strengthen exploration, tactical decisions, or persistent consequences.

## 16. Open decisions for our next planning session

These need explicit choices and prototypes, not assumptions:

1. **Surface map scale:** *Decided for now (2026-11-09): a few connected maps; to be rethought later.* Still open: how many turns should travel take?
2. **Vertical topology:** how many revisitable layers and alternate entrances are needed for the first meaningful dungeon?
3. **Permadeath and persistence:** *Decided for now (2026-11-09): permadeath; the world resets on death and persists fully during a run; no campaign continuity. A seed reproduces the same layout for anyone who wants to retry it.*
4. **Player starting status:** *Decided (2026-11-09): neutral traveler.*
5. **Negotiation model:** fixed choices and prices first; which stats (e.g. Charisma) influence terms without making outcomes opaque?
6. **Stealth readability:** what can the player reliably infer about enemy sight, suspicion, noise and search?
7. **Place ownership:** can the player capture a camp, merely clear it, or change who occupies it? What is the minimum fun consequence?
8. **Exploration rewards:** how much XP/progression comes from discoveries and objectives versus kills?
9. **Technical boundary:** when to replace the single active \`World\` / regenerated \`Descend\` model with persistent location state and a world clock?
10. **First playable target:** define one map, one camp, one underground location, three approaches, and measurable acceptance tests before expanding.

---

## Working rule

**Build a place. Give it inhabitants with reasons to act. Give the player several ways to deal with it. Let the consequences persist. Then connect that place to a wider world and a deeper dungeon.**

If that loop is enjoyable with one camp, it can scale to forts, settlements, caves, mines, ruins, and eventually a memorable region.
