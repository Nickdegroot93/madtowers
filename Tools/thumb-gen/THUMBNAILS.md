# Hazard Heights — Level Thumbnail Brief

A complete, self-contained brief for generating the level thumbnails. Hand this whole
document to an image AI; it contains everything: the specs, the style contract, every
chapter's look, and every file name. The core rule: **each thumbnail shows the level's
GAME MODE, set in its chapter's scenery** — mode first, chapter dressing second.

---

## 1. Hard specs (non-negotiable)

- **Aspect ratio: 2:3 portrait** (e.g. generate at 1024×1536).
- **Final file: JPG, long edge ≤ 800 px, quality ~80** (opaque, no transparency).
  From a PNG, `Tools/compress_chapter_image.sh thumbnail <in> <out>` does this in one step.
- **Save location: `Assets/Art/Chapters/<ChapterFolder>/<stem>-N.jpg`** where N is the
  level number (1–5). Exact names are listed per chapter below.
- **Overwrite the existing .jpg in place — same exact name.** Never delete-then-add,
  never rename, never touch `.meta` files (they hold the GUIDs the levels reference).
- **Do NOT replace files without a `-N` number** (`jungle-depths.jpg`, `kvartal-4.jpg`,
  `monsoon-sector.jpg`, `crimson-core.jpg`, …) — those are full-screen chapter
  *backgrounds*, not thumbnails.
- No text, no numbers, no logos, no UI, no frames or borders, no characters.

## 2. The style contract (every thumbnail)

Vertical storybook illustration for a charming block-stacking tower game.
Painterly textured flat-color style with soft gradients and gentle paper grain.
The subject is always **a stack/tower of chunky rounded stone tetromino blocks** with
subtle cracks and bevels, colored in the chapter's block palette. Soft atmospheric
lighting; cozy, playful adventure mood. The whole set must read as ONE series.

**Reference prompt template** — fill `{composition}` from the game-mode table in
section 3, and `{scenery}`/`{palette}`/`{blocks}` from the chapter in section 4:

> Vertical mobile game level thumbnail, storybook illustration for a block-stacking
> tower game. {composition}, set in {scenery}. Painterly textured flat-color style with
> soft gradients and gentle paper grain, {palette}. The blocks are chunky rounded stone
> squares with subtle cracks and bevels, colored {blocks}. Soft atmospheric lighting,
> cozy and playful adventure mood. No characters, no text, no lettering, no numbers,
> no UI, no frames or borders.

## 3. Composition = the level's GAME MODE in the chapter's scenery

**This is the rule.** A thumbnail is not decoration and not a journey — it tells the
player what they will PLAY, dressed in where they'll play it. Take the level's mode from
the chapter listings below, build the mode's composition, and set it in the chapter's
scenery/palette/blocks. Someone who knows the game should identify the mode from the
thumbnail alone.

| Mode | Rule (one line) | The composition to draw |
|---|---|---|
| **Classic Stack** | place N blocks without losing all lives | a sturdy, honest stack of tetromino blocks in the scenery — the chapter's plain postcard |
| **Puzzle Waves** | fit each wave's pieces under a height laser | blocks INTERLOCKED tightly like a solved puzzle — a low, wide, neat tetris-fit arrangement under a thin glowing horizontal laser line |
| **The Flood** | water rises on a timer — out-climb it | clearly risen WATER: a tall tower climbing out of it, lowest blocks submerged, foam at the waterline |
| **Airtight** | sealed air pockets are forbidden | a perfectly gapless, airtight brick wall — every block flush, zero holes; the craftsmanship IS the picture |
| **Void Zones** | glowing forbidden sky rectangles | one or two glowing rectangular ZONES hanging in the sky, the tower visibly built around/beside them |
| **Blackout** | scheduled darkness | the scene half-swallowed by darkness, the tower warm-lit against it |
| **Timed Rush** | place N blocks before the clock runs out | a hurried, leaning tower with motion energy — speed lines, toppling-forward urgency |
| **Maw Climb** | a plain climb over block-eating maws | a climbing tower with ominous mouth-like maws lurking in the ground below |

When a level combines modes (e.g. Classic + Void Zones, Flood + Blackout), the TWIST wins
the composition: show the zones / the darkness / the water — that's what makes the level
different from its neighbors.

## 4. The chapters — look, palette, blocks, files

All files go in `Assets/Art/Chapters/<Folder>/`. **Make one thumbnail per EXISTING
level** — the level list under each chapter is the work order (file `-N` = level N).
Higher-numbered spare files (`-4`/`-5` in three-level chapters) exist on disk but are
wired to nothing; skip them unless told a new level shipped.

