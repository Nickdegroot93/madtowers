# MadTowers Backdrop Pack Guide

This document explains how imported 2D landscape/parallax packs are adapted for
MadTowers' portrait, vertical-climbing gameplay.

Most Unity Asset Store parallax packs are built for wide horizontal movement.
MadTowers moves upward, so we do not use those demo scripts directly. Instead,
we import their sprite layers as data into a `BackdropPreset` and let our own
runtime place them behind the tower.

## Core Idea

Landscape packs need two different treatments:

1. **Camera-cover plate**
   - A full background image, usually sky plus distant horizon.
   - Scaled until it covers the whole portrait camera view.
   - Crops left/right because the source art is wide.
   - Stays anchored to the camera so the game never reveals empty space above or
     below the original image.

2. **Floor/parallax scenery**
   - Separate cactus, hill, rock, cloud, building, tree, mountain, etc. layers.
   - Placed around the floor/horizon.
   - As the camera climbs, near layers fall out of view quickly and far layers
     linger longer.
   - This produces the vertical parallax feel even when the pack was authored
     for horizontal scrolling.

We cannot create extra art above or below a landscape image. The correct
compromise is to crop the wide full background and use the separated scenery
layers for depth.

## Where This Lives

The reusable data is on `BackdropPreset`:

`Assets/Data/Backdrops/*.asset`

The renderer is:

`Assets/SourceFiles/Scripts/Levels/LevelPresentationController*.cs`

A chapter points to its backdrop through:

`ChapterDefinition.backdrop`

Examples:

- `Assets/Data/Backdrops/Backdrop_Desert.asset` uses the imported Desert Vibe
  layers.
- `Assets/Data/Backdrops/Backdrop_JungleDepths.asset` uses the imported Jungle
  Landscape layers.

## Layer Order

Imported packs usually provide folders named something like:

```text
BG/
layer1/
layer2/
layer3/
layer4/
layer5/
```

General interpretation:

| Pack layer | Meaning | MadTowers treatment |
|---|---|---|
| `BG` full image | sky/full landscape plate | camera-cover, anchor to camera |
| clouds/sky overlays | clouds/light/sun haze | camera-anchored or very high parallax |
| `layer1` | far horizon | slow vertical parallax |
| `layer2` | far/mid scenery | medium-slow parallax |
| `layer3` | mid scenery | medium parallax |
| `layer4` | near scenery | fast drop-away |
| `layer5` | closest foreground | fastest drop-away |

If a pack uses different names, inspect the demo scene or sort order. Farther
layers usually render behind nearer layers and have lighter/lower-contrast art.

## Important Imported Layer Settings

Each imported sprite layer on a `BackdropPreset` has these key fields:

| Field | Purpose |
|---|---|
| `sprite` | One imported sprite for this depth band. Add layers far-to-near. |
| `worldHeight` | Rendered height in world units. `0` means roughly camera-height. |
| `floorOffsetY` | Bottom of the layer relative to the floor. Use this to hide pack edges below the play area. |
| `worldOffsetX` | Horizontal offset from camera center. Useful for left/right foreground pieces. |
| `horizontalTileRadius` | How many duplicate tiles render on each side. `2` means five copies total. `0` draws one sprite only. |
| `horizontalTileOverlap` | Small overlap between tiles to hide transparent/cropped seams. |
| `verticalParallax` | `0` drops with the floor quickly; `1` stays with the camera. |
| `driftSpeedX` | Constant sideways scroll in world units/sec — the "this layer is clouds" switch. The tile row wraps around itself so the loop is endless and seamless. Positive = rightward. Keep it subtle (`0.1`–`0.15`); needs `horizontalTileRadius >= 1`. `0` = static (default). Ignored for a fill layer. |
| `alpha` | Layer opacity. Prefer moving/scaling first; low alpha can reveal every overlapped silhouette. |

When importing a new pack, find its cloud / mist / fog-bank layer and give it a small
`driftSpeedX`. Vertical games barely move sideways, so one endlessly drifting layer is
what stops the backdrop reading as a static image. Ambient particles (the preset's
`particleCount`/`particleColor` block) are the second half of that: tint them to the
chapter's palette (jungle spores green, sakura petals pink, desert dust sand).

The full "make it feel alive" pass for a new pack — drift, particles, flybys, heat haze,
per-theme recipes — lives in **AMBIENCE.md**. Run its curation checklist after every import.

`Tools > MadTowers > Validate Chapter Content` lint-checks every chapter's backdrop layers
(missing sprite, drift without tiling, misplaced fillView, invisible alpha, particle/flyby
misconfig) — run it after authoring or editing a preset, especially when editing the
`.asset` YAML directly.

Imported sprite layers are deterministic. The same preset values produce the same
composition every run, so phone/editor tuning does not drift between sessions.

## Recommended Defaults

For a full background plate or sky/horizon sprite:

```text
worldHeight = 30 to 36
floorOffsetY = 0
horizontalTileRadius = 2 if the source is horizontally cropped, 0 if it is already a complete plate
verticalParallax = 0.60 to 0.85
alpha = 1
```

For clouds or sun haze:

```text
verticalParallax = 0.85 to 1.0
floorOffsetY = high enough to sit above the tower start
alpha = 0.5 to 0.8
```

For far horizon scenery:

```text
verticalParallax = 0.55 to 0.75
floorOffsetY = around 4 to 6
worldHeight = 12 to 18
```

For middle scenery:

```text
verticalParallax = 0.20 to 0.45
floorOffsetY = around 1.5 to 4
worldHeight = 10 to 14
```

