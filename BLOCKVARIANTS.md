# MadTowers Block Variants — catalog, looks & architecture (binding)

What special bricks exist, how each behaves and looks, and the architecture for adding, removing, or
re-skinning them. **Binding** for any variant work. Sister contracts:
- [BLOCKS.md](BLOCKS.md) — counting / life-loss (every variant must respect the two flags).
- [PHYSICS.md](PHYSICS.md) — looks are **cosmetic-only**: no colliders on overlays, never write transforms on a landed body.
- [ART.md](ART.md) §13 — the theme-independent look rule.
- [CUSTOMGAME.md](CUSTOMGAME.md) — how to force a variant to spawn for testing.
- [ABILITIES.md](ABILITIES.md) — abilities that apply variants.

Code: `Assets/SourceFiles/Scripts/Blocks/Variants/` · Data assets: `Assets/Data/Blocks/` ·
Procedural skin shaders: `Assets/Resources/<Name>.shader`.

---

## 1. The three layers of a variant

A variant is up to three pieces, by **naming convention**:

| File | Type | Owns |
|---|---|---|
| `<Name>BlockData.cs` *(or plain `BlockData`)* | `BlockData` / subclass | stats (mass, friction, flags, tint) + lifecycle hooks (`OnApplied`, `OnLocked`, `OnRotationDenied`) |
| `<Name>BlockSkin.cs` | `BlockVariantSkin` | the fixed, theme-independent **look** (per-cell procedural overlay + motion) |
| `<Name>BlockBehaviour.cs` | `MonoBehaviour` | runtime **behaviour** added on lock (welds, explosions, …) |

- A variant with **no behaviour and no custom look** is just a `BlockData` **asset** — no code (Ice,
  Vortex, Locked, Feather differ only by serialized stats/material).
- A variant with a **look** adds `<Name>BlockSkin` from `<Name>BlockData.OnApplied`.
- A variant with **behaviour** adds `<Name>BlockBehaviour` (or acts directly) from `OnLocked`.

The data asset's `m_Script` decides which `BlockData` (sub)class it is — that's the only wiring.

**Transmute vs defuse** (Nick 2026-08-30): a mid-air re-apply (`ApplyVariantConsumable`,
e.g. Vine cast on a falling Tremor) REPLACES the data that drives flags/steering/accounting,
but the replaced variant's **landing behaviour still fires** — `BlockController` remembers
replaced datas (`_replacedDatas`) and calls their `OnLocked` too, oldest first (a Tremor
turned Vine still quakes, now dragging its welded cluster). A **defuse**
(`AbilityEffects.NeutralizeToPlain` — Sanitize, Ward) clears that memory: defused is gone.
Skins are additive the same way — the old skin stays unless `StripVariantSkins` removes it.

---

## 2. The catalog

Look status: ✅ procedural skin · 🟡 has a feel moment but only a colour tint · 🔴 colour tint only, look pending.

