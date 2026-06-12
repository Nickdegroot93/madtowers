# MadTowers Level Design Guide

How levels, game modes, bricks, and power-ups fit together, every dial you can turn, and
recipes for common authoring tasks. **Everything in this document is data — no programmer
needed** unless a row says "needs code". Physics tuning dials have their own contract in
[PHYSICS.md](PHYSICS.md); read that before touching anything under its "frozen" list.

---

## 1. The data model (what references what)

```
ThemeDefinition  (Assets/Resources/Themes/  — an Archero-style chapter)
 ├─ sortOrder (themes play lowest-first; leave gaps: 10, 20, 30...)
 ├─ presentation shared by its levels: backdrop (BackdropPreset: layered sky/clouds/
 │   hills/particles - see ART.md §3), musicPlaylist (random opener, then rotating;
 │   stops on game over),
 │   skinFolder (generated art; missing files fall back to Classic)
 ├─ featuredUnlocks: power-ups introduced by this theme (messaging; availability is
 │   authored per level pool)
 └─ levels: ordered list of LevelDefinitions — any count per theme

LevelDefinition  (Assets/Resources/Levels/  — one per level)
 ├─ identity: display name (presentation lives on the theme)
 ├─ instruction: one-sentence goal banner shown (fade in/out) at level start
 ├─ GOAL: targetType (Endless | PlaceBlocks | ReachHeight) + targetValue.
 │   Reaching it pauses and shows Level Complete with "Next: <level>" (next in theme),
 │   Keep Building, and Replay.
 ├─ modifiers: LevelModifier assets — custom behaviour beyond settings (see below)
 └─ GameModeConfig  (Assets/Resources/GameModes/  — the entire rule set)
     ├─ Difficulty: fall speed start/ramp/cap, lives, spawn delay
     ├─ Floor: segments (position + width — gaps and multiple towers possible)
     ├─ Block bag: which BlockDefinitions are in play, and how many copies each
     │    └─ BlockDefinition → shape prefab + default BlockData variant
     ├─ Ambient variant chances: % rolls that replace spawns with a variant
     ├─ Sky platforms (static support islands): on/off, frequency, shapes, columns
     ├─ Power-up choices: cadence (every N blocks) + pool of PowerUpDefinitions
     ├─ Camera: leniency (reaction room), zoom limits
     └─ Physics dials (see PHYSICS.md before changing)

BlockData variants     (Assets/Data/Blocks/    — one asset per brick type)
PowerUpDefinitions     (Assets/Data/PowerUps/<Rarity>/ — one asset per power-up)
```

Key separation: **LevelDefinition = identity + look. GameModeConfig = all rules.** Two levels
can share one mode; a level 1→100 campaign is 100 LevelDefinitions pointing at progressively
meaner GameModeConfigs (or fewer shared ones).

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

Rule of thumb: if two levels could ever want it with different numbers, it's a modifier with
serialized fields, not a one-off hack.

### Level types (the catalog)

A level *type* is a different way to play — not just harder numbers. Mechanically, a type
is **a GameModeConfig flavour + (optionally) a LevelModifier that owns the special rule and
its visuals**, with winning expressed through the standard goal system. New types never
touch engine code.

