# MadTowers Art & Asset Guide

This is the spec for all images Nick supplies. Everything else (randomization,
tinting, parallax, particles, animations) is done in code. Drop finished files
into the `Assets/Art/` subfolder named by the section below; chapter menu
backgrounds live in `Assets/Art/Chapters/`.
**Exception:** block skins live in `Assets/Resources/Skins/<Theme>/` —
import settings are applied automatically to anything dropped there.

General rules for every image:
- **Format:** PNG. Transparency only where the spec says "transparent".
  **Exception:** opaque photographic art (level thumbnails, chapter backgrounds)
  ships as downscaled JPG — far smaller, no alpha to lose. See [images.md](images.md).
- **Color:** Where the spec says "grayscale", paint in white/gray only — the
  game tints it at runtime, so any color you bake in will distort the tint.
- **Sizes** are exact unless marked "~". Power-of-two sizes are nice but not
  required.
- If you decide on a **pixel-art** style, say so before exporting — sizes and
  import settings change (much smaller images, point filtering).

---

## 1. Whole-piece block sprites (Tricky-Towers style)

Each tetromino shape gets **one sprite covering the whole piece** (`piece_I.png`
… `piece_Z.png`), color baked in. Every T looks identical — no randomization;
variety comes from rotation and the 7 shapes/colors.

**These are generated, not painted.** `Tools/generate_piece_sprites.py` renders
all 7 (silhouette, outline, gradient, bevel, seam cracks) per entry in its
`THEME_PRESETS` table into `Assets/Resources/Skins/<Chapter>/`. A chapter without
an entry falls back to the Classic pieces automatically. **New block look for
a chapter = one preset dict** (7 hue-identity-preserving colors + an outline
factor) and a rerun — nothing else.

Hand-made art can still override any shape: export a transparent PNG at
**256 px per cell + 32 px bleed** (exact canvases: I 1088×320, O 576×576,
others 832×576; paint guides in `ArtTemplates/template_piece_X.svg`, drawn in
spawn orientation — T stem up, L corner top-right, J top-left, S top row
right, Z top row left) and overwrite the file. Import settings are applied
automatically to `piece_*` files in any `Assets/Resources/Skins/<Theme>/` folder.

## 2. Special block looks (procedural, theme-independent)

Special bricks (Anchor, Boulder, Vine, Magma, …) do **not** use a flat icon/emblem overlay — that
approach was tried and dropped (it read as a sticker, not part of the brick). Each instead gets a
**fixed, procedural look** drawn per cell by a small URP shader and a `BlockVariantSkin` subclass, so it
needs **no hand-authored art**. The catalog and the full "add a brick" recipe live in
[BLOCKVARIANTS.md](BLOCKVARIANTS.md); the theme-independence rule is §13 below.

## 3. Backgrounds / backdrop packs

