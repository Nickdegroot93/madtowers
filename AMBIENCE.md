# MadTowers Ambience Guide

How we make a chapter feel **alive**. Binding for ambience work, the same way BACKDROPS.md
is binding for layer import work — read both before touching backdrop code or presets.

## Why this exists

Our backdrop packs (DenielHest and similar) are designed for *horizontal* games: their
parallax only shows when the camera moves sideways. MadTowers is *vertical* — the camera
barely pans, so an imported pack reads as a static image. We fix that with a small toolkit
of **generic, data-driven ambience effects** layered on top of the untouched pack art.

Two hard rules:

1. **No per-chapter code. Ever.** Every effect is a preset field on `BackdropPreset`;
   a chapter opts in by setting data on its `Assets/Data/Backdrops/Backdrop_<Chapter>.asset`.
   If an idea can't be expressed as preset data, generalize the idea until it can.
2. **Subtle and intermittent beats loud and constant.** Constant motion becomes wallpaper
   within a minute; rare motion (a flyby, a heat gust) keeps registering as "alive" forever.
   When in doubt, halve the alpha and double the interval.

## The toolkit

All rendered by `LevelPresentationController` (four partials), all configured per chapter
on its `BackdropPreset`:

| Effect | Preset fields | What it gives you | File |
|---|---|---|---|
| Sky gradient + altitude fade | `skyTopLow/High`, `skyBottomLow/High`, `altitudeFadeMeters`, `skyShimmer*` | The air changes as you climb; shimmer adds banded variation | `LevelPresentationController.cs` |
| Sun / moon disc | `sunEnabled`, `sunColor`, `sunSize`, `sunScreenX`, `sunHeightMeters` | A celestial body drifting through a band of the climb | `.cs` |
| Procedural clouds | `cloudCount`, `cloudStyle` (Soft/Blocky/Streak), `cloudColor`, `cloudDriftSpeed`, `cloudScaleRange` | Infinite-height drifting clouds when the pack has none | `.Elements.cs` |
| Hills / mesas / props | `hillsEnabled`, `hillStyle`, `hill*Color`, `propCount`, ... | Ground-level silhouettes when the pack lacks a floor line | `.Elements.cs` |
| Imported layer **drift** | `spriteBackdropLayers[i].driftSpeedX` | THE "this layer is clouds" switch: endless seamless sideways scroll of any pack layer | `.Elements.cs` |
| Imported layer **hover** | `spriteBackdropLayers[i].hoverAmount` (world units) + `hoverPeriodSeconds` | Smooth sine bob for flying craft — hovering pyramids, airships, balloons. Phase-offset per layer; ignored on fillView layers (validator lints this) | `.Elements.cs` |
| Ambient particles | `particleCount`, `particleColor`, `particleSize`, `particleFallSpeed`, `particleSwayAmount` | Spores / petals / dust / snow / embers — color + motion is all a mood needs | `.Elements.cs` |
| Heat haze | `heatHazeAmount` | Gusting hot-air shimmer (2x-multiply ripple overlay). Comes and goes on a slow gust envelope — never constant — and fades out as the tower climbs off the hot ground | `.Ambience.cs` + `Assets/Resources/HeatHaze.shader` |
| Flybys | `flybyFlockSize`, `flybyColor`, `flybyIntervalSeconds`, `flybySpeed`, `flybyScale` | Rare bird silhouettes crossing the sky. One procedural bird serves all themes — data does the casting (see below) | `.Ambience.cs` + `RuntimeSprites.Backdrop.cs` |

### Casting the flyby bird

There is exactly one bird silhouette (3 flap frames, procedurally drawn). Flock size,
scale, speed and tint turn it into different fauna:

- **Songbirds / parrots**: flock 4–6, scale ~0.8, speed 2.5+, short intervals. Quick and busy.
- **Cranes / geese**: flock 2–3, scale ~1.3, speed ~1.7, dusk-tinted. Stately V formation.
- **Vulture / hawk / lone crow**: flock 1, scale ~1.6, speed ~1.3, near-black. Slow menace.