| Type | Assembled from | Win condition |
|---|---|---|
| **Classic stacking** | any mode, no modifiers — the base game | `Endless` (free play), `ReachHeight` (climb to X m), or `PlaceBlocks` (stack N) — three sub-flavours for free |
| **Height-Limit Waves** ("Laser Limit" — Tricky Towers' puzzle mode) | `HeightLimitWavesModifier` asset on the level | `PlaceBlocks` = sum of wave counts |
| *future: rising water, timed rush, wind gauntlet…* | one modifier subclass each, same recipe | standard goals |

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
- Laser **art** follows the active theme automatically: drop a `laser.png` into
  `Resources/Skins/<Theme>/` (see ART.md) and every laser level in that theme uses it;
  no file = the code-built bar. Zapped blocks burst via the reusable `BlockShatterFx`
  (shards tinted to the laser color) plus a subtle camera impact.

### Campaign structure & progression

The game is a campaign of themes (chapters): themes unlock in `sortOrder` once the
previous theme's levels are ALL completed; levels within a theme unlock sequentially.
Rules live in `Campaign.cs` (read-side only); completions and personal bests persist via
`ProgressStore` (see **DATA.md** for the persistence architecture and cloud-sync plan).
A theme with `alwaysUnlocked: true` is a sandbox — always playable, never gates the
campaign (that's Testing Grounds, parked at sortOrder 1000). The menu shows campaign
themes as a carousel (one theme per screen, arrows cycle); sandbox + unthemed levels
live behind the "Test Levels" button.
⚠️ `Campaign.UnlockAllForTesting` is currently **true** (everything playable while
building content) — flip to false before release.
Each theme's `skinFolder` drives all generated art (blocks/ground/laser) via
`ThemeSkins`; empty = Classic skin.

### Current level inventory

**Theme: Training Wheels (sortOrder 10)**
| Level | Mode | Goal | Notes |
|---|---|---|---|
| Foundations | GameMode_Classic | Place 100 | Plain stacking endurance. |
| Under Pressure | GameMode_LaserLimit | Place 52 | Height-limit waves (4 waves, standard asset). |
| The Spire | GameMode_Spire | Reach 10m | 4-column floor climb. |

**Theme: Desert (sortOrder 20)** — sunset gradient with sky shimmer, faint sun revealed
while climbing, streak clouds, rolling dunes at ground level
| Level | Mode | Goal | Notes |
|---|---|---|---|
| Dunes | GameMode_Classic | Place 100 | Stacking endurance, desert dressing. |
| Mirage | GameMode_LaserLimit | Place 52 | Height-limit waves. |
| The Obelisk | GameMode_Spire | Reach 10m | 4-column floor; uses the narrow ziggurat variant. |

**Theme: Testing Grounds (sortOrder 1000, alwaysUnlocked)** — sandboxes, not part of the campaign
| Level | Mode asset | Goal | What's different |
|---|---|---|---|
| Classic | GameMode_Classic | Reach 10m | The baseline (canonical values in §2). |
| Narrow Tower | GameMode_Narrow | Endless | ~5-column floor, slightly faster, stricter camera. |
| Sky Platforms | GameMode_SkyPlatforms | Endless | Island sandbox — same island settings as Classic now (islands are on everywhere). |
| Hard Mode | GameMode_Hard | Reach 12m | Classic + pressure: fall 2.6 → cap 6.5, ramp 0.04/block, 7-column floor, peak 0.65, buffer 2, power-ups every 15, ambient **Ice 8% / Heavy 6% / Dizzy 4% / Stubborn 4%**. Physics contract untouched. |
| Laser Limit | GameMode_LaserLimit | Place 52 | **Height-limit waves type** (above). No speed ramp, no power-up pauses, 2 lives, classic 9-column floor. |

| Path | Contents |
|---|---|
| `Assets/Resources/Themes/` | ThemeDefinition assets. **Must stay here** (loaded by path at runtime). |
| `Assets/Resources/Levels/` | LevelDefinition assets. **Must stay here** (loaded by path at runtime). |
| `Assets/Resources/GameModes/` | GameModeConfig assets used by levels. |
| `Assets/SourceFiles/Scripts/Levels/Modifiers/` | LevelModifier behaviour classes (code). |
| `Assets/Data/Blocks/` | BlockData variant assets (Normal, Heavy, Anchor, ...). |
| `Assets/Data/BlockDefinitions/` | BlockDefinition assets — one per tetromino shape (Block_I ... Block_Z); these are what block bags list. |
| `Assets/Data/PowerUps/Common|Rare|Epic/` | PowerUpDefinition assets, foldered by rarity (the asset's rarity **field** is what the game reads). |
| `Assets/Prefabs/Blocks/` | The 7 tetromino shape prefabs (I, O, T, S, Z, L, J). |
| `Assets/SourceFiles/Scripts/Blocks/Variants/` | BlockData base + behaviour subclasses (code, one per *behaviour*). |
| `Assets/SourceFiles/Scripts/PowerUps/Definitions/` | Power-up behaviour classes (code). |

---

## 2. Every level dial (GameModeConfig)

### Canonical Classic values (GameMode_Classic.asset — the "works perfectly" baseline)

| Group | Values |
|---|---|
| Round | lives **0** · fall speed **2** → cap **5** · scaling **PerBlock, Additive, +0.025/block** (OverTime alt: +0.1 per 60s) · spawnDelay **0** |
| Spawning | bag: **all 7 tetrominoes ×1 copy** · fallback variants: Normal, Heavy · ambient variant rolls: **none** |
| Placement | gridSpacing **1** · placement buffer **3 columns** |
| Floor | 1 segment: center **0**, **9 columns** (Narrow: ~5) |
| Power-ups | choice every **10** blocks · pool: Extra Life, Slow Time, Anchor Brick, Cement Tower · slowMotionScale **0.5** |
| Islands | **enabled** · row interval 1 · chance 0.25 per side (floor-distance weighted) · first 9 · camera lead 2 · columns ±6, center clear 3 · shapes Single 12 / Two Wide 2 / Two Tall 2 / Corner 1 (details: §3 islands) |
| Camera | peak **0.5** · spawn **0.9** · zoom **15–24** · smooth **0.28 / 0.35** · padding **1.5** · safe area **0.78** · min Y **0** |
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
| `floorSegments` | List of (centerColumn, columnCount). One wide segment = classic. One narrow = Narrow mode. **Multiple segments = islands with gaps / build two towers.** |
| `gridSpacing` | Cell size. Leave at 1 unless everything else is retuned. |
| `horizontalPlacementBufferColumns` | How far past the tower/floor edge the player may steer. |

### Blocks
| Setting | What it does |
|---|---|
| `blockBag` | Which BlockDefinitions are in play. **Exclude the L-piece by leaving it out.** `bagCopies` per definition weights frequency (3 copies of I = I-heavy level). Bag-randomised: every copy appears once before reshuffle, Tetris-style. |
| BlockDefinition → `defaultData` | The variant a shape spawns as by default. |
| `fallbackBlockDataVariants` | Random variant pool used when a definition has no default data. |
| `ambientBlockVariantChances` | **Level-flavour rolls**: list of (variant, chance). Example: Boulder at 0.03 → 3% of all spawns are Boulders. Stacks with power-up-granted chances. |

### Brick variants (Assets/Data/Blocks/)
| Variant | What it is | Polarity |
|---|---|---|
| Normal | Mass 1 baseline. | — |
| Heavy | Mass 3, dark tint. | Neutral |
| Anchor | Freezes exactly where it lands (player-made platform). Blue. | Positive |
| Vine | Welds itself to everything it touches shortly after landing (breakable joints — local cement). Green. | Positive |
| Boulder | Mass 4 — strains everything below it. Brown. | Negative |
| Feather | Mass 0.4 — shoved around by every later landing. Pale yellow. | Negative |
| Ice | Near-zero friction — slides off anything not flat. Pale cyan. | Negative |
| Stubborn | Cannot be rotated while falling. Orange. | Negative |
| Dizzy | Left/right steering mirrored. Pink. | Negative |
| Tremor | Jolts the whole tower the moment it lands. Amber. | Negative |
| Bomb | Red-pulsing 1s fuse after landing, then deletes itself + every touching block (no blast impulse — the tower sags, not flies). Dark gray. | Negative |

Classic is vanilla (no ambient rolls) — special variants enter levels via
`ambientBlockVariantChances`; production levels should pick 1–2 signature variants each.
Keep mass between 0.4 and 4: Box2D contacts go mushy past ~10:1 ratios between touching blocks.

New stat-only variants (different mass, friction material, control quirks via canRotate /
invertHorizontalControls, tint) are pure assets: right-click
> Create > Stacking > Blocks > Block Variant. Behaviour variants (act on land/spawn) need a
small subclass in `Scripts/Blocks/Variants/` overriding `OnApplied`/`OnLocked` — AnchorBlockData
is the 8-line template.

### Floating support islands (sky blocks)
**On in every campaign level.** Static 1x1 themed cells flanking the tower (Tricky
Towers' sky stones). Generation is **camera-driven**: every grid row up to
`spawnAheadHeight` above the visible screen top is rolled exactly once
(`StaticSupportIslandManager`), so islands always exist before they scroll into
view — no pop-in during endless play. Each row rolls **independently per side band**
(the columns between the center clear lane and min/max column), producing the two
flanking stone lines from Tricky Towers.

Under a height-limit waves level, generation is capped **1.5 cells below the line**
(`TowerHeightLimit.CeilingY`, published by HeightLimitWavesModifier once the line
settles; the margin means a block placed ON an island can't cross the line). When a
wave clears and the line finishes rising, the newly legal band **materializes on
screen**: staggered scale-in pops (`IslandPopFx` — visual child only, colliders are
full-size from frame one) + the `pop_01` sound.

| Setting | What it does |
|---|---|
| `staticSupportIslandsEnabled` | Master switch per level. **On in Classic, LaserLimit, Spire, SkyPlatforms.** |
| `staticSupportIslandHeightInterval` | Meters between spawn rows (snapped to grid). Canonical **1** = every row. |
| `staticSupportIslandSpawnChance` | Chance per row PER SIDE, before floor-distance weighting. Canonical **0.25** ≈ a few stones per screen, almost all on the flanks (≈ half the Tricky Towers reference density out there). ⚠️ Playtested: 0.4 cluttered the narrow phone screen, 0.05 felt empty (whole games with 0–1 stones). |
| `staticSupportIslandFirstHeight` | Meters above the floor where generation starts (**9**) — the first screens of building stay completely clean. |
| `staticSupportIslandSpawnAheadHeight` | Generation lead above the **tower's peak** (**6**; SkyPlatforms **8**). Islands materialize with the laser-reveal pop (animation + sound) once the build climbs within this height of them — the sky ahead stays clean until you're nearly there. Keep below the spawn-line offset (~12 above the peak) so revealed islands are immediately landable. |
| `staticSupportIslandMin/MaxColumn`, `CenterClearColumns` | **±6, clear 3** → side bands of 5 columns each (2–6 from center): nothing in the falling lane, nothing far out. Off-screen columns are fine — the camera zooms out as the tower widens. |
| *(code)* floor-width weighting | Within a band, columns are weighted by distance past the **floor's edge** (derived per mode from `floorSegments`): over the floor **×0.12**, first column beyond the edge **×0.5**, clear of it **×1**. Islands exist to grow wider than the floor — above the floor they'd just block the landing lane. A narrow Spire floor therefore keeps full side density automatically. Constants: `StaticSupportIslandManager.OverFloorWeight` / `FloorEdgePlusOneWeight`. |
| `staticSupportIslandShapes` | Weighted clusters, authorable inline per mode. Canonical: **Single 12, Two Wide 2, Two Tall 2, Corner 1** — mostly lone stones, occasional pairs, rare 3-cell corner. |

Visuals: `Skins/<Theme>/island_1..3.png` (see ART.md) — plateau-material 1x1 cells;
each spawn picks a random variant + random 90° rotation = 12 looks per theme.

### Power-up choices
| Setting | What it does |
|---|---|
| `powerUpChoiceEveryBlocks` | Every N placed blocks: full pause + pick 1 of 3. 0 disables for the level. |
| `powerUpChoicePool` | Which PowerUpDefinitions can be offered. Per level — hard levels can ban Cement Tower, gift levels can offer only Legendaries. Rarity weighting (Common 100 / Rare 40 / Epic 15 / Legendary 5) lives in `PowerUpRarityInfo`. |

Current power-ups: Extra Life (Common), Slow Time (Common), Anchor Brick (Rare — your NEXT
brick becomes an Anchor, one brick only), Cement Tower (Epic). Adding more: see the doc
comment on `PowerUpDefinition.cs` — many new power-ups are zero-code: a
`NextBlockVariantPowerUp` asset pointing at any variant (one-shot), or a
`BlockVariantChancePowerUp` asset for a persistent chance (e.g. "Curse: Boulders" as a
negative offer; persistent positives like the old 20%-Anchors proved overpowered).

### Camera & leniency
| Setting | What it does |
|---|---|
| `towerPeakScreenY` | **The leniency dial.** Lower = more room between tower and spawn = more reaction time. 0.5 default, 0.58 Narrow (harder), range 0.35–0.9. |
| `spawnPointScreenY` | Where pieces spawn on screen (0.9). |
| `minimum/maximumCameraSize`, padding, smooth times | Zoom behaviour as towers widen. |

### Physics dials
Also serialized per mode (settle thresholds, micro-align caps, grounded distance...). These are
**not** difficulty dials — they're the stability contract. Read [PHYSICS.md](PHYSICS.md) first;
in practice, keep them identical across modes.

---

## 3. Recipes

**New level:** duplicate a GameModeConfig in `Resources/GameModes/`, tweak dials → duplicate a
LevelDefinition in `Resources/Levels/`, name it, point it at the mode, set a goal → add it to
a theme's `levels` array at the position it should play. The menu groups by theme automatically.

**New theme (complete recipe):**
1. ThemeDefinition in `Resources/Themes/` (Create > Stacking > Levels > Theme Definition):
   `sortOrder` (leave gaps), `levels` list, `skinFolder`, `backdrop`, `musicPlaylist`.
2. Backdrop: BackdropPreset in `Data/Backdrops/` (Create > Stacking > Levels > Backdrop
   Preset) — sky color pairs + altitude fade, cloud style/count/drift, hills on/off +
   style, ambient particles. No preset = classic dark sky. Best workflow: give Claude an
   inspiration image; palette and mood translate directly into preset values.
3. Skin: a preset per generator (`Tools/generate_piece_sprites.py`,
   `generate_ground_sprite.py`) writing to `Resources/Skins/<Theme>/`; set the theme's
   `skinFolder`. Missing files fall back to Classic file-by-file, so a ground-only skin
   is fine.
4. Music: 1–2 tracks in `Assets/Audio/Music/`, dragged onto `musicPlaylist`
   (random opener, then rotating; survives level restarts, stops on game over).
   Specs in ART.md.
5. Levels: per the "New level" recipe, each with a one-sentence `instruction`.
   Locks/unlocks and menu placement come automatically from `sortOrder` + completion.

**"1-grid floor, stack 5" level:** mode with `floorSegments: columnCount 1` + level with
`targetType: PlaceBlocks`, `targetValue: 5`. Pure settings — no code.

**Exclude the L-piece:** in the mode's `blockBag`, delete the Block_L definition entry. Done.

**3% Boulders on a hard level:** mode's `ambientBlockVariantChances` → add element:
Variant = `Data/Blocks/Boulder`, ChancePerBlock = 0.03.

**New power-up, zero code:** duplicate an asset in `Data/PowerUps/<Rarity>/`, change fields
(e.g. a second BlockVariantChance asset granting 35% Anchor as an Epic), add to a mode's pool.

**New power-up with new behaviour:** subclass `PowerUpDefinition` in
`Scripts/PowerUps/Definitions/`, implement `Apply(context)` (context has GameManager + Spawner —
extend `PowerUpContext` if you need more), create the asset, add to pools.

**New brick behaviour:** subclass `BlockData` in `Scripts/Blocks/Variants/`, override
`OnApplied` (at spawn) or `OnLocked` (at landing), create the asset. Reach it via a bag
default, ambient chance, or a power-up.

---

## 4. Level-modifier idea backlog

Already possible with today's data (no code):

- **Piece-diet levels** — only S/Z pieces (pain), only I/O (zen), double bag copies of one shape.
- **Ice level** — variant with a slippery PhysicsMaterial2D at high ambient chance + lower floor friction.
- **Heavy industry** — Heavy as the default data for every definition; landing rhythm changes completely.
- **Two towers** — two floor segments with a gap; islands disabled; narrow camera.
- **Gift run** — power-up choice every 5 blocks, pool of Epics only.
- **Hardcore** — `powerUpChoiceEveryBlocks 0`, peak at 0.7, fast ramp, 3% Boulders, no islands.
- **Platform hopper** — tiny 3-wide floor, very frequent wide islands: the tower must live on platforms.

Needs code (rough effort, all fit the existing hooks):

- ~~**Theme/level unlock persistence**~~ — done: `ProgressStore` (local JSON, cloud-sync
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
