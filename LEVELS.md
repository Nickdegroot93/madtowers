# MadTowers Level Design Guide

How levels, game modes, bricks, and power-ups fit together, every dial you can turn, and
recipes for common authoring tasks. **Everything in this document is data — no programmer
needed** unless a row says "needs code". Physics tuning dials have their own contract in
[PHYSICS.md](PHYSICS.md); read that before touching anything under its "frozen" list.

---

## 1. The data model (what references what)

```
ChapterDefinition  (Assets/Resources/Chapters/  — an Archero-style chapter)
 ├─ sortOrder (chapters play lowest-first; leave gaps: 10, 20, 30...)
 ├─ presentation shared by its levels: backdrop (BackdropPreset: layered sky/clouds/
 │   hills/particles - see ART.md §3), musicPlaylist (random opener, then rotating;
 │   stops on game over),
 │   skinFolder (generated art; missing files fall back to Classic)
 ├─ featuredUnlocks: power-ups introduced by this chapter (messaging; availability is
 │   authored per level pool)
 └─ levels: ordered list of LevelDefinitions — any count per chapter

LevelDefinition  (Assets/Resources/Levels/  — one per level)
 ├─ identity: display name + menu thumbnail (presentation lives on the chapter)
 ├─ instruction: one-sentence goal banner shown (fade in/out) at level start
 ├─ GOAL: targetType (Endless | PlaceBlocks | ReachHeight) + targetValue.
 │   Reaching it enters the `WinVerifying` phase for a 5 s "Hold steady!" countdown:
 │   normal bag spawning is suspended, physics and the loss rules stay live, and only a tower that survives the
 │   window wins — rapid-dropping the last blocks buys nothing. ReachHeight is also
 │   re-checked against the LIVE standing tower during the countdown (the recorded max
 │   is monotonic); a collapse below the target aborts the countdown and play resumes.
 │   Surviving to zero raises `GameEvents.LevelCompleted(level, result)`, pauses, and
 │   shows Level Complete with "Next: <level>" (next in chapter), Keep Building, and
 │   Replay. Losing the last life mid-countdown is a normal game over; losing a
 │   non-final life is survivable ("lucky") by design.
 ├─ modifiers: LevelModifier assets — custom behaviour beyond settings (see below)
 ├─ abilities: bannedAbilities (per-level lockouts) + abilityRarityProfile (override
 │   the progress-scaled rarity odds of offers; see ABILITIES.md §7)
 └─ GameModeConfig  (Assets/Resources/GameModes/  — the entire rule set)
     ├─ Difficulty: fall speed start/ramp/cap, lives, spawn delay
     ├─ Floor: segments (span + heights + pockets — pillars, stairs, valleys, niches; FLOORS.md)
     ├─ Block bag: which BlockDefinitions are in play, and how many copies each
     │    └─ BlockDefinition → shape prefab + default BlockData variant
     ├─ Ambient variant chances: % rolls that replace spawns with a variant
     ├─ Sky platforms (static support islands): on/off, frequency, shapes, columns
     ├─ Power-up choices: cadence (every N blocks) + pool of AbilityDefinitions
     ├─ Camera: leniency (reaction room), zoom limits
     └─ Physics dials (see PHYSICS.md before changing)

BlockData variants     (Assets/Data/Blocks/    — one asset per brick type)
AbilityDefinitions     (Assets/Data/PowerUps/<Rarity>/ — one asset per ability)
```

Key separation: **LevelDefinition = identity + look. GameModeConfig = all rules.** Two levels
can share one mode; a level 1→100 campaign is 100 LevelDefinitions pointing at progressively
meaner GameModeConfigs (or fewer shared ones).

Custom Game builds a runtime `LevelDefinition` and selects it through `LevelSelectionState`, so
it is part of this same pipeline rather than a separate scene mode.

### Folder map

### Custom levels beyond settings: LevelModifier

When a level needs behaviour no setting covers, don't touch engine code — write a
`LevelModifier` subclass (in `Scripts/Levels/Modifiers/`), override the hooks you need
(`OnLevelStart`, `OnUpdate`, `OnBlockLocked`), create an asset, and drag it onto the level's
Modifiers list. They compose (a level can stack several), they're cloned per run (instance
fields are safe per-play state), and they receive a context (GameManager + Spawner — extend
`LevelModifierContext` when more is needed). `EarthquakeModifier` is the working example:
periodic velocity jolts to the whole tower. Wind, fog, timed events, starting towers — all
belong here.

`OnBlockLocked(context, totalBlocksPlaced)` is driven by physical pieces that successfully
joined the tower. It is not score, and score/status bonuses do not inflate it.

Rule of thumb: if two levels could ever want it with different numbers, it's a modifier with
serialized fields, not a one-off hack.

Spawn timing is phase/gate driven: `GameManager` publishes `GameEvents.SpawnAvailabilityChanged`
after scene `Awake` has completed, while `CameraIntroGate` and `WaveRevealGate` can suspend and
republish availability when their holds release. `Spawner` owns the actual next-piece creation and
ignores availability pings until its startup has registered the active mode's spawn tables.

#### Scheduled chapter/theme effects

For "every N seconds / every N blocks, enter a temporary state" levels, use
`ScheduledStatusModifier` instead of writing a bespoke scheduler. The modifier lives in
`Scripts/Levels/Modifiers/`; create an asset with **Create > Stacking > Levels > Modifiers >
Scheduled Status**, store it under `Assets/Data/Modifiers/`, then drag it onto a
`LevelDefinition.modifiers` slot.

Each scheduled entry points at a `StatusEffectDefinition` asset and chooses one trigger:

- `Time`: starts after `firstDelaySeconds` (or `intervalSeconds` when first delay is 0), then
  repeats every `intervalSeconds`.
- `BlockCount`: repeats every `intervalBlocks` **physical placed pieces**. This deliberately
  ignores score bonuses such as Overdrive, so "every 30 blocks" means 30 real pieces joined
  the tower.

Shared knobs: `graceBlocks` delays activation until the player has built a small base;
`triggerAtLevelStart` applies the state immediately once grace rules allow; duration and
magnitude overrides can replace the status asset's defaults per level; multiple entries in
one modifier can overlap naturally (snow + wind, sand + lightning, etc.).

Authoring recipe for a chapter-flavoured pressure event:

1. Create or reuse a `StatusEffectDefinition` asset (currently under
   `Assets/Data/PowerUps/Status/`). Use `Custom` when no built-in core consult point exists.
2. Put the player-facing field/overlay prefab on `StatusEffectDefinition.screenEffect` when
   the state needs obvious feedback (snowstorm veil, sand haze, neon pulse).
3. If the status changes gameplay beyond the built-in kinds, add a small runtime listener
   component that queries `StatusEffects.IsActive(status)` and owns that behaviour
   (wind jolts, block visual override, temporary friction change). Keep the scheduler unaware
   of the effect details.
4. Create a `ScheduledStatusModifier` asset per level/chapter difficulty, add one or more
   entries, then assign it to the level's Modifiers list.

### Level types (the catalog)

A level *type* is a different way to play — not just harder numbers. Mechanically, a type
is **a GameModeConfig flavour + (optionally) a LevelModifier that owns the special rule and
its visuals**, with winning expressed through the standard goal system. New types never
touch engine code.

