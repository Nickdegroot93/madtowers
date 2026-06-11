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
- Ground sprite (128 px = one unit): `plateau.png`, one 256×96 tile, tiled by
  code to the floor width (design it seamless, with outlined end caps). The
  plateau is the only floor visual — scenery is the backdrop's job, never
  attached to the floor.
- Silhouette corner radius: **22 px** on blocks (≈ 8.5% of a cell); concave
  corners stay sharp. Ground top corners ~20–24 px.
- One sprite per tetromino shape, drawn in spawn orientation. No per-cell
  randomization, ever.

**Outline**
- Block pieces: every silhouette has a closed outline, **13–14 px** thick (at
  256 px/cell), colored **the local base color at 30% value** — never pure
  black, never a different hue.
- Plateau strips: strong edge lines and end caps in a darker shade of the
  base (30–50% value per theme). Edges stay crisp and axis-aligned; the
  invariant is "darker shade of itself", not the exact strength.

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
- Background −100 · hills/scenery −86…−83 · ground skin −50 · placement beam −10 · blocks 0.

**Global post-processing (the cross-theme glue)**
- One stack over every theme (`PostFxController`): soft vignette (0.22), gentle bloom
  (0.35 @ 0.9 threshold — lasers/sun/glow bleed light), +8 saturation / +6 contrast.
  Themes never override it; it's what makes different palettes read as one game.

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
