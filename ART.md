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
all 7 (silhouette, outline, gradient, bevel, seam cracks) straight into
`Assets/Resources/BlockSkins/Classic/`. To restyle: tweak the knobs in the
script (palette, outline, corner radius, crack density) or ask Claude, rerun,
done. New themes (ice, haunted, neon outline…) = a style preset in the script
writing to `BlockSkins/<Theme>/`.

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

## 3. Background (parallax, scrolls as the tower grows)

You do **not** need one giant looping image. The camera only moves **up**, so
the background is layered and code handles infinite height:

| File | Size | Transparent? | What it is / how code uses it |
|---|---|---|---|
| `bg_sky.png` | 1080×2160 | No | Tall vertical gradient/painting (ground mood at bottom → sky at top). Code stretches it and fades the top edge into a solid color it keeps using forever, so it never "runs out" as you climb. Keep the **top ~200px close to one flat color**. |
| `bg_far.png` | 1080×~800 | Yes (transparent top) | Distant silhouette anchored at the bottom of the world: skyline, mountains, treeline. Scrolls away slowly (parallax) and disappears as you climb. |
| `bg_mid.png` | 1080×~600 | Yes (transparent top) | Optional second silhouette layer, closer/darker than `bg_far`, for depth. |
| `cloud_01.png` … `cloud_04.png` | ~512×300 each | Yes | Individual clouds (or birds, balloons, floating debris — theme-dependent). Code spawns and drifts these procedurally at all heights, so 4 loose sprites = infinite varied sky. Soft edges are fine. |

## 4. Ground / floor

**Generated, not painted** — `Tools/generate_ground_sprite.py` renders the
base the tower stands on (flat plateau on top, mass running below the screen)
into `Assets/Resources/Skins/<Theme>/`. Classic uses the stone tower base
(`ground.png`); a grassy-hill preset stays in the script for future themes.
The game scales the plateau to the floor width and
hides the old floor bar; the collider is untouched. Per-theme variants
(haunted house, glacier, dune…) are presets in the script. Hand-made
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
Assets/Resources/Skins/Classic/   piece_I..Z, ground, (optional) laser
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

## 8. Sound effects

**Generated, not sourced** — `Tools/generate_sfx.py` synthesizes the SFX (16-bit WAVs)
into `Assets/Resources/Audio/Sfx/`; playback goes through `SfxPlayer`
(pooled, cached, pitch-jittered one-shots). Iterate by tweaking the parameter dicts,
rerunning, and previewing with `afplay` — no Unity needed. Current set: two
flick-drop impact variants (the picked "round 2" recipe) + a softer landing
(not yet wired). Hand-made/downloaded WAVs (prefer **CC0**, e.g. Kenney packs)
drop into the same folder and play through the same system. Background music
is a separate future system (per-theme tracks, ducking).

## What code handles (no art needed)

- Generating the block and ground sprites (`Tools/generate_piece_sprites.py`,
  `Tools/generate_ground_sprite.py`)
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