Bigger birds automatically flap slower. Flybys render at sorting order −87 — in front of
the far cloud band, *behind* mid/near scenery — so they vanish behind treelines and
rooftops naturally.

## Curating a new pack — the ambience checklist

After the layer import itself (that part is BACKDROPS.md):

1. **Find the drifting layer.** Almost every pack has clouds, mist, fog or smog as its own
   sprite. Give that layer `driftSpeedX` 0.10–0.15. Needs `horizontalTileRadius >= 1`.
   Pack ships no cloud layer? Generate one in the packs' flat vector language:
   `python3 Tools/generate_chapter_clouds.py "<r,g,b>" "<theme>-clouds" "Assets/Art/Chapters/<Id>/clouds_<x>.png"`
   (tint slightly lighter than the sky behind it; Frozen Peaks and Fangkuai District use this).
2. **Tint the particles to the palette.** Pick the mood: what would float in this air?
   Tune alpha for contrast with the background — bright-on-dark needs ~0.3, tone-on-tone
   (dust on sand, petals on pink) needs 0.55–0.7 and a bigger size. The dot sprite has soft
   edges, so effective brightness is lower than the alpha number; when faint, grow **size**
   before pushing alpha further.
3. **Cast the flybys.** What flies here? Birds, cranes, bats read from the table above.
   Skip them only if nothing plausibly flies (e.g. underwater); intervals stay ≥ 20s.
4. **Hot chapter?** Set `heatHazeAmount` 0.5–0.7. That's the whole integration.
5. **Sun/moon.** Night and sunset themes almost always want the disc (`sunEnabled`) in a
   matching color — it doubles as a moon.
6. **Playtest bracket.** Tune numbers live in the inspector during play mode, then copy the
   winners into the asset. Expect Nick to bracket density/alpha once on device.

## Theme recipes — DenielHest catalog

Planned/likely packs and their ambience recipes, using only the existing toolkit
(**bold** = ideas that need a future effect from the wishlist below):

| Pack / theme | Drift layer | Particles | Flybys | Overlay / sky | Future extras |
|---|---|---|---|---|---|
| Darkwave City | smog / haze band | faint cool-grey drizzle motes | 1 lone crow, near-black | moon disc, cold sky pair | **neon flicker** (per-layer alpha pulse), **rain** |
| Sovietwave | low grey clouds | slow sparse snow (white, a 0.5) | 2–3 grey pigeons | pale sun low on screen | **chimney smoke plume**, **snow (FX pack)** |
| Chinatown | lantern-glow haze | warm ember/firefly motes (rise = negative fall speed once supported) | 2 small birds | dusk gradient | **lantern glow pulse**, **fireworks burst** |
| Japanese Theme Night | thin cloud band | pale blue fireflies | 1 owl silhouette, slow | big moon via sun disc | **star twinkle** |
| Apocalypse / Apocalypse II | ash clouds | grey ash fall (slow, heavy sway) | none — dead sky sells it | ochre sky pair, dim sun | **ember gusts**, **lightning flash** |
| Cyber Egypt | sand haze | gold dust (like Barren Lands) | 1 vulture | heatHaze 0.6 | **god rays** |
| Neon City Mountains | valley fog | neon-tinted motes | 3 distant birds | — | **neon flicker** |
| Spring Sunset / Sunrise City | streak clouds | pollen / seeds (warm white) | 3–4 songbirds | sun disc in sunset color | — |
| Arctic Sea | drifting ice fog | fine snow, high sway | 2 gulls (pale) | pale sun | **aurora band** |
| Winter Mountain / Forest Snow | low cloud band | snow (white, size up, slow) | 1–2 crows | — | **snow (FX pack)** |
| Night Mountain | thin moonlit clouds | fireflies near ground | 1 owl | moon disc | **star twinkle** |
| Military / War Core | smoke banks | drifting ash/smoke motes | none or 1 distant flight | muted sky | **rain**, **searchlight sweep** |
| Industrial City | smog layer | soot motes (dark! needs light bg) | 3 pigeons | hazy sun | **chimney smoke** |
| Chinese Nature | mist band | petals (white-pink) | 2 cranes | — | — |
| Graffiti City | haze | neon paint motes | 3 sparrows | — | **neon flicker** |
| Future City | high thin clouds | faint light motes | none — **flying vehicles instead** | sun disc | **vehicle flyby variant** |

