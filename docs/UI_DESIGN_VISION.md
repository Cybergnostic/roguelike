# UI Design Vision - Modern Classical Roguelike

> **Status:** Research-informed design proposal, 2026-11-08. This document is for discussion and planning, not an assertion that these features exist.
>
> **Related:** [Project handbook](PROJECT.md) (current implementation and technical conventions) · [World design vision](WORLD_DESIGN_VISION.md) (horizontal regional exploration, vertical dungeons, camps, factions, and persistence).
>
> **Research influences:** Cogmind, Caves of Qud, Brogue/Brogue CE. We want to learn from their interfaces, not copy their graphics. Primary references are linked in §16.

## 1. Executive direction

**Recommendation: Brogue's clarity + Cogmind's information architecture + Caves of Qud's modern interaction model.**

Build an **Enhanced ASCII** game with a readable, atmospheric map, excellent tooltips and inspection, modern panels, responsive layouts, and restrained animation. Keep the classical roguelike's speed and dense mechanical information. An optional hybrid-tile mode can come later, using the same UI logic and game rules.

The guiding rule:

> **The map is the game. Everything else helps the player understand the map.**

The distinctive opportunity is not adding more visual effects. It is making the **living world legible**. The player should understand, from what they could plausibly see or hear:

- Why a goblin sentry is watching them.
- Whether they have been warned away from a camp.
- Where an enemy was last seen, without the enemy magically tracking the player through walls.
- Whether pursuers are closing in, searching, regrouping, or withdrawing.
- That a raid happened while the player was underground, and what it changed.
- Why a location feels different on a return visit.

The main UI must not resemble a spreadsheet of every hidden simulation variable. Use **progressive disclosure**: immediate information on the map and tactical HUD; deeper context on inspection; past events and regional knowledge in the journal.

## 2. The games we should learn from

| Reference | What to borrow | What not to copy |
|---|---|---|
| **Brogue** | Map-first composition, readable glyphs, restrained panels, information conveyed by the environment, clear interaction feedback | Its exact visual palette or the assumption that every mechanic must fit in minimal UI |
| **Cogmind** | Dense but organized information, contextual help, excellent keyboard/mouse workflows, visual effects on an ASCII foundation, scalable panel strategies | Interface density for its own sake; effects that obscure a fantasy scene |
| **Caves of Qud** | Atmospheric tile/glyph world, modern equipment/interaction screens, journal-like exploration information, scalable and configurable UI, flexible input | Heavy thematic imitation; a large tile production pipeline before systems are stable |

A useful comparison is **functional rather than aesthetic**. Brogue teaches us which information belongs directly in the dungeon. Cogmind teaches us how to make complex games efficient to operate. Qud demonstrates how a text-and-tile world can feel rich without becoming a conventional 3D RPG.

Other possible influences: modern tactical games for attack previews and hazard clarity; Unexplored for communicating interconnected environments; classic isometric RPGs for inspection and discovered-place journal organization.

**Original visual identity:** grounded dark-fantasy archaeology and wilderness. Worn stone, earth, torchlight, shadow, iron, wood, moss, bones and faint magical accents. Use the setting and simulated history to establish mood, not a blanket sepia filter that destroys tactical readability.

## 3. Three presentation options

### A. Classic ASCII (baseline and fallback)

- Crisp monospaced glyph map, modest color coding, minimal borders.
- Status strip and small event log; optional threat sidebar.
- Keyboard-first, fully playable and fast.
- Best for automated tests, low-resolution screens, accessibility fallback, development/debugging.

**Tradeoff:** robust and economical, but may under-communicate the atmosphere and spatial identity of larger outdoor locations.

### B. Enhanced ASCII (recommended default)

- Still fundamentally cell-based: every creature, tile, object and effect has a grid position.
- Carefully drawn glyphs, richer contrast, clean typography, subtle background color, gradients/lighting, sparse particles, short animations, and layered overlays.
- Map remains dominant; compact inspectable context panels, expandable log, optional sidebar.
- The user can choose reduced motion, larger font, high contrast, and a minimalist classic profile.

**Tradeoff:** more renderer work, but it fits the present SadConsole-based architecture and stays legible during complicated fights.

