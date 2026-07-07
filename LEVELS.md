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
| **Height-Limit Waves** ("Laser Limit" — Tricky Towers' puzzle mode) | `HeightLimitWavesModifier` asset on the level | `PlaceBlocks` = sum of wave counts |
| **Scheduled theme pressure** (snowstorms, sandstorms, neon sync, rain...) | `ScheduledStatusModifier` applying one or more `StatusEffectDefinition` assets | any standard goal |
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
4. Validate wiring in `OnLevelStart` (warn loudly if the level's goal doesn't match).
5. Document it here and add a catalog row.

#### Height-Limit Waves details

- Blocks arrive in **waves**; the whole tower must stay under a glowing **limit line**.
- Clearing a wave's block count makes the line **glide up** and the next, bigger wave begins.
- A **landed** block crossing the line is **zapped** (destroyed) and costs a life through the
  normal lives/GameOver flow. The falling piece passes the line freely (it spawns above it).
- Wire-up: pair the modifier with `targetType: PlaceBlocks`, `targetValue: <sum of wave
  counts>` — clearing the last wave completes the level via the normal goal system, and
  "Keep Building" continues as a free endless run (line disappears). A console warning
  fires at level start if the goal doesn't match the waves.
- **Per-level difficulty = per-level modifier assets**: duplicate
  `Data/Modifiers/HeightLimitWaves_Standard`, change its waves (count, sizes, line
  heights), assign to another level. Lower starting line / smaller rises / bigger waves =
  harder puzzle. Mode dials and other modifiers stack on top (icy laser level, earthquake
  laser level...).
- Tuning knobs on the asset: `waves[]` (blockCount + lineHeightAboveFloor), `lineRiseSeconds`,
  and the laser style — `lineColor`, `lineThickness`, `lineBaseAlpha`, `linePulseAmount`,
  `linePulseSpeed`. Defaults: **6 @ 5m → 10 @ 8m → 15 @ 13m → 21 @ 20m** (52 blocks total) —
  the rises follow ~3 blocks per meter, so late waves force building wider than the floor
  without becoming unfair. Retune if the floor width changes. A countdown rides the right
  end of the line showing blocks left until it rises.
- Laser **art** follows the active chapter automatically: drop a `laser.png` into
  `Resources/Skins/<Chapter>/` (see ART.md) and every laser level in that chapter uses it;
  no file = the code-built bar. Zapped blocks burst via the reusable `BlockShatterFx`
  (shards tinted to the laser color) plus a subtle camera impact.

### Campaign structure & progression

The game is a campaign of chapters: chapters unlock in `sortOrder` once the
previous chapter's levels are ALL completed; levels within a chapter unlock sequentially.
Rules live in `Campaign.cs` (read-side only); completions and personal bests persist via
`ProgressStore` (see **DATA.md** for the persistence architecture and cloud-sync plan).
A chapter with `alwaysUnlocked: true` is a sandbox — always playable, never gates the
campaign. The menu shows chapters as a carousel (one chapter per screen).
`Campaign.UnlockAllForTesting` is **off by default everywhere** so editor testing exercises
real progression. Add the `MADTOWERS_UNLOCK_ALL` scripting define only when a temporary
local build truly needs every chapter open.
Each chapter's `skinFolder` drives all generated art (blocks/ground/laser) via
`ChapterSkins`; empty = Classic skin.

### Current level inventory

**Chapter: Sakura Ridge (sortOrder 10)** — imported Japan Landscape menu art, layered Japan
gameplay backdrop, Japan skin folder, sakura-ridge A/B music
| Level | Mode | Goal | Notes |
|---|---|---|---|
| Morning Gate | GameMode_Classic | Place 100 | Stacking endurance, Japan dressing. |
| Lantern Drift | GameMode_LaserLimit | Place 50 | Height-limit waves (5 waves, standard asset). |
| Temple Steps | GameMode_Narrow3 | Reach 50m | 3-column floor climb. |

**Chapter: Barren Lands (sortOrder 20)** — imported Desert Vibe menu art, layered desert
gameplay backdrop, desert skin, desert A/B music
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Mirage | GameMode_Classic | Place 100 | Stacking endurance, desert dressing. |
| Sandswept Path | GameMode_LaserLimit | Place 50 | Height-limit waves (5 waves, standard asset). |
| Rising Dunes | GameMode_Narrow3 | Reach 50m | 3-column floor climb. |

**Chapter: Jungle Depths (sortOrder 30)** — imported Jungle Landscape menu art, layered
jungle gameplay backdrop, jungle skin folder, jungle A/B music
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Undergrowth | GameMode_JungleClassic | Place 125 | Faster classic variant; fewer placement buffer columns; choices every 12 blocks. |
| Canopy Trial | GameMode_JungleLaserLimit | Place 65 | Height-limit waves with larger waves and fewer lives. |
| Vine Ascent | GameMode_JungleNarrow3 | Reach 60m | Faster 3-column climb with denser side islands and stricter camera framing. |

**Chapter: Frozen Peaks (sortOrder 40)** — imported Winter Mountain Landscape gameplay
backdrop (+ generated `clouds_winter` drift strip), Winter skin folder, frozen-peaks
menu art, frozen-peaks A/B music
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Snowline | GameMode_Classic | Place 100 | Stacking endurance, winter dressing. |
| Whiteout Pass | GameMode_LaserLimit | Place 50 | Height-limit waves (5 waves, standard asset). |
| Summit Climb | GameMode_Narrow3 | Reach 50m | 3-column floor climb. |

**Chapter: Fangkuai District (sortOrder 50)** — imported Chinese City gameplay backdrop
(feathered `bg_dusk` plate + generated `clouds_dusk` drift strip), Fangkuai skin folder,
fangkuai-district menu art, fangkuai-district A/B music
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Night Market | GameMode_Classic | Place 100 | Stacking endurance, dusk-city dressing. |
| Firecracker Alley | GameMode_LaserLimit | Place 50 | Height-limit waves (5 waves, standard asset). |
| Pagoda Climb | GameMode_Narrow3 | Reach 50m | 3-column floor climb. |

**Chapter: Kvartal 4 (sortOrder 60)** — imported Sovietwave Panel Buildings pack as a
hand-placed night skyline (individual panelka sprites + treeline/fence strips + pack moon,
generated `clouds_night` drift strip), Kvartal skin folder, kvartal-4 menu art,
kvartal-4 A/B music
| Level | Mode | Goal | Notes |
|---|---|---|---|
| Panelka Row | GameMode_Classic | Place 100 | Stacking endurance, sovietwave night dressing. |
| Curfew Line | GameMode_LaserLimit | Place 50 | Height-limit waves (5 waves, standard asset). |
| Antenna Climb | GameMode_Narrow3 | Reach 50m | 3-column floor climb. |

**Chapter: Neon Nightfall (sortOrder 70)** — imported Glowing City pack as a waterfront
skyline (three hand-placed building bands + far strip, generated `water_neon` band, two
drifting boat strips, fairy-light promenade + vine-fence foreground), Neon skin folder
with the wet-pavement floor, neon-nightfall menu art, neon-nightfall A/B music
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Waterfront | GameMode_Classic | Place 100 | Stacking endurance, glowing-city dressing. |
| Voltage Line | GameMode_LaserLimit | Place 50 | Height-limit waves (5 waves, standard asset). |
| Penthouse Run | GameMode_Narrow3 | Reach 50m | 3-column floor climb. |

**Chapter: Burning Steppes (sortOrder 80)** — imported 2D Volcano Landscape pack (erupting
hero volcano centered via `worldOffsetX`, chapter-owned `cliffs_near` copy with a jagged-cut
skyline replacing the vendor sprite's flat crop top, `light MF` lava-glow wash, generated
`clouds_ash` drift strip), Volcano skin folder with the basalt/lava-joint floor, ember
particles + heat haze + lone vulture, burning-steppes menu art, burning-steppes A/B music
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Ashfall | GameMode_Classic | Place 100 | Stacking endurance, caldera dressing. |
| Eruption Line | GameMode_LaserLimit | Place 50 | Height-limit waves (5 waves, standard asset). |
| Crater Climb | GameMode_Narrow3 | Reach 50m | 3-column floor climb. |

**Chapter: Giza Dusk (sortOrder 90)** — imported Cyber Egypt pack (the pack's flying
pyramids extracted from the plate into two hovering sky layers `pyramid_small` +
`pyramid_big` (vP 0.8, desynced hover periods, sink slowly on the climb); chapter-owned `bg_dusk_ce` plate copy keeps the
sun but drops the baked-in fleet so it can't crop at the portrait edge; four silhouette
skyline bands + two hand-placed masses per side, pack `light under city` haze wash;
clouds and heat haze deliberately omitted — the hovering fleet carries the motion),
Egypt skin folder with sandstone ashlar floor, gold dust + heat haze + lone vulture;
menu art and music still to come
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Sphinx Road | GameMode_Classic | Place 100 | Stacking endurance, dusk-Giza dressing. |
| Obelisk Line | GameMode_LaserLimit | Place 50 | Height-limit waves (5 waves, standard asset). |
| Pyramid Climb | GameMode_Narrow3 | Reach 50m | 3-column floor climb. |

**Chapter: Lost City (sortOrder 100)** — imported Lost City / Distant Planet pack (giant-moon
plate as a chapter-owned `bg_moon_lc` copy with the below-skyline half flattened to clean fog
— the vendor plate's moon bottom + fog-band edges expose as tone rectangles mid-climb in
portrait; eight-band teal/orange depth ladder LC1→LC8 used in full, pack streak-cloud strip
drifting), LostCity skin folder with slate-teal cobble ruin floor, pale teal motes + 2 night
birds, lost-city menu art, lost-city A/B music
| Level | Mode | Goal | Notes |
|---|---|---|---|
| The Oasis Gate | GameMode_Classic | Place 100 | Stacking endurance, alien-moon dressing. |
| Aqueduct Line | GameMode_LaserLimit | Place 50 | Height-limit waves (5 waves, standard asset). |
| Monolith Climb | GameMode_Narrow3 | Reach 50m | 3-column floor climb. |

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

- ~~**Chapter/level unlock persistence**~~ — done: `ProgressStore` (local JSON, cloud-sync
  ready — see DATA.md) + `Campaign` lock rules + menu locks/checkmarks/personal bests.
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