Each chapter has a `BackdropPreset` asset (`Assets/Data/Backdrops/`, assigned
to the chapter's `backdrop` field). Backdrops can be either procedural, imported
sprite layers from an Asset Store pack, or a mix of both. The full workflow for
landscape parallax packs in portrait vertical gameplay lives in
[BACKDROPS.md](BACKDROPS.md).

Procedural backdrop features:

- **Sky**: vertical gradient glued to the camera, crossfading to a second
  "high altitude" color pair as the tower climbs (`altitudeFadeMeters`).
- **Clouds**: procedural sprites drifting horizontally, recycled around the
  camera — infinite height coverage from zero assets. Count/color/speed/scale
  per preset.
- **Hills**: ground-level silhouettes with slight parallax that sink out of
  view as you climb (the ground vanishes; only sky and clouds remain).
- **Ambient particles**: falling, swaying soft dots — snow, petals, embers are
  the same system with different color/size/speed numbers.
- **Sky shimmer**: optional altitude variation — the low/high blend oscillates
  gently while climbing (darker, lighter, darker…) instead of fading once.
- **Sun**: optional faint disc at a configured height, drifting slowly relative
  to the camera so it floats through view over a long band of the climb.
- **Ground props**: procedural cacti (etc.) flanking the floor, sinking away as
  the tower climbs.
- **Imported sprite layers**: full background plates can be scaled to cover the
  portrait camera while separated far/mid/near scenery layers use vertical
  parallax.

A chapter without a preset gets the classic dark sky. To design a new chapter's
backdrop, give Claude an **inspiration image** (screenshot, painting, photo) —
palette and mood translate directly into preset values. For Asset Store packs,
import the pack and configure its layers according to [BACKDROPS.md](BACKDROPS.md).

## 4. Ground / floor

**Generated, not painted** — `Tools/generate_ground_sprite.py` renders each
chapter's ground set into `Assets/Resources/Skins/<Chapter>/`:

- `ground_fill.png` — a **128×128 seamless masonry tile** (1×1 u; running-bond
  0.5×0.25 u bricks with dark mortar, per-brick tone steps and a top-lit bevel).
  `FloorTerrain` tiles it from every floor column's landable top down past the
  screen bottom — the floor is **grounded terrain**, never a floating strip.
- `ground_cap.png` — a **256×64 horizontally-seamless cap band** (2×0.5 u) laid
  along every walkable top: baked near-black outline at the landable line, then
  a top-lit band (stone/sand/moss/grass per chapter, optional flecks) with a
  scalloped, shadowed lower edge hanging over the masonry.
- `plateau.png` — **legacy** (the old floating strip); still generated but no
  longer used by the floor.

At runtime `FloorTerrain` (built by `PlayAreaController` from the mode's
`floorSegments` — per-column heights, steps, valleys, free-standing pillars,
and carved 1×1 nudge-in **pockets**; the authoring bible is FLOORS.md) adds a
depth-shade ramp, silhouette outline strips on exposed sides (split around
pocket openings — pockets are REAL holes cut from the fill, the backdrop shows
through, outlined on their solid edges), and a bottom fade into a chapter-tinted **fog bank**
(camera-following bands + drifting world wisps, behind the ground and IN
FRONT of the blocks, so pieces falling into pillar gaps sink into it). Fog
colour: `BackdropPreset.groundFogColor`, auto-derived from the near-hill
colour when unset.

The terrain matches the landable colliders exactly — what you see is what you
can land on. There are deliberately **no buildings under the floor**: anything
decorative near the platform risks reading as a landing surface. Chapter
scenery (hills, dunes, mountains, props) lives in the backdrop system (§3).

**Floating support islands** — the same script renders `island_1..3.png`
(128×128 px = one 1×1 cell, 128 px/unit) per chapter: the sky stones pieces can
land on (LEVELS.md has the spawn rules). Same material language as the plateau
(base color, edge-line border ring, grain) but deliberately **symmetric — no
"top"**: the spawner rotates each cell in random 90° steps, so 3 variants give
12 looks per chapter. Variants stay subtle: 1 plain, 2 hairline crack, 3 pebble
flecks. The spawner picks variant + rotation randomly per cell
(`StaticSupportIslandManager.ConfigureIslandCellVisual`). Hand-made
override: transparent PNG at 128 px/unit whose flat top spans exactly the
**middle 85%** of the canvas width, surface at the exact top edge. Shared
style rules: see STYLE.md.

## 5. HUD & menus

| File | Size | Transparent? | Notes |
|---|---|---|---|
| `panel.png` | 256×256, ~48px corner radius | Yes | One rounded panel, **9-sliced** in code (corners stay crisp, middle stretches) — used for HUD cards, popups, level-select. Light/neutral so code can tint per chapter. |
| `button.png` | 256×128, ~32px corners | Yes | Same idea, for buttons. A pressed variant (`button_pressed.png`) is optional — code can darken instead. |
| `icon_heart.png` | 128×128 | Yes | Lives. |
| `icon_height.png` | 128×128 | Yes | Height arrow/flag. |
| `icon_trophy.png` | 128×128 | Yes | Score/best. |
| `logo.png` | ~900×500 | Yes | Game title art for the main menu. |
| Font (`.ttf`/`.otf`) | — | — | Optional. Pick any font you like (check license); code converts it to a TextMeshPro asset. Otherwise Claude picks a free one. |

## 6. Particle sprites (optional — code can generate basic ones)

| File | Size | Transparent? | Used for |
|---|---|---|---|
| `fx_dust.png` | 128×128 | Yes | soft puff — block landing |
| `fx_spark.png` | 64×64 | Yes | small star/spark — scoring, milestones |
| `fx_smoke.png` | 128×128 | Yes | wisp — bomb aftermath |

White/grayscale; code tints them.

## 7. Per-chapter reskins

A chapter = one folder with the same file names:

```
Assets/Resources/Skins/Classic/   piece_I..Z, plateau, island_1..3, (optional) laser
Assets/Art/Chapters/              <chapter-slug>.png menu backgrounds
(<Theme2>: same file names in sibling folders, different art)
```

Optional per-chapter `laser.png`: the height-limit line for puzzle levels. Horizontal
strip, ~1024×32–64 px (128 px/unit — height is kept as authored, length is stretched),
transparent PNG, glow baked in light tones (the level tints and pulses it). Without it,
a clean code-built bar is used.

Code loads the matching skin when a chapter starts. Emblems, HUD, particles can
be shared across chapters or overridden per chapter — only supply what should
differ. Start with **Classic only**; once it looks good, each new chapter is
just "fill the folder again."

---

## 8. Music

Per chapter: 1–N tracks (**OGG preferred**, MP3 fine; convert WAV→OGG from
lossless, never lossy→lossy; matched loudness between tracks), dropped in
`Assets/Audio/Music/` and assigned to the chapter's `musicPlaylist`. Playback:
a **random track opens**, then the rotation is fixed (A → B → A …) while the
level is alive; music survives level restarts within a chapter, **stops on game
over** (a shared game-over jingle is planned), and a retry starts fresh.
Music imports as *streaming* automatically (memory-friendly on phones).
License: CC0, CC-BY (credits screen later), or owned.

## 9. Sound effects

**Generated, not sourced** — two generators, one folder, one player:
- `Tools/generate_elevenlabs_sfx.py` — **the ability/impact SFX library** (July 2026):
  AI-generated with the ElevenLabs sound-generation API from a per-sound prompt table with
  duration matched to the gameplay moment (e.g. `zap_charge` is exactly ZapSession's 3.0 s).
  `export ELEVENLABS_API_KEY=…` then `python3 Tools/generate_elevenlabs_sfx.py`
  (`--only <name>` regenerates one sound — THE tuning loop: tweak prompt, regen, listen).
  Destruction sounds are per-cause (`shatter_zap/bomb/sacrifice/generic`, `maw_crunch`) and
  routed via `ImpactFx.DestroyBlockWithShatter(..., sfx:)`. Never hardcode/commit the key.
- `Tools/generate_sfx.py` — the older stdlib synth (landing thumps, nudges, swooshes).
Playback goes through `SfxPlayer` (pooled, cached, pitch-jittered one-shots; charge-style
clips are played with 0 jitter so their length stays synced to the visual). Iterate by tweaking the parameter dicts,
rerunning, and previewing with `afplay` — no Unity needed. Current set: two
flick-drop impact variants (the picked "round 2" recipe), `impact_soft_01` —
the quiet dull thud (now wired as Zap's wasted-shot feedback; must stay
clearly duller than the shatter), `swoosh_01` — the corner-nudge dash
(band-swept noise through a falling crude bandpass, swell-then-die envelope;
`synth_swoosh`), `pop_01` — a support island materializing under a risen laser
line (the impact recipe with f_end > f_start: a friendly rising blip),
`nudge_thud_01` — a failed nudge's knock (short, higher-pitched than the
landing thumps, hard click: reads as a dry refusal, not a landing),
`impact_shatter_01` — a block being destroyed (bright sharp stone crack; Zap,
Sacrifice, Fission), and `gun_cock_01` — a single gun cock (pull-back click,
slide scrape, slam-home clack; `synth_gun_cock`, the multi-stage mechanical
recipe to copy for future weapon-like abilities).
Hand-made/downloaded WAVs (prefer **CC0**, e.g. Kenney packs)
drop into the same folder and play through the same system. Background music
is the per-chapter playlist system (see §8 and SOUNDS.md).

## 10. Fonts

UI display font: **Rajdhani Bold** (Indian Type Foundry, **SIL OFL 1.1** — license
text ships beside the font at `Assets/Resources/Fonts/OFL.txt`; credit on the future
credits screen). Loaded via `RuntimeUiKit.TitleFont` with a built-in fallback, so a
missing font degrades instead of breaking. HUD numbers stay on TMP's default face.

## 11. HUD top bar (code-built, UIManager)

The in-game top bar is built at runtime in `UIManager.BuildTopBar` — non-obvious
mechanics a future change must respect:
- **Two bar segments**, not one: nothing may render behind the translucent NEXT card
  (it must show the game, not UI). Segments use `RoundedPanelSquareRight` (rounded
  outer corners, square inner edge; the right segment's fill is the sprite rotated
  180°) and tuck `BarSeamTuck` (1px ≈ the card border's half-width) under the card.
- **Safe-area aware**: positioned below `Screen.safeArea`'s top inset (clamped to 10%
  of screen height — the raw value can be garbage during early Awake) and re-applied
  whenever screen geometry changes.
- The scene's `scoreText`/`heightText` TMPs are **reparented** into the bar's stat
  cards (that's why `UIManager.HudRoot()` caches its root before the bar builds).
- The pause button lives in the bar; `PauseMenuController` owns only the menu and the
  `PauseAvailable` visibility predicate.

## 12. Ability icons (the house style — binding for ALL ability art)

Every ability-card illustration comes from `Tools/generate_ability_icons.py`
(pure stdlib, like the piece/sfx generators) into `Assets/Art/Abilities/`
(`icon_<ability>.png`). One render function + one `ARTWORK` entry per ability;
rerun the script to regenerate. The style rules below are a contract — every
future icon follows them so the card grid reads as one set:

- **One bold emblem, centered.** A single readable object that says what the
  ability does (e.g. Zap = a downward laser bolt). No scenes, no text, no
  tiny detail — it must read at HUD-slot size (~96 px).
- **512×512, transparent background**, emblem within the middle ~70% — cards
  and HUD render the sprite untouched, the margin IS the breathing room.
- **Same lighting language as the block sprites** (§1): thick rounded
  silhouette, dark outline (~30% of base color), vertical gradient (lighter
  top), soft top bevel highlight. The shared `shade()` helper in the generator
  does exactly this — use it for every emblem so lighting never drifts.
- **Soft radial glow behind the emblem** (`draw_glow`): quadratic falloff to
  TRUE zero well inside the texture bounds (a clipped tail shows as a square
  halo on dark cards — the same bug the card frame once had).
- **Motion/accents, sparingly:** 4-point sparkles (`draw_sparkle`) and motion
  streaks (`draw_speed_line`) in near-white; 2–3 accents max.
- **Palette is the ability's own**, not its rarity — rarity is already the
  card chrome/header. Neutral silver suits physical objects; saturate only
  when the ability is inherently colored (fire, vines...).
- Icons are wired into the ability `.asset` via the sprite sub-asset ref
  (`fileID: 21300000` + the png's meta guid). PNG metas: copy the island
  template (spriteMode 1, textureType Sprite), PPU irrelevant for UI.

In-game **ability block sprites** (a transmuted piece an ability swaps in, when one
needs its own look) live in the
same generator/folder as `block_<name>.png`, 256×256 at PPU 256 (one cell),
reusing the same `shade()` lighting so they sit naturally next to the
tetromino pieces.

## 13. Special blocks look the same in every chapter (theme-independent)

A **unique/special block** (Magma, Anchor, Boulder, Vine, Ice, Vortex, Locked, Tremor, Feather, Maw,
Bomb — every special brick now carries a procedural BlockVariantSkin look) must be instantly
recognizable and look **identical regardless of the chapter theme** —
it does NOT adopt the chapter's local block art the way normal bricks do.
(Exception: when a special block decomposes into normal bricks — Magma melting
into 1×1 cells — those resulting bricks use the level's ordinary skin; only the
special block *itself* is theme-locked.)

The look overrides the chapter skin in `ApplyData`/`OnApplied`, which run *after*
`ApplyBlockSkin`, so the override always wins:
- **Static look:** set `BlockData.spriteOverride` / `materialOverride`.
- **Animated/procedural look (the standard):** a `BlockVariantSkin` subclass added in `OnApplied` that
  draws a per-cell procedural overlay (`Resources/<Name>.shader`). References: `AnchorBlockSkin` →
  `Anchor.shader`, `BoulderBlockSkin` → `Boulder.shader`, `VineBlockSkin` → `Vine.shader`, `MagmaBlockSkin`
  → `Lava.shader`. The shared base owns the per-cell scaffold; see **[BLOCKVARIANTS.md](BLOCKVARIANTS.md)**
  for the catalog + recipe.

Theme independence comes from the material being procedural/fixed (it ignores the
chapter art and uses only the sprite alpha as the silhouette mask).

## Under the hood: how chapter skins work at runtime (exact pipeline)

What happens when a level loads, in order:

1. **Chapter resolution** — `GameManager.Awake` calls `Campaign.FindChapterOf(selectedLevel)`
   once, then `ChapterSkins.Apply(chapter)` (sets the static `ChapterSkins.Folder`, e.g.
   `Skins/Desert`) and `MusicPlayer.PlayForChapter(chapter)` (playlist looped in order;
   keeps playing across level restarts within the same chapter).
2. **Floor** — `PlayAreaController.ApplyGroundSkin`: `ChapterSkins.LoadPlateau()` resolves
   `<Folder>/plateau` and falls back to `Skins/Classic` per file. The strip is rendered
   with `SpriteRenderer.drawMode = Tiled` at exactly the collider width (end caps kept
   by the 12px sprite border); the original floor bar renderer is disabled. The collider
   is never touched.
3. **Blocks** — each spawned piece (`BlockController.ApplyBlockSkin`) loads
   `ChapterSkins.LoadPiece(shape)` with the same fallback chain; the HUD's "next" ghost
   (`UIManager`) builds a desaturated copy of the same sprite (cached per folder+shape).
   **Support islands** load `ChapterSkins.LoadIsland(1..3)` once at level start
   (`StaticSupportIslandManager.Start`); each spawned cell gets a random variant and a
   random 90° rotation on a visual child (the cell collider never rotates or scales).
4. **Backdrop** — `LevelPresentationController` (on the scene's Background object,
   `[ExecuteAlways]`; world elements split into `LevelPresentationController.Elements`):
   - Resolves `chapter.Backdrop` (a `BackdropPreset`), or `BackdropPreset.Defaults`
     (classic dark sky) when none. Cached per level; re-resolved only on change.
   - **Sky**: two gradient sprites built by `RuntimeSprites.VerticalGradient`
     (curve 0.8, top color fully reached at 60% height), regenerated only on preset
     change and destroyed with their owner (they're HideAndDontSave). The "high"
     gradient overlays the low one with alpha = `Altitude01()` =
     `clamp01(towerHeight / altitudeFadeMeters)` ± the shimmer sine
     (`skyShimmerAmount`, `skyShimmerPeriodMeters`). Camera clear color follows.
     The quad is fitted to the camera **non-uniformly** each frame (uniform scaling
     once blew the 1px-wide gradient to ~4000 units — one flat color on screen).
   - **World elements** (play-mode only, spawned once per scene under
     `BackdropElements`, recycled forever): clouds (style sprite per preset, drift
     ±40% speed variance, gentle sine bob, wrap horizontally at ±1.5× half-width,
     respawn above when fallen 1.6× below view), hills (3 layers, far→near colors
     lerped from the preset's 2, parallax 0.20/0.13/0.06, plus a solid base fill
     anchored below the lowest valley *scaled with zoom* so no cutoff line exists at
     any zoom), sun (fixed screen X, world Y = floor + `sunHeightMeters` + 0.9×climb),
     props (screen-edge anchored with a floor-clearance minimum), particles (fall +
     sway, recycled to the top).
   - **Parallax baseline**: all climb-based offsets measure from the camera's Y at
     backdrop spawn (`_climbBaseY`), NOT from the floor — the camera starts ~11.5
     units above the floor, which once lifted everything.
5. **Laser** (height-limit levels) — `ChapterSkins.LoadLaser()` or the code-built bar.
6. **Post-FX** — `PostFxController` (self-installed, survives scene loads) applies one
   global URP volume to every chapter: vignette, bloom, color grading (values in
   STYLE.md). Re-attaches `renderPostProcessing` to the camera on each scene load.

**Sorting orders** (back → front): sky −100 · sky-high overlay −99 · sun −95 ·
clouds −90 · hill base −86 · hills −85/−84/−83 · props −82 · particles −80 ·
placement beam −60 · plateau −50 · blocks 0 · nudge wind streaks & impact debris 40 ·
laser line 50 · shatter shards 60.

**Sprite factory** — `RuntimeSprites` (core: beam, heart, panel, soft bar, wind
streak, chevron, square, gradient) + `RuntimeSprites.Backdrop` (clouds, hills,
mesas, streaks, cacti, dots):
fixed shapes cached per session, parameterized builders caller-owned; everything
HideAndDontSave. Generators in `Tools/` own all themed PNGs (pieces, plateau, sfx).

## What code handles (no art needed)

- Generating the block and ground sprites (`Tools/generate_piece_sprites.py`,
  `Tools/generate_ground_sprite.py`)
- The layered backdrop (sky gradient + altitude crossfade, procedural clouds/hills/particles,
  plus optional imported pack sprites) — per-chapter `BackdropPreset` data
- Wiring each shape's sprite onto the physics piece (colliders never change)
- Per-chapter block skins; runtime tints/sprite-swaps for power-ups (e.g. cement)
- Vertical parallax (sky stretch/fade, silhouette layers, procedural clouds)
- Juice: landing squash & dust, camera shake on heavy impacts, lock flash,
  score popups, bomb explosion, milestone effects
- HUD restyle with 9-sliced panels, icons, custom font; menu/overlay polish
- Transitions: level intro/outro fades, chapter crossfade
- Unity import settings (PPU, filtering, compression) for everything supplied

## Build order

1. **Block skin system** + `piece_I..Z` → game immediately stops looking flat
2. **Background parallax** + sky/silhouette/clouds
3. **Juice pass** (code-only)
4. **HUD/menu restyle** + panel/icons/font
5. **Special block emblems** + unique effects
6. **Chapter #2** — refill the folder