### C. Hybrid tiles (future optional visual mode)

- Same game state, camera, navigation and input; actors/terrain can use small sprites or glyphs.
- Keep text panels and tactical overlays independent from map art.
- Avoid demanding hand-made tiles for every procedural item affix and world variation.

**Tradeoff:** asset cost, animation complexity, and a higher risk that sprites conceal precise map cells or make unfamiliar monsters harder to identify.

**Decision:** design the UI for **A and B now** with visual assets represented semantically; **C later**, if playtests justify it. Do not maintain two different interaction systems.

## 4. Visual language specification (proposed defaults)

These are **design starting points**, not engine constraints. Validate on real screenshots at 1366×768, 1920×1080, and a higher-DPI display.

| Element | Proposal |
|---|---|
| Map priority | Give the playable map roughly 70–80% of the available width in the normal desktop view; collapse panels before reducing readability |
| Grid | Discrete square or near-square tactical cells; verify glyph alignment and creature recognition in both ASCII and enhanced profiles |
| World font | Distinct, highly legible monospaced face with complete box-drawing and gameplay glyph coverage; pixel-perfect scaling when appropriate |
| UI font | Same family or restrained companion font; never let stylized fantasy typography impair data reading |
| Hierarchy | Place label > threat/interaction cue > body text > secondary metadata; use spacing and alignment before decorative frames |
| UI chrome | Dark neutral surfaces, thin dividers, limited ornament, consistent padding |
| Colors | Semantic roles (safe/neutral, suspicious, hostile, allied, selected, invalid, damage, healing, rarity, memory, unknown) mapped through a theme |
| Visibility | Strong contrast for active visible cells; distinctly desaturated explored-but-not-currently-visible terrain; unknown cells remain unknown |
| Motion | Brief, optional and interruptible. Gameplay turn resolution is deterministic; visuals may interpolate without affecting simulation |
| Sound | Short distinct audio signatures for alarm, warning, detection, pursuit, enemy withdrawal, off-screen fighting; always paired with text/visual feedback |
| Accessibility | Do not rely on hue, volume, sound, or animation alone for gameplay-critical information |

### Suggested palette roles

Use a restrained palette with semantic slots, not arbitrary colors wired into systems:

- **Canvas / unexplored:** near-black with a slight cool cast.
- **Walls / rock:** cool charcoal and desaturated slate.
- **Soil / wood / surface paths:** muted earth tones; enough contrast to distinguish traversable ground.
- **Vegetation / water:** low-saturation greens and blue-greens.
- **Visible characters:** brighter than surrounding terrain; faction and threat identity remain readable.
- **Player / selection:** a single clear, unmistakable accent.
- **Warning / suspicion:** warm amber.
- **Immediate danger / hostile:** strong vermilion/red with a separate shape or label.
- **Allied / friendly:** cool light teal or another distinct non-hostile semantic style.
- **Magic / rare phenomena:** limited accent colors reserved for exceptional effects.
- **Memory / uncertain rumor:** dimmer text and explicit uncertainty wording.

Do **not** use item-rarity color to override urgent combat meaning. A red rare item and a red imminent danger indicator cannot be allowed to compete at the same level of emphasis. Keep high-frequency effects subdued.

### Glyph rules

- A glyph's **shape carries primary meaning**, color is secondary.
- Actors are always visually distinct from terrain; player identity is stable.
- Different types of walls, gates, stairs, water and bushes must be recognizable before decorative variation.
- A gate should visibly distinguish open, closed, barred, or damaged states.
- Do not use subtle background effects to encode impassable terrain.
- The same symbol must not mean "safe passage" in one screen and "hostile target" in another.
- Inspection provides explicit names for unfamiliar glyphs.

### Effects budget

- One obvious focal effect at a time during routine movement/combat.
- A visible hit can flash, displace light briefly, or emit 2–3 particles; it should not obscure adjacent actors for multiple turns.
- Alarms should have a concise visual cue **and** an event message.
- Faint dust, torch flicker, rain or embers can create atmosphere if they never hide the tactical map.
- No camera shake by default for common attacks; if offered, keep it subtle and toggleable.
- Prefer a short animation of **what happened** over ambient motion that distracts from decisions.