| Variant | Mass | Behaviour | Look (colour / texture / motion) | Look | Files |
|---|---|---|---|---|---|
| **Normal** | 1 | — | the chapter's own brick art | n/a | `Normal.asset` |
| **Boulder** | 4 | very heavy (strains the tower); heavy landing **slam** | broad quarried granite planes, sampled pits/plate fractures and restrained mica flecks; matte and motionless at rest. Existing mass-four landing compression and impact punch | ✅ | `BoulderBlockData`, `BoulderBlockSkin`, `Boulder.shader` |
| **Anchor** | 1 | **freezes static** where it lands (permanent terrain) | weathered navy iron plate and raised X brace, worn contact edges and overhead-lit rivets; quiet sheen and the existing lock glint/metal settle | ✅ | `AnchorBlockData`, `AnchorBlockSkin`, `Anchor.shader` |
| **Vine** | 1 | on land **welds instantly** to every block it touches, and **creeps vines onto each** | fixed mossy carved stone with woody stems, folded pointed leaves and contact shadows; root-directed spread preserves the neighbour’s chapter colour. Existing half-second growth, quiet tip sway, deterministic material | ✅ | `VineBlockData`, `VineBlockSkin`, `VineBlockBehaviour`, `Vine.shader` |
| **Magma** | 1 | on land, connected cells with equal fall distance become rigid fragments and drop through the existing auto-drop path; the lowest cell in each original column determines clearance. A T over a one-cell pocket yields two Pips and one vertical Domino. Total cell mass is retained; only the first fragment counts for the original single placement | fixed weathered basalt with baked molten plate seams; quiet heat, no independent cell wobble. **Connected equal-drop fragments stay joined** and cool over the existing 0.32 s splat while retaining dark ember seams. Continuous outer outline and carved internal seams | ✅ | `MagmaBlockData`, `MagmaBlockSkin`, `MagmaMelt`, `MagmaBlobVisual`, `Lava.shader` |
| **Bomb** | 1 | on land **detonates** after the unchanged 1 s fuse, deleting touched landed neighbours and dropping their support; no blast impulse | weathered grey-green stone casing, forged iron hoops, worn studs and a recessed copper fuse grate; a quiet ember heats the core and fractures through the existing fuse, with small whole-casing tremble; compact flash, smoke and chipped debris | ✅ | `BombBlockData`, `BombBlockSkin`, `BombBlockBehaviour`, `Bomb.shader`, `BombBlast.prefab`, `BombDebris.prefab` |
| **Ice** | 1 | slippery (low-friction `IceSurface` material) | fixed glacial blue with cloudy depth, trapped air, bright fracture lips and broad frozen planes; isolated `Ice.shader` leaves the Freeze ability’s `Frost.shader` unchanged. Dead still, follows existing landing squash, accepts foreign vines | ✅ | `IceBlockData`, `IceBlockSkin`, `Ice.shader` |
| **Vortex** | 1 | **inverts** left/right steering | full-brick dusk marble with narrower cream mineral veins, sampled wear and a slower reversing spiral (0.65 rad/s maximum cosmetic churn); no orbiting ember specks. Steering inversion remains constant | ✅ | `VortexBlockData`, `VortexBlockSkin`, `Vortex.shader` |
| **Locked** | 1 | **cannot rotate** | fixed weathered slate with rusted gear teeth, a seated iron chain and a recessed locking pin. Existing directional flinch, damped gear/chain strain and pin spark on denied rotation; still accepts foreign vines | ✅ | `LockedBlockData`, `LockedBlockSkin`, `Locked.shader` |
| **Feather** | 0.25 | very light — shoved around by later landings | warm layered ivory plumes with folded shafts, fine barbs and soft overlapping shadows, within the standard frame. Existing falling float/sway and landing flutter; settled plume surface stays still | ✅ | `FeatherBlockData`, `FeatherBlockSkin`, `Feather.shader` |
| **Tremor** | 1 | on land **shakes the whole tower** — a short shake burst (velocity kicks radiating from the brick, per-block shear topples bad placements); lock = shockwave ring + flash + camera kick + ground-dust puff | weathered ochre fault plates, sampled relief and engraved amber faults; quieter 0.006 u idle buzz. Existing delayed quake, discharge ring, ground-hit dust and impact punch | ✅ | `TremorBlockData`, `TremorBlockSkin`, `TremorBlockBehaviour`, `Tremor.shader` |
| **Sandstone** | 1 | **load-bearing limit** — reads the weight of the stack resting on it (support graph walked transitively via thin probes at the resting plane above each **top-exposed** cell; each direct rester's branch is **divided by its distinct support count**, so a pure tower presses exact while a bridge/tucked overhang presses only its share; structural on purpose: the settle system force-sleeps quiet stacks and Box2D reports no contact impulses for sleeping islands, so a solver-force gauge reads zero exactly when it matters). Break limit is authored in **normal-brick weights** (`breakLoadBrickWeights`, default 3: two bricks sit, the third breaks it; a Boulder (4×) crushes instantly, Feathers (0.25×) barely count; an internal 0.45 bw margin makes the Nth brick actually cross the smoothed threshold). **Static bodies shield**: a frozen block above is self-supporting terrain — contributes nothing and carries its own stack, so Freeze is a legitimate rescue. **Sandstone is ordinary weight to other sandstone** (Nick 2026-08-03; the original "sand stacks safely on sand" shield read as a bug in play): a sand layer presses on the layer below and transmits the stack above it, so a sand-on-sand column cracks from the bottom, where the load is largest. Probes always run along **world up** — gravity does not rotate with the piece (a quarter-turned piece probing its local up read its side-neighbours' towers as phantom load). Damage is smoothed + **ratcheted** (cracks never heal; current load also drives sand trickle); crack SFX at each damage third; crumble = shatter + burst + small camera kick through the standard destruction flow (BLOCKS.md accounting, count −1), **no life charged** — the collapse above is the punishment. Frozen sandstone stops reading load entirely (preserved stone) | porous layered sediment with the standard frame; sampled plate fractures deepen and widen under the existing damage/load readout, with trickle and high-load shiver. Crumble uses the same fourteen shards with chipped sprites | ✅ | `SandstoneBlockData`, `SandstoneBlockSkin`, `SandstoneBlockBehaviour`, `Sandstone.shader` |
| **Pyramid** | 1 | **no flat top** — a block TYPE, not an appliable variant: a 3-column monument SHAPE (`Block_Pyramid`: three real 1×1 base cells — grid anchor, beam footprint, COM — plus the pyramid top as a `PolygonCollider2D` on the "Body" child carrying `LandableSlope`, its ~42/44° faces opt in under the 0.7 landing gate, see PHYSICS.md; apex a single point offset 0.07 u off-centre so a column-aligned piece ALWAYS tips — symmetric apexes let perfectly-aligned pieces balance and stack). Its `BlockData` exists only as the stats/Vault card: `canRotate:false`, `BlockDefinition.lockDefaultData` keeps ambient rolls / variant overrides from replacing it, and `ContentCatalog.IsShapeBound` hides it from the Custom Game variant sliders (it's toggled in the Blocks list; it still gets a Vault card). Anything landing on the faces locks, goes Dynamic, and slides off — Giza Dusk's signature brick, a real placement it punishes you for building on | fixed Giza sandstone in every chapter — straight ashlar base course + pyramid-top masonry + a gold capstone tip, shipped as ONE `piece_Pyramid.png` in `Skins/Classic` (the file-by-file fallback IS the fixed look; `Tools/generate_pyramid_sprite.py`), no shader/skin code | ✅ (sprite) | `Block_Pyramid.prefab`, `Pyramid.asset` (plain BlockData), `Block_Pyramid.asset` definition, `LandableSlope.cs` |
| **Maw** | 1 | falls dormant, then on land **devours any block placed on it, forever** — each devour shatters the prey (removed from the live count, BLOCKS.md) and **costs a life** (`GameManager.LoseLifeToHazard`), so you build *around* it. **Maws never eat each other**, so two in a row stack safely — and on land a maw **welds to every maw it touches** (unbreakable `FixedJoint2D`, the Vine weld pattern but maw-only & permanent), so a stack of maws fuses into one rigid "huge maw" that can't be toppled by loose leaning. Because they're one welded cluster, maws are also **excluded from fly-out targeting** (Extract/Suspension): they stay put while the rest of the tower spreads, and can't be selected. `counts:true`, `costsLife:true` | weathered violet shell plates, dark eye sockets, rooted ivory teeth and a deep red gullet. The existing exposed world-up face, wake, tongue and bite gape remain; quiet breathing and compact prey breakup | ✅ | `MawBlockData`, `MawBlockSkin`, `MawBlockBehaviour`, `Maw.shader` |
| **Curse** | 1 | **bury-me countdown** — while ANY top cell is exposed to the sky, every COUNTED placement (`GameEvents.BlockPlaced` - never raw locks: magma pips and off-board losses must not tick, review 2026-08-02) burns one sigil (default 4, `buryWithinPlacements` on the asset — Nick 2026-08-02, bracket in playtesting); at zero it fires: **one life** through the hazard path (`LoseLifeToHazard`, so immunity/Ward/Purifier apply) and it re-arms. Covering every top cell pacifies it — cover = any LANDED block or static terrain (own upper cells included; a still-falling piece never counts), and the burying placement itself never ticks (exposure is re-scanned before the tick). Re-exposing (cover destroyed / knocked off) restarts a FRESH countdown, never a half-burned one - and the placement that knocked the cover off doesn't also burn sigil one. Multiple exposed curses tick independently (N at zero = N lives). Inert once the run is over, below the cull line, or while a rescue (Rebound) is carrying it away. The deliberate INVERSE of the Maw: the Maw punishes building ON it, the Curse punishes NOT doing so. `isHazard: true` | fixed green-black obsidian with sampled pits, broad facets and engraved fractures. The **eye remains the countdown** on every cell; no runes. Existing exposure-gated eye/smoke, last-sigil alarm and fire flash/rings remain; buried stone stays quiet | ✅ | `CurseBlockData`, `CurseBlockSkin`, `CurseBlockBehaviour`, `Curse.shader` |

Every special brick now carries a procedural `BlockVariantSkin` look — the backlog is clear. New variants
follow the recipe in §4.

---

## 3. The look layer — `BlockVariantSkin`

`BlockVariantSkin` (abstract base) owns the per-cell overlay machinery; a brick's skin subclass supplies
only what's unique:

| Member | Purpose |
|---|---|
| `MaterialResource` | Resources path of the procedural shader/material, e.g. `"Anchor"` (cached per name) |
| `HidesChapterArt` | **replace** the chapter art (Anchor/Boulder/Magma → `true`) or **overlay** on top, keeping the chapter colour (Vine owner → `true`, neighbour spread → `false`) |
| `CellScale` | overlay quad size as a multiple of the cell (default `1`). `>1` lets a skin draw **past** the brick edge (Maw's tentacles overhang the top); the contract is then that the shader insets its body to `0.5/CellScale` so the brick still tiles exactly one cell |
| `ConfigureCell(index, col, row, overlay, mpb)` | optional per-cell material props — Magma's exposed edges, Vine's body mode + root direction, Maw's seed + `_BodyHalf` |
| a `LateUpdate` | the motion, using `Cells` / `BaseScales` / `BasePositions` (scale + position) and `SetCellsFloat` (animated shader props) |

`BuildCells()` does the rest (find the sort renderer, optionally hide chapter art, scan cell colliders,
size + place an overlay quad per cell, call `ConfigureCell`). It is idempotent. So a new skin is ~30 lines.

The **shader** is a URP sprite shader (crib `Lava.shader` / `Anchor.shader`): an SDF rounded box matching
the brick silhouette, an in-hue bevel, the procedural surface, and optional `_Time` animation + props the
component drives (`_LockFlash`, `_Growth`). Colours are **fixed** = theme-independent (ART.md §13).
Replace-mode skins draw the whole brick; Vine uses a fixed stone body on its owner and sparse plant overlays on neighbours. `BlocksForeignOverlays` is independently overridable so changing the material does not change spread eligibility.

**Sampled materials (September 2026, all thirteen hazards migrated).** `HazardSurface.hlsl` supplies
top-lit stone relief and the invariant bevel/outline. The inner outline uses screen-pixel
antialiasing (`fwidth`) for stable coverage during subpixel movement. Sandstone, Ice,
Tremor, Locked, Boulder and Vine opt into DXC for Vulkan shader compilation: the HLSLcc Vulkan output produced flickering
dark edge marks on Adreno 830. Their authored material formulas are retained;
Tremor and Vine square their signed pulse/stem offsets with multiplication so both sides
remain defined under DXC. Locked, Boulder and Vine additionally opt into full-precision
material sampling and arithmetic (`MADTOWERS_HAZARD_FLOAT`). Boulder skips inactive
bevel calculations on its flat face (`MADTOWERS_HAZARD_BRANCH_BEVEL`); the other
hazards retain their existing shared-helper paths. See
[Android hazard pixel artifacts](#6-android-hazard-pixel-artifacts) for the diagnosis,
regression coverage and limits.
`HazardSurface.png` packs baked
relief, broad noise, fine noise and fractures into a small linear RGBA texture;
`Tools/generate_variant_surfaces.py` authors it deterministically. Materials declaring
`_HazardSurface` receive it from the shared loader. No per-fragment hash noise is needed
on this path. Each migrated shader multiplies vertex colour and `unity_SpriteColor`.
`BuildCells` clears inherited data RGB on existing variant cells when applying a skin,
preserving their alpha, so reapplication/transmutation does not recolour fixed materials.

Bomb retains the existing fuse, pre-flash threshold, sound, camera/hit-stop and destruction
flow. Its stone casing has a closed 17 px equivalent outline, 22 px corners and a 26 px
top-lit bevel; the 0.018 u arming jitter moves all visual cells together. Idle heat breathes
slowly without moving the casing. Collider-free rendering remains independent of physics.

Curse uses fixed green-black obsidian facets, pits and engraved plate fractures; the
eye remains its sole countdown symbol, with no decorative runes. Its shader replaces
the per-fragment Voronoi/hash work with sampled fields and honours vertex/sprite colour.
`CurseBlockSkin` and `CurseBlockBehaviour` are unchanged: the same eye states, exposure
smoke, fire flash/rings, tick/seal/fire audio and camera/hit-stop remain. The buried
quiescence guard still stops phase advancement and per-cell property pushes.

Vine uses fixed mossy stone on the owner and plant-only overlays on neighbours.
Its deterministic stems and pointed folded leaves grow from the existing contact
direction in 0.5 s, with quiet tip sway; it retains foreign-overlay eligibility.
Maw uses sampled violet shell plates, shaded ivory teeth, deep gullet and dark eye
sockets, retaining the same face exposure, wake/chomp signals and safe-Maw welds.

Locked now seats its existing gear, chain and pin in fixed weathered slate. Sampled
wear and overhead highlights replace clean metal shading; rotation refusal, spring
timing and foreign vine eligibility are unchanged.

Magma now partitions **connected cells with equal fall distance** into rigid fragments
(user-approved gameplay exception). The lowest cell in each original column determines
its clearance. Fragments retain total per-cell Pip mass and use normal descent/landing;
only the first credits the single original placement. Fixed basalt persists after the
0.32 s cooling splat; offline `MagmaCracks.png` replaces fragment noise/Voronoi work.

Tremor uses broad weathered ochre fault plates with sampled engraved amber seams.
Its idle visual buzz is 0.006 u; the existing arm callback and all real quake
parameters remain untouched.

Sandstone uses sampled porous sediment, horizontal strata and baked plate boundaries.
The original damage/load uniforms retain continuous fracture widening, trickle and
high-load shiver. Its cosmetic shatter hook selects the chipped sprite without
changing the fourteen-shard motion or destruction path.

Vortex keeps its full-brick reversing dusk marble, with narrower cream mineral veins,
sampled wear and 0.65 rad/s maximum cosmetic churn. The reversal cadence and actual
steering inversion remain independent and unchanged.

Ice now uses the isolated `Ice.shader`: fixed glacial blue, cloudy depth, trapped air
and lit fractures. The Freeze ability’s `Frost.shader` remains unchanged. The skin
still follows the collider-free PieceSkin squash parent, accepts foreign vines and
consumes its former RNG draw without randomising the material.

Boulder uses broad quarried granite planes, sampled pits/plate fractures and mica
flecks, with the standard frame. Its skin and mass-four impact behavior are unchanged.

Anchor uses weathered navy forged plate, a raised X brace with worn contact edges,
overhead-lit rivets and a quieter slow sheen. Its skin and freeze-on-lock code remain
unchanged.

Feather uses layered ivory plumes, folded shafts, fine barbs and soft overlap
shadows within the standard frame. Its airborne float/sway and landing flutter
remain unchanged; the settled shader no longer animates flecks.

| Upgraded trigger | As-built effect source | Shared behaviour preserved |
|---|---|---|
| Bomb blast | `Resources/BombBlast.prefab`: project-owned variant of CFXR4 Explosion Orange + Smoke; compact soft core flash, sparse sparks, short smoke; starburst/line emitters disabled | Original pack untouched; existing scale 2.5, `Vfx` settings gate, sound and hit-stop |
| Bomb victim debris | `Resources/BombDebris.prefab`: project-owned variant of CFXR2 Debris Hit (Lit), six compact debris particles per cell | Same `ImpactFx.BurstFromEveryCell` placement calls |
| Bomb self/victim shards | Cosmetic `BombBlockSkin.Shatter` hook → `BlockShatterFx` with `HazardShard.png` | Same 12 particles, lifetime, gravity, trajectories, RNG draw sequence and accounting; other callers explicitly reset to their original square sprite on pool reuse |
| Curse exposure/countdown/fire | `Curse.shader` eye, smoke, fracture glow and fire rings; existing `curse_tick`, `curse_seal`, `curse_fire` audio + `ImpactFx.ImpactPunch` | No prefab added; exposure, countdown, quiescence and all timing remain in the unchanged skin/behaviour |
| Vine weld/spread | `Vine.shader` root-directed stems and leaves driven by `VineBlockSkin`; shared landing feedback | Same 0.5 s growth; no bespoke particles needed; weld logic and neighbour eligibility unchanged |
| Maw devour | Existing CFXR2 Disintegrate prefab, `BlockShatterFx`, `maw_crunch`, `ImpactFx.ImpactPunch`; shader jaw/tongue | Retained compact prey breakup and bite feedback; same scale .7, lifetime 2, counts, life charge and cadence |
| Locked refusal | Existing shader pin spark, gear/chain strain and `LockedBlockSkin` visual flinch | Same left/right denial hooks, spring and decay; no additional emitter needed |
| Magma melt/solidify | `Lava.shader`, `MagmaBlobVisual` heat/splat, existing soft impact sounds and shared landing feedback; burst slot remains null | Same 0.32 s cooling and single-placement credit; joined fragments and their mass aggregation are the explicitly approved rule change |
| Tremor quake | Existing CFXR2 Ground Hit prefab, shader discharge ring/fault light and `ImpactFx.ImpactPunch` | Retained physical dust/punch; 1 s arm, strength 2, duration .5 s, radius 8 and frozen-body exclusion unchanged |
| Sandstone crumble | `SandstoneBlockSkin.Shatter` → `BlockShatterFx` with `HazardShard.png`; existing crack/burst audio and camera kick | Same 14 shards, RNG draws, motion and destruction/life accounting; only the particle sprite changes |
| Vortex steering/landing | Reversing shader marble driven by scaled `VortexBlockSkin`; shared landing feedback | No discrete hazard burst is appropriate; inversion remains constant, independent of cosmetic reversal |
| Ice landing/sliding | Fixed `Ice.shader` pane and existing shared landing squash/feedback | No idle animation or bespoke emitter; original IceSurface friction .05 and landing parent retained; Freeze ability unchanged |
| Boulder slam | Existing `BoulderBlockSkin` compression and `ImpactFx.ImpactPunch`, shared landing dust/sound | Same .5 s impact envelope, 18% visual compression, mass four; no additional emitter needed |
| Anchor lock | Existing shader lock glint, `AnchorBlockSkin` damped metal settle and shared landing feedback | Original .45 s visual lock cue and Static freeze-on-lock retained; no extra emitter needed |
| Feather landing | Existing `FeatherBlockSkin` float/sway taper and landing flutter, shared landing feedback | Same mass .25, flutter and rest timing; no bespoke punch or particles added |

---

## 4. Recipe — add a new brick variant

1. **Stats / behaviour.** Create `Assets/Data/Blocks/<Name>.asset`.
   - *No behaviour, no custom look* → leave it a plain `BlockData` asset (set mass, friction material,
     flags, tint). **Done** — skip to step 3.
   - *Behaviour and/or a look* → write `<Name>BlockData : BlockData` in `Blocks/Variants/` and point the
     asset's `m_Script` at it. Override `OnApplied` to get-or-add `<Name>BlockSkin` (the look) and/or
     `OnLocked` to add behaviour. Respect [BLOCKS.md](BLOCKS.md): set the two scoring flags; for a
     **hostile** brick set `isHazard: true` so the all-hazards abilities (Ward, Purifier) cover it
     automatically — no list to edit. Any code destroying a placed block calls `GameManager.RemovePlacedBlock`.
2. **Look** (optional). Write `<Name>BlockSkin : BlockVariantSkin` (override `MaterialResource`,
   `HidesChapterArt`, optional `ConfigureCell`, a `LateUpdate` for motion) + a procedural
   `Assets/Resources/<Name>.shader`. Keep colours fixed (theme-independent).
3. **Test.** Custom Game → **Block Variants** → set `<Name>` to **1.00** → Start. It's auto-discovered
   (CUSTOMGAME.md) — no list to edit.
4. **Introduce in play** — pick one:
   - **Ambient** (a level/chapter): add an `AmbientBlockVariantChance { variant, chancePerBlock }` to a
     `GameModeConfig` — a per-piece roll.
   - **Ability**: `ApplyVariantConsumable` (tap → active piece becomes the variant) or
     `BlockVariantChancePowerUp` (stacking % passive). New ability **assets**, no code (ABILITIES.md).
   - **Targeted**: `Spawner.ApplyVariantToNextBlock` / `BlockController.ApplyData` (how Suspension turns a
     chosen landed block into an Anchor).

5. **Demo + Vault** (see [BLOCKPREVIEWS.md](BLOCKPREVIEWS.md) §5). Author a scenario loop in
   `BlockDemoScenarios` + a `BlockDemoCatalog` entry (this gates the one-time debut modal), and fill
   the asset's `behaviourSummary` / `vaultDescription` fields. Without a catalog entry the brick
   debuts silently: first spawn marks it discovered and unlocks its Vault entry, no modal.

**Remove from play:** delete the ambient entry / drop the ability from the mode (or ban it via
`LevelDefinition.bannedAbilities`). **Delete a variant entirely:** remove its asset + code + shader, then
grep the asset GUID and clear any ambient/ability references (and its BlockDemoCatalog/Scenarios entries).

---

## 5. Conventions & hard rules

- **Naming:** `<Name>BlockData` / `<Name>BlockSkin` / `<Name>BlockBehaviour`, all in `Blocks/Variants/`;
  the shader is `Resources/<Name>.shader`, loaded by name. Every new brick follows this so the set stays
  uniform.
- **Looks are cosmetic only** (PHYSICS.md): overlays carry no colliders; never write a transform on a
  landed body; scale/tint only the visual children; run timed motion on **scaled** time so a pause freezes it.
- **Theme-independent** (ART.md §13): a unique block looks identical in every chapter — fixed shader
  colours, not the chapter tint. Magma fragments retain cooled basalt. Vines spreading onto
  normal neighbours preserve those neighbours' chapter colours.
- **`colorTint` is NOT the standard way to mark a variant.** Leave it **white** so the brick keeps its
  real chapter colour, and express identity through the procedural overlay, which reads on whatever colour
  the chapter already uses (the dark seating outline carries it on any palette). A fixed full-brick tint
  recolours the brick in *every* chapter, which fights muted/dark chapter palettes — that's why the old
  per-variant tints (orange Locked, pink Vortex, …) were removed once those blocks got real overlays. A
  non-white `colorTint` is reserved for a **deliberate, block-specific** colour you actually want in all
  chapters — never a default differentiator.
- **Reuse the base:** don't re-roll the per-cell overlay loop or material loading — `BlockVariantSkin`
  owns them. New shared cell behaviour goes in the base, not copied per skin.
- **Fixed-look bricks refuse foreign cosmetic creep** (`BlockVariantSkin.BlocksForeignOverlays`
  = replace-mode skins by default; Nick 2026-08-02): a vine growing over the Curse's eye or the Bomb's
  fuse hides exactly the signal the brick exists to show. Physical effects (welds, joints)
  still apply — only the LOOK is refused. Vine, Ice and Locked explicitly keep accepting
  foreign overlays despite their fixed primary materials. Anything new that spreads visuals onto neighbouring
  blocks must check the flag (`VineBlockBehaviour.SpreadVineTo` is the pattern).

---

## 6. Android hazard pixel artifacts

**September 2026.** Tiny flickering dark dots or short bars appeared near cell edges,
corners and faces on Android, moving with rotation/subpixel placement. They were absent
from the Unity Metal Game view. Device reproduction used a Samsung SM-S938B (Adreno 830),
Unity 6000.4.10f1 Vulkan shaders and the mipmapped linear `HazardSurface` texture;
the game's 0.8 render scale matters when reproducing the sampling conditions.

The evidence isolates a compiler/numeric shader-path issue on this device, rather than
the authored PNG or collider geometry; it does not establish a specific driver defect.
The earlier screen-derivative outline antialiasing adjustment did **not** fix these marks.

Keep these two parts of the workaround together:

- **Vulkan compiler:** `#pragma use_dxc vulkan` in `Sandstone.shader`, `Ice.shader`,
  `Tremor.shader`, `Locked.shader`, `Boulder.shader` and `Vine.shader`. The first three
  cleared with this change. Tremor's pulse and Vine's stem square signed offsets with
  multiplication, avoiding undefined negative-base `pow` results under DXC while
  retaining the intended shapes.
- **Residual marks:** DXC alone was insufficient for Locked, Boulder and Vine. They
  define `MADTOWERS_HAZARD_FLOAT` before including `HazardSurface.hlsl`, keeping material
  sampling, intermediate colours and fragment output at full precision. Boulder also
  defines `MADTOWERS_HAZARD_BRANCH_BEVEL`: the shared helper skips inactive bevel
  normal/power calculations on the flat face with `[branch] if (band > 0)`. Other
  hazards retain their existing helper paths. No texture, geometry, gameplay or
  animation parameter was changed for this fix.

**Verified scope:** off-screen rendering on the phone passed 65,536 frames for
Sandstone/Ice/Tremor (including Tremor pulse/quake combinations), then 49,152 frames
for Locked/Boulder/Vine, with zero detected near-black/non-finite pixels. The latter
sweep covered 40–112.45 pixels per cell, fractional positions, gamma/linear material
colours and the `log2(0.8)` mip bias. All 26 Android shader compilation checks
(13 hazards × Vulkan/GLES) passed. Production Vulkan binaries matched the passing
candidates; the first three remained byte-identical after the residual fix.
These are device shader tests, **not a rebuilt-app gameplay run**: a fresh Android
build still needs in-game confirmation. Evidence is archived outside the repository
in the `android-dot-fix-20260905` and `android-dot-residual-20260905` review bundles.

**If it returns:** reproduce on the affected Android GPU at gameplay zoom and render
scale, checking falling/rotating bricks and Vault demos. Sweep sizes and subpixel
positions; a single fixed-size test missed the residual marks. Use a material-relative
near-black threshold plus non-finite checks: a pure-black-only detector missed dark
brown artifacts. Editor screenshots and successful compilation alone cannot validate
this bug. Check that the six DXC directives, precision opt-ins and Boulder branch
survived before changing textures or shared rendering paths.
