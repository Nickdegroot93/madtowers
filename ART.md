# MadTowers Art & Asset Guide

This is the spec for all images Nick supplies. Everything else (randomization,
tinting, parallax, particles, animations) is done in code. Drop finished files
into `Assets/Art/<Theme>/` using the names below — Claude wires them up.
**Exception:** block skins live in `Assets/Resources/BlockSkins/<Theme>/` —
import settings are applied automatically to anything dropped there.

General rules for every image:
- **Format:** PNG. Transparency only where the spec says "transparent".
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
`THEME_PRESETS` table into `Assets/Resources/Skins/<Theme>/`. A theme without
an entry falls back to the Classic pieces automatically. **New block look for
a theme = one preset dict** (7 hue-identity-preserving colors + an outline
factor) and a rerun — nothing else.

Hand-made art can still override any shape: export a transparent PNG at
**256 px per cell + 32 px bleed** (exact canvases: I 1088×320, O 576×576,
others 832×576; paint guides in `ArtTemplates/template_piece_X.svg`, drawn in
spawn orientation — T stem up, L corner top-right, J top-left, S top row
right, Z top row left) and overwrite the file. Import settings are applied
automatically to `piece_*` files in any BlockSkins folder.

## 2. Special block emblems

Special blocks = same cell texture + tint + a **centered icon overlay**. One
transparent icon per special type, drawn as a bold, readable symbol.

| File | Size | Transparent? | Symbol idea |
|---|---|---|---|
| `emblem_ice.png` | 256×256 | Yes | snowflake |
| `emblem_bomb.png` | 256×256 | Yes | bomb / fuse |
| `emblem_anchor.png` | 256×256 | Yes | anchor |
| `emblem_vine.png` | 256×256 | Yes | leaf / tendril |
| `emblem_heavy.png` | 256×256 | Yes | weight (1-ton block) |
| `emblem_feather.png` | 256×256 | Yes | feather |
| `emblem_boulder.png` | 256×256 | Yes | rough rock cracks |
| `emblem_stubborn.png` | 256×256 | Yes | padlock |
| `emblem_dizzy.png` | 256×256 | Yes | spiral / stars |
| `emblem_tremor.png` | 256×256 | Yes | zigzag crack |

Keep ~24px of empty margin around the symbol. White or light icons work best
(they get a subtle dark outline in code for readability).

## 3. Background (layered, no images)

Backgrounds are **not images** — each theme has a `BackdropPreset` asset
(`Assets/Data/Backdrops/`, assigned to the theme's `backdrop` field) that
composes generated layers at runtime:

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

A theme without a preset gets the classic dark sky. To design a new theme's
backdrop, give Claude an **inspiration image** (screenshot, painting, photo) —
palette and mood translate directly into preset values. Hand-made art can
still join later as additional layers if a hero theme needs it.

## 4. Ground / floor

**Generated, not painted** — `Tools/generate_ground_sprite.py` renders each
theme's `plateau.png` into `Assets/Resources/Skins/<Theme>/`: one tile
(256×96 px = 2×0.75 u) of the landable strip, **tiled** by the game to any
floor width (never stretched, outlined end caps mark its exact boundary).
Each theme picks a material via the renderer's parameters: beveled stone
blocks (Classic), grass-capped earth (Training Wheels), sandstone slabs
(Desert) — block count, bevel, tone steps, and an optional cap band (grass,
snow, moss…) per theme.

The plateau is the **only** ground visual, and it matches the landable
collider width exactly — what you see is what you can land on. There are
deliberately **no buildings under the floor**: anything decorative near the
platform risks reading as a landing surface, and it's invisible once the
tower climbs anyway. Theme scenery (hills, dunes, mountains, props) lives in
the backdrop system (§3) instead.

**Floating support islands** — the same script renders `island_1..3.png`
(128×128 px = one 1×1 cell, 128 px/unit) per theme: the sky stones pieces can
land on (LEVELS.md has the spawn rules). Same material language as the plateau
(base color, edge-line border ring, grain) but deliberately **symmetric — no
"top"**: the spawner rotates each cell in random 90° steps, so 3 variants give
12 looks per theme. Variants stay subtle: 1 plain, 2 hairline crack, 3 pebble
flecks. The spawner picks variant + rotation randomly per cell
(`StaticSupportIslandManager.ConfigureIslandCellVisual`). Hand-made
override: transparent PNG at 128 px/unit whose flat top spans exactly the
**middle 85%** of the canvas width, surface at the exact top edge. Shared
style rules: see STYLE.md.

## 5. HUD & menus

| File | Size | Transparent? | Notes |
|---|---|---|---|
| `panel.png` | 256×256, ~48px corner radius | Yes | One rounded panel, **9-sliced** in code (corners stay crisp, middle stretches) — used for HUD cards, popups, level-select. Light/neutral so code can tint per theme. |
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

## 7. Per-theme reskins

A theme = one folder with the same file names:

```
Assets/Resources/Skins/Classic/   piece_I..Z, plateau, island_1..3, (optional) laser
Assets/Art/Classic/               bg_sky, bg_far, clouds, ...
(<Theme2>: same file names in sibling folders, different art)
```

Optional per-theme `laser.png`: the height-limit line for puzzle levels. Horizontal
strip, ~1024×32–64 px (128 px/unit — height is kept as authored, length is stretched),
transparent PNG, glow baked in light tones (the level tints and pulses it). Without it,
a clean code-built bar is used.

Code loads the matching skin when a theme starts. Emblems, HUD, particles can
be shared across themes or overridden per theme — only supply what should
differ. Start with **Classic only**; once it looks good, each new theme is
just "fill the folder again."

---

## 8. Music

Per theme: 1–N tracks (**OGG preferred**, MP3 fine; convert WAV→OGG from
lossless, never lossy→lossy; matched loudness between tracks), dropped in
`Assets/Audio/Music/` and assigned to the theme's `musicPlaylist`. Playback:
a **random track opens**, then the rotation is fixed (A → B → A …) while the
level is alive; music survives level restarts within a theme, **stops on game
over** (a shared game-over jingle is planned), and a retry starts fresh.
Music imports as *streaming* automatically (memory-friendly on phones).
License: CC0, CC-BY (credits screen later), or owned.

## 9. Sound effects

**Generated, not sourced** — `Tools/generate_sfx.py` synthesizes the SFX (16-bit WAVs)
into `Assets/Resources/Audio/Sfx/`; playback goes through `SfxPlayer`
(pooled, cached, pitch-jittered one-shots). Iterate by tweaking the parameter dicts,
rerunning, and previewing with `afplay` — no Unity needed. Current set: two
flick-drop impact variants (the picked "round 2" recipe), `impact_soft_01` —
the quiet dull thud (now wired as the Bullet's wasted-shot feedback; must stay
clearly duller than the shatter), `swoosh_01` — the corner-nudge dash
(band-swept noise through a falling crude bandpass, swell-then-die envelope;
`synth_swoosh`), `pop_01` — a support island materializing under a risen laser
line (the impact recipe with f_end > f_start: a friendly rising blip),
`nudge_thud_01` — a failed nudge's knock (short, higher-pitched than the
landing thumps, hard click: reads as a dry refusal, not a landing),
`impact_shatter_01` — the Bullet destroying a block (bright sharp stone crack),
and `gun_cock_01` — the Bullet transform (a single gun cock: pull-back click,
slide scrape, slam-home clack; `synth_gun_cock`, the multi-stage mechanical
recipe to copy for future weapon-like abilities).
Hand-made/downloaded WAVs (prefer **CC0**, e.g. Kenney packs)
drop into the same folder and play through the same system. Background music
is a separate future system (per-theme tracks, ducking).

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
  ability does (the Bullet = a shell plunging down). No scenes, no text, no
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