The recipes are starting points, not specs — the checklist above plus taste wins.

## Full-screen / effect wishlist (designed, not built)

Roughly in priority order. Each one must follow the architecture rules below.

- **Per-layer alpha pulse** (`pulseAmount`, `pulseSpeed` on `SpriteBackdropLayer`): slow
  sine on a layer's alpha. One field powers neon flicker, lantern glow, aurora breathing,
  distant-window shimmer. Cheapest high-value addition; probably next.
- **Multi-emitter particles**: promote the particle block to an array so one chapter can
  stack moods (near dust + far haze; rising embers + falling ash). Needs a `direction`/
  `riseSpeed` field — rising particles unlock fireflies, embers, bubbles.
- **Weather (rain / snow / storm)**: evaluate Nick's Cartoon FX pack. Design decision to
  make first: weather likely wants to be its own preset section (or per-level override),
  not baked into chapter identity — "Sakura Ridge but raining" should be possible. Revisit
  when we do difficulty/variety modifiers.
- **Lightning flash**: rare full-screen brightness pop + silhouette punch-through. Pairs
  with rain; trivially data-driven (`flashIntervalSeconds`, `flashColor`).
- **God rays**: 2–3 soft diagonal light shafts, slow alpha breathing. Overlay quad like
  heat haze, additive.
- **Star twinkle**: night-sky variant of ambient particles that doesn't fall — needs the
  multi-emitter work (speed 0, alpha flicker).
- **Searchlight / lighthouse sweep**: one rotating light cone silhouette; niche but cheap.
- **Vehicle flyby**: second flyby silhouette set (blimp / drone / plane) for city themes;
  reuse the whole flyby system, swap the sprite source.

## Architecture rules for adding an ambience effect

Where things go, so the system stays uniform:

1. **Config**: fields on `BackdropPreset` (its own `[Header]` block), or on
   `SpriteBackdropLayer` if the effect belongs to one imported layer. Defaults must mean
   **off** (0 / disabled) so every existing and future preset is unaffected until it opts in.
2. **Runtime**: `LevelPresentationController.Ambience.cs`. Create in
   `CreateAmbienceElements()` (parented under `_worldRoot`), tear down refs/materials in
   `ResetAmbienceElements()`, update from `LateUpdate` in the main partial. Play-mode only.
3. **Procedural sprites**: `RuntimeSprites.Backdrop.cs` — white shapes tinted via renderer
   color, deterministic (fixed shape tables, no `Random` in sprite generation), cached.