| Type | Assembled from | Win condition |
|---|---|---|
| **Classic stacking** | any mode, no modifiers — the base game | `Endless` (free play), `ReachHeight` (climb to X m), or `PlaceBlocks` (stack N) — three sub-flavours for free |
| **Height-Limit Waves** ("Laser Limit" — Tricky Towers' puzzle mode) | `HeightLimitWavesModifier` asset on the level | `ClearWaves` = waves to win (waves continue endlessly after) |
| **Scheduled theme pressure** (blackouts, snowstorms, sandstorms, wind...) | `ScheduledStatusModifier` applying one or more `StatusEffectDefinition` assets | any standard goal |
| **Airtight** (no sealed hollows) | `AirPocketModifier` asset on the level | any standard goal (typically `PlaceBlocks`) |
| **Void Zones** (forbidden sky rectangles) | `VoidZoneModifier` asset on the level | any standard goal (typically `PlaceBlocks`) |
| *future: rising water, timed rush, wind gauntlet…* | one modifier subclass each, same recipe | standard goals |

> **Win conditions are polymorphic.** A level still authors `targetType` + `targetValue`, but the
> *behaviour* (arming, hold-steady verification, run progress, menu text) lives on a `WinCondition`
> built from that enum in `LevelDefinition.WinCondition` — no scattered `switch`es across the runtime,
> the picker and the menu. The three built-ins are in `Scripts/Levels/WinConditions/`; a brand-new win
> *type* (e.g. "survive N seconds") is one `WinCondition` subclass + one factory line — the same
> one-file-per-type ergonomics as `LevelModifier`.

**Building a new type** (the recipe Height-Limit Waves followed):
1. Subclass `LevelModifier` in `Scripts/Levels/Modifiers/`; the modifier owns ALL the
   rule logic *and* its visuals (use `RuntimeSprites` for code-built shapes).
2. Serialize every tunable (counts, heights, colors…) so per-level variants are pure assets.
3. Express winning through the existing `targetType` — never invent a parallel win path.
4. **Name the type**: implement `ILevelMenuProgressProvider` and return the type name from
   `MenuChallengeLabel` ("AIRTIGHT", "VOID ZONES") — the menu and results card then show
   the TYPE as the challenge, with the goal as the progress line. Label-only types return
   null from `MenuProgressLabel`/`EndOfRunMetric` (the goal's defaults fall through);
   bespoke-metric types (waves) implement all three. Resolution order: level's
   `menuChallengeLabelOverride` > first providing modifier > the goal's default.
5. Validate wiring in `OnLevelStart` (warn loudly if the level's goal doesn't match).
6. Document it here and add a catalog row.

#### Height-Limit Waves details (REBUILT July 2026 — endless generated waves)

- Blocks arrive in **endless waves**; the whole tower must stay under a glowing **limit line**.
  Clearing a wave makes the line **glide up** and the next, bigger wave begins — forever.
- **Counting is the LIVE standing count** (BLOCKS.md), never cumulative placements: a wave of
  10 means 10 blocks *standing*. A block that is zapped, scrapped, sacrificed, bombed or
  knocked off **reopens its wave's bill** (the counter grows back; a "10 block" wave can owe
  13 after losses). There is no crediting a block that no longer exists, and no ability
  cheese — destruction never shortens a wave. The line itself **never descends**.
- A **landed** block crossing the line is **zapped** (destroyed) and costs a life through the
  normal lives/GameOver flow. The falling piece passes the line freely (it spawns above it).
- **A wave must be SURVIVED, not touched — the clear-confirm gate**: reaching the target only
  *arms* the clear. The spawn hold goes up immediately (it has to — the count signal fires
  inside `LockBlock`, just ahead of the next spawn), but the line stays put and the advance
  lands only after ~0.35 s with the count still holding and **nothing standing above the
  line** — checked geometrically, independent of the zap cooldown, so a violation still
  waiting out the cooldown blocks the advance just as hard as one already zapped. A zap or
  collapse inside the window cancels the clear: the wave stays open, the counter shows the
  reopened bill, the next piece resumes. Without this, the last block of a wave banked the
  wave *and then* got zapped — cleared waves are monotonic, so the credit was unrecoverable
  (Nick, July 2026).
- Wire-up: pair the modifier with `targetType: ClearWaves`, `targetValue: <waves to win>`
  (`Endless` is also legal — waves with no win, daily-mode material). The win lands exactly
  when wave N clears (same standing-count signal, via `ClearWavesWinCondition` reading
  `HeightLimitWavesModifier.ActiveRun`), and "Keep Playing" continues INTO the wave chase —
  the line keeps rising; there is no fallback to a plain endless run. A console warning
  fires at level start for any other goal pairing.
- **Nothing is hand-authored — every height is SOLVED from the floor** (the wave engine, all
  code-owned constants in the modifier):
  1. Wave `n` asks a quota `q(n)` of net new standing blocks: `6 + 1.5·(n−1)`, capped at 24.
  2. Capacity: per playable grid column, the cells between its top and the line. Interior gap
     columns count from the height they become bridgeable (the lower flanking top); **overhang
     columns** outside the footprint (3 per side) count from `edgeTop + 3·i` cells — building
     wider than the floor is part of the game, but walking outward costs rise.
  3. The asset's **`difficultyRank` 1–5** sets the required packing density
     (start 0.45/0.55/0.65/0.75/0.85 by rank, +0.012 per wave, capped 0.60/0.68/0.76/0.83/0.90).
     Densities are NOMINAL against a capacity model that budgets overhang/gap columns the
     player isn't forced to use, so they sit well above the experienced squeeze (0.72 nominal
     played as "could place 20 where 8 were asked" — Nick). Rank 5's 0.90 cap is
     PROGRESSION.md's achievability hard edge.
  4. Line height for wave `n` = smallest `h` with `capacity(h) · density(n) ≥ cumQuota(n) ·
     avgCellsPerBlock`, min rise 1 cell (the solver's tiny late-wave rises are the squeeze —
     never pad them). `avgCellsPerBlock` comes from the level's shape bag
     (bag-weighted prefab cell count) **divided by the magma inflation** `1 + 3·magmaRate`
     (PROGRESSION.md's ×4-counted-blocks rule, read off the mode's ambient variant chances).
  Deterministic per level (floor + bag + rank), so leaderboard runs race identical waves. The
  engine re-solves if the floor config resolves late (procedural floors).
- **Per-level difficulty = `difficultyRank` on the per-chapter modifier asset** — but ALL
  shipped assets run rank 5 (Nick, 2026-07-26: rank 2 played far too easy — "needed 8, could
  have placed 20"; puzzle chapters differentiate by brick variants + per-chapter speed
  instead, the squeeze stays uniformly tight). Waves-to-win stays 5/6/7 by chapter third.
  Lower ranks remain in the code for a future easier mode. Style knobs stay per asset:
  `lineRiseSeconds`, `lineColor`, `lineThickness`, `lineBaseAlpha`, `linePulseAmount`,
  `linePulseSpeed`. Mode dials and other modifiers stack on top. The old flat-rise principle
  is subsumed: density ramping per wave makes rise-per-block naturally non-growing.
- **Scores are ENCODED**: bests and leaderboards store `wavesCleared × 1000 + peak in-wave
  progress` (`HeightLimitWavesModifier.OverrideReportedScore`; every display — menu, results,
  RANKS rows, the modal's BOOSTED BEST caption — decodes through
  `ClearWavesWinCondition.FormatBoardScore`/`DecodeWaves`).
  Sub-wave granularity keeps board ties rare. Pre-rebuild block-count bests decode as garbage
  waves; dev saves only (nothing shipped).
- **Half-cell grace (code)**: the line renders and zaps **half a cell above** the solved
  height — a tower that exactly fills the solved rows can wobble without grazing the laser,
  but one more full row still crosses. The grace never feeds the island ceiling.
- A countdown rides the right end of the line showing blocks left to STAND until it rises.
- Laser **art** follows the active chapter automatically: drop a `laser.png` into
  `Resources/Skins/<Chapter>/` (see ART.md) and every laser level in that chapter uses it;
  no file = the code-built bar. Zapped blocks burst via the reusable `BlockShatterFx`
  (shards tinted to the laser color) plus a subtle camera impact.

#### Airtight details

- Build without sealing empty space: open gaps (reachable from the sky or the flanks) are
  legal; the placement that closes an empty region's LAST opening arms an **air pocket** —
  dark pressure-smoke fills it (shared `Resources/AirPocketSmoke.shader`, theme-independent
  fixed look like Magma), and a full pocket **detonates and costs a life** through the
  normal hazard flow (`LoseLifeToHazard` — immunity abilities apply). One connected sealed
  region = one pocket = one life, any size.
- The fuse is the **rescue window**: destroying any sealing block (Zap, Extract, a topple)
  reconnects the region to open air and the smoke **vents** harmlessly — no charge, and the
  cells can seal again later. A detonated pocket's cells go **inert** (spent): the stack
  above is untouched and spent space can never charge a second life, so one mistake is one
  life, ever.
- Detection: the settled stack is rasterized onto the placement grid (landed block cells +
  floor terrain incl. carved pockets + islands; the active piece and falling debris are
  never solid) and open air is flood-filled from outside — 4-connectivity, so a diagonal
  crack is not an opening. Rescans run on lock/destroy events plus a 0.5 s cadence for
  settle drift.
- **Tilt-proofing** — the raster alone lies once the tower leans (centres round on/off the
  lattice, hairline cracks open between bricks), so a coarse sealed region passes two
  stricter gates before it can cost a life: **coverage** (every candidate cell is sampled
  against the blocks' live colliders — ≥65% covered is brick, not air; 35–65% is a sliver
  crack, passable but never pocket volume; and a raster-solid wall cell that's physically
  cracked below 65% is a **leak**, the region isn't airtight) and **persistence** (a fresh
  seal sits on silent probation for ~0.9 s of scans before it arms — drift flickers
  evaporate, real seals arm a beat after the lock). Strictness errs toward the player: a
  wrongly-vented pocket costs nothing, a wrongly-armed one costs a life.
- Look: the pocket's cell set is baked into a tiny bilinear **mask texture** (one texel per
  cell) that one quad renders in world space — the smoke's boundary is the mask edge torn
  by noise, bulging a fraction of a cell past the bricks, so no fill level ever reads as a
  clean rectangle. The detonation flare renders through the **same mask**
  (`Resources/AirPocketFlash.shader`): a cavity-shaped, noise-eaten flash, not a bounding
  box.
- Tuning knobs on the asset (`Data/Modifiers/AirPocket_Standard`): `fuseSeconds` (5, the
  1-cell rescue window), `extraSecondsPerCell` (1 — bigger pockets hold more air and fill
  slower), optional per-cell `popEffect` prefab + scale (null-safe; the code-built flash,
  shockwave ring and camera impact always play).
- The blast also **rocks the tower**, scaled by pocket size: a Tremor-style kick burst
  (`TremorBlockBehaviour`, velocity-only, frozen blocks immune) from the pocket's centre at
  `shakeStrengthPerCell` (0.45) × cells, capped at `shakeCellCap` (6) — a 1-cell pop is a
  shiver well under the Tremor brick's 1.5, a 4-cell blunder is ~4× that plus a bigger
  camera impact. `shakeDurationSeconds` (0.4) and `shakeRadius` (7) tune the burst.
- SFX: `pocket_seal` / `pocket_vent` / `pocket_pop` (generated via ElevenLabs;
  prompts in `Tools/generate_elevenlabs_sfx.py`).

#### Void Zones details

- **Forbidden rectangles torn into the sky** (2×2 / 2×3, `VoidZone.shader` black-hole look:
  dark eye, spiral arms, pulsing accretion rim — the visual fills the exact danger rect so
  the kill boundary is honest). They spawn AHEAD of the tower peak like sky islands — you
  always see them coming — and the falling piece steers straight through them (they render
  behind bricks, order −3). A **LANDED block overlapping one is sucked in**: the legal
  doomed-block animation (kinematic first, PHYSICS.md I1) spirals it into the eye over
  ~0.45 s, then the standard destruction flow + `LoseLifeToHazard`.
