# MadTowers Floors — authoring, looks, physics & procedural generation (binding)

Everything about the ground a level is played on: how to define any floor shape as pure
data, how the per-chapter look is generated, what the physics guarantees are, and how to
generate floors procedurally. **Binding** for any floor work. Sister contracts:
[PHYSICS.md](PHYSICS.md) §3 (collider rules) · [LEVELS.md](LEVELS.md) (the level data model)
· [ART.md](ART.md) §4 (art pipeline) · [STYLE.md](STYLE.md) (sorting orders, invariants).

The one-sentence architecture: a floor is **data on the mode asset** (`floorSegments`),
rendered and collided at runtime by **`FloorTerrain`** (built by `PlayAreaController`),
skinned by **two generated PNGs per chapter** (`ground_fill` + `ground_cap`), dissolving
into a per-chapter **fog bank**. Nothing about a floor's shape or look ever needs
per-level code.

---

## 1. Defining a floor (any shape, pure data)

Select the level's **GameModeConfig** asset (`Assets/Resources/GameModes/…`) →
**Play Area → Floor Segments**. Each element is one piece of ground:

| Field | Meaning |
|---|---|
| `Center Column` | Where the segment sits (grid columns; 0 = play-area centre; negative = left). |
| `Column Count` | Width in columns. |
| `Base Height Cells` | Raises the WHOLE segment this many cells above the **datum** (0 = classic ground level). |
| `Column Height Steps` | Optional per-column EXTRA cells on top of the base, left → right (size ≤ Column Count; missing = 0). Bumps, walls, valleys, stairs. |
| `Pockets` | Optional carved 1×1 **nudge-in niches**: `Column` (0-based from this segment's left edge) + `Depth Cells` (1 = directly under that column's top). A pocket is a REAL hole — hollow collider, masonry cut away, backdrop visible through it, outlined on its solid edges. Entry is island-grade lenient (~±0.4 cell of vertical slack). |

**The datum** is the height-0 top surface (the legacy Base Platform's top): the lowest
landable line and the origin for tower height, island generation and backdrop anchoring.
All heights are ≥ 0 above it — that is what keeps every other system working unchanged.

### Rules of thumb

- A **gap** is simply the columns no segment covers — pieces dropped there sink into the
  fog and cost a life. Pieces spawn near column 0, so a gap spanning the centre punishes
  an unsteered first drop (fine for hard levels, mean for early ones).
- Keep heights **≤ ~8 cells** (they eat reaction room) and remember a raised start reaches
  height goals sooner — tune `targetValue` accordingly.
- Keep pockets **≤ ~3 cells below the datum** — the active piece force-locks ~3 units under
  it, so deeper niches can't be steered into. Deep pockets sit inside the fog (that's the
  look). Put pockets on columns whose side face is exposed (outer edges / beside a step).
- Leave **≥ 1 void column between segments** (butt-joined segments double their seam outlines).
- Fully **enclosed caves are not expressible** (heights + pockets only carve from surfaces
  and side faces). If ever needed, that's a new feature, not a config trick.
- The camera frames all segments automatically; very wide layouts play zoomed out.

### Worked example — the shipped Jungle 1 floor (`GameMode_JungleUndergrowth`)

```
Floor Segments (size 2)
├─ [0]  centerColumn -4 · columnCount 5 · baseHeightCells 0
│       columnHeightSteps [2,0,0,0,0]        ← 2-high wall on the outer edge
│       pockets [ (column 0, depthCells 4) ] ← niche low in the outer-left face
│                                               (wall top is +2 → hole at datum−2…−1)
└─ [1]  centerColumn  4 · columnCount 5 · baseHeightCells 0
        columnHeightSteps [0,0,1,0,0]        ← 1-cell bump in the middle
        pockets [ (column 0, depthCells 1),  ← notch under the surface, opening into the gap
                  (column 4, depthCells 3) ]  ← niche low in the outer-right face
```
Columns −6…−2 and 2…6 are ground; −1…1 is a void gap.

### Copy-paste shapes

- **Flat classic**: one segment, `columnCount 9`, everything else default.
- **Staircase**: one segment, `columnHeightSteps [0,0,1,1,2,2,3,3,4]`.
- **Valley**: `columnHeightSteps [3,1,0,0,0,1,3]` — nudge bricks into the bowl.
- **Twin pillars**: two segments, `columnCount 3`, `centerColumn ±4`, `baseHeightCells 4` / `6`.
- **Trio (Tricky-Towers)**: three segments, `baseHeightCells` 5 / 2 / 7.
- The **Custom Game** screen ships these as `Floor shape` presets (Flat/Steps/Valley/Twin/
  Trio) for instant testing with any width — no assets touched
  (`GameModeConfig.BuildCustomFloorSegments`).

### New-level checklist

1. Duplicate a GameModeConfig (or share one — many levels can point at the same mode).
2. Edit **Floor Segments** per the table above.
3. Point the LevelDefinition's mode at it. Done — terrain, colliders, outlines, caps and
   fog all derive automatically, in the chapter's look.

---

## 2. The look (per chapter, generated)

The ground's look is **chapter-wide** (levels inherit their chapter's `skinFolder`), and
generated — never painted — by `Tools/generate_ground_sprite.py`. Two calls per theme in
its `__main__` block:

