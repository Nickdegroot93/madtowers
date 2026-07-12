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

---

## 2. The catalog

Look status: ✅ procedural skin · 🟡 has a feel moment but only a colour tint · 🔴 colour tint only, look pending.

| Variant | Mass | Behaviour | Look (colour / texture / motion) | Look | Files |
|---|---|---|---|---|---|
| **Normal** | 1 | — | the chapter's own brick art | n/a | `Normal.asset` |
| **Boulder** | 4 | very heavy (strains the tower); heavy landing **slam** | faceted mid-grey **granite** — posterized rock plates with carved crevices (lit lower lips) + mica flecks; matte, **no idle**; slam = squash + hit-stop + camera kick | ✅ | `BoulderBlockData`, `BoulderBlockSkin`, `Boulder.shader` |
| **Anchor** | 1 | **freezes static** where it lands (permanent terrain) | navy iron plate with a bolted **X cross-brace**, domed centre hub + corner rivets, slow sheen — it visibly clamps itself down; lock = rivet/rim **glint** + settle | ✅ | `AnchorBlockData`, `AnchorBlockSkin`, `Anchor.shader` |
| **Vine** | 1 | on land **welds instantly** to every block it touches, and **creeps vines onto each** | keeps the **chapter colour** + vine overlay: dark-outlined winding stems + layered shaded leaf clusters (dark rims, bright tip lobes, per-leaf value variance), grow-in + sway, per-instance variety | ✅ (overlay) | `VineBlockData`, `VineBlockSkin`, `VineBlockBehaviour`, `Vine.shader` |
| **Magma** | 1 | on land **melts** into one stone Pip per cell (conforms to terrain) | charcoal **crust riven by a molten vein network** (bloom-emissive white-hot cores, warm halo bleed, per-cell vein layout via `_Seed`, hot/cool cells alternate subtly), breathing glow + gentle wobble | ✅ | `MagmaBlockData`, `MagmaBlockSkin`, `MagmaMelt`, `MagmaBlobVisual`, `Lava.shader` |
| **Bomb** | 1 | on land **detonates** (fuse), dropping neighbours' support; blast = CFXR orange explosion + camera punch, each neighbour breaks with shard shatter + smoke puff | near-black iron **powder-keg**: two riveted reinforcement bands + a recessed **fuse-porthole ember** in each cell; jagged radial cracks split outward and the glow climbs to white-hot as the fuse runs (accelerating heartbeat + pre-flash) | ✅ | `BombBlockData`, `BombBlockSkin`, `BombBlockBehaviour`, `Bomb.shader` |
| **Ice** | 1 | slippery (low-friction `IceSurface` material) | reuses the **Freeze** ability's Frost material, tuned **glacial blue** for bricks (IceBlockSkin lowers `_ColorPreserve` per cell — Freeze keeps the victim's colour, Ice reads properly blue): translucent pane, glass bevel, branching frost **cracks** (per-cell pattern); born fully iced, **dead still**; a hint of chapter colour shows through (overlay). Distinct from Feather: cold + cracked + still vs warm + soft + floating | ✅ (overlay) | `IceBlockData`, `IceBlockSkin` (reuses `Frost.shader`) |
| **Vortex** | 1 | **inverts** left/right steering | inset **galaxy whirlpool** per cell over the kept chapter art — void-indigo spiral arms, starlight vein cores, a cyan energy band, orbiting star specks and a deep dark centre eye; churns and periodically **reverses** direction (the on-block cue for the flip) | ✅ (overlay) | `VortexBlockData`, `VortexBlockSkin`, `Vortex.shader` |
| **Locked** | 1 | **cannot rotate** | aged-iron **gear bound by a chain + locking pin** per cell over the kept chapter art (cool steel glints — the only warm colour is the refusal spark; chain runs continuous across the piece); idle strains; on a denied rotate the gear **lurches against the chain and springs back** with a spark at the pin, and the whole brick gives a tiny **flinch** in the pressed direction (visual-only, falling-piece only — never the body, I1) so it visibly tries to turn but can't | ✅ (overlay) | `LockedBlockData`, `LockedBlockSkin`, `Locked.shader` |
| **Feather** | 0.25 | very light — shoved around by later landings | warm cream **plume pillow** — overlapping cosine-scalloped feather shingles with soft crescent shadows, extra-round corners, whisper-fine barb strands and tiny down-flecks drifting upward; **floats + sways** while falling; soft landing **flutter**, no slam, then the float eases out and it sits dead still — a placed feather doesn't hover (Nick's call, July 2026). Light because it's downy — the deliberate opposite of Ice's cold gloss | ✅ | `FeatherBlockData`, `FeatherBlockSkin`, `Feather.shader` |
| **Tremor** | 1 | on land **shakes the whole tower** — a short shake burst (velocity kicks radiating from the brick, per-block shear topples bad placements); lock = shockwave ring + flash + camera kick + ground-dust puff | warm ochre **fault-stone** that never holds still: micro-buzz (calms a few seconds after landing) + amber fault cracks with a travelling pulse; lock discharge = ring + squash | ✅ | `TremorBlockData`, `TremorBlockSkin`, `TremorBlockBehaviour`, `Tremor.shader` |
| **Sandstone** | 1 | **load-bearing limit** — reads the weight of the stack resting on it (support graph walked transitively via thin probes at the resting plane above each **top-exposed** cell; each direct rester's branch is **divided by its distinct support count**, so a pure tower presses exact while a bridge/tucked overhang presses only its share; structural on purpose: the settle system force-sleeps quiet stacks and Box2D reports no contact impulses for sleeping islands, so a solver-force gauge reads zero exactly when it matters). Break limit is authored in **normal-brick weights** (`breakLoadBrickWeights`, default 3: two bricks sit, the third breaks it; a Boulder (4×) crushes instantly, Feathers (0.25×) barely count; an internal 0.45 bw margin makes the Nth brick actually cross the smoothed threshold). **Static bodies shield**: a frozen block above is self-supporting terrain — contributes nothing and carries its own stack, so Freeze is a legitimate rescue. **Sandstone never burdens sandstone** (the maw-on-maw precedent): each sand layer carries only what rests directly on it, so sand stacks safely on sand. Probes always run along **world up** — gravity does not rotate with the piece (a quarter-turned piece probing its local up read its side-neighbours' towers as phantom load). Damage is smoothed + **ratcheted** (cracks never heal; current load also drives sand trickle); crack SFX at each damage third; crumble = shatter + burst + small camera kick through the standard destruction flow (BLOCKS.md accounting, count −1), **no life charged** — the collapse above is the punishment. Frozen sandstone stops reading load entirely (preserved stone) | warm layered **sediment stone** (fixed look in every chapter): strata + grain body; damage grows a **Voronoi cracked-earth network** — plates fracture one by one (per-plate reveal order) and every open crack widens with damage; fine sand **trickles** from open cracks while under load; edges chip at high damage; while the **current** load sits past ~85% of the limit the whole brick **shivers** (visual cells only, I1; stops when the weight comes off or the brick is frozen — the cracks stay) — the unmistakable "one more and it bursts" | ✅ | `SandstoneBlockData`, `SandstoneBlockSkin`, `SandstoneBlockBehaviour`, `Sandstone.shader` |
| **Pyramid** | 1 | **no flat top** — a block TYPE, not an appliable variant: a 3-column monument SHAPE (`Block_Pyramid`: three real 1×1 base cells — grid anchor, beam footprint, COM — plus the pyramid top as a `PolygonCollider2D` on the "Body" child carrying `LandableSlope`, its ~42/44° faces opt in under the 0.7 landing gate, see PHYSICS.md; apex a single point offset 0.07 u off-centre so a column-aligned piece ALWAYS tips — symmetric apexes let perfectly-aligned pieces balance and stack). Its `BlockData` exists only as the stats/Vault card: `canRotate:false`, `BlockDefinition.lockDefaultData` keeps ambient rolls / variant overrides from replacing it, and `ContentCatalog.IsShapeBound` hides it from the Custom Game variant sliders (it's toggled in the Blocks list; it still gets a Vault card). Anything landing on the faces locks, goes Dynamic, and slides off — Giza Dusk's signature brick, a real placement it punishes you for building on | fixed Giza sandstone in every chapter — straight ashlar base course + pyramid-top masonry + a gold capstone tip, shipped as ONE `piece_Pyramid.png` in `Skins/Classic` (the file-by-file fallback IS the fixed look; `Tools/generate_pyramid_sprite.py`), no shader/skin code | ✅ (sprite) | `Block_Pyramid.prefab`, `Pyramid.asset` (plain BlockData), `Block_Pyramid.asset` definition, `LandableSlope.cs` |
| **Maw** | 1 | falls dormant, then on land **devours any block placed on it, forever** — each devour shatters the prey (removed from the live count, BLOCKS.md) and **costs a life** (`GameManager.LoseLifeToHazard`), so you build *around* it. **Maws never eat each other**, so two in a row stack safely — and on land a maw **welds to every maw it touches** (unbreakable `FixedJoint2D`, the Vine weld pattern but maw-only & permanent), so a stack of maws fuses into one rigid "huge maw" that can't be toppled by loose leaning. Because they're one welded cluster, maws are also **excluded from fly-out targeting** (Extract/Suspension): they stay put while the rest of the tower spreads, and can't be selected. `counts:true`, `costsLife:true` | dark-violet fleshy brick that IS a monster: falling, it shows only a pressed-shut **mouth seam** on its world-up face; on landing a toothy ivory **grin over a dark gullet + two mismatched eyes** wake up (whatever rotation it landed in), and only the piece's truly **exposed** top cells show the face — a covered cell (incl. one under a stacked maw) stays a smooth block. Each devour **gapes the jaw up past the brick's top edge and slams it shut** (eyes squeeze, a tongue shows behind the lower teeth) with the lash + a subtle one-shot **disintegrate** puff | ✅ | `MawBlockData`, `MawBlockSkin`, `MawBlockBehaviour`, `Maw.shader` |

Every special brick now carries a procedural `BlockVariantSkin` look — the backlog is clear. New variants
follow the recipe in §4.

---

## 3. The look layer — `BlockVariantSkin`

`BlockVariantSkin` (abstract base) owns the per-cell overlay machinery; a brick's skin subclass supplies
only what's unique:

| Member | Purpose |
|---|---|
| `MaterialResource` | Resources path of the procedural shader/material, e.g. `"Anchor"` (cached per name) |
| `HidesChapterArt` | **replace** the chapter art (Anchor/Boulder/Magma → `true`) or **overlay** on top, keeping the chapter colour (Vine → `false`) |
| `CellScale` | overlay quad size as a multiple of the cell (default `1`). `>1` lets a skin draw **past** the brick edge (Maw's tentacles overhang the top); the contract is then that the shader insets its body to `0.5/CellScale` so the brick still tiles exactly one cell |
| `ConfigureCell(index, col, row, overlay, mpb)` | optional per-cell material props — Magma's hot/cool parity + vein seed, Vine's seed + root direction, Maw's seed + `_BodyHalf`, Ice's blue-tune (`_ColorPreserve`) |
| a `LateUpdate` | the motion, using `Cells` / `BaseScales` / `BasePositions` (scale + position) and `SetCellsFloat` (animated shader props) |

`BuildCells()` does the rest (find the sort renderer, optionally hide chapter art, scan cell colliders,
size + place an overlay quad per cell, call `ConfigureCell`). It is idempotent. So a new skin is ~30 lines.

The **shader** is a URP sprite shader (crib `Lava.shader` / `Anchor.shader`): an SDF rounded box matching
the brick silhouette, an in-hue bevel, the procedural surface, and optional `_Time` animation + props the
component drives (`_LockFlash`, `_Growth`). Colours are **fixed** = theme-independent (ART.md §13).
Replace-mode skins draw the whole brick; overlay-mode (Vine) draws sparse and alpha-blends over the kept art.

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
  colours, not the chapter tint. Exception: a block that **decomposes into normal bricks** (Magma's melt)
  leaves chapter-skinned cells.
- **`colorTint` is NOT the standard way to mark a variant.** Leave it **white** so the brick keeps its
  real chapter colour, and express identity through the procedural overlay, which reads on whatever colour
  the chapter already uses (the dark seating outline carries it on any palette). A fixed full-brick tint
  recolours the brick in *every* chapter, which fights muted/dark chapter palettes — that's why the old
  per-variant tints (orange Locked, pink Vortex, …) were removed once those blocks got real overlays. A
  non-white `colorTint` is reserved for a **deliberate, block-specific** colour you actually want in all
  chapters — never a default differentiator.
- **Reuse the base:** don't re-roll the per-cell overlay loop or material loading — `BlockVariantSkin`
  owns them. New shared cell behaviour goes in the base, not copied per skin.