- **Absolute law, with three principled exemptions**: blocks pushed in later by a topple
  or settle drift are devoured too (0.5 s sweep cadence on top of lock-driven checks), and
  suck cascades may drain multiple lives. Exempt: a hair's-width graze (a cell must reach
  `overlapInset` (0.15) into the rect), blocks already falling away (the loss line owns
  them — no double jeopardy), and **maws** (the Extract precedent: maws never participate
  in removal effects; their welds are unbreakable and dragging one member of a fused
  cluster would haul the rest through the tower). A zone only becomes lethal AFTER its
  0.7 s tear-open animation — the danger never outruns what the player can see.
- **The route guarantee** (the fairness core, mirroring PHYSICS.md's reach guarantee): a
  zone never spawns where it would wall off the sky — at least `WidestBlockColumns` (4)
  clear reachable columns always remain past one side, computed with the island guardrail
  math (max zoom-out anchored at floor centre). Zones also never materialize overlapping
  islands, other zones (1-cell gap), the tower, or the falling piece.
- Dials on `Data/Modifiers/VoidZones_Standard`: `firstZoneHeight` (10), `heightInterval`
  (8), `spawnChance` (0.85), `spawnAheadHeight` (7), width 2–3 × height 2, lateral band
  ±6 columns, `maxZonesPerRun` (0 = unlimited), `suckSeconds`, `overlapInset`. Per-level
  variants = difficulty tiers.
- Campaign debut: **Neon Nightfall's opener "The Waterfront"** (Ch3, `VoidZones_NeonDebut`
  at half exposure: first zone 12m, every 11m, 65%). Recurring/full-strength showcase:
  Hallow's End's 4th level **"Void Zones"** (Classic, place 100, `VoidZones_Standard`). Stacks freely
  with other modifiers — voids under a Blackout are memorized hazards.
- SFX: `void_open` / `void_suck` (generated via ElevenLabs; prompts in the tool).

#### Blackout details (the first scheduled-pressure state)

- **The district loses power** for ~20 s: a PITCH-BLACK curtain covers the world
  (`Resources/BlackoutOverlay.shader` + `BlackoutOverlay.cs`) with exactly ONE light — a
  feathered lantern riding the falling piece. Outside it the tower is a memory: the
  3-second power-down is the deliberate memorize window, then you place by what the
  lantern reveals as the piece descends. The HUD is screen-space canvas and stays readable
  by construction; ability-session overlays (Extract/Overdraw, order 220+) render above
  the curtain (order 200) so ability use stays possible in the dark.
- **Playtested-and-rejected looks, do not regress**: partial darkness (0.93 and 0.985 both
  read as "I can still see everything" — display gamma turns even 1.5% linear bleed into
  ~12% on-screen brightness, so anything below ~1.0 is decorative, not difficult); a
  tower-peak bearing glow (it lit the very thing the mode hides); a plain smoothstep edge
  (gamma compresses the fade's tail into a visible ring — the shader feathers by running
  the falloff past the radius and easing quadratically into black).
- **It is a STATUS, not a mode**: `Status_Blackout` (kind Custom, 20 s) owns the look via
  its `screenEffect` prefab (`Data/PowerUps/Status/BlackoutOverlay.prefab`), so anything
  can trigger a blackout — the scheduler, a future cursed brick, a chapter gimmick. The
  overlay pre-fades via `StatusEffects.GetRemaining` so the relight never pops.
- **Scheduling is data**: `Data/Modifiers/ScheduledStatus_Blackout_Standard` — first
  blackout after 45 s, then every 75 s, `graceBlocks 8` (never blinds a near-empty board),
  duration from the status (override per level for gentler/meaner chapters). Attached to
  all three Crimson Core levels; drop the same asset (or a re-tuned copy) on any dark
  chapter's levels. Stacks freely with other modifiers — note that world-space indicator
  lines (the wave limit laser at order 50, Sacrifice/Hardline at −40/−39) go dark with
  everything else: during a blackout you stay under the line you MEMORIZED. Only the
  lantern, ability-session overlays (220+) and the HUD canvas pierce the dark.
