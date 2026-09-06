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
- Ground sprites (128 px = one unit): `ground_fill.png`, a 128×128 seamless
  material tile (chapter-specific masonry/rock) that `FloorTerrain` tiles from
  every floor column's top down past the screen bottom, and `ground_cap.png`,
  a 256×64 horizontally-seamless walkable cap band (baked top outline +
  lower edge: carved stone in Jungle, legacy scallops elsewhere). The floor is GROUNDED terrain — columns that sink
  into a chapter-tinted fog bank — never a floating strip. Scenery is the
  backdrop's job, never attached to the floor. (`plateau.png` is legacy.)
- Silhouette corner radius: **22 px** on blocks (≈ 8.5% of a cell); concave
  corners stay sharp. Ground top corners ~20–24 px.
- One sprite per tetromino shape, drawn in spawn orientation. No per-cell
  randomization, ever.

**Outline**
- Block pieces: every silhouette has a closed outline, **17 px** thick (at
  256 px/cell), colored **the local base color at ~20–28% value, desaturated
  30% toward gray** — reads near-black but never pure black, never a
  different hue. (Variant shaders mirror this as `_OutlineWidth 0.066`,
  outline colour `lerp(tint, luma, 0.30) * 0.22`.)
- Ground terrain: the cap band bakes a near-black top outline (the landable
  line), and exposed column sides get near-black silhouette strips at
  runtime — **one weight everywhere: 8 px at 128 px/unit (0.0625 u), fully
  opaque**, matching the blocks' 17 px / 256. Edges stay crisp and
  axis-aligned; the invariant is "darker shade of itself", not the exact
  strength. Never mix widths or alphas on the floor: three line weights read
  as old-school pixel art (2026-09-01).

**Lighting (the light always comes from straight above)**
- Vertical gradient on every body: **1.13× at the top edge falling to 0.77×
  at the bottom** (curve `1.13 − 0.36·t^1.15`).
- Embossed bevel just inside the outline, over a **26 px band** (variant
  shaders: `_BevelWidth 0.102`): a faint **−9% AO ring** all around, a strong
  top rim **blended 72% toward the base color pushed ~40% to white** (hue
  kept — dark tints stay mostly in-hue), a **−26% bottom** inner shadow, and
  **−12% sides**. This is what makes bricks read as 3D.
- Body mottling: multi-octave value noise, ~**±8%** brightness, feature size
  ~110/52/22 px. Grain/noise: **±5%** per-pixel brightness on top, always on,
  never stronger.

**Surface language**
- Cell seams are **bold dark carved cracks** (~9 px, −55% brightness),
  jittered, never straight grid lines, and they **run all the way through the
  outline** so every cell reads as its own stone. Cracks are embossed: a
  **+30% lit lip below** each chunky crack, a −13% shadow above. A few short
  **plate cracks** (7 px) anchor to the silhouette edge or branch off seams;
  faint hairlines (~3 px) and sparse pit specks add wear. In a theme where
  cracks make no sense (Neon), the seam *placement* stays but its rendering
  flips (e.g. glowing lines) — the motif "pieces are assembled from cells"
  must stay readable.
- Every theme's pieces are **materials of that chapter's world** (Desert is the
  reference: bone, sand, clay-rose, sage, terracotta, slate, burnt orange — not
  the Classic rainbow re-tinted). The 7 hue FAMILIES stay as loose anchors so a
  returning player still reads I as coolest and L/Z as warmest, but each colour
  is named for something in the scene, saturation sits well below Classic
  (Neon exempt — neon reads as light, not pigment), and the whole set shares the
  chapter's cast. Never reassign hues *between* shapes, and keep every shape
  pair at RGB distance ≥ ~40 (Desert's proven floor — validate when authoring).

**Composition (sorting orders)**
- Background −100 · hills/scenery −86…−83 · placement beam −60 · loss lasers −57…−51
  (Sacrifice/Hardline warning lines: behind the ground and blocks, in front of the
  backdrop) · ground fill −50 ·
  ground mottle −49 · ground atmosphere −48 · caps/outlines −47 · ground fade −45 · back fog −44 ·
  blocks 0 · front fog 43 (pieces falling into gaps sink INTO it; all fog = GroundFog shader). Pockets are
  REAL holes in the fill geometry (backdrop shows through), outlined only on
  their solid edges.

**Ground material relief (all fifteen chapters, September 2026)**
- Ground structure follows the chapter: masonry, irregular stone, concrete, metal
  cladding, timber, plaster, rock bedding or ice. Keep the approved palettes; do not
  apply a universal brick bond. Share straight-above light, depth and restrained wear.
  The cap's 13 px bevel at 128 px/unit matches the bricks' 26 px/256; the ground
  contour stays 8 px and fully opaque.
- Cut-face shading sits inside exposed boundaries, with a lit pocket floor and a
  shadowed ceiling/underside. No coloured silhouette rim or new sorting layer.
- Chapter atmosphere overlay strengths are ambient **0.20**, mottle **0.10**, wash
  **0.09**, bottom haze **0.30**, sky light **0.06** (sky fraction remains **0.22**).
  Classic and unknown folders keep the legacy overlay values; see FLOORS.md §2.
  Fog rendering is independent and unchanged.
- Carved island art stays upright so the baked light remains above; legacy island
  materials keep their quarter-turn presentation. All island geometry and gameplay
  random draw counts remain unchanged.

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

- All block/ground art is generated: `Tools/generate_piece_sprites.py`
  (needs numpy + Pillow), `Tools/generate_ground_sprite.py`. A new theme = a preset (colors + motif
  parameters) in those scripts writing to `Assets/Resources/Skins/<Theme>/`,
  never a fork of the pipeline.
- Hand-made override PNGs must follow every invariant above to be accepted.
- Judge all art in-game at gameplay zoom, not at full resolution.
