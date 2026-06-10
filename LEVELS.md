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
 ├─ presentation shared by its levels: background, tint, music (brick skins later)
 ├─ featuredUnlocks: power-ups introduced by this theme (messaging; availability is
 │   authored per level pool)
 └─ levels: ordered list of LevelDefinitions — any count per theme

LevelDefinition  (Assets/Resources/Levels/  — one per level)
 ├─ presentation: display name, background image/tint, music (overrides theme when set)
 ├─ GOAL: targetType (Endless | PlaceBlocks | ReachHeight) + targetValue.
 │   Reaching it pauses and shows Level Complete with "Next: <level>" (next in theme),
 │   Keep Building, and Replay.
 ├─ modifiers: LevelModifier assets — custom behaviour beyond settings (see below)
 └─ GameModeConfig  (Assets/Resources/GameModes/  — the entire rule set)
     ├─ Difficulty: fall speed start/ramp/cap, lives, spawn delay
     ├─ Floor: segments (position + width — gaps and multiple towers possible)
     ├─ Block bag: which BlockDefinitions are in play, and how many copies each
     │    └─ BlockDefinition → shape prefab + default BlockData variant
     ├─ Ambient variant chances: % rolls that replace spawns with a variant (giant bricks!)
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

| Path | Contents |
|---|---|
| `Assets/Resources/Themes/` | ThemeDefinition assets. **Must stay here** (loaded by path at runtime). |
| `Assets/Resources/Levels/` | LevelDefinition assets. **Must stay here** (loaded by path at runtime). |
| `Assets/Resources/GameModes/` | GameModeConfig assets used by levels. |
| `Assets/SourceFiles/Scripts/Levels/Modifiers/` | LevelModifier behaviour classes (code). |
| `Assets/Data/Blocks/` | BlockData variant assets (Normal, Heavy, Sturdy, Giant, ...). |
| `Assets/Data/BlockDefinitions/` | BlockDefinition assets — one per tetromino shape (Block_I ... Block_Z); these are what block bags list. |
| `Assets/Data/PowerUps/Common|Rare|Epic/` | PowerUpDefinition assets, foldered by rarity (the asset's rarity **field** is what the game reads). |
| `Assets/Prefabs/Blocks/` | The 7 tetromino shape prefabs (I, O, T, S, Z, L, J). |
| `Assets/SourceFiles/Scripts/Blocks/Variants/` | BlockData base + behaviour subclasses (code, one per *behaviour*). |
| `Assets/SourceFiles/Scripts/PowerUps/Definitions/` | Power-up behaviour classes (code). |

---

## 2. Every level dial (GameModeConfig)

### Difficulty & pacing
| Setting | What it does |
|---|---|
| `startingLives` | Lives before game over. |
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
| `ambientBlockVariantChances` | **Level-flavour rolls**: list of (variant, chance). Example: Giant at 0.03 → 3% of all spawns are double-size bricks. Stacks with power-up-granted chances. |

### Brick variants (Assets/Data/Blocks/)
| Variant | What it is |
|---|---|
| Normal | Mass 1 baseline. |
| Heavy | Mass 3, dark tint. |
| Sturdy | Freezes exactly where it lands (player-made platform). Blue tint. |
| Giant | `sizeScale 2` — double width/height, fits nowhere. Red tint. The "annoying brick". |

New stat-only variants (slippery ice brick via a low-friction PhysicsMaterial2D, feather brick
via gravityScaleMultiplier, half-size brick via sizeScale 0.5...) are pure assets: right-click
> Create > Stacking > Blocks > Block Variant. Behaviour variants (act on land/spawn) need a
small subclass in `Scripts/Blocks/Variants/` overriding `OnApplied`/`OnLocked` — SturdyBlockData
is the 8-line template.

### Sky platforms (static support islands)
| Setting | What it does |
|---|---|
| `staticSupportIslandsEnabled` | Master switch per level. |
| `staticSupportIslandHeightInterval` / `SpawnChance` / `FirstHeight` | How often platforms roll as the tower grows. |
| `staticSupportIslandSpawnAheadHeight` | How far above the tower peak they appear. Keep below the spawn line (~12 units with default camera) so they're landable immediately. |
| `staticSupportIslandMin/MaxColumn`, `CenterClearColumns` | Where they may appear; keeps the main lane open. |
| `staticSupportIslandShapes` | Weighted shape list (Single, Two/Three Wide, Corner...). Authorable inline per mode. |

### Power-up choices
| Setting | What it does |
|---|---|
| `powerUpChoiceEveryBlocks` | Every N placed blocks: full pause + pick 1 of 3. 0 disables for the level. |
| `powerUpChoicePool` | Which PowerUpDefinitions can be offered. Per level — hard levels can ban Cement Tower, gift levels can offer only Legendaries. Rarity weighting (Common 100 / Rare 40 / Epic 15 / Legendary 5) lives in `PowerUpRarityInfo`. |

Current power-ups: Extra Life (Common), Slow Time (Common), Sturdy Bricks 20% (Rare),
Cement Tower (Epic). Adding more: see the doc comment on `PowerUpDefinition.cs` — many new
power-ups are zero-code (another `BlockVariantChancePowerUp` asset pointing at a different
variant, e.g. "Curse: Giant Bricks" as a negative offer).

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

**New theme:** create a ThemeDefinition in `Resources/Themes/` (Create > Stacking > Levels >
Theme Definition), set `sortOrder`, presentation, and its level list.

**"1-grid floor, stack 5" level:** mode with `floorSegments: columnCount 1` + level with
`targetType: PlaceBlocks`, `targetValue: 5`. Pure settings — no code.

**Exclude the L-piece:** in the mode's `blockBag`, delete the Block_L definition entry. Done.

**3% giant bricks on a hard level:** mode's `ambientBlockVariantChances` → add element:
Variant = `Data/Blocks/Giant`, ChancePerBlock = 0.03.

**New power-up, zero code:** duplicate an asset in `Data/PowerUps/<Rarity>/`, change fields
(e.g. a second BlockVariantChance asset granting 35% Sturdy as an Epic), add to a mode's pool.

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
- **Hardcore** — `powerUpChoiceEveryBlocks 0`, peak at 0.7, fast ramp, 3% Giants, no islands.
- **Platform hopper** — tiny 3-wide floor, very frequent wide islands: the tower must live on platforms.

Needs code (rough effort, all fit the existing hooks):

- **Theme/level unlock persistence** (medium) — themes and completion exist; saving which
  levels are beaten (PlayerPrefs or a save file) and locking later themes in the menu is the
  next milestone before this becomes a real campaign.
- **Wind gusts** (small) — a LevelModifier like Earthquake but with telegraphed directional pushes. Watch PHYSICS.md I1: forces only, never positions.
- **Bomb brick** (small) — `OnLocked`: apply explosion impulse to neighbours, destroy self. Negative ambient chance or cursed power-up.
- **Brittle brick** (medium) — breaks into single cells when load exceeds a threshold.
- ~~**Earthquake events**~~ — done: `EarthquakeModifier` (interval, jolt strength, grace blocks). Add its asset to any level.
- ~~**Win conditions**~~ — done: per-level targets (PlaceBlocks / ReachHeight) with completion screen and next-level progression.
- **Starting tower** (small) — pre-placed blocks/obstacle layout spawned at level start (data: list of cell positions, like island shapes).
- **Checkpoint heights** (small) — every X meters: +1 life or a bonus choice. Height tracking now works (measured from the floor).
- **Per-level rarity weight override** (tiny) — make Legendaries common on gift levels.
- **Fog ceiling** (small, visual) — darkness above a height; build into the unknown.
- **Sticky bricks** (medium) — weld-joint on contact; the inverse of Giant: forgiving but messy.

A good campaign curve mixes one *pressure* dial (speed, camera, Giants) with one *relief*
dial (more choices, wider floor, islands) per tier, rather than turning everything at once.

---

*Update this file when settings or systems are added — it is the designer-facing index of
what exists. Physics stability rules live in PHYSICS.md and win every conflict.*
