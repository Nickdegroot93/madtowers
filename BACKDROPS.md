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

Example:

`Assets/Data/Backdrops/Backdrop_Desert.asset` uses the imported Desert Vibe
pack from `Assets/Desert Vibe`.

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

## Important SpriteLayer Settings

Each imported sprite layer on a `BackdropPreset` has these key fields:

| Field | Purpose |
|---|---|
| `sprites` | The imported sprites for this depth band. |
| `instanceCount` | How many renderers to spawn. `0` means one per sprite. |
| `verticalParallax` | `0` drops with the floor quickly; `1` stays with the camera. |
| `baseYOffset` | Vertical position before parallax. Floor-relative unless `anchorToCamera` is enabled. |
| `horizontalSpread` | How widely multiple sprites spread across the camera. |
| `positionJitter` | Small random x/y offset so repeated sprites do not look too tiled. |
| `scaleRange` | Deterministic scale variation for spawned scenery sprites. |
| `fitToCameraWidth` | Scale sprite to fit the camera width. Useful for cloud strips. |
| `coverCameraView` | Scale sprite to cover the whole camera, cropping sides if needed. Best for full BG plates. |
| `anchorToCamera` | Position relative to camera instead of floor. Required for full BG plates. |
| `sortingOrder` | Render order behind gameplay. More negative = farther back. |

Imported sprite layer jitter and scale are deterministic. The same preset,
sprites, labels, and instance counts produce the same composition every run, so
phone/editor tuning does not drift between play sessions.

Backdrop presets also have an optional **bottom clarity veil**: a camera-anchored
gradient drawn in front of backdrop layers but behind blocks, floor, and UI. Use
this when the full layer stack is correct but too visually sharp or detailed near
the bottom of the screen.

| Field | Purpose |
|---|---|
| `bottomClarityVeilEnabled` | Draws the veil behind gameplay objects. |
| `bottomClarityVeilColor` | Tint and maximum opacity at the bottom. |
| `bottomClarityVeilHeight` | Screen fraction covered by the veil. |
| `bottomClarityVeilCurve` | Fade shape. Higher values hold opacity lower before fading. |

## Recommended Defaults

For a full background plate:

```text
coverCameraView = true
anchorToCamera = true
fitToCameraWidth = true
fittedWidthMultiplier = 1.03 to 1.10
baseYOffset = 0
verticalParallax = 0.9 to 1.0
sortingOrder = about -99
```

For clouds or sun haze:

```text
anchorToCamera = true
fitToCameraWidth = true
coverCameraView = false
baseYOffset = positive, if the strip should sit high in frame
verticalParallax = 0.85 to 1.0
sortingOrder = about -94
```

For far horizon scenery:

```text
anchorToCamera = false
coverCameraView = false
verticalParallax = 0.55 to 0.75
baseYOffset = around 3
scaleRange = modest, around 1.1 to 1.35
sortingOrder = about -88
```

For middle scenery:

```text
verticalParallax = 0.20 to 0.45
baseYOffset = around 2.3 to 3
scaleRange = around 1.2 to 1.5
sortingOrder = about -86 to -84
```

For near foreground:

```text
verticalParallax = 0.02 to 0.15
baseYOffset = around 1.5 to 2.3
scaleRange = larger, around 1.35 to 1.8
sortingOrder = about -82 to -80
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
- Lower near layers aggressively with `baseYOffset` instead of removing them
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
- If the full stack is still too sharp near the floor, use the bottom clarity
  veil. It reduces contrast/detail without creating blur halos around transparent
  sprite edges.

Good first-pass gameplay values:

```text
far horizon:      verticalParallax 0.40-0.60, alpha near 1.0
mid scenery:      verticalParallax 0.10-0.30, alpha near 1.0
near scenery:     verticalParallax 0.00-0.08, alpha near 1.0 or remove
foreground strip: usually very low, or reserved for a menu/intro if it still crowds gameplay
bottom clarity veil: height 0.25-0.40, alpha 0.25-0.45
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
6. Add imported `SpriteLayer` entries from far to near.
7. Assign the backdrop to the chapter's `ChapterDefinition.backdrop`.
8. Enter Play Mode and tune:
   - full BG crop/scale first
   - horizon Y position second
   - foreground scale and parallax third
   - bottom clarity veil last, only if the full stack still competes with blocks

## What Not To Use From Packs

Usually skip:

- Horizontal parallax scripts from the asset pack.
- Demo scene cameras.
- Demo scene movement scripts.
- Project-wide TagManager presets.
- Render pipeline assets, unless the current project pipeline is incompatible.

MadTowers already has URP and its own vertical camera system. Imported packs
should mostly provide sprites, not runtime behaviour.

## Desert Vibe Mapping

Current Desert Vibe mapping:

| Source | Use |
|---|---|
| `Sprites/BG/BG DV.png` | camera-cover background plate |
| `Sprites/BG/clouds 2 dsrt.png` | camera-anchored cloud strip |
| `Sprites/layer1/*` | far horizon |
| `Sprites/layer2/*` | far/mid desert |
| `Sprites/layer3/*` | middle desert |
| `Sprites/layer4/*` | near cactus/brush |
| `Sprites/layer5/*` | closest foreground |

The pack's own `BackgroundParalax.cs` scrolls horizontally, so it is not used by
gameplay.

## Visual Tuning Checklist

When a new pack looks wrong, check in this order:

1. Is the full background plate using `coverCameraView` and `anchorToCamera`?
2. Is the full background centered on the camera with `baseYOffset = 0`?
3. Are the foreground layers too small for portrait? Increase `scaleRange`.
4. Is the horizon too low/high? Adjust `baseYOffset` on far/mid layers.
5. Do near objects stay too long while climbing? Lower `verticalParallax`.
6. Do far objects vanish too quickly? Raise `verticalParallax`.
7. Are there visible horizontal gaps? Increase `horizontalSpread`,
   `instanceCount`, or `scaleRange`.
8. Are sprites covering blocks or UI? Lower their `sortingOrder`.
9. Are there visible center cuts or hard vertical edges? Increase `instanceCount`
   or reduce `horizontalSpread` so the enabled band covers the gameplay area.
10. Are horizontal layer bottoms visible? Bring back one covering layer in front,
   or lower the exposed layer.
11. Is the bottom too busy? Remove the closest layer first, then lower
   `baseYOffset`, reduce `instanceCount`, or scale down the remaining near
   layers. Avoid low alpha on overlapping scenery.

## Rule Of Thumb

Use pack art as authored for depth order, but use MadTowers logic for movement.
The pack answers "what is far vs near"; the game answers "how does far vs near
move during a vertical climb."