```python
render_ground_fill("<Theme>", (r, g, b))                  # masonry base colour
render_ground_cap("<Theme>", (r, g, b),                   # cap band colour
                  fleck=(r, g, b), fleck_chance=0.012)    # optional flecks (petals, grains)
```

- `ground_fill.png` — 128×128 = **1×1 world unit, seamless both axes**. Running-bond
  0.5×0.25 u bricks: dark mortar (`mortar_factor`, default 0.32), per-brick tone spread
  (`tone_var`, default 0.10), top-lit bevel, grain, sparse pits.
- `ground_cap.png` — 256×64 = **2×0.5 u, horizontally seamless**. Baked near-black top
  outline (THE landable line), top-lit band, scalloped shadowed lower edge, flecks.
- Rerun `python3 Tools/generate_ground_sprite.py`. Import settings auto-apply to any
  `ground_*` file under `Assets/Resources/Skins/<Theme>/`. A theme without its own set
  falls back to Classic. **New chapter look = two lines + rerun.**

**Want something that isn't bricks?** Two sanctioned paths:

1. **Hand-drop override**: place ANY seamless 128×128 PNG named `ground_fill.png` (and a
   256×64 `ground_cap.png`) in the theme folder — smooth earth, wood planks, ice, circuit
   board, whatever. Import settings apply automatically; the runtime doesn't care what the
   pixels are, only the sizes. Follow STYLE.md's lighting language to keep it cohesive.
2. **New render function** in `generate_ground_sprite.py` (e.g. `render_ground_fill_planks`)
   writing the same file name/size — keeps the "everything is regenerable" property. Never
   fork the pipeline; add a function + preset parameters.

Runtime dressing (free, per shape): depth-shade ramp, near-black silhouette outlines on
exposed sides (split around pocket openings), the fade into fog. Sorting orders in STYLE.md.

---

## 3. The fog

Bottom of every floor dissolves into a fog bank: three **camera-following bands** (their
side edges can never show, however the camera pans/zooms) + world-anchored drifting wisps,
behind the ground AND in front of the blocks — pieces falling into gaps sink into it.

Colour: `BackdropPreset.groundFogColor` (per chapter). Leave alpha 0 to auto-derive a haze
from the chapter's near-hill colour — every backdrop gets a plausible fog with zero authoring.

---

## 4. Physics contract (summary — PHYSICS.md §3 is authoritative)

- One **static BoxCollider2D per column** (grid − 2·0.03 inset wide, 24 u deep), split
  vertically around pockets. Friction 0.95 like every surface.
- **Pocket leniency**: boxes around a pocket use the island collider prescription (shrink
  by 2r + `edgeRadius` 0.06·grid rounding) and the pocket ceiling is raised 0.05 for real
  clearance; the pocket floor stays grid-exact. This is what makes nudging in feel like the
  sky-platform pockets (±0.4 cell slack). Do not "simplify" it away — without it, pockets
  classify as enterable but the tuck's final overlap check reverts every entry.
- Plain floor spans keep sharp coplanar boxes — proven landing behaviour, untouched.
- Landing, casts, camera framing, reach bounds and island weighting are collider/config
  generic: they need **no registration** when floors change shape.

---

## 5. Procedural / randomized floors

Two levels of dynamism beyond hand-authored assets:

### A. Ship-ready: `ProceduralFloorModifier`

A `LevelModifier` asset (Create → MadTowers → Modifiers → Procedural Floor) you attach to a
level's Modifiers list. Every run generates a fresh layout from designer constraints:

- pillar count min/max · width min/max · **"one pillar at least this wide"**
- height min/max · **"the tallest stands ≥ N cells above the rest"**
- gap width min/max · pocket chance/depth per pillar (auto-clamped steerable)
- `seed` — 0 = new floor every run, non-zero = the same floor every run

Example: "three pillars, one ≥ 3 wide, the others narrower, one 2 rows higher" =
count 3/3 · width 2/3 + guarantee 3 · heights 0/6 + tallestExceeds 2.

### B. The underlying hook (for anything fancier)

```csharp
config.SetRuntimeFloorOverride(FloorSegmentConfig[]);  // runtime-only, never dirties the asset
playAreaController.ApplyConfig();                      // rebuilds terrain + colliders
```

Because EVERY consumer reads `config.FloorSegments`, one override keeps terrain, camera,
reach bounds and islands consistent. Contract for custom generators (copy
`ProceduralFloorModifier`):

1. Build segments with the public constructors:
   `new FloorSegmentConfig(center, width, baseHeight, heightSteps, pockets)`.
2. Set the override + `ApplyConfig()` in `OnLevelStart` (before any piece spawns).
3. **Clear the override in `OnLevelEnd`** — the config asset instance outlives the scene in
   the editor; a stale override leaks into other levels sharing the config.
4. Respect §1's rules of thumb (they're not validated for you).
5. Known cosmetic caveat: backdrop prop spacing samples the floor width before modifiers
   run — props may space as if the floor were the asset's default. Scenery only.

---

## 6. What is deliberately NOT per-level

- **The look** — chapter-wide by design (ART.md §7: a chapter is one visual world). A level
  needing a unique material means a new chapter/theme folder, which is two generator lines.
- **The datum** — one per scene. Terrain varies above it, never below.
- **Enclosed caves** — see §1.