Animation durations (proposed tuning targets, not promises):

| Event | Starting range |
|---|---:|
| Player/NPC move interpolation | 60–120 ms |
| Hit, miss, blocking impact | 80–160 ms |
| Sentry reaction / warning cue | 100–220 ms |
| Major spell/alarm | 180–350 ms |
| Screen/panel transition | 100–180 ms |
| Reduced-motion mode | Instant or near-instant |

Nothing about the simulation should depend on frame rate or animation completion. Fast input must be able to skip nonessential animations safely.

## 5. Screen architecture

The primary desktop game view should have four clear regions:

1. **Map:** the dominant canvas.
2. **Awareness:** threats, NPC intent, local situation; collapsible.
3. **Status:** HP, critical resources, depth, location, relevant conditions.
4. **Log and contextual commands:** recent events, expandable history, commands relevant here.

A working wireframe:

```text
┌────────────────────────────── WORLD / MAP ─────────────────────────────┬──── AWARENESS ──────┐
│  Red Valley · Evening                                                 │ Goblin sentry      │
│                                                                      │ 18/18 · Watching   │
│       trees / rocks       ROAD                                        │ Goblin archer      │
│         ♣   ♣        .. .. ..                                         │ 10/10 · Guarding   │
│                @  ..     ..       ╔══════ GOBLIN CAMP ═════╗         │                    │
│       brush  ♣      ..           ║  g    g      ▲ tower   ║         │ Local situation    │
│                   ..  ..         ║    supplies             ║         │ Camp: suspicious   │
│                         ..       ╚═══════+════════════════╝         │ Gate: guarded      │
│                                                                      │                    │
│  (map occupies most of the screen; no permanent giant HUD here)      │ [x] inspect        │
├──────────────────────────────────────────────────────────────────────┴────────────────────┤
│ HP 26/30    DEF 4    Bow 7     Red Valley > Roadside Camp    Dusk      Turn 847              │
├───────────────────────────────────────────────────────────────────────────────────────────┤
│ You hear a horn from inside the camp.                                                        │
│ The goblin sentry falls back toward the gate.                                                 │
│ [x] inspect   [e] interact   [f] fire   [i] inventory   [j] journal   [?] commands           │
└───────────────────────────────────────────────────────────────────────────────────────────┘
```

*The map is illustrative. Final glyphs, layout and keybindings are not chosen.*

### Desktop sizing

- **1920×1080:** show map, awareness, status, log simultaneously; allow larger tile/glyph scaling rather than filling spare space with extra panels.
- **1366×768:** preserve a practical map viewport, collapse awareness into an overlay or narrow mode, keep short status and 2–3 important event lines.
- **Small/high-DPI window:** prioritize map and status; inspection and log become temporary panels. Do not shrink text to keep every module visible.

**Avoid hardcoding an 80×24 viewport as the finished UI layout.** The current fixed map size remains useful for tests, but viewport dimensions, UI panel dimensions, and simulation map dimensions should be separate concepts.

### Responsiveness

Provide profiles such as **Classic, Enhanced, Compact** and adjustable scale. The user should be able to disable the awareness panel without losing access to its information. A hidden panel is an input away, not removed from the experience.

## 6. Surface exploration screen

Surface exploration needs **recognition before proximity**:

- Show the road, a fork, ridge, bridge, smoke, palisade or watch post as soon as legitimately visible.
- Differentiate terrain by affordance: bushes hide, walls block, roads speed travel, high ground has a view, gates constrain entrances (only if those rules exist).
- Let the player recognize a place and decide whether to approach it.
- Display named location on arrival; maintain an optional region map for already discovered places.
- Put route information and landmarks on the map, not floating quest arrows over every point of interest.

Potential context strip:

```text
RED VALLEY · Old north road
Visible: smoke beyond trees; palisade gate to the east
Known route: southern footpath (unexplored)
```

**Information policy:** "Visible" requires actual sight. "Known" can be retained from past exploration or reliable reports. "Rumored" is marked as uncertain. No hidden camp occupancy or exact off-screen enemy counts in the ordinary HUD.