### 1 · Jungle Depths — folder `JungleDepths`, files `jungle-1.jpg` … `jungle-5.jpg`
- **Scenery:** deep misty jungle, mossy ancient ruins, hanging vines.
- **Palette:** layered deep greens with shafts of soft jungle light.
- **Blocks:** mossy green, fern, ripe papaya orange, river-stone teal.
- Levels: 1 The Undergrowth (Classic, tutorial) · 2 Canopy Trial (Waves). (Vine Ascent
  was cut 2026-08-23 — `jungle-3.jpg` is now a spare.)

### 2 · Sakura Ridge — folder `SakuraRidge`, files `sakura-ridge-1..5.jpg`
- **Scenery:** serene Japanese mountain ridge, cherry blossoms, distant snow-capped Fuji, red torii gate.
- **Palette:** muted washi tones — soft pink sakura, pale sky, temple indigo accents.
- **Blocks:** patinated teal, washi gold, wisteria purple, torii vermilion.
- Levels: 2 Lantern Drift (Waves) · 3 Temple Steps (Flood, 50 m — the flood's debut).
  (Morning Gate was cut 2026-08-23 — `sakura-ridge-1.jpg` is now a spare; the files keep
  their `-2`/`-3` names, wiring follows level identity, not menu position.)

### 3 · Neon Nightfall — folder `NeonNightfall`, files `neon-nightfall-1..5.jpg`
- **Scenery:** neon-lit night-city waterfront, glowing skyscraper reflections.
- **Palette:** deep night violet with electric cyan and hot magenta neon.
- **Blocks:** electric cyan, amber signage, hot magenta, ultraviolet blue.
- Levels: 1 The Waterfront (Classic + Void Zones debut) · 2 Voltage Line (Waves) · 3 Penthouse Run (Flood).

### 4 · Frozen Peaks — folder `FrozenPeaks`, files `frozen-peaks-1..5.jpg`
- **Scenery:** high snowy alpine peaks, frosted pines, drifting snow.
- **Palette:** pale glacial blues and snow whites with a touch of alpenglow.
- **Blocks:** glacial ice blue, pale winter gold, frosted pine green, granite.
- Levels: 1 The Snowline (Classic) · 2 Whiteout Pass (Waves) · 3 Summit Climb (Flood).

### 5 · Kvartal 4 — folder `Kvartal4`, files `kvartal-4-1..5.jpg`
- **Scenery:** snowy sovietwave courtyard at night between tall panel apartment blocks, warm lit windows.
- **Palette:** cold teal-green night with sodium-lamp amber glow on snow.
- **Blocks:** lamplit snow grey, sodium amber, faded brick red, panel concrete blue.
- Levels: 1 Panelka Row (Classic) · 2 Curfew Line (Waves) · 3 Antenna Climb (Flood) · 4 Airtight (Airtight).

### 6 · Barren Lands — folder `BarrenLands`, files `barren-lands-1..5.jpg`
- **Scenery:** sun-baked desert, layered mesas, saguaro cacti, drifting heat haze.
- **Palette:** warm golden sand, terracotta, burnt orange under a pale hot sky.
- **Blocks:** golden sand, terracotta, slate, bleached bone.
- Levels: 1 The Mirage (Classic) · 2 Sandswept Path (Waves) · 3 Rising Dunes (Flood).

### 7 · Sector Isla — folder `SectorIsla`, files `sector-isla-1..5.jpg`
- **Scenery:** secret tropical island lagoon at green dusk, palms, a distant ferris wheel.
- **Palette:** lagoon teals and jungle greens with dusk-gold highlights.
- **Blocks:** lagoon aqua, dusk-gold sand, palm green, hibiscus red.
- Levels: 1 The Lagoon (Classic + Airtight) · 2 Marina Line (Waves) · 3 Skywheel Climb (Timed Rush).

### 8 · Fangkuai District — folder `FangkuaiDistrict`, files `fangkuai-district-1..5.jpg`
- **Scenery:** Chinese hillside district at dusk, glowing paper lanterns, tiled pagoda rooftops.
- **Palette:** dusk aubergine and mauve with warm lantern-pink glow.
- **Blocks:** lantern gold, jade green, cinnabar red, indigo.
- Levels: 1 The Night Market (Classic) · 2 Firecracker Alley (Waves) · 3 Pagoda Climb (Flood) · 4 Void Gate (Void Zones).

### 9 · Lost City — folder `LostCity`, files `lost-city-1..5.jpg`
- **Scenery:** alien desert ruins on a distant planet under a **giant rising moon**.
- **Palette:** moonlit teals and dusky violet with warm ember accents.
- **Blocks:** moonlit teal, moon gold, alien violet, rust orange.
- Levels: 1 The Oasis Gate (Classic) · 2 Aqueduct Line (Waves) · 3 Monolith Climb (Flood) · 4 Hollow Moon (Airtight).

### 10 · Burning Steppes — folder `BurningSteppes`, files `burning-steppes-1..5.jpg`
- **Scenery:** volcanic ash plain, a smoking volcano, drifting embers.
- **Palette:** warm ash greys and deep maroon under an ember-lit sky.
- **Blocks:** warm ash grey, ember gold, lava red, molten orange.
- Levels: 1 The Ashfall (Classic) · 2 Eruption Line (Waves) · 3 Crater Climb (Flood).

### 11 · Giza Dusk — folder `GizaDusk`, files `giza-dusk-1..5.jpg`
- **Scenery:** futuristic Egyptian desert at dusk — pyramids with **glowing hieroglyph monuments** ("cyber Egypt").
- **Palette:** dusky sandstone and gold under a deep violet evening sky.
- **Blocks:** limestone, pharaoh gold, lapis blue, carnelian red.
- Levels: 1 The Sphinx Road (Classic + Void Zones) · 2 Obelisk Line (Waves) · 3 Pyramid Climb (Flood).

### 12 · Amber Tide — folder `AmberTide`, files `amber-tide-1..5.jpg`
- **Scenery:** tropical sunset coast, palm jungle hills, a huge pale setting sun.
- **Palette:** pink-amber sunset tones from pale rose to deep magenta.
- **Blocks:** shell pink, sun amber, bougainvillea magenta, mango orange.
- Levels: 1 The Palm Coast (Classic) · 2 Tide Line (Waves) · 3 Sundown Climb (Flood).

### 13 · Monsoon Sector — folder `MonsoonSector`, files `monsoon-sector-1..5.jpg` — **NEW, none exist yet**
- **Scenery:** rain-soaked neon city in a monsoon downpour — wind-blown rain streaks, glowing skyline, wet reflective streets.
- **Palette:** storm greys and deep blue-greens cut by neon signage glow.
- **Blocks (proposal):** wet asphalt grey, neon cyan, rain-slick blue, taxi amber.
- Levels: 1 The Palm Road (Classic + Airtight) · 2 Wire Line (Waves) · 3 Skyline Climb (Flood).
- After these files exist, tell Claude — the levels need one-time wiring (`wire_thumbs.py`).

### 14 · Hallows' End — folder `HallowsEnd`, files `hallows-end-1..5.jpg`
- **Scenery:** Halloween graveyard at blood-red dusk — jack-o'-lanterns, crooked fences, a red eclipse moon.
- **Palette:** deep blood-red and plum dusk with warm pumpkin glow.
- **Blocks:** pumpkin orange, candlelight amber, witch violet, spectral teal.
- Levels: 1 The Pumpkin Patch (Classic) · 2 Lantern Line (Waves) · 3 Blood Moon Climb (Maw Climb — NO flood) · 4 Void Zones (Void Zones).

### 15 · Crimson Core — folder `CrimsonCore`, files `crimson-core-1..5.jpg` — **NEW, none exist yet**
- **Scenery:** synthwave/outrun finale — a giant retro banded sun on a crimson-black horizon, ember sky, dark silhouette terrain.
- **Palette:** molten reds and oranges over near-black, retro-sun gradient.
- **Blocks (proposal):** molten red, ember orange, jet black, chrome silver.
- Levels: 1 Night Shift (Classic + Blackout) · 2 Red Grid (Waves + Blackout) · 3 Core Ascent (Flood + Blackout).
- After these files exist, tell Claude — the levels need one-time wiring (`wire_thumbs.py`).

---

## 5. Checklist for delivery

1. One image per existing level, named exactly as listed above — **49 images**
   (11 chapters × 3, 4 chapters × 4). Spare `-4`/`-5` files in three-level chapters are optional.
2. Every image = the level's game-mode composition (section 3) in the chapter's scenery (section 4).
3. 2:3 portrait, compressed to ≤800 px long edge JPG q80 (or hand over PNGs and run the compress script).
4. Drop into `Assets/Art/Chapters/<Folder>/`, overwriting in place. Unity refresh picks them up; existing wiring survives.
5. Monsoon Sector + Crimson Core are net-new: after dropping their files, the levels need one wiring pass.
