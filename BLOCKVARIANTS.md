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
  Dizzy, Stubborn, Feather differ only by serialized stats/material).
- A variant with a **look** adds `<Name>BlockSkin` from `<Name>BlockData.OnApplied`.
- A variant with **behaviour** adds `<Name>BlockBehaviour` (or acts directly) from `OnLocked`.

The data asset's `m_Script` decides which `BlockData` (sub)class it is — that's the only wiring.

---

## 2. The catalog

Look status: ✅ procedural skin · 🟡 has a feel moment but only a colour tint · 🔴 colour tint only, look pending.

| Variant | Mass | Behaviour | Look (colour / texture / motion) | Look | Files |
|---|---|---|---|---|---|
| **Normal** | 1 | — | the chapter's own brick art | n/a | `Normal.asset` |
| **Boulder** | 4 | very heavy (strains the tower); heavy landing **slam** | dark cracked basalt, matte, **no idle**; slam = squash + hit-stop + camera kick | ✅ | `BoulderBlockData`, `BoulderBlockSkin`, `Boulder.shader` |
| **Anchor** | 1 | **freezes static** where it lands (permanent terrain) | gunmetal riveted plate, slow sheen; lock = rivet/rim **glint** + settle | ✅ | `AnchorBlockData`, `AnchorBlockSkin`, `Anchor.shader` |
| **Vine** | 1 | on land **welds instantly** to every block it touches, and **creeps vines onto each** | keeps the **chapter colour** + green/woody vine overlay (stems + leaves), grow-in + sway, per-instance variety | ✅ (overlay) | `VineBlockData`, `VineBlockSkin`, `VineBlockBehaviour`, `Vine.shader` |
| **Magma** | 1 | on land **melts** into one stone Pip per cell (conforms to terrain) | molten black/red, bloom glow, gentle wobble | ✅ | `MagmaBlockData`, `MagmaBlockSkin`, `MagmaMelt`, `MagmaBlobVisual`, `Lava.shader` |
| **Bomb** | 1 | on land **detonates** (fuse), dropping neighbours' support; blast = CFXR orange explosion + camera punch, each neighbour breaks with shard shatter + smoke puff | near-black riveted iron **powder-keg** casing; seams glow from a faint idle ember to white-hot; fuse = accelerating heartbeat + rising tremble + pre-flash | ✅ | `BombBlockData`, `BombBlockSkin`, `BombBlockBehaviour`, `Bomb.shader` |
| **Ice** | 1 | slippery (low-friction `IceSurface` material) | reuses the **Freeze** ability's Frost material — translucent cyan ice pane: glass bevel, cloudy mottle, branching frost **cracks** (per-cell pattern); born fully iced, **dead still**; chapter colour shows faintly through (overlay). Distinct from Feather: cold + cracked + still vs warm + soft + floating | ✅ (overlay) | `IceBlockData`, `IceBlockSkin` (reuses `Frost.shader`) |
| **Dizzy** | 1 | **inverts** left/right steering | inset pink-marble **vortex** per cell over the kept chapter art; churns and periodically **reverses** direction (the on-block cue for the flip) | ✅ (overlay) | `DizzyBlockData`, `DizzyBlockSkin`, `Dizzy.shader` |
| **Stubborn** | 1 | **cannot rotate** | rusted iron **gear bound by a chain + locking pin** per cell over the kept chapter art (chain runs continuous across the piece); idle strains; on a denied rotate the gear **lurches against the chain and springs back** with a spark at the pin, and the whole brick gives a tiny **flinch** in the pressed direction (visual-only, falling-piece only — never the body, I1) so it visibly tries to turn but can't | ✅ (overlay) | `StubbornBlockData`, `StubbornBlockSkin`, `Stubborn.shader` |
| **Feather** | 0.25 | very light — shoved around by later landings | **translucent** frosted cloud-glass (see-through centre, frostier rim, soft glowing edge) — light because you see through it; keeps the brick bevel; a couple of faint **down wisps** suspended inside; perpetually **floats + sways**; soft landing **flutter**, no slam | ✅ | `FeatherBlockData`, `FeatherBlockSkin`, `Feather.shader` |
| **Tremor** | 1 | on land **shakes the whole tower** — a short shake burst (velocity kicks radiating from the brick, per-block shear topples bad placements); lock = shockwave ring + flash + camera kick + ground-dust puff | warm ochre **fault-stone** that never holds still: micro-buzz (calms a few seconds after landing) + amber fault cracks with a travelling pulse; lock discharge = ring + squash | ✅ | `TremorBlockData`, `TremorBlockSkin`, `TremorBlockBehaviour`, `Tremor.shader` |
| **Bullet** | 1 | projectile — destroys the dynamic block below on lock; `counts:false`, `costsLife:false` | own `Block_Bullet` prefab + impact FX | ✅ (own prefab) | `BulletBlockData`, `BulletImpact` |

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
| `ConfigureCell(index, col, row, overlay, mpb)` | optional per-cell material props — Magma's black/red parity, Vine's seed + root direction |
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
     `OnLocked` to add behaviour. Respect [BLOCKS.md](BLOCKS.md): set the two flags; any code destroying a
     placed block calls `GameManager.RemovePlacedBlock`.
2. **Look** (optional). Write `<Name>BlockSkin : BlockVariantSkin` (override `MaterialResource`,
   `HidesChapterArt`, optional `ConfigureCell`, a `LateUpdate` for motion) + a procedural
   `Assets/Resources/<Name>.shader`. Keep colours fixed (theme-independent).
3. **Test.** Custom Game → **Block Variants** → set `<Name>` to **1.00** → Start. It's auto-discovered
   (CUSTOMGAME.md) — no list to edit.
4. **Introduce in play** — pick one:
   - **Ambient** (a level/chapter): add an `AmbientBlockVariantChance { variant, chancePerBlock }` to a
     `GameModeConfig` — a per-piece roll.
   - **Ability**: `ApplyVariantConsumable` (tap → active piece becomes the variant), `NextBlockVariantPowerUp`
     (instant), or `BlockVariantChancePowerUp` (stacking % passive). New ability **assets**, no code (ABILITIES.md).
   - **Targeted**: `Spawner.ApplyVariantToNextBlock` / `BlockController.ApplyData` (how Suspension turns a
     chosen landed block into an Anchor).

**Remove from play:** delete the ambient entry / drop the ability from the mode (or ban it via
`LevelDefinition.bannedAbilities`). **Delete a variant entirely:** remove its asset + code + shader, then
grep the asset GUID and clear any ambient/ability references.

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
  per-variant tints (orange Stubborn, pink Dizzy, …) were removed once those blocks got real overlays. A
  non-white `colorTint` is reserved for a **deliberate, block-specific** colour you actually want in all
  chapters — never a default differentiator.
- **Reuse the base:** don't re-roll the per-cell overlay loop or material loading — `BlockVariantSkin`
  owns them. New shared cell behaviour goes in the base, not copied per skin.