4. **Shaders**: `Assets/Resources/*.shader`, URP HLSL style (copy HeatHaze/Lava's header).
   Remember the project trap: procedural shaders ignore SpriteRenderer alpha — hide such
   renderers via `.enabled`, never `color.a = 0`.
5. **Materials you instantiate, you destroy** (see `ResetAmbienceElements`) — same leak
   rule as the generated sky sprites.
6. **Sorting orders**: constants in the controller partials. Backdrop band is −100..−60;
   imported layers stack upward from −89 (one order per layer!), so full-screen overlays
   sit at −60, scenery-level effects pick their spot among the layers deliberately.
7. **Document it**: add the effect to the toolkit table here, and to BACKDROPS.md only if
   it's a per-layer field.
8. **Lint it**: if the effect has an easy authoring mistake (a field combination the
   renderer ignores or renders wrong), add a rule to `ChapterContentValidator.ValidateBackdrop`
   so `Validate Chapter Content` catches it — presets are authored as raw data, often by
   editing YAML directly, and the validator is the safety net for that workflow.

## Current per-chapter settings (v1, tuned July 2026)

| Chapter | Drift | Particles | Flybys | Haze |
|---|---|---|---|---|
| Jungle Depths | `clouds jn` 0.12 | 28 green spores, a 0.34 | 5 small dark birds, fast, every 22–48s | — |
| Barren Lands | `clouds 2 dsrt` 0.15 | 26 sand dust, a 0.6 | 1 vulture, slow, every 35–70s | 0.65 |
| Sakura Ridge | `cloud1` 0.10 | 30 pink petals, a 0.7 | 3 cranes, stately, every 30–60s | — |
| Frozen Peaks | `clouds_winter` 0.12 (generated strip — the pack ships no cloud layer) | 30 white snow, a 0.75, size 0.18 | 2 crows, every 30–60s | — |
| Fangkuai District | `clouds_dusk` 0.11 (generated strip) | 26 warm ember motes, a 0.55 | 2 small dark birds, every 25–55s | — |
| Kvartal 4 | `clouds_night` 0.10 (generated strip) | 18 sparse snow, a 0.5 | 3 grey pigeons, every 28–60s | — (warm glow overlays: generated `glow_moon`/`glow_city`/`glow_lantern` alpha-gradient rebuilds of the pack's additive lights, plus a `glow_wash` full-screen fillView tint (170,100,45 @ a 0.08) as the LAST layer — the nostalgic warm grade over the whole backdrop; see ASSET_IMPORTS.md) |
| Neon Nightfall | two boat strips drift the water (0.16 / −0.10 — a wide mostly-transparent canvas per boat so one sails per screen) | 24 neon motes, a 0.5 | 3 small dark birds, every 30–60s | — (generated `water_neon` band via Tools/generate_water_sprite.py; aprons on far bands are DARK night tones — bright aprons on high-parallax layers become full-screen curtains at altitude) |
| Burning Steppes | `clouds_ash` 0.10 (generated strip — the pack ships no cloud layer) | 26 ember motes (1, 0.6, 0.3), a 0.5, slow fall + high sway | 1 vulture, slow, every 40–80s | 0.55 heat haze (+ pack `light MF` red wash as a near layer, alpha 0.32, sinking with the ground) |
| Lost City | pack `clouds LC` streak strip 0.12 | 22 pale teal motes (0.7, 0.95, 0.85), a 0.4, near-still fall | 2 small night birds, every 30–60s | — (moon baked into the plate — `sunEnabled` stays 0) |
| Sector Isla | boat strip drifts the lagoon (+1.6 u/s, bow-first, on a 6144px mostly-empty canvas with an alpha-feathered water top - a FAST crossing that fully exits and returns ~1x/min; canopy + cloud puff skipped) | 20 fireflies (0.85, 1, 0.6), a 0.4, near-still | 4 songbirds, fast, every 25-50s | - (crescents baked in the pack's moons sheet) |
| Giza Dusk | — (clouds tried and cut — Nick's call; the two flying pyramids hover instead, split into `pyramid_small`/`pyramid_big` layers with different periods (0.26/5.7s vs 0.35/7.3s) so they never bob in sync) | 26 gold dust (1, 0.82, 0.5), a 0.6, slow fall + high sway | 1 vulture, slow, every 35–70s | — (heat haze tried and cut too; pack `light under city` white haze band stays, alpha 0.28; sun baked into the plate, so `sunEnabled` stays 0 — one sun rule) |
| Hallow's End | pumpkin wagon drifts the graveyard road (−1.35 u/s hitch-first on a 6144px mostly-empty `cart_strip_hallow`, crossing ~every 36s, wheels behind the fence rows; white `cloud4` skipped — wrong mood) | 24 ember motes (1, 0.62, 0.32), a 0.5, slow fall + high sway | 3 bats — small near-black flock, fast flaps, every 25–55s | — (eclipse ring + halo are layers upper-right; `sunEnabled` stays 0 — one sun rule; `light2` soft red horizon glow behind the skyline, alpha 0.75) |
