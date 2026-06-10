# MadTowers Visual Style Bible

Every theme (Classic, Ice, Haunted, Desert, Neon, …) must read as the same
game. That works because some settings are **invariant** — they never change,
no matter the theme — while everything else is the theme's playground.
The generators in `Tools/` are the single source of truth; when a constant
here changes, change it in every generator (and the C# constant it mirrors).

## Invariants (identical in every theme)

**Geometry**
- Block sprites: **256 px = one cell**, 32 px bleed margin (canvas sizes fixed
  per shape; see ART.md §1).
- Ground sprites: **128 px = one unit**, canvas 2080×1568, flat plateau =
  **middle 85%** of canvas width, surface at the exact top edge
  (mirrored by `PlayAreaController.GroundPlateauWidthFraction`).
- Silhouette corner radius: **22 px** on blocks (≈ 8.5% of a cell); concave
  corners stay sharp. Ground top corners ~20–24 px.
- One sprite per tetromino shape, drawn in spawn orientation. No per-cell
  randomization, ever.

**Outline**
- Every silhouette has a closed outline, **13–14 px** thick (at 256 px/cell).
- Outline color is always **the local base color at 30% value** — a dark
  version of the thing it outlines, never pure black, never a different hue.

**Lighting (the light always comes from straight above)**
- Vertical gradient on every body: ~**+10–16% brightness at the top edge**
  falling to ~**−35–50% at the bottom**.
- Bevel highlight just inside the top outline: **+20–24%** over a 16–26 px
  band. Bottom-facing inner edges get a subtle −16% shade.
- Grain/noise: **±5%** per-pixel brightness, always on, never stronger.

**Surface language**
- Cell seams are hinted by **bold dark cracks** (~9 px, −50% brightness),
  jittered, never straight grid lines. Thin hairline cracks (~5 px, −30%)
  wander elsewhere. In a theme where cracks make no sense (Neon), the seam
  *placement* stays but its rendering flips (e.g. glowing lines) — the motif
  "pieces are assembled from cells" must stay readable.
- The 7 shapes keep their **hue identities** in every theme: I cyan-family,
  O yellow/gold, T purple, S green, Z red, J blue, L orange. A theme may
  shift saturation/value (Haunted = desaturated, Ice = pale) but never
  reassign hues between shapes.

**Composition (sorting orders)**
- Background −100 · ground skin −50 · placement beam −10 · blocks 0.

## Theme variables (what makes a theme)

- Palette treatment: saturation/value curve over the invariant hues
  (Classic: S 45–75%, V 55–90%; Haunted: S 20–40%; Ice: V 75–95%; Neon:
  dark fills + bright outline/seam glow).
- Block body material: stone cracks, ice sheen, sandstone, circuit lines —
  any texture, rendered within the invariant outline/bevel/gradient frame.
- Ground motif: grassy hill, stone tower, dune, haunted house, glacier —
  any shape honoring the plateau contract above.
- Background art, music, particle tints.

## Process

- All block/ground art is generated: `Tools/generate_piece_sprites.py`,
  `Tools/generate_ground_sprite.py`. A new theme = a preset (colors + motif
  parameters) in those scripts writing to `Assets/Resources/Skins/<Theme>/`,
  never a fork of the pipeline.
- Hand-made override PNGs must follow every invariant above to be accepted.
- Judge all art in-game at gameplay zoom, not at full resolution.