World-map idea: a player-authored **knowledge map** with discovered roads and sites, optional annotated rumors, and links down into known underground systems. Do not turn it into a GPS omniscience display.

## 7. Underground tactical screen

In caves and forts, optimize the same shell for tactical reasoning:

- Show field of view and explored-but-currently-unknown areas distinctly.
- Keep target, cover, blockers, dangerous terrain and potential escape corridors readable.
- List visible threats with short **observable** intents: "aiming", "moving to cover", "retreating", "looking around". Avoid displaying exact private AI values.
- Prefer compact status indicators over a permanent large combat control deck.
- Make doors, sightlines and chokepoints obvious.

Target preview (future spell/area abilities may use this layout):

```text
TARGETING — Burning flask
Target: Goblin shaman
Estimated hit: 82%
Damage: 5–9
Blast area: 3 hostiles, 1 neutral in range
Line: clear        Cover: low

[Tab] next target  [arrows] aim  [Enter] throw  [Esc] cancel
```

Only display estimates if the simulation can compute them from information the character plausibly has. Show **unknown**, **approximate**, or ranges rather than invented precision.

Add an optional path preview for deliberate movement and an explicit warning when pathing through danger. The world does not advance while aiming/inspecting unless the player confirms an action that consumes a turn.

## 8. Inspect, examine and contextual interaction

**Examine mode is the first high-value UI feature to implement.**

A keyboard-controlled cursor and mouse hover should use the same inspection model. Inspection should answer:

- What is at this cell?
- What is it doing right now?
- What can I infer about it?
- What can I do from here?
- How certain is my information?

Example NPC inspect panel:

```text
GOBLIN SENTRY
Faction: Red Tooth
Position: eastern gate
Visible posture: wary; watching the road
Known: raised a warning when you approached before
Current intent: guarding the entrance
Options nearby: speak · offer toll · step back
```

Example place inspect panel:

```text
EASTERN GATE — RED TOOTH CAMP
Wooden palisade · closed gate
Two visible sentries
Public road lies outside their marked territory
Known approach: wooded flank to the north
```

An interaction menu at a guarded gate might offer **Talk, Ask for passage, Pay toll, Offer bribe, Threaten, Leave**. These are proposed *fixed game intents*, resolved by deterministic rules. They do not require free-form generated dialogue.

**Inspection must never reveal private NPC memory that the player has not learned.** Add a separate debug inspector for the real underlying AI state.

### Intent presentation

- Map: visible motion, orientation/posture where supported, shout/alarm effect, rally movement.
- Sidebar: brief action/intent tied to visible evidence.
- Event log: consequential transitions ("A horn sounds inside the camp"; "The pursuers turn back").
- Inspection: more context, knowledge/uncertainty, what can be interacted with.
- Journal: only recorded history, discovered places, rumors and notable incidents.

Use explicit words when ambiguity could kill the player.

## 9. A UI for the living world (our main differentiator)

The interface should communicate a **layered causal model**, never the raw AI blackboard.

### Awareness and escalation

One possible visible progression:

1. **Unaware:** guard patrols normally. No player-facing alert meter.
2. **Watching:** guard faces/studies the player; inspection says "wary".
3. **Warning:** distinct shout and message; the player knows the boundary/condition.
4. **Alarm:** horn, running sentries, gate movement, reinforcements become visible when observed.
5. **Search:** enemies move to last-known area, communicate, and stop if no further evidence.
6. **Pursuit:** pursuers visibly commit beyond their post for a limited purpose.
7. **Withdrawal:** guard retreats, closes gate, forms defensive line, or returns to post.
8. **Return:** changes persist and are discoverable through empty posts, damage, replacements, rumors and behavior.

### Observed versus known versus inferred

**Observed:** "The sentry raises a horn."

**Known from earlier experience:** "You were warned away from this gate."

**Inferred:** "The camp appears tense."

**Unknown:** no exact alert score and no hidden list of reinforcements.

The research report suggested a camp panel with exact alert/reputation bars; treat those as **debug values or optional coarse indicators**, not automatically accurate player-facing facts. Display "calm / wary / alarmed" or estimates when realistic.