In-game **ability block sprites** (the Bullet projectile piece) live in the
same generator/folder as `block_<name>.png`, 256×256 at PPU 256 (one cell),
reusing the same `shade()` lighting so they sit naturally next to the
tetromino pieces.

## Under the hood: how theming works at runtime (exact pipeline)

What happens when a level loads, in order:

1. **Theme resolution** — `GameManager.Awake` calls `Campaign.FindThemeOf(selectedLevel)`
   once, then `ThemeSkins.Apply(theme)` (sets the static `ThemeSkins.Folder`, e.g.
   `Skins/Desert`) and `MusicPlayer.PlayForTheme(theme)` (playlist looped in order;
   keeps playing across level restarts within the same theme).
2. **Floor** — `PlayAreaController.ApplyGroundSkin`: `ThemeSkins.LoadPlateau()` resolves
   `<Folder>/plateau` and falls back to `Skins/Classic` per file. The strip is rendered
   with `SpriteRenderer.drawMode = Tiled` at exactly the collider width (end caps kept
   by the 12px sprite border); the original floor bar renderer is disabled. The collider
   is never touched.
3. **Blocks** — each spawned piece (`BlockController.ApplyBlockSkin`) loads
   `ThemeSkins.LoadPiece(shape)` with the same fallback chain; the HUD's "next" ghost
   (`UIManager`) builds a desaturated copy of the same sprite (cached per folder+shape).
   **Support islands** load `ThemeSkins.LoadIsland(1..3)` once at level start
   (`StaticSupportIslandManager.Start`); each spawned cell gets a random variant and a
   random 90° rotation on a visual child (the cell collider never rotates or scales).
4. **Backdrop** — `LevelPresentationController` (on the scene's Background object,
   `[ExecuteAlways]`; world elements split into `LevelPresentationController.Elements`):
   - Resolves `theme.Backdrop` (a `BackdropPreset`), or `BackdropPreset.Defaults`
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
5. **Laser** (height-limit levels) — `ThemeSkins.LoadLaser()` or the code-built bar.
6. **Post-FX** — `PostFxController` (self-installed, survives scene loads) applies one
   global URP volume to every theme: vignette, bloom, color grading (values in
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
- The entire layered backdrop (sky gradient + altitude crossfade, clouds, hill/mesa
  silhouettes, ambient particles) — per-theme `BackdropPreset` data, zero image assets
- Wiring each shape's sprite onto the physics piece (colliders never change)
- Per-theme block skins; runtime tints/sprite-swaps for power-ups (e.g. cement)
- Vertical parallax (sky stretch/fade, silhouette layers, procedural clouds)
- Juice: landing squash & dust, camera shake on heavy impacts, lock flash,
  score popups, bomb explosion, milestone effects
- HUD restyle with 9-sliced panels, icons, custom font; menu/overlay polish
- Transitions: level intro/outro fades, theme crossfade
- Unity import settings (PPU, filtering, compression) for everything supplied

## Build order

1. **Block skin system** + `piece_I..Z` → game immediately stops looking flat
2. **Background parallax** + sky/silhouette/clouds
3. **Juice pass** (code-only)
4. **HUD/menu restyle** + panel/icons/font
5. **Special block emblems** + unique effects
6. **Theme #2** — refill the folder
