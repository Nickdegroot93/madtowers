# Asset Import Layout & New-Chapter Playbook

How a purchased parallax pack becomes a playable chapter, start to finish: import cleanup,
layer authoring, testing, and the troubleshooting table for when it looks wrong. Deep dives
live in the linked docs — this file is the checklist that ties them together.

Imported chapter/environment packs live here:

`Assets/Art/ChapterPacks/<Pack Name>/`

Current packs:

- `Assets/Art/ChapterPacks/Jungle Landscape`
- `Assets/Art/ChapterPacks/Desert Vibe`
- `Assets/Art/ChapterPacks/Japan Landscape`
- `Assets/Art/ChapterPacks/Winter Mountain Landscape`
- `Assets/Art/ChapterPacks/Chinese City`
- `Assets/Art/ChapterPacks/Sovietwave Panel Buildings`
- `Assets/Art/ChapterPacks/Glowing City 2D Landscape`
- `Assets/Art/ChapterPacks/Volcano Landscape` (import dropped its sprites inside Jungle
  Landscape and strays inside Desert Vibe + `Assets/Volcano Landscape/Scripts/`; all
  relocated here, Desert Vibe overwrites reverted — the Step 1 drill exactly)
- `Assets/Art/ChapterPacks/Cyber Egypt` (same drill: sprites landed in Jungle Landscape,
  light elements in Glowing City, scripts at `Assets/Cyber Egypt/`; overwrites of Chinese
  City / Desert Vibe / Glowing City reverted)
- `Assets/Art/ChapterPacks/Lost City` (same drill: sprites in Desert Vibe + Jungle
  Landscape, scripts at `Assets/Lost City/`; Desert Vibe overwrites reverted)
- `Assets/Art/ChapterPacks/Secret Island` (worst drill yet: sprites scattered across Japan
  Landscape / Jungle Landscape / Desert Vibe, and the pack SHIPS its own bg/q/w/boat sprites
  at other packs' GUIDs+paths - the plate, layer1 mountains, layer2 buildings w1-w4 and the
  boat had to be re-extracted from the .unitypackage with fresh GUIDs after reverting the
  overwrites of Chinese City / Glowing City / Desert Vibe)
- `Assets/Art/ChapterPacks/Tropical Landscape` (mild drill: all sprites landed inside Jungle
  Landscape as NEW TL-suffixed files with their own GUIDs (no content overwrites for once),
  scripts at `Assets/Tropical Landscape/`; relocated with vendor metas, Desert Vibe
  importer-meta churn reverted)
- `Assets/Art/ChapterPacks/Halloween` (same drill: sprites scattered across Japan Landscape /
  Jungle Landscape / Desert Vibe, scripts at `Assets/Halloween/`, and the pack ships its
  bg / layer1 d1-d3 / layer2 c1-c4 / layer3 b1-b3+pl / cart / bushes a1-a3 at Japan
  Landscape + Sovietwave GUIDs+paths — those 17 were copied out meta-less for fresh GUIDs
  after reverting the Japan Landscape / Sovietwave / Desert Vibe overwrites)

Game-authored chapter presentation stays separate:

- `Assets/Art/Chapters/<ChapterId>`: menu image, generated cloud strip, any chapter-owned
  edited copies of pack art (never edit vendor files in place)
- `Assets/Data/Backdrops`: gameplay backdrop presets that reference selected pack sprites by GUID
- `Assets/Resources/Chapters`: chapter definitions loaded at runtime
- `Assets/Resources/Skins/<Theme>`: runtime block/ground/laser skins loaded by `Resources.Load`

## Why Imported Packs Can Look Wrong

`.unitypackage` files store the original project paths chosen by the pack author. If a package
contains `Assets/Jungle Landscape/...`, Unity imports those files there. The import dialog can
toggle files on/off, but it does not remap them into a cleaner destination folder.

That is why Desert files landed under `Assets/Jungle Landscape`, and why the Japan package preview
also wants to write some files there. This is a package-path issue, not a runtime chapter system
requirement.

## Step 1 — Import & Relocate