### Last-known position and pursuit

This interface must preserve the meaning of stealth:

- Show *where the player believes the enemy last saw them*, only if it was observable (e.g., a guard calls out the player's location).
- Use a search cue for visible NPCs; do not render hidden patrol destinations.
- When a pursuer stops, the log can describe the observable action: "The goblins break off and return to the palisade."
- If the player was never aware of a guard or alarm, the UI should not reveal it in advance.

Debug overlays may reveal exact last-known positions, confidence, pursuit leash, NPC knowledge, and utility scores. Ordinary presentation must not.

### The information pipeline

```text
World simulation (authoritative)
    ├─ Actor / faction / place state
    └─ Structured GameEvents
             ↓
Player knowledge & perception filtering
             ↓
Presentation snapshots / UI models
             ├─ World map renderer
             ├─ Awareness / inspect
             ├─ Event log
             └─ Journal / region map

Debug inspector -> raw state (explicitly non-player-facing)
```

Do not store free-form sentences as authoritative NPC memories. Store structured facts and sources; let the UI turn them into language. The present `EventText` system is a good starting pattern.

## 10. Inventory, equipment, character and loot

The current letter-driven inventory works for the small prototype. With generated affixes, classes, crafting materials, provenance and more equipment slots, it needs a better information model.

**Recommended default:** two-pane inventory with category filters, search, active equipment, item detail/comparison and one-key actions.

```text
┌──────────────────── INVENTORY ────────────────────┬──────────── EQUIPMENT / DETAILS ────────────┐
│ Weapons                                          │ Equipped: Grim Fang (+1 long sword)        │
│ a  Keen iron sword                              │ Bow: Hunting bow                           │
│ b  Goblin spear                                 │ Armor: Leather hauberk                    │
│                                                  │                                            │
│ Consumables                                      │ Selected: Keen iron sword                 │
│ c  Healing potion ×3                            │ Damage:    5 -> 7  (+2)                    │
│ d  Unidentified vial                            │ Accuracy: +1                               │
│                                                  │ Origin:   Red Tooth Warren                │
│ Materials                                        │                                            │
│ e  Dwarven iron ×2                              │ [Enter] equip  [d] drop  [x] inspect      │
│ [/] search  [f] filter                           │                                            │
└──────────────────────────────────────────────────┴────────────────────────────────────────────┘
```

Important principles:

- Item rarity, enchantment and special properties remain readable separately.
- Comparison should show **net changes**, including tradeoffs, not only green "+X".
- A discovered item's origin/history should be inspectable but not clutter the item list.
- Menu opening, searching and comparison should not advance a game turn.
- Gear actions consume turns according to game rules, not UI convenience.
- Do not require drag-and-drop; add it as optional later.
- Keep fast letter/key actions for experienced players.

Character view: readable tabs or sections for attributes, derived stats, talents/skill trees, faction standing and character history. Player should understand where a derived number came from; do not hide everything in decorative cards.

## 11. Log, journal, map and navigation

### Event log

Keep the small live log, but back it with a full **session/event history**:

- Combat, danger, exploration, faction, dialogue, items, off-screen sounds.
- Expandable/filterable log; timestamps/turn numbers in detailed mode.
- Repeated low-value events may collapse; alarms/deaths/major discoveries do not.
- Clicking or selecting a message can highlight the known location involved, if any.

The existing structured `GameEvent` architecture makes this cleaner than scraping printed strings.

### Journal and rumor screen

A journal should let the player navigate by:

- **Places:** discovered sites, entrances, landmarks, visited and rumored routes.
- **People and factions:** what the player learned, not full omniscient simulation data.
- **Recent events:** sightings, raids, stolen objects, warnings, changes at camps.
- **History:** generated background that has actually become known.

Example:

```text
JOURNAL > RED VALLEY > RED TOOTH CAMP

Visited: yes               Last seen: day 3
Known entrances: road gate
Rumor: a tunnel runs beneath the palisade
Observed: two sentries were killed near the gate
Recent: horns heard at dusk; source uncertain
```

Do not build a large quest tracker that dictates the player's curiosity. The journal is an **organized memory**, not a list of mandatory objectives.

### Auto-explore and travel

Implement interruption rules before automation:

- Stop on a visible new threat or hostile action.
- Stop on a warning, alarm, fresh important sound, or obvious new landmark.
- Stop if the player is injured, a new hazard is noticed, a door blocks travel, or a relevant item appears.
- Clearly distinguish "travel to a remembered location" from unknown-world pathfinding.

An optional region map becomes useful when horizontal exploration spans multiple connected tactical areas. Avoid an always-on minimap that competes with the primary map.

## 12. Input design

**The game should be keyboard-efficient and mouse-capable without duplicating rules.**

Introduce a UI command vocabulary above simulation `GameAction`:

```csharp
public enum UiCommand
{
    MoveN, MoveNE, MoveE, MoveSE, MoveS, MoveSW, MoveW, MoveNW,
    Wait, Examine, Interact, Inventory, Equipment,
    Fire, Confirm, Cancel, NextTarget, PreviousTarget,
    AutoExplore, Travel, Character, Journal, Factions, Help
}
```

Suggested flow:

```text
Keyboard / Mouse / (future) Controller
                ↓
          InputRouter
                ↓
       UiCommand + UiContext
           ├── UI-only command -> UiState
           └── game command -> GameAction -> Core.World.Step
```

- Maintain traditional movement keys; allow remapping.
- Tooltips, inspect, filters, cursor moves, panel switching and opening the journal are free UI actions.
- Actions which change the world produce normal deterministic `GameAction` objects.
- Show a small hint strip for **context-relevant** commands, with a searchable help/reference screen for the rest.
- Ensure mouse hover does not silently consume turns or reveal hidden entities.
- Use consistent confirm/cancel semantics in every modal interaction.

An `IInputBinding` abstraction is useful; do not add a heavyweight framework unless the current UI proves it necessary.

## 13. Presentation architecture and feasible implementation

**Do not rewrite the simulation for visual polish.** Continue separating Core from SadConsole.

Current relevant files:

- [`GameScreen.cs`](../src/Roguelike.App/GameScreen.cs): map, sidebar/status/log layout and input.
- [`GameScreen.Items.cs`](../src/Roguelike.App/GameScreen.Items.cs): item modal UI and targeting.
- [`GameScreen.Character.cs`](../src/Roguelike.App/GameScreen.Character.cs): sheet and progression UI.
- [`GameScreen.Debug.cs`](../src/Roguelike.App/GameScreen.Debug.cs): debugging overlays and spectator features.
- [`Theme.cs`](../src/Roguelike.App/Theme.cs): glyph/color mapping from `data/looks.json`.
- [`EventText.cs`](../src/Roguelike.App/EventText.cs): structured events to player-facing prose.

A practical incremental split:

| New responsibility | Proposed component |
|---|---|
| Input interpretation and remapping | `InputRouter`, `UiCommand`, `UiContext` |
| Modal focus, selection, cursor, tooltip, panels | `UiState` |
| Viewport size, layout profiles and scaling | `LayoutManager` |
| Current player-visible information | `PresentationSnapshot` / semantic panel models |
| Map drawing and layered effects | `WorldMapRenderer` |
| Threats/inspection/inventory/log | Dedicated panel renderers |
| Theme semantic colors, glyph/sprite lookup | Evolve `Theme`, optionally an `IRenderTheme` |
| Debug visibility of real AI state | Separate `DebugInspector` path |

Use a lightweight MVVM-like split **without** a framework dependency. Small classes that build immutable or read-only presentation models are enough.

### Layered rendering order

```text
terrain / explored memory
→ world objects (gates, corpses, loot, effects on tiles)
→ visible actors
→ reachable/targeting/tactical overlays
→ cursor / selected target
→ transient effects and text labels
→ UI chrome (HUD, panels, messages)
```

The renderer should not decide whether a target is visible, whether a camp is suspicious, or how much an attack costs. It displays a **filtered model** produced from authoritative Core state and player knowledge.

### Technical realities

- Present SadConsole is an appropriate first implementation platform for enriched glyphs, colors and panels. Test any desired shaders/zoom/effect features before promising them.
- A more advanced renderer or engine is a **future option**, not a precondition. The architecture should leave simulation independent so a replacement is possible.
- Separate simulation cell coordinates, camera viewport coordinates, UI panel coordinates, and physical screen pixels.
- Revalidate cached paths and FOV/opacity if dynamic gates/walls arrive; this is Core correctness, not a cosmetic concern.
- Avoid expensive panel rebuilds on every render frame. Cache stable models and invalidate when events or selection change.

## 14. Testing, performance, and accessibility

### Required UI tests

- Fixed seed + commands produce repeatable simulation **and** stable player-visible events.
- Opening/closing UI panels, examining cells, searching inventory, moving the inspect cursor and resizing never consume world turns.
- Player view never exposes unseen actors or private knowledge; debug view remains explicitly opt-in.
- Mouse and keyboard produce equivalent commands for the same action.
- At small resolution, critical map cells, HP, warnings and recent messages remain visible/accessible.
- Colorblind and monochrome/high-contrast modes still distinguish friend/foe, open/closed gates, valid/invalid targets and hazard types.
- Reduced-motion mode does not change action order or obscure consequences.
- Target preview matches the Core action rules; log descriptions agree with events.

### Visual regression scenes

Record seeded/replayable scenarios and compare screenshots or semantic snapshots:

1. Calm surface route beside a camp.
2. Sentry warning and alarm.
3. Chase and withdrawal at a territorial boundary.
4. Underground darkness and a ranged-attack preview.
5. Faction clash with off-screen sounds.
6. Dense inventory with affixes and comparisons.
7. Compact window and large-font profile.

Test at 1366×768, 1920×1080 and a high-DPI setting. Expect glyph rasterization differences across platforms: reserve strict pixel comparisons for controlled environments, and assert semantic layout/information separately.

### Performance targets (to validate, not guarantees)

- UI-only actions should feel immediate and never block on AI simulation.
- Prefer dirty-region redraw and cached view models where useful.
- A visible tactical turn should not trigger an unnecessary redraw of every journal/history item.
- Rendering animations should not affect deterministic world state.
- Profiling should distinguish **simulation turns/second**, **input-to-feedback latency**, and **frame rendering**.

### Accessibility from the start

- Scalable text, comfortable spacing, high contrast and optional classic monochrome profile.
- Color + shape + text for critical states.
- Remappable keys, keyboard focus indicators, no mouse-only action.
- Reduced motion and adjustable/silent sound.
- An accessible text description of important map and event information where feasible.
- Clear uncertainty language for rumors and estimates.
- Avoid using tiny icons without labels as the only means of communication.

## 15. Development roadmap and scope control

**Priority is functional legibility before art polish.** These estimates are rough single-developer planning placeholders, not commitments; overlap and pre-existing infrastructure may change them substantially.

| Stage | Deliverable | Rough effort |
|---|---|---|
| **P0 - UI foundation** | `UiCommand` routing, adaptive layout/scaling, examine mode, log expansion, semantic colors, accessible panels, improved target preview, inventory comparison | ~3–5 developer-weeks |
| **P1 - Living-world readability** | Camp awareness, warnings, pursuit/retreat messaging, inspectable NPC behavior, contextual interaction, filtered player knowledge, journal basics | ~6–10 weeks |
| **P2 - Navigation and tooling** | Region map, travel/auto-explore with interruption, mouse workflows, richer tooltips, traceable event/AI debug views, replay/visual checks | Estimate after P0/P1 |
| **P3 - Visual polish** | Tile/hybrid mode if desired, richer lighting/effects, alternate themes and HUD modes, drag-and-drop, audio/animation polish, controller support | ~6–12+ weeks depending on scope |

**First design/UI vertical slice:**

One surface area, one goblin camp with a watch post and gate, and one underground entry. The player can pass outside, get challenged, negotiate, sneak, fight or retreat; the UI makes the difference obvious. A returning player can understand what changed. The same content should work in Classic and Enhanced ASCII profiles.

Acceptance:

- Player can identify road, camp, gate, guard position, cover and cave entrance without a manual.
- Player sees or hears a warning before predictable trespass escalation.
- Player can inspect the gate and guards without time passing.
- Player can understand why a chase starts and ends.
- All UI knowledge is justified by observation, memory or rumor.
- A full seeded scenario can be replayed and its key screens verified.
- The map remains prominent at both common desktop sizes.

### What not to do yet

- Do not replace SadConsole just to obtain bloom/shaders.
- Do not draw dozens of sprites before exploration and detection are readable.
- Do not make a floating RPG dashboard that consumes half the map.
- Do not put raw exact "AI morale 62 / alert 34 / knows X at coordinate Y" in the player HUD.
- Do not require mouse use; do not force a novel control system on traditional roguelike players.
- Do not implement a full journal/rumor UI before the underlying knowledge model exists.
- Do not make every effect animate at once.
- Do not build separate game logic for tiles and ASCII.
- Do not optimize for screenshots at the expense of minute-to-minute readability.

### Decisions for the next planning meeting

1. Choose the default **Enhanced ASCII** aesthetic and whether the first prototype needs any tiles.
2. Select a working typography/glyph strategy and semantic color palette.
3. Decide map viewport size, camera behavior and information panel policies at target resolutions.
4. Specify inspect/look mode interaction and what the player may legitimately know.
5. Prioritize status, threat panel, log, inventory and contextual interaction screens.
6. Decide minimal scene effects: torchlight, alarms, movement, combat and detection feedback.
7. Set a target for accessible keyboard/mouse workflows and reduced motion.
8. Agree on the exact first scene and screenshot/replay acceptance suite.

## 16. Research links and source notes

The following are reference materials used in the UI research; they support **lessons from existing games**. The proposed visual identity, layout, API names and roadmap above are recommendations for this project, not claims about those games.

### Cogmind - Grid Sage Games (primary developer sources)

- [Full UI Upscaling: History and Theory](https://www.gridsagegames.com/blog/2024/01/full-ui-upscaling-part-1-history-and-theory/) - scaling and layout tradeoffs.
- [Adventures in Map Zooming, Part 5: QoL](https://www.gridsagegames.com/blog/2024/02/adventures-in-map-zooming-part-5-qol/) - attention and map viewport issues.
- [Inventory Management, Revisited](https://www.gridsagegames.com/blog/2016/06/inventory-management-revisited/) - fast inventory interaction.
- [Tutorials and Help: Easing Players into the Game](https://www.gridsagegames.com/blog/2016/07/tutorials-help/) - discoverability and progressive teaching.
- [UI Feedback: Map Dynamics 2.0](https://www.gridsagegames.com/blog/2014/11/ui-feedback-map-dynamics-2-0/) - making map events legible.
- [Drag & Drop](https://www.gridsagegames.com/blog/2013/10/drag-drop/) - optional mouse workflows.
- [Cogmind home / blog](https://www.gridsagegames.com/cogmind/) - visual and interaction references.

### Caves of Qud and Brogue

- [Caves of Qud official wiki: Options](https://wiki.cavesofqud.com/wiki/Options) - scaling, UI configurations and accessibility settings.
- [Caves of Qud Steam announcements](https://steamcommunity.com/app/333640/allnews/) - changes to UI, auto-explore and input.
- [Brogue CE repository and changelog](https://github.com/tmewett/BrogueCE/blob/master/CHANGELOG.md) - rendering and UI improvements.
- [Brogue introduction / UI guide](https://brogue.wiki/wiki/Traditional_roguelikes_via_Brogue) - readable traditional roguelike presentation.

### Our own code

- [GameScreen.cs](../src/Roguelike.App/GameScreen.cs)
- [GameScreen.Items.cs](../src/Roguelike.App/GameScreen.Items.cs)
- [Theme.cs](../src/Roguelike.App/Theme.cs)
- [EventText.cs](../src/Roguelike.App/EventText.cs)
- [Project handbook](PROJECT.md)
- [World design vision](WORLD_DESIGN_VISION.md)

---

## Working rule

**Make the player understand the world through the screen before making the screen spectacular.**

If one camp and one cave are beautiful, readable, fast, and support multiple meaningful approaches, that presentation system can scale to the wider region and deeper dungeon.