- Playtest dials: `darkness` (1 - see above), `lanternRadius` (7), `lanternFlicker`, fade
  timings (fade-out must stay under StatusFieldController's 1.2 s teardown grace) on the
  prefab; the feather band (`0.45`/`1.35` × radius) in the shader.
- SFX: `blackout_in` / `blackout_out` (generated via ElevenLabs; the generic
  `status_engage` also fires on activation).

### Campaign structure & progression

The game is a campaign of chapters: chapters unlock in `sortOrder` once the
previous chapter's levels are ALL completed; levels within a chapter unlock sequentially.
Rules live in `Campaign.cs` (read-side only); completions and personal bests persist via
`ProgressStore` (see **DATA.md** — cloud sync is live) and bests feed the per-level
CLEAN/BOOSTED leaderboards. Campaign runs are additionally server-gated: `RunGate.BeginRun`
must obtain a `start_run` grant (the attempts meter) before a run starts, and results go
back through `finish_run` (BACKEND.md §6 — Custom Game is exempt).
A chapter with `alwaysUnlocked: true` is a sandbox — always playable, never gates the
campaign. The menu shows chapters as a carousel (one chapter per screen).
`Campaign.UnlockAllForTesting` is **off by default everywhere** so editor testing exercises
real progression. Add the `MADTOWERS_UNLOCK_ALL` scripting define only when a temporary
local build truly needs every chapter open.
Each chapter's `skinFolder` drives all generated art (blocks/ground/laser) via
`ChapterSkins`; empty = Classic skin.

### Current level inventory

**Chapter: Jungle Depths (sortOrder 10)** — imported Jungle Landscape menu art, layered
jungle gameplay backdrop, jungle skin folder, jungle A/B music. **Chapter 1, authored to
PROGRESSION.md**: Vine ambient 5% everywhere, islands/sky platforms off, leniency 0.50,
power-up choice every 10, speeds from the Ch1 row (2.5→5.0 × mode multiplier) with the
ramp derived to hit cap at 90% of the goal. The gesture **tutorial**
(`Tutorial_GestureBasics`) is attached to The Undergrowth (self-gates once completed).
Former 4th level Temple Sprint (timed) retired to `Assets/Data/LegacyLevels/` — timed
modes join mid-campaign.
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Undergrowth | GameMode_JungleUndergrowth | Place 100 | Tutorial level. Flat 9-column floor (pocketed terrace removed). 2.5→5.0 @ +0.028/blk. |
| Canopy Trial | GameMode_JungleLaserLimit | Place 65 | 5 height-limit waves (8@5 → 18@19). 2.2→4.4 @ +0.038/blk. |
| Vine Ascent | GameMode_JungleNarrow3 | Reach 75m | 4-column floor climb, no islands. 2.4→4.75 @ +0.022/blk (~120 expected blocks — calibrate blocks-per-meter). |

**Chapter: Sakura Ridge (sortOrder 20)** — imported Japan Landscape menu art, layered Japan
gameplay backdrop, Japan skin folder, sakura-ridge A/B music. **Chapter 2, authored to
PROGRESSION.md**: Feather debuts at 6% ambient on all three levels, chapter-owned mode
copies (the standard `GameMode_Classic`/`LaserLimit`/`Narrow3` assets are shared by other
chapters — never edit those for one chapter), Ch2 speeds (2.6→5.2 × mode multiplier),
derived ramps. Signature floors: every level plays on a **pagoda-roof profile** (low eaves
at the edges, high in the middle) — three unique variants, none flat.
| Level | Mode | Goal | Notes |
|---|---|---|---|
| Morning Gate | GameMode_SakuraClassic | Place 100 | Ziggurat pagoda floor [0,1,2,3,3,3,2,1,0]. 2.6→5.2 @ +0.029/blk. |
| Lantern Drift | GameMode_SakuraLaserLimit | Place 72 | 6 waves (7@6 → 17@24, `HeightLimitWaves_SakuraRidge`; final line raised 22→24 after cell-math showed the squeeze beat Jungle's endgame); upturned-eave floor [1,0,1,2,2,2,1,0,1] kept ≤ +2 for wave headroom. 2.3→4.6 @ +0.036/blk. |
| Temple Steps | GameMode_SakuraNarrow | Reach 75m | Raised 3-wide altar on 5 columns [0,2,2,2,0]. 2.45→4.95 @ +0.023/blk. |

**Chapter: Neon Nightfall (sortOrder 30)** — imported Glowing City pack as a waterfront
skyline (three hand-placed building bands + far strip, generated `water_neon` band, two
drifting boat strips, fairy-light promenade + vine-fence foreground), Neon skin folder
with the wet-pavement floor, neon-nightfall menu art, neon-nightfall A/B music
**Chapter 3, authored to PROGRESSION.md**: no special bricks yet (signature TBD — more
ideas to come), Ch3 speeds (2.7→5.4 × mode multiplier), derived ramps. The chapter's
identity for now: the campaign's **first Void Zones level** as the opener, and its first
genuinely hard puzzle.
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Waterfront | GameMode_NeonVoidZones | Place 100 | **Void Zones debut** (`VoidZones_NeonDebut`: first zone at 12m, every 11m, 65% chance — roughly half the standard asset's exposure; `VoidZones_Standard` stays the recurring-level tuning). Flat 9 floor. 2.3→4.6 @ +0.026/blk (×0.85 hard-mode multiplier). |
| Voltage Line | GameMode_NeonLaserLimit | Place 60 | **Hard puzzle**: 5 waves 7@4 · 9@7 · 12@10 · 14@13 · 18@18 (integer heights only — a .5 height plus the half-cell grace would land the line back on a row boundary) (`HeightLimitWaves_NeonNightfall`) — low opening line, tight rises; final squeeze needs ~13.5 of the 15 reachable columns (Jungle-endgame density, verified by cell math). 2.4→4.75 @ +0.044/blk. |
| Penthouse Run | GameMode_NeonNarrowTrio | Reach 60m | **Two rooftop pillars** (w2 @ +2 low, w1 spire @ +7, 1-col gap — iterated down from trio; three pillars played too easy, Nick's call). The 2-wide sits under the spawn. 2.55→5.15 @ +0.031/blk. |

**Chapter: Frozen Peaks (sortOrder 40)** — imported Winter Mountain Landscape gameplay
backdrop (+ generated `clouds_winter` drift strip), Winter skin folder, frozen-peaks
menu art, frozen-peaks A/B music
**Chapter 4, authored to PROGRESSION.md**: **Ice debuts as the chapter's identity** — the screw is
volume, not speed: **28% ambient on all three levels** (playtest-bracketed: 10–15% too easy,
50% too rough — just over a quarter of the bag slippery is where the mountain bites
without tipping unfair). Ch4 speeds
(2.8→5.6 × mode multiplier), derived ramps, mountain-profile floors.
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Snowline | GameMode_FrozenClassic | Place 100 | Ice 28%. Rounded summit floor [0,0,1,2,3,2,1,0,0]. 2.8→5.6 @ +0.031/blk. |
| Whiteout Pass | GameMode_FrozenLaserLimit | Place 65 | Ice 28%. 5 waves 8@5 · 10@8 · 12@11 · 15@15 · 20@20 (`HeightLimitWaves_FrozenPeaks`, final squeeze at the Jungle edge). Twin-peak pass floor [1,2,1,0,0,0,1,2,1]. 2.45→4.95 @ +0.043/blk. |
| Summit Climb | GameMode_FrozenNarrow | Reach 75m | **The wall**: Ice 28% on a 5-column floor split by a +5 summit spike [1,2,5,2,1] — two 2-wide ledges, slippery bricks, bridge over the spike to build wide. 2.65→5.3 @ +0.025/blk. |

**Chapter: Kvartal 4 (sortOrder 50)** — imported Sovietwave Panel Buildings pack as a
hand-placed night skyline (individual panelka sprites + treeline/fence strips + pack moon,
generated `clouds_night` drift strip), Kvartal skin folder, kvartal-4 menu art,
kvartal-4 A/B music
**Chapter 5, authored to PROGRESSION.md**: **Locked debuts** (can't-rotate planning
hazard) on the first three levels, and the campaign's **first Airtight** closes the
chapter as its real wall — deliberately Locked-free (a can't-rotate piece in a
seal-no-hollows mode would be a double screw). Ch5 speeds (3.0→5.8 × mode multiplier),
derived ramps. Floors are **panelka silhouettes** — each of the first three levels plays
over raised apartment-slab terrain with a nudge-in **pocket just under a roofline** on an
exposed side face (depth 1 from the column top); only the Airtight wall keeps a flat
yard, so the sealing rule is read against clean ground.
| Level | Mode | Goal | Notes |
|---|---|---|---|
| Panelka Row | GameMode_KvartalClassic | Place 100 | Locked 10%. Two slabs [3,3,·,·,·,·,2,2,·] with a street at datum under the spawn; roofline pockets on both inner faces. 3.0→5.8 @ +0.031/blk. |
| Curfew Line | GameMode_KvartalLaserLimit | Place 68 | Locked 8%. Courtyard floor [2,2,·,·,·,·,·,3,3] — edge buildings, build in the yard; inward-facing roofline pockets. 5 waves 8@5 · 11@8 · 13@11 · 16@15 · 20@21 (`HeightLimitWaves_Kvartal4`, comfy-side squeeze since Locked rides the bag). 2.65→5.1 @ +0.04/blk. |
| Antenna Climb | GameMode_KvartalNarrow3 | Reach 60m | Locked 10% on the campaign's **first true 3-column climb** [0,0,2] — a rooftop with a stairwell-exit corner (pocket under its top); a piece that won't rotate on three columns is the level. 2.85→5.5 @ +0.031/blk. |
| Airtight | GameMode_KvartalAirtight | Place 100 | **Airtight debut** (AirPocket_Standard: sealed hollows detonate — see "Airtight details"). No variants; the rule is the whole screw. 2.55→4.95 @ +0.027/blk (×0.85). |

**Chapter: Barren Lands (sortOrder 60)** — imported Desert Vibe menu art, layered desert
gameplay backdrop, desert skin, desert A/B music
**Chapter 6, authored to PROGRESSION.md**: **Sandstone debuts** (5% on all three levels — 12%
playtested as too frequent; unlike slippery Ice, a load-bearing brick occupies the tower
and stays a problem, so its felt rate is much higher than its roll rate) at
Ch6 speeds (3.1→6.0 × mode multiplier). Floor identity: **deep pockets** — carved
cliff-dwelling niches, the pocket showcase chapter (Kvartal's roofline nicks were the
soft introduction; here the niches are big enough to matter). Possible later addition:
a dust-weather scheduled status (parked — Nick).
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Mirage | GameMode_BarrenClassic | Place 100 | Sandstone 5%. Canyon bed between two +4 mesa walls [4,4,·,·,·,·,·,4,4]; **outer-face stacked niches** (depths 1+3 on columns 0 and 8) — the stack leaves a floating brick over each mesa, the look Nick picked from Rising Dunes. Pockets go on the OUTSIDE of structures (Nick's call, July 2026). 3.1→6.0 @ +0.032/blk. |
| Sandswept Path | GameMode_BarrenLaserLimit | Place 72 | Sandstone 5%. Sunken wadi [2,1,·,·,·,·,·,1,2] with a ground-level cave in each OUTER rim face (depth 2, columns 0/8). 5 waves 9@5 · 12@8 · 14@12 · 17@16 · 20@21 (`HeightLimitWaves_BarrenLands`). 2.75→5.3 @ +0.039/blk. |
| Rising Dunes | GameMode_BarrenNarrow | Reach 65m | Sandstone 5%. 4 columns beside a **+5 cliff** [0,0,0,5] with two niches in its face (depths 1 and 3) — tuck pieces into the cliff as you climb past it. 2.95→5.7 @ +0.029/blk. |

**Chapter: Sector Isla (sortOrder 70)** — imported Secret Island pack (green-dusk lagoon:
crescents sheet, pale far mountains, wheel-city masses over a tiled palm row, teal island
bands incl. the two-figure accent, generated `water_isla` band + `boat_strip_isla` — the
pack's speedboat on a wide mostly-empty drift strip at +1.6 u/s, so it crosses fast, leaves
the screen entirely and returns ~once a minute; hanging-canopy fg sprites deliberately
skipped in a vertical game), Island skin folder with mossy cobble floor, firefly motes +
songbird flock, sector-isla menu art, one music track (b was a duplicate of a)
**Chapter 7, authored to PROGRESSION.md**: **Anchor debuts** as a rare lucky drop (3% on
all three levels — it freezes into permanent terrain, incredibly powerful, so seeing one
is a jackpot moment) and the `AnchorSpawn` ability was promoted **Rare → Epic** to match.
The chapter opens on **Airtight** (watertight fits the island) — second Airtight in the
campaign, first as an opener. Ch7 speeds (3.2→6.2 × mode multiplier); the campaign's
**first timed level** closes the chapter.
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Lagoon | GameMode_IslaAirtight | Place 100 | **Airtight opener** (AirPocket_Standard). Gentle beach-berm floor [·,1,1,·,·,·,1,1,·]; no terrain pockets (a hollow beside the sealing rule reads as a trap). 2.7→5.25 @ +0.028/blk (×0.85). |
| Marina Line | GameMode_IslaLaserLimit | Place 64 | **Tough puzzle**: 5 waves 8@4 · 10@7 · 13@10 · 15@14 · 18@19 (`HeightLimitWaves_SectorIsla`, hard-edge squeeze). Dock-deck floor [1,1,1,1,·,·,·,3,·] with an outside dock-edge cave (col 0) and a piling niche (col 7 depth 2). 2.8→5.45 @ +0.046/blk. |
| Skywheel Sprint | GameMode_IslaTimed | 40 blocks / 180 s | **First timed level** (TimedPlaceBlocks, 4.5 s/block budget — generous debut; tighten later per the timed ladder). Flat floor, one screw: the clock. 3.2→6.2 @ +0.083/blk. |

**Chapter: Fangkuai District (sortOrder 80)** — imported Chinese City gameplay backdrop
(feathered `bg_dusk` plate + generated `clouds_dusk` drift strip), Fangkuai skin folder,
fangkuai-district menu art, fangkuai-district A/B music
**Chapter 8, authored to PROGRESSION.md**: **Vortex debuts** (inverted steering — a
control hazard: 8% on the first three levels (6% felt too scarce — Nick), 4% on the fourth) at Ch8 speeds
(3.4→6.4 × mode multiplier). Fourth level added: **Void Zones round two** at the standard
asset's full strength (the Ch3 debut ran the half-exposure asset).
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Night Market | GameMode_FangkuaiClassic | Place 100 | Vortex 8%. Two **floating lanterns** [·,3,·,·,·,·,·,3,·] — free-floating cubes at +1..+2 via float-pair pockets (fixed from the truncated-post first pass). 3.4→6.4 @ +0.033/blk. |
| Firecracker Alley | GameMode_FangkuaiLaserLimit | Place 68 | Vortex 8%. Walled alley [3,·,·,·,·,·,·,·,3], outside ground caves (cols 0/8 depth 2). 5 waves 8@4 · 11@7 · 13@11 · 16@15 · 20@20 (`HeightLimitWaves_Fangkuai`, hard edge). 3.0→5.65 @ +0.043/blk. |
| Pagoda Climb | GameMode_FangkuaiNarrow | Reach 80m | Vortex 8%. Descending staircase [4,3,2,1,0] with floating cap + niche on the tall end (col 0, depths 1+3). 3.25→6.1 @ +0.025/blk. |
| Void Gate | GameMode_FangkuaiVoidZones | Place 100 | **Void Zones II** — `VoidZones_Standard` full strength (first zone 10m, every 8m, 85%). Vortex 4%. Flat floor. 2.9→5.45 @ +0.028/blk (×0.85). NOTE: menu thumbnail temporarily reuses Night Market's — needs its own art. |

**Chapter: Lost City (sortOrder 90)** — imported Lost City / Distant Planet pack (giant-moon
plate as a chapter-owned `bg_moon_lc` copy with the below-skyline half flattened to clean fog
— the vendor plate's moon bottom + fog-band edges expose as tone rectangles mid-climb in
portrait; eight-band teal/orange depth ladder LC1→LC8 used in full, pack streak-cloud strip
drifting), LostCity skin folder with slate-teal cobble ruin floor, pale teal motes + 2 night
birds, lost-city menu art, lost-city A/B music
**Chapter 9, authored to PROGRESSION.md**: **Boulder + Tremor debut together** (the tower
fights back — weight and shakes pair naturally; the planned double-negative exception
chapter). Ch9 speeds (3.5→6.7 × mode multiplier). Floor identity: **levitating ruins** —
floating capstones (depth-1 pocket stacks) sell the anti-gravity lost-city fantasy under
the giant moon, plus the campaign's first **sky platforms**, kept sparse (interval 6m ·
35% ≈ one island per ~17m — "1–2 every now and then", Nick) and OFF in the climb.
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Oasis Gate | GameMode_LostCityClassic | Place 100 | Boulder 5% + Tremor 6%. **Levitating debris field** [3,·,4,·,1,1,·,5,·] with float-pair pockets: a free-floating cube (col 0), stub+float (col 2), stub+2-tall floating slab (col 7), low threshold under the spawn (reworked from the notched-post first pass — Nick: boring). Sparse sky platforms ON. 3.5→6.7 @ +0.036/blk. |
| Aqueduct Line | GameMode_LostCityLaserLimit | Place 69 | Boulder 4% + Tremor 5%. **Five ruined aqueduct piers** [2,·,2,·,2,·,2,·,2] (1-col gaps to bridge), a fragment floats mid-span over the centre pier (+3, float pair). Sparse sky platforms ON. 5 waves 9@5 · 11@8 · 13@11 · 16@15 · 20@20 (`HeightLimitWaves_LostCity`). 3.1→5.9 @ +0.045/blk. |
| Monolith Climb | GameMode_LostCityNarrow | Reach 85m | Boulder 4% + Tremor 5%. 4 columns beside a **+6 monolith with a levitating shard** (depths 1+3). Sky platforms OFF (pure climb). 3.35→6.35 @ +0.025/blk. |
| Hollow Moon | GameMode_LostCityAirtight | Place 100 | **Airtight III** (AirPocket_Standard) — first Airtight on real terrain: sunken plaza [1,1,·,2,·,·,·,1,1] with a 1-wide broken altar beside the spawn; sealing against terrain faces is the new lesson. Boulder 4% + Tremor 5% (the chapter pair rides every level — Nick), no pockets/islands. 3.0→5.7 @ +0.03/blk (×0.85). Thumbnail reuses Oasis Gate's — needs art. |

**Chapter: Burning Steppes (sortOrder 100)** — imported 2D Volcano Landscape pack (erupting
hero volcano centered via `worldOffsetX`, chapter-owned `cliffs_near` copy with a jagged-cut
skyline replacing the vendor sprite's flat crop top, `light MF` lava-glow wash, generated
`clouds_ash` drift strip), Volcano skin folder with the basalt/lava-joint floor, ember
particles + heat haze + lone vulture, burning-steppes menu art, burning-steppes A/B music
**Chapter 10, authored to PROGRESSION.md**: **Magma debuts at home** (10% everywhere — it
melts to terrain, more gift than hazard) paired with **Bomb** (4/3/4% — destructive,
single-digit band). Ch10 speeds (3.7→6.9 × mode multiplier). Difficulty carried by the
**floors** (Nick's call) and by the first wave set built on the **flat-rise principle**
(see the Height-Limit Waves section: rise-per-block must not grow across waves).
**Magma count inflation (Nick's catch)**: each magma melts into 4 pips that each count
as a placed block (the body is removed), so at magma rate m the counted-block rate is
×(1+3m). All counted targets here are scaled ×1.3 (100→130, waves ×1.3 with LINES
unchanged — pips are 1 cell per count, so the physical squeeze is identical) and ramps
derive from the inflated targets. Height goals are unaffected.
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Ashfall | GameMode_BurningClassic | Place 130 | Magma 10% + Bomb 4%. **Rift floor**: two plates tilting into a 1-col chasm left of spawn ([4,3,2,1] · gap · [1,2,3,4]), floating lava rock over the tall edge. 3.7→6.9 @ +0.027/blk (counted-block target). |
| Eruption Line | GameMode_BurningLaserLimit | Place 91 | Magma 10% + Bomb 3%. **Vent field** [2,·,3,·,·,·,3,·,2] — build in the bowl between +3 vents. 5 waves 12@6 · 16@10 · 18@13 · 20@17 · 25@21 (magma inflation loaded into the LATE waves — a small first wave can roll zero magmas, so it must fit as real pieces) (`HeightLimitWaves_BurningSteppes`): rises stay ~flat (4,3,4,4) while waves grow, so rise-per-block SHRINKS 0.31→0.25 — no late-wave relief window. 3.25→6.05 @ +0.034/blk. |
| Crater Climb | GameMode_BurningNarrow | Reach 85m | Magma 10% + Bomb 4%. **Climb out of a crater** [5,2,0,2,5] — +5 rims pinch the 5-col bowl, floating lava rock over the left rim. 3.5→6.55 @ +0.019/blk. |

**Chapter: Giza Dusk (sortOrder 110)** — imported Cyber Egypt pack (the pack's flying
pyramids extracted from the plate into two hovering sky layers `pyramid_small` +
`pyramid_big` (vP 0.8, desynced hover periods, sink slowly on the climb); chapter-owned `bg_dusk_ce` plate copy keeps the
sun but drops the baked-in fleet so it can't crop at the portrait edge; four silhouette
skyline bands + two hand-placed masses per side, pack `light under city` haze wash;
clouds and heat haze deliberately omitted — the hovering fleet carries the motion),
Egypt skin folder with sandstone ashlar floor, gold dust + heat haze + lone vulture;
menu art and music still to come
**Chapter 11, authored to PROGRESSION.md**: Pyramid stays the signature at its rare bag
rate (~3% — a big event, not a pattern). The opener becomes **Void Zones III**
(`VoidZones_Giza`: first zone 9m, every 7m, 90% — the meanest void tuning yet; the void +
pyramid dead-tops carry the difficulty). **No Airtight in this chapter by design** —
sealing rules around pyramid faces would be a triple screw (Nick). Ch11 speeds (3.8→7.2
× mode multiplier), floors themed to the monument skyline.
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Sphinx Road | GameMode_GizaClassic | Place 100 | **Void Zones III** + Pyramid bag (~3%). Processional road floor [3,·,1,1,1,1,1,·,3] with **hovering stones** at both edges (float pairs — echoes the backdrop's flying pyramids). 3.25→6.1 @ +0.032/blk (×0.85). |
| Obelisk Line | GameMode_GizaLaserLimit | Place 68 | Pyramid bag (~3%). **Broken obelisks** [·,4,·,·,·,·,·,4,·] with hovering tips (float pairs). 5 waves 12@6 · 14@10 · 14@13 · 14@17 · 14@21 (`HeightLimitWaves_GizaDusk`, flat-rise: flat block sizes + flat rises = no late relief). Scaling fixed to PerBlock (was None). 3.35→6.35 @ +0.049/blk. |
| Pyramid Climb | GameMode_GizaNarrow | Reach 90m | **Climb the great pyramid** [0,2,4,2,0] — 5-col stepped monument, spawn lands beside the peak. Deliberately NO pyramid brick (unchanged: 2-wide dead-top on narrow ground is brutal). 3.6→6.85 @ +0.025/blk. |

Giza's signature brick is the **Pyramid** (see BLOCKVARIANTS.md): a 3-column monument SHAPE
(`Block_Pyramid`, straight base course + pyramid top, non-rotatable, nothing rests on its
faces). The Giza mode assets are copies of the standard ones with every standard definition
four times in the bag + the pyramid once (≈1/29 ≈ 3% of spawns — Nick's call: a 3-wide
dead-top brick is a big event, keep it rare).

**Chapter: Amber Tide (sortOrder 120)** — imported Tropical Landscape pack (pink-amber
tropical sunset composed at the demo scene's proportions: baked-sun sky plate with the
`Glare TL` lens-flare bubbles as a companion layer (`sunEnabled` stays 0 — one sun rule),
the pack's own 30u cloud sheet drifting 0.12, two pale far ridges + two coral mid rows
with mirrored pan copies, magenta palm-jungle hills, darkest plum palm valleys sunk into
a matching plum fog; vendor `light TL` glow blob and `shadow TL` vignette deliberately
skipped — no pulse support yet / vendor-scene leftover), Tide skin folder with dusk-plum
shore cobbles under a coral sunset cap, warm dusk motes + a busy songbird flock,
amber-tide menu art (light top), amber-tide A/B music
**Chapter 12, authored to PROGRESSION.md**: a deliberate **breather chapter** — Vine
recurs at 12% (the campaign's friendliest brick, back from Ch1; welds help survive the
Ch12 pace) and nothing else. Signature mechanic still TBD (Nick will pick one later);
the interim identity is **nothing touches the ground** — floating-slab and archipelago
floors plus sky platforms (interval 4m @50%, the most island-forward chapter; OFF in the
climb). Ch12 speeds (4.0→7.4 ×
mode multiplier) — by now the speed IS the pressure.
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Palm Coast | GameMode_AmberClassic | Place 100 | Vine 12%. **One 2-wide slab pillar** (top +2, spawn-covering) and **one 1-wide monolith** (top +3) across a 1-col void — three landable columns total, each with a single bottom WINDOW (top slots removed: a hole above the landing surface reads as nonsense — Nick; the slotted-pillar look is the accepted rendering of "floating", see FLOORS.md on why true floating terrain isn't expressible). Sky platforms ON (they're the expansion room). 4.0→7.4 @ +0.038/blk. |
| Tide Line | GameMode_AmberLaserLimit | Place 68 | Vine 12%. **Archipelago** — three grounded islets at base heights 1/0/2 with 1-col channels (first multi-baseHeight floor). Sky platforms ON. 5 waves 13@6 · 14@10 · 14@14 · 14@18 · 13@21 (`HeightLimitWaves_AmberTide`, flat-rise). 3.5→6.5 @ +0.049/blk. |
| Sundown Climb | GameMode_AmberNarrow | Reach 95m | Vine 12% — welds are the climb's friend at this speed. Two-step shore [0,0,1,1]; sky platforms OFF (pure climb). 3.8→7.05 @ +0.024/blk. |

**Chapter: Monsoon Sector (sortOrder 130)** — imported TechnoCity Rain Mode pack (rainy
green cyber-city, composed by the deterministic method: each vendor layer folder shares
one ground line (far `d` −12.87, mid `c` −8.25, dark `b` −9.10, foreground −10.2), mapped
to our floor by a single constant so floorOffsetY = vendor_bottom + 9.4. Three building
rows placed DENSE and OVERLAPPING (cycling the folder's sprites at ~2.9u/3.9u steps with
x-jitter) so silhouettes merge into a continuous jagged skyline instead of isolated towers
with sharp vertical gaps; two stacked aprons make the depth gradient — a medium-green mist
apron on the mid row (top +1.15) and a darker apron on the near-dark row (top +0.3) that
takes over below the building bases, so the lower city fades light→dark→fog with no flat
light-green band. Giant foreground palms, tiled powerline strip, the pack's `road`
bridge/railing at the play-area base, palm-bush clusters straddling the rail line,
dissolving into a deep wet-teal fog. Layer count held to 35 — backdrop sorts `−89+i` and
the floor fill is −50, so ≤~36 layers or near ones render over the floor. Vendor `bg city2`
(a solid green RECTANGLE, not a skyline — its straight top was a persistent horizontal-line
artifact), the neon-car drive-by (built then cut, didn't fit — Nick's call), `f1-f4`
strips and `shadow` vignette all deliberately dropped. FIRST RAIN CHAPTER: the generic
particle-streak rain (`particleStreakLength` 0.75, `particleWindX` −1.3, fall 7.5, 90
streaks, `particlesInFront` so it falls over the scenery — see AMBIENCE.md toolkit) ships
with this chapter; vendor rain prefab kept for reference only. No flybys — rain carries the
motion, a flock in a downpour read wrong), Techno skin folder with wet neon pavement floor,
monsoon-sector menu art, monsoon-sector A/B music
**Chapter 13, authored to PROGRESSION.md**: the **poison chapter** — an Airtight opener
(rain-sealed city fits) with **Locked riding every level at just 3%**: the first hostile
brick inside an Airtight (Kvartal kept its debut clean on purpose; five chapters later the
combo IS the difficulty — a piece that won't rotate while you're trying to seal seams).
Ch13 speeds (4.2→7.7 × mode multiplier). `menuTopIsLight` fixed 1→0 (dark rainy art was
getting a dark-ink title).
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Palm Road | GameMode_MonsoonAirtight | Place 100 | **Airtight IV + Locked 3%**. Raised plaza centre [·,·,·,1,1,1,·,·,·]. 3.55→6.55 @ +0.033/blk (×0.85). |
| Wire Line | GameMode_MonsoonLaserLimit | Place 70 | Locked 3%. **Catenary dip floor** [3,2,1,0,0,0,1,2,3] — the sagging powerline. 5 waves 13@6 · 14@10 · 14@13 · 14@17 · 15@21 (`HeightLimitWaves_MonsoonSector`, flat-rise). 3.7→6.8 @ +0.049/blk. |
| Skyline Climb | GameMode_MonsoonNarrow | Reach 100m | Locked 3%. **Descending tower-block skyline** [5,3,1,0]. The campaign's first 100m goal. 4.0→7.3 @ +0.023/blk. |

**Chapter: Hallow's End (sortOrder 140)** — imported Halloween pack (blood-dusk graveyard,
composed at the demo scene's own proportions — sizes are scene px × scale, all rows share
one ground line with bases sunk into the fog; a first hand-eyeballed pass read half-empty
with apron seams. Ember sky plate, `light2` red horizon glow (soft-edged, single placement),
eclipse ring + halo upper-right (`sunEnabled` stays 0 — one sun rule), tiled far-mountain
band + masses, ruined church/farm/fence skyline ring, the three big `b1-b3` vine-canopy
masses as the midground wall, three 12u gnarled trees on the flanks, telephone-pole wire
strip (poles plant below the datum), graves-and-fence plate (its uniform fill colour ==
apron == `groundFogColor`, so it dissolves seamlessly), `cart_strip_hallow` — the pack's
pumpkin wagon rebuilt from car + 4 spoked wheels + glow at the demo scene's offsets,
drifting −1.35 u/s hitch-first on a 6144px mostly-empty strip (~36s per crossing), wheels
tucked behind the fence rows; chunky glowing jack-o'-lantern hedge capped at ~+0.8 so the
tower base stays readable; hanging-canopy `fgrnd`, white `cloud4` and the additive pumpkin
light sheets deliberately skipped), Hallow skin folder with graveyard cobble floor and
lantern-amber cap, ember motes + bat flock, hallows-end menu art, hallows-end A/B music
**Chapter 14, authored to PROGRESSION.md**: **Maw debuts** — 3% on the opener (it costs a
life per devour, the rarest-tier rule), absent from the puzzle and the void level (a
devour mechanic beside the line, or beside the void, muddies both — Nick/design), and then
the chapter's signature: **the Maw Sort** at 50% on the climb. Ch14 speeds (4.3→7.9 ×
mode multiplier).
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Pumpkin Patch | GameMode_HallowsClassic | Place 100 | **Maw debut @3%**. Washboard pumpkin-row floor [1,0,1,0,1,0,1,0,1]. 4.3→7.9 @ +0.04/blk. |
| Lantern Line | GameMode_HallowsLaserLimit | Place 72 | NO maw. **Gallows platform** [·,·,·,4,4,·,·,·,·] centre obstruction with mid-face niches. 5 waves 12@6 · 14@10 · 15@14 · 15@18 · 16@22 (`HeightLimitWaves_HallowsEnd`, flat-rise). 3.8→6.95 @ +0.049/blk. |
| Blood Moon Climb | GameMode_HallowsMawClimb | Reach 90m | **THE MAW SORT**: two 4-wide pillars at +3 (1-col void between), **Maw at 50%** — maws never eat each other and weld into a monolith, so you build a maw tower on one pillar and a real tower on the other, sorting every piece at 4.1→7.5. startingLives 2 (one misroute = a devour). @ +0.026/blk. |
| Void Zones | GameMode_HallowsVoidZones | Place 100 | Void Zones showcase (`VoidZones_Standard`, unchanged), now on a chapter-owned mode at Ch14 pace. NO maw. Flat floor. 3.65→6.7 @ +0.034/blk (×0.85). |

**Chapter: Crimson Core (sortOrder 150)** — the Blackout home chapter (dark crimson city,
chapter-owned pre-tinted retro-sun flipbook — 20 scrolling-band frames @ 12 fps, see
AMBIENCE.md/BACKDROPS.md/ASSET_IMPORTS.md). All three levels attach
`ScheduledStatus_Blackout_Standard` (first blackout after 45 s, then every 75 s,
graceBlocks 8 — see the Blackout game type above).
**Chapter 15, authored to PROGRESSION.md**: the **finale**. Blackout is the identity (all
three levels keep `ScheduledStatus_Blackout_Standard`); **Bomb joins** (4/3/4% — its fuse
glow is one of the few things that pierces the dark, a deliberate blackout synergy). Ch15
speeds (4.5→8.2 × mode multiplier × Blackout's 0.90 — the scheduled dark stacks its
fairness discount on every level). More per-level ideas to come (Nick).
| Level | Mode | Goal | Notes |
|---|---|---|---|
| Night Shift | GameMode_CrimsonClassic | Place 100 | Bomb 4%. **The Reactor**: two containment pylons — each a stub with a **2×2 slab floating above it** — separated by a **4-column void over the core** (centre platform removed, Nick: the gap IS the floor; an unsteered first drop falls into the reactor). 4.05→7.4 @ +0.037/blk. |
| Red Grid | GameMode_CrimsonLaserLimit | Place 72 | Bomb 3%. **Circuit-trace floor** [2,0,1,0,2,0,1,0,2] with outer ground-caves. 5 waves 12@5 · 14@9 · 15@13 · 15@17 · 16@21 (`HeightLimitWaves_CrimsonCore`, flat-rise, lowest opening line since Neon). 3.55→6.5 @ +0.046/blk. |
| Core Ascent | GameMode_CrimsonNarrow | Reach 110m | Bomb 4%. **The Core Shaft**: a 3-wide channel between +6 containment walls, a fragment floating over the left wall — the campaign's final wall, in the dark. 3.85→7.0 @ +0.02/blk. |

| Path | Contents |
|---|---|
| `Assets/Resources/Chapters/` | ChapterDefinition assets. **Must stay here** (loaded by path at runtime). |
| `Assets/Resources/Levels/` | LevelDefinition assets. **Must stay here** (loaded by path at runtime). |
| `Assets/Resources/GameModes/` | GameModeConfig assets used by levels. |
| `Assets/Data/Backdrops/` | BackdropPreset assets assigned by chapters. |
| `Assets/Data/LegacyLevels/` | Retired LevelDefinition assets kept for reference; not loaded by runtime menus. |
| `Assets/Data/Modifiers/` | Shared or chapter-specific LevelModifier assets. |
| `Assets/Art/Chapters/` | Chapter menu background images, referenced directly by ChapterDefinition. |
| `Assets/Data/Audio/Music/` | Chapter music clips, referenced directly by ChapterDefinition. |
| `Assets/Data/Audio/Source~/` | Ignored source audio exports, such as pre-conversion WAVs. |
| `Assets/Resources/Audio/Music/` | Menu music only, loaded by path. |
| `Assets/Resources/Skins/<Theme>/` | Runtime-loaded chapter skin sprites (`piece_*`, `plateau`, `island_*`, optional `laser`). |
| `Assets/SourceFiles/Scripts/Levels/Modifiers/` | LevelModifier behaviour classes (code). |
| `Assets/Data/Blocks/` | BlockData variant assets (Normal, Boulder, Anchor, ...). |
| `Assets/Data/BlockDefinitions/` | BlockDefinition assets — one per tetromino shape (Block_I ... Block_Z); these are what block bags list. |
| `Assets/Data/PowerUps/Common|Rare|Epic/` | AbilityDefinition assets, foldered by rarity (the asset's rarity **field** is what the game reads). |
| `Assets/Prefabs/Blocks/` | The 7 tetromino shape prefabs (I, O, T, S, Z, L, J). |
| `Assets/SourceFiles/Scripts/Blocks/Variants/` | BlockData base + behaviour subclasses (code, one per *behaviour*). |
| `Assets/SourceFiles/Scripts/Abilities/` | Ability code: `AbilityDefinition` + kind bases (`Kinds/`), behaviours (`Definitions/`), runtime effects (`Effects/`). |

---

## 2. Every level dial (GameModeConfig)

### Canonical Classic values (GameMode_Classic.asset — the "works perfectly" baseline)

| Group | Values |
|---|---|
| Round | lives **0** · fall speed **2** → cap **5** · scaling **PerBlock, Additive, +0.025/block** (OverTime alt: +0.1 per 60s) · spawnDelay **0** |
| Spawning | bag: **all 7 tetrominoes ×1 copy** · fallback variants: Normal · ambient variant rolls: **none** |
| Placement | gridSpacing **1** · placement buffer **3 columns** (effective steer reach is `max(buffer, 4)` — the widest block always fits past the edge; see PHYSICS.md reach guarantee) |
| Floor | 1 segment: center **0**, **9 columns** (Narrow: ~5) |
| Power-ups | choice every **10** blocks · pool: a broad set across rarities (~27 abilities; see GameMode_Classic) · slowMotionScale **0.5** |
| Islands | **enabled** · row interval 1 · chance 0.25 per side (floor-distance weighted) · first 9 · camera lead 2 · columns ±6, center clear 3 · shapes Single 12 / Two Wide 2 / Two Tall 2 / Corner 1 (details: §3 islands) |
| Camera | peak **0.5** · spawn **0.9** · zoom **15–24** · smooth **vert 0.28 / zoom 0.35 / horiz-follow 0.21 (code const)** · padding **1.5** (= column margin) · min Y **0** — follow camera: pans+zooms to frame floor/tower/nearby islands/active piece (safe area **0.78** is now unused by framing) |
| Physics ⚠️ contract — identical in every mode | grounded **0.03** · impact cap **2** · settle **0.08 / 8°s / 0.35s** · sleepOnLock **on** · microAlign **on, 0.08 / 4°** · maxControlTime **12** |

### Difficulty & pacing
| Setting | What it does |
|---|---|
| `startingLives` | Lives before game over. A life is charged the moment a falling block fully leaves the screen at the bottom (camera-relative cull in `LossZone` + `BlockController.IsLostBelow`) — never when it eventually reaches the world floor, and never for resting tower blocks below the camera. |
| `initialFallSpeed` | Descent speed at level start. |
| `speedIncreasePerBlock` / `difficultyScalingMode` | Ramp per placed block (or over time). |
| `maxFallSpeed` | Hard ceiling for the ramp — keeps long games playable. |
| `maxLandingImpactSpeed` | How hard blocks thump in. Keep at 2 (see PHYSICS.md) — difficulty should come from reaction time, not impact. |
| `spawnDelay` | Pause between lock and next spawn. Keep ~0. |

### Floor & play area
| Setting | What it does |
|---|---|
| `floorSegments` | The level's terrain: per-segment column span, base height, per-column height steps, carved nudge-in pockets — flat strips, stairs, valleys, pillars, side niches, all pure data. **Full field reference, worked examples and rules: [FLOORS.md](FLOORS.md) (binding).** |
| `gridSpacing` | Cell size. Leave at 1 unless everything else is retuned. |
| `horizontalPlacementBufferColumns` | How far past the tower/floor edge the player may steer. Floored in code at `BlockController.WidestBlockColumns` (4): the effective reach is `max(this, 4)` so the widest block (horizontal 1×4) can always slip down the outer side of any obstacle — block **or** sky island — and fall off. Islands count toward this reach (PHYSICS.md). |

### Blocks
| Setting | What it does |
|---|---|
| `blockBag` | Which BlockDefinitions are in play. **Exclude the L-piece by leaving it out.** `bagCopies` per definition weights frequency (3 copies of I = I-heavy level). Bag-randomised: every copy appears once before reshuffle, Tetris-style. |
| BlockDefinition → `defaultData` | The variant a shape spawns as by default. |
| `fallbackBlockDataVariants` | Random variant pool used when a definition has no default data. |
| `ambientBlockVariantChances` | **Level-flavour rolls**: list of (variant, chance). Example: Boulder at 0.03 → 3% of all spawns are Boulders. Stacks with power-up-granted chances. |

### Brick variants (Assets/Data/Blocks/)
Canonical catalog, looks & the "add a brick" recipe: **[BLOCKVARIANTS.md](BLOCKVARIANTS.md)**.
Level-design quick reference (polarity = help or hazard to the player):

| Variant | What it is | Polarity |
|---|---|---|
| Normal | Mass 1 baseline. | — |
| Anchor | Freezes exactly where it lands (player-made platform). Gunmetal look. | Positive |
| Vine | Welds instantly to everything it touches on landing, and creeps vines onto those blocks. Keeps the chapter colour. | Positive |
| Boulder | Mass 4 — strains everything below it; heavy landing slam. Dark-basalt look. | Negative |
| Feather | Mass 0.25 — shoved around by every later landing. Pale yellow. | Negative |
| Ice | Near-zero friction — slides off anything not flat. Pale cyan. | Negative |
| Locked | Cannot be rotated while falling. Orange. | Negative |
| Vortex | Left/right steering mirrored. Pink. | Negative |
| Tremor | Jolts the whole tower the moment it lands. Amber. | Negative |
| Bomb | Red-pulsing 1s fuse after landing, then deletes itself + every touching block (no blast impulse — the tower sags, not flies). Dark gray. | Negative |

Classic is vanilla (no ambient rolls) — special variants enter levels via
`ambientBlockVariantChances`; production levels should pick 1–2 signature variants each.
Keep mass between ~0.25 and 4 (Feather↔Boulder ≈ 16:1, near Box2D's ~10:1 mushiness threshold — don't widen it further; see PHYSICS.md).

New stat-only variants (different mass, friction material, control quirks via canRotate /
invertHorizontalControls, tint) are pure assets: right-click
> Create > Stacking > Blocks > Block Variant. Behaviour variants (act on land/spawn) need a
small subclass in `Scripts/Blocks/Variants/` overriding `OnApplied`/`OnLocked` — AnchorBlockData
is the 8-line template.

### Floating support islands (sky blocks)
**On in every campaign level.** Static 1x1 themed cells flanking the tower (Tricky
Towers' sky stones). Generation is **tower-driven**: every grid row up to
`spawnAheadHeight` above the tower's peak is rolled exactly once
(`StaticSupportIslandManager`); the camera only decides whether a newly-in-range row
pops in visibly or silently pre-exists off-screen. Each row rolls **independently per
side band** (the columns between the center clear lane and min/max column), producing
the two flanking stone lines from Tricky Towers. Each band is additionally clipped to
the **reachable range** (see the columns row below) so no island spawns where a piece
couldn't slip past it.

Under a height-limit waves level, generation is capped **1.5 cells below the line**
(`TowerHeightLimit.CeilingY`, published by HeightLimitWavesModifier once the line
settles; the margin means a block placed ON an island can't cross the line). When a
wave clears and the line finishes rising, the newly legal band **materializes on
screen**: staggered scale-in pops (`IslandPopFx` — visual child only, colliders are
full-size from frame one) + the `pop_01` sound.

| Setting | What it does |
|---|---|
| `staticSupportIslandsEnabled` | Master switch per level. **On in most modes; off in Hard and Narrow.** |
| `staticSupportIslandHeightInterval` | Meters between spawn rows (snapped to grid). Canonical **1** = every row. |
| `staticSupportIslandSpawnChance` | Chance per row PER SIDE, before floor-distance weighting. Canonical **0.25** ≈ a few stones per screen, almost all on the flanks (≈ half the Tricky Towers reference density out there). ⚠️ Playtested: 0.4 cluttered the narrow phone screen, 0.05 felt empty (whole games with 0–1 stones). |
| `staticSupportIslandFirstHeight` | Meters above the floor where generation starts (**9**) — the first screens of building stay completely clean. |
| `staticSupportIslandSpawnAheadHeight` | Generation lead above the **tower's peak** (**6**; SkyPlatforms **8**). Islands materialize with the laser-reveal pop (animation + sound) once the build climbs within this height of them — the sky ahead stays clean until you're nearly there. Keep below the spawn-line offset (~12 above the peak) so revealed islands are immediately landable. |
| `staticSupportIslandMin/MaxColumn`, `CenterClearColumns` | **±6, clear 3** → side bands of 5 columns each (2–6 from center): nothing in the falling lane. Bands are additionally clipped at spawn to the **reachable range** (`TryGetReachableColumnRange`, anchored to the floor centre at max zoom) so a piece can always slip ≥`WidestBlockColumns` (4) clear columns past any island and drop down its outer side. On a normal phone aspect the ±6 band fits inside this and nothing is clipped; very narrow screens trim the outermost column. The follow camera pans/zooms to keep islands and the steered piece framed. |
| *(code)* floor-width weighting | Within a band, columns are weighted by distance past the **floor's edge** (derived per mode from `floorSegments`): over the floor **×0.12**, first column beyond the edge **×0.5**, clear of it **×1**. Islands exist to grow wider than the floor — above the floor they'd just block the landing lane. A narrow floor therefore keeps full side density automatically. Constants: `StaticSupportIslandManager.OverFloorWeight` / `FloorEdgePlusOneWeight`. |
| `staticSupportIslandShapes` | Weighted clusters, authorable inline per mode. Canonical: **Single 12, Two Wide 2, Two Tall 2, Corner 1** — mostly lone stones, occasional pairs, rare 3-cell corner. |

Visuals: `Skins/<Chapter>/island_1..3.png` (see ART.md) — plateau-material 1x1 cells;
each spawn picks a random variant + random 90° rotation = 12 looks per chapter.

### Power-up choices
| Setting | What it does |
|---|---|
| `powerUpChoiceEveryBlocks` | Every N placed blocks: enters the `AbilityChoice` phase, pushes a pause owner, and offers 1 of 3. 0 disables for the level. |
| `powerUpChoicePool` | Which AbilityDefinitions can be offered (see **ABILITIES.md** for the full ability architecture: kinds, stacking, conditions, status effects, combo triggers). Per level — hard levels can ban abilities via `LevelDefinition.bannedAbilities`, gift levels can offer only Legendaries. Rarity weighting lives in `AbilityRarityProfile` (staged weights; `AbilityRarityInfo` owns only the rarity colours). |

Power-ups span Common/Rare/Epic (the full catalog lives in `Assets/Data/PowerUps/`; ABILITIES.md
documents the kinds and each shipped ability). Adding more: see the doc
comment on `AbilityDefinition.cs` — many new power-ups are zero-code: an
`ApplyVariantConsumable` asset pointing at any variant (tap → the falling brick becomes it), or a
`BlockVariantChancePowerUp` asset for a persistent chance (e.g. "Curse: Boulders" as a
negative offer; persistent positives like the old 20%-Anchors proved overpowered).

### Camera & leniency
| Setting | What it does |
|---|---|
| `towerPeakScreenY` | **The leniency dial.** Lower = more room between tower and spawn = more reaction time. 0.5 default, 0.58 Narrow (harder), range 0.35–0.9. |
| `spawnPointScreenY` | Where pieces spawn on screen (0.9). |
| `minimum/maximumCameraSize`, padding, smooth times | **Follow camera** (`TowerCameraController`): frames floor + nearby tower + nearby islands + the active piece, then **pans (X) and zooms** to fit, with `horizontalCameraPadding` as the visible column margin (≈1.5). `minimumCameraSize` doubles as the vertical reaction-room floor — lowering it zooms in but cuts reaction time. Horizontal-follow responsiveness is the code const `HorizontalFollowSmoothTime` (0.21s), separate from vertical (`cameraSmoothTime`) and zoom (`cameraZoomSmoothTime`). `horizontalCameraSafeArea` is no longer used by framing. |

### Physics dials
Also serialized per mode (settle thresholds, micro-align caps, grounded distance...). These are
**not** difficulty dials — they're the stability contract. Read [PHYSICS.md](PHYSICS.md) first;
in practice, keep them identical across modes.

---

## 3. Recipes

**New level:** duplicate a GameModeConfig in `Resources/GameModes/`, tweak dials → duplicate a
LevelDefinition in `Resources/Levels/`, name it, point it at the mode, set a goal → add it to
a chapter's `levels` array at the position it should play. The menu groups by chapter automatically.

**New chapter (complete recipe):**
1. ChapterDefinition in `Resources/Chapters/` (Create > Stacking > Levels > Chapter Definition):
   `sortOrder` (leave gaps), `levels` list, `skinFolder`, `backdrop`, `musicPlaylist`.
2. Backdrop: BackdropPreset in `Data/Backdrops/` (Create > Stacking > Levels > Backdrop
   Preset) — sky color pairs + altitude fade, cloud style/count/drift, hills on/off +
   style, ambient particles. No preset = classic dark sky. Best workflow: give Claude an
   inspiration image; palette and mood translate directly into preset values.
3. Skin: a preset per generator (`Tools/generate_piece_sprites.py`,
   `generate_ground_sprite.py`) writing to `Resources/Skins/<Chapter>/`; set the chapter's
   `skinFolder`. Missing files fall back to Classic file-by-file, so a ground-only skin
   is fine.
4. Music: 1–2 tracks in `Assets/Data/Audio/Music/`, dragged onto `musicPlaylist`
   (random opener, then rotating; survives level restarts, stops on game over).
   Specs in ART.md.
5. Levels: per the "New level" recipe, each with a one-sentence `instruction`.
   Locks/unlocks and menu placement come automatically from `sortOrder` + completion.
6. Run `Tools > MadTowers > Validate Chapter Content` before committing. Fix errors;
   warnings are intentional review prompts (for example, WAV music or orphan levels).

## Floor terrain (see FLOORS.md — binding)

Floors are pure data on the mode asset: `floorSegments` = per-segment column span,
base height, per-column height steps, and carved nudge-in pockets — flat strips, stairs,
valleys, free-standing pillars, side niches. Per-chapter looks and the fog are generated.
Randomized floors: `ProceduralFloorModifier` (constraints in, fresh layout per run).
**The complete authoring guide, field reference, worked examples, physics contract and
procedural recipe live in [FLOORS.md](FLOORS.md)** — point any new-level work there.

**"1-grid floor, stack 5" level:** mode with `floorSegments: columnCount 1` + level with
`targetType: PlaceBlocks`, `targetValue: 5`. Pure settings — no code.

**Exclude the L-piece:** in the mode's `blockBag`, delete the Block_L definition entry. Done.

**3% Boulders on a hard level:** mode's `ambientBlockVariantChances` → add element:
Variant = `Data/Blocks/Boulder`, ChancePerBlock = 0.03.

**New power-up, zero code:** duplicate an asset in `Data/PowerUps/<Rarity>/`, change fields
(e.g. a second BlockVariantChance asset granting 35% Anchor as an Epic), add to a mode's pool.

**New power-up with new behaviour:** subclass the right ability *kind* (Instant / Consumable /
Passive / Combo) in `Scripts/Abilities/Definitions/`, override the hooks it needs (the context is
`AbilityContext` — extend it, not the signatures, if you need more of the game), create the asset,
add to pools. See ABILITIES.md for the full recipe.

**New brick behaviour:** subclass `BlockData` in `Scripts/Blocks/Variants/`, override
`OnApplied` (at spawn) or `OnLocked` (at landing), create the asset. Reach it via a bag
default, ambient chance, or a power-up.

---

## 4. Level-modifier idea backlog

Already possible with today's data (no code):

- **Piece-diet levels** — only S/Z pieces (pain), only I/O (zen), double bag copies of one shape.
- **Ice level** — variant with a slippery PhysicsMaterial2D at high ambient chance + lower floor friction.
- **Heavy industry** — Boulder as the default data for every definition; landing rhythm changes completely.
- **Two towers** — two floor segments with a gap; islands disabled; narrow camera.
- **Gift run** — power-up choice every 5 blocks, pool of Epics only.
- **Hardcore** — `powerUpChoiceEveryBlocks 0`, peak at 0.7, fast ramp, 3% Boulders, no islands.
- **Platform hopper** — tiny 3-wide floor, very frequent wide islands: the tower must live on platforms.

Needs code (rough effort, all fit the existing hooks):

- ~~**Chapter/level unlock persistence**~~ — done: `ProgressStore` (local JSON, cloud sync
  live — see DATA.md) + `Campaign` lock rules + menu locks/checkmarks/personal bests.
- **Wind gusts** (small) — a LevelModifier like Earthquake but with telegraphed directional pushes. Watch PHYSICS.md I1: forces only, never positions.
- ~~**Bomb brick**~~ — done: Bomb variant (1s fuse, chain-deletes touching blocks). Use via ambient chance or a cursed power-up.
- **Brittle brick** (medium) — breaks into single cells when load exceeds a threshold.
- ~~**Earthquake events**~~ — done: `EarthquakeModifier` (interval, jolt strength, grace blocks). Add its asset to any level.
- ~~**Win conditions**~~ — done: per-level targets (PlaceBlocks / ReachHeight) with completion screen and next-level progression.
- **Starting tower** (small) — pre-placed blocks/obstacle layout spawned at level start (data: list of cell positions, like island shapes).
- **Checkpoint heights** (small) — every X meters: +1 life or a bonus choice. Height tracking now works (measured from the floor).
- **Per-level rarity weight override** (tiny) — make Legendaries common on gift levels.
- **Fog ceiling** (small, visual) — darkness above a height; build into the unknown.

A good campaign curve mixes one *pressure* dial (speed, camera, Boulders) with one *relief*
dial (more choices, wider floor, islands) per tier, rather than turning everything at once.

---

*Update this file when settings or systems are added — it is the designer-facing index of
what exists. Physics stability rules live in PHYSICS.md and win every conflict.*