1. Import the package.
2. **Run `git status` immediately.** Same-author packs share GUIDs and paths, so an import can
   silently **overwrite files of already-imported packs** (the Chinese City import downgraded
   Desert Vibe's URP pipeline asset and materials). `git checkout` any modified files that belong
   to other packs; only the `??` (new) files are this pack's.
3. Move every new file into `Assets/Art/ChapterPacks/<Pack Name>/`, **together with its `.meta`**
   so GUID references stay intact. Watch for new files dropped *inside other packs' folders*
   (e.g. `Jungle Landscape/Sprites/layer2/CH1.png`) and top-level strays (`Assets/<Pack Name>/`).
4. Rename the vendor `Scripts/` folder to `Scripts~` (and delete its `.cs.meta` files). The tilde
   makes Unity ignore it — these packs reuse class names (`BackgroundParalax`, `CameraUtils`),
   so two compiled copies = duplicate-class errors. We never use vendor runtime scripts.
5. Keep vendor demo scenes, pipelines, presets, and PDFs inside the pack folder; don't adopt them.
6. Do not put raw vendor packs under `Assets/Resources`.
7. In import dialogs, leave already-imported duplicate files **unchecked** unless you intentionally
   want an overwrite.

## Step 2 — Inspect Every Sprite Before Using It

Open each sprite (or preview the PNG) and classify it. **Not every pack sprite is a tiling
strip**, and using the wrong kind causes most visual bugs:

| Kind | How to recognize | Use |
|---|---|---|
| Full background plate | ~16:9, sky + sun + far horizon, opaque | The one `fillView` layer |
| Cloud/mist strip | wide, transparent, drifts | The `driftSpeedX` layer |
| Horizon/scenery strip | very wide (ratio ≥ ~2.5:1), solid opaque **bottom row**, silhouette top | Parallax layers, far → near |
| Hand-placed mass | straight **vertical** edges, content doesn't reach the sides | **Don't tile it** — skip, or place once with `worldOffsetX`, tileRadius 0 |
| Reflection variant | mirrored ghost copy below the scenery | Skip (made for the pack's lake demo) |
| Overlays (light/shadow/halftone/sun sheets) | screen-sized gradients, particle sheets, animation frames | Usually skip — but see below when the pack's LOOK depends on them |
| Glow lights (moon halo, lanterns, city glow) | **opaque** color fills with no alpha falloff — built for the pack's additive shader | Don't use directly (they render as solid blocks). Rebuild as alpha-gradient sprites: `Tools/generate_glow_sprite.py radial\|band\|wash "<r,g,b>" <peak_alpha> <out.png>`, layer at the light source's position. For a whole-scene color grade (nostalgic warm cast), add a `wash` sprite as the LAST layer with `fillView: 1` and a low layer `alpha` (~0.08) — Kvartal 4 is the reference for all of these. Author glows BIG with LOW peaks; dense saturated discs read as spotlights |

Also check the plate for a **baked horizontal seam** (a hard tone step below its horizon —
common in these packs). It will be exposed mid-climb when the scenery strips sink. Fix: write a
chapter-owned copy into `Assets/Art/Chapters/<ChapterId>/` with the step feathered
(Gaussian-blur a band around it), author your own `.meta` for it, and reference that copy.
Leave the vendor file untouched.

## Step 3 — Author the Backdrop

Layer order, worldHeight/parallax/tiling values and the tuning checklist: **[BACKDROPS.md](BACKDROPS.md)**.
Drift, particles, flybys, per-theme recipes: **[AMBIENCE.md](AMBIENCE.md)**. Points learned the hard way:

- Layers stack far → near: fill plate, cloud strip, far mountains, mid scenery, near scenery,
  optional thin foreground strip. 6–8 layers is plenty; drop the busiest near band if the floor
  area gets noisy (gameplay clarity beats pack completeness).
- **Every scenery layer gets a `groundFillColor` apron** in the *exact* color of its bottom row
  (sample the pixel — don't eyeball; being off by 2% shows as a line). The apron fills below the
  layer at that layer's own depth, so no bottom edge can ever be exposed through gaps in nearer
  layers. Strips with airy gaps (pagodas, bridges) make this mandatory.
- Pack has no cloud layer? Generate one in the packs' flat vector style:
  `python3 Tools/generate_chapter_clouds.py "<r,g,b>" "<theme>-clouds" "Assets/Art/Chapters/<Id>/clouds_<x>.png"`.
  Tint it slightly lighter than the sky behind it.
- Set `cloudCount: 0` when the preset has a fill plate — procedural clouds render *behind*
  imported layers (order −90 vs −89+) and are invisible.
- A sun/moon baked into the plate is camera-anchored and always visible; don't also enable the
  procedural sun disc, or you get two.

## Step 4 — The Rest of the Chapter

Full data-model recipe: **[LEVELS.md](LEVELS.md)** ("New chapter"). Conventions used by every
chapter so far:

- Chapter asset `Chapter_<PascalId>` (sortOrder gaps of 10), three starter levels
  `Level_<XX><n>_<Name>` reusing `GameMode_Classic` (place 100), `GameMode_LaserLimit` +
  `HeightLimitWaves_Standard` modifier (place 50), `GameMode_Narrow3` (reach 50m).
- Skin: add a theme entry to **both** `Tools/generate_piece_sprites.py` (palette keeps the 7 hue
  identities — STYLE.md) and `Tools/generate_ground_sprite.py` (stone + cap matching the
  backdrop), run both, set the chapter's `skinFolder`. Both generators are deterministic —
  rerunning must not dirty other themes.
- **Menu background image** (one per chapter):
  `Tools/compress_chapter_image.sh background <src.png> "Assets/Art/Chapters/<Id>/<id>.jpg"`.
  Set `menuTopIsLight` to match the image's top region.
- **Music** (two tracks per chapter, `<id>-a` + `<id>-b`): WAV sources archived in
  `Assets/Data/Audio/Source~/`, OGGs (~150–170 kbps) in `Assets/Data/Audio/Music/`, both on
  `musicPlaylist` (random opener, then fixed rotation — automatic). Specs: ART.md §8.
  If one-shot encoding crashes, encode in chunks via `soundfile.SoundFile` blocks.
- Let Unity generate `.meta` files for plain media (refresh, then read the GUIDs out of the metas
  to wire references). Hand-author metas only when writing `.asset` YAML directly, and always
  write asset + meta together.

## Step 5 — Test It

1. `Tools > MadTowers > Validate Chapter Content` — must be 0 errors; warnings are review prompts.
2. Play the chapter's first level and look at **three heights**: ground, mid-climb (~15m), and
   high (~35m+). Layers must sink smoothly, the plate must never expose an edge, and nothing may
   pop or cut. In the editor this can be driven headlessly: select the level via
   `LevelSelectionState.SelectLevel(...)` + scene reload, temporarily disable
   `TowerCameraController` and set the camera Y, screenshot each height.
3. Editor-scripting traps: set `Application.runInBackground = true` *after* entering play mode
   (domain reload wipes it), and disable console Error Pause — harmless "memoryless depth
   surface" errors will otherwise freeze the run.

## Troubleshooting — "I see a line / band that shouldn't be there"

| Symptom | Cause | Fix |
|---|---|---|
| Thin **horizontal** line near the floor, lighter or darker than its surroundings | A scenery layer's bottom edge is exposed through a gap in nearer layers; the line color = that sprite's bottom-row color | Find which strip's bottom row matches the line color, set that layer's `groundFillColor` to the exact sampled color |
| **Horizontal** tone step across the sky, appears while climbing | Baked seam in the vendor background plate | Feathered chapter-owned copy (Step 2) |
| Full-height **vertical** band(s), crisp edges | A hand-placed "mass" sprite is being tiled — its straight vertical art edges show | Remove that layer or place it once (`horizontalTileRadius: 0`) |
| Faint full-height **vertical** column that *moves with the falling piece* | The placement beam (landing preview) — more visible on dark backdrops | Not a bug. Leave it; alphas live in `BlockController.PlacementBeam.cs` if it ever needs a per-theme pass |
| Repeating silhouettes look obviously cloned | Tile width too small on screen | Larger `worldHeight` (wider tiles), or accept — distant repeats read fine |
| Scenery visibly slides sideways relative to neighbors during pans | `horizontalParallax` out of order | Must decrease monotonically far → near (1.0 plate → ~0.02 foreground) |
| Layer pops in/out or unveils gaps while climbing | `verticalParallax` out of order, or a near layer offset too high | Nearer = lower `verticalParallax`; sink near layers with negative `floorOffsetY` |
| Clouds missing | Procedural clouds behind the fill plate | `cloudCount: 0` + a sprite/generated cloud strip layer |
| Generated clouds look like pills / rounded rectangles | Old wisp shapes | Regenerate with `Tools/generate_chapter_clouds.py` (circle-union shapes only) |
| "guid mismatch" / pink sprites after hand-authoring | Asset written without its meta (or vice versa) in one refresh window | Write asset+meta together, then force refresh; worst case close Unity, verify files on disk, delete `Library` |