For near foreground:

```text
verticalParallax = 0.02 to 0.15
floorOffsetY = -0.5 to 1.5
worldHeight = 8 to 12
```

## Gameplay Clarity Rule

The tower is the main readable object. Imported packs often look great in their
demo scenes but become too busy behind falling blocks, especially near the
floor. Treat every pack with this default restraint:

- Use the full background plate.
- Use most far layers, but remove the noisiest ones if they create strong
  shapes behind the tower.
- Keep the full stack when the pack depends on layers covering each other. Some
  packs look unfinished when middle/front layers are removed.
- Lower near layers aggressively with `floorOffsetY` instead of removing them
  when they are needed to cover lower edges.
- Give near layers low `verticalParallax` so they fall out of view early.
- Keep any enabled scenery band continuous across the screen. Avoid using just
  two left/right instances if it creates a visible center gap or hard vertical
  sprite edge.
- If removing front layers exposes a horizontal bottom edge on a back layer,
  bring back the next layer in front or lower the exposed layer until the cut
  sits below the active play area.
- Keep opaque scenery layers close to full alpha. Low alpha on overlapping
  landscape sprites usually makes every silhouette visible through every other
  silhouette, which is busier and can reveal horizontal layer bottoms.
- Prefer lowering or scaling down busy layers over fading them. If removing a
  layer exposes unfinished edges, restore it and move the full stack down.
- If a layer shows hard vertical cuts, increase `horizontalTileRadius`, tune
  `horizontalTileOverlap`, or use a more complete sprite from the pack. Do not
  stretch a cropped horizontal layer to portrait width; that just magnifies the cut.

Good first-pass gameplay values:

```text
far horizon:      verticalParallax 0.40-0.60, alpha near 1.0
mid scenery:      verticalParallax 0.10-0.30, alpha near 1.0
near scenery:     verticalParallax 0.00-0.08, alpha near 1.0 or remove
foreground strip: usually very low, or reserved for a menu/intro if it still crowds gameplay
```

If a pack has beautiful dense foreground art, consider using it in the chapter
menu, a pre-level camera intro, or a non-interactive showcase, then disable it
or push it lower during active play.

## Importing A New Pack

1. Import the Asset Store pack into `Assets/<Pack Name>/`.
2. Do not apply the pack's project-wide `TagManager` or render-pipeline presets
   unless there is a specific reason. Our renderer uses `sortingOrder`, not the
   pack's sorting layers.
3. Remove or disable the pack's demo/runtime scripts if they define generic
   classes such as `BackgroundParalax`, `CameraUtils`, or `MovingScene`. We use
   our own backdrop renderer, and repeated packs from the same author may ship
   duplicate script class names.
4. Open the pack's demo scene only to understand sprite order and grouping.
5. Create or duplicate a `BackdropPreset` in `Assets/Data/Backdrops/`.
6. Add imported `spriteBackdropLayers` entries from far to near.
7. Assign the backdrop to the chapter's `ChapterDefinition.backdrop`.
8. Enter Play Mode and tune:
   - full BG height/tile radius first
   - horizon Y position second
   - foreground scale and parallax third

## What Not To Use From Packs

Usually skip:

- Horizontal parallax scripts from the asset pack.
- Demo scene cameras.
- Demo scene movement scripts.
- Project-wide TagManager presets.
- Render pipeline assets, unless the current project pipeline is incompatible.

MadTowers already has URP and its own vertical camera system. Imported packs
should mostly provide sprites, not runtime behaviour.

## Pack Mappings

Current Desert Vibe and Jungle Landscape mapping:

| Source | Use |
|---|---|
| `Sprites/BG/*` or `Sprites/J BG.png` | background plate / sky strip |
| `Sprites/clouds*.png` | high cloud or haze strip |
| `Sprites/layer1/*` | far horizon |
| `Sprites/layer2/*` | far/mid scenery |
| `Sprites/layer3/*` | middle scenery |
| `Sprites/layer4/*` | near trees/cactus/brush |
| `Sprites/layer5/*` | closest foreground |

The pack's own `BackgroundParalax.cs` scrolls horizontally, so it is not used by
gameplay.

## Visual Tuning Checklist

When a new pack looks wrong, check in this order:

1. Is the full background plate tall enough (`worldHeight`) and tiled if cropped?
2. Is the full background bottom aligned sensibly with `floorOffsetY = 0`?
3. Are the foreground layers too small for portrait? Increase `worldHeight`.
4. Is the horizon too low/high? Adjust `floorOffsetY` on far/mid layers.
5. Do near objects stay too long while climbing? Lower `verticalParallax`.
6. Do far objects vanish too quickly? Raise `verticalParallax`.
7. Are there visible horizontal gaps? Increase `horizontalTileRadius` or reduce
   `horizontalTileOverlap` only if overlap is visibly eating art.
8. Are sprites covering blocks or UI? Move near layers lower or reduce
   `worldHeight`; sorting order is fixed by layer order.
9. Are there visible center cuts or hard vertical edges? Increase
   `horizontalTileRadius` or use a wider/more complete sprite.
10. Are horizontal layer bottoms visible? Bring back one covering layer in front,
   or lower the exposed layer.
11. Is the bottom too busy? Remove the closest layer first, then lower
   `floorOffsetY`, reduce `worldHeight`, or disable tiling on the remaining near
   layers. Avoid low alpha on overlapping scenery.

## Rule Of Thumb

Use pack art as authored for depth order, but use MadTowers logic for movement.
The pack answers "what is far vs near"; the game answers "how does far vs near
move during a vertical climb."
