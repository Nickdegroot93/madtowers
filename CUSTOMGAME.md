# Custom Game (the testing setup screen)

Binding for the dev/testing entry point. The hand-made "test levels" are gone; to test, open
**Settings tab → Custom Game** and dial in a run.

**Availability:** the Unity editor **and (temporarily) all player builds**. The
`Debug.isDebugBuild` gate that normally hid it from release builds is currently disabled in
`ContentCatalog.IsAvailable` (marked `TEMPORARY`) so the button is reachable on device for testing
— restore that gate before shipping. Gated by `ContentCatalog.IsAvailable`, which both the
Settings-tab button
([MainMenuRuntime](Assets/SourceFiles/Scripts/Menu/MainMenuRuntime.cs)) and the no-chapters fallback
check. In the editor it discovers content live via `AssetDatabase`; a player build has no
AssetDatabase, so it reads a [ContentManifest](Assets/SourceFiles/Scripts/Levels/ContentManifest.cs)
baked under `Resources/` by a build preprocessor (see below).

## What it does

A single screen ([CustomGameMenu.cs](Assets/SourceFiles/Scripts/Levels/CustomGameMenu.cs)) that
edits a [CustomGameSettings](Assets/SourceFiles/Scripts/Levels/CustomGameSettings.cs) model seeded
from a **preset** (any `GameModeConfig` in `Resources/GameModes`, e.g. Classic). On **Start** it:

1. clones the preset (`Object.Instantiate`),
2. applies the curated overrides (`GameModeConfig.ApplyCustomGameOverrides`),
3. wraps it in a runtime level (`LevelDefinition.CreateRuntime`, equal-odds rarity profile so every
   enabled ability is equally likely to appear),
4. `LevelSelectionState.SelectLevel(...)` + reloads the gameplay scene — exactly like a real level.

The runtime config/level are throwaway `ScriptableObject` instances held alive by the static
`LevelSelectionState`; they are never written to disk. Settings persist for the editor session.
After the reload, Custom Game uses the same `GameManager`/`LevelRuntimeController`/`Spawner`
pipeline as chapter levels. That means intro pans, wave-reveal holds, ability offers, win
verification, and level-complete events should behave identically; don't add a separate custom-run
gameplay path unless the whole shared level pipeline is deliberately being changed.

**Testing defaults** (override the preset on purpose, since this is a dev tool): **all abilities
OFF** (enable one at a time to test it in isolation), **3 starting lives**, **power-up choice
every 5 blocks**. Blocks default to the preset's bag. Set in `CustomGameSettings.FromConfig`
(lives / interval) and `CustomGameMenu.EnsureState` (abilities).

## Build-safe content (how it works on device)

[ContentCatalog](Assets/SourceFiles/Scripts/Levels/ContentCatalog.cs) has two backends behind one
API: in the **editor** it queries `AssetDatabase` live; in a **player build** it reads a
[ContentManifest](Assets/SourceFiles/Scripts/Levels/ContentManifest.cs) asset under `Resources/`.
The manifest is **baked automatically before every build** by
[ContentManifestBuilder](Assets/SourceFiles/Scripts/Editor/ContentManifestBuilder.cs) (an
`IPreprocessBuildWithReport`), which runs the same editor discovery and writes the result in. You
can also rebuild it by hand via **Tools ▸ MadTowers ▸ Rebuild Content Manifest** (the committed
asset is empty until first baked; the editor never needs it, so that's fine). Because the manifest
is regenerated from a full project scan, the no-maintenance rule below still holds.

## The maintenance rule (read this before adding content)

- **New ability or block → nothing to do.** ContentCatalog loops over *every* `AbilityDefinition` /
  `BlockDefinition` in the project (and the build manifest is re-baked from that same scan), so
  anything you author appears as a toggle automatically (abilities grouped by rarity, dummies
  excluded). This was the whole point — no list to keep in sync.
- **New tweakable FIELD on `GameModeConfig` that you want exposed → three edits.** Add the field to
  `CustomGameSettings` (+ seed it in `FromConfig`), add a control for it in the matching
  `Build*Section` of `CustomGameMenu`, and write it through in `GameModeConfig.ApplyCustomGameOverrides`.
  If you skip this, the new field simply keeps the preset's value (safe, just not adjustable here).

## Curated scope (intentionally not everything)

Exposed: goal (Endless / Place Blocks / Reach Height + target), starting lives, initial/max fall
speed, difficulty ramp (None / Per Block / Over Time + amounts — "Over Time" is the time-based
difficulty), floor width, spawn delay, power-up frequency, static islands (+ chance), block bag,
**block-variant spawn chances** (a dev knob — set Anchor/Boulder/… to 1.00 to force every piece to
that variant; editor-only, empty in player builds), ability pool (per-rarity All/None). The fiddly
physics/camera tuning (settle thresholds, micro-align, camera smoothing) is **left at the preset's
values** — change those on the preset asset if you need to. Widen the curated set by following the
maintenance rule above.

## Removed

The `Chapter_TestingGrounds` sandbox chapter and its levels (Classic, Narrow, SkyPlatforms, Hard,
LaserLimit, Test10Blocks, TestAbilities) and the `GameMode_AbilityTest` bench were deleted. The mode
*configs* are kept and reused as Custom Game **presets** — the list is auto-discovered from
`Resources/GameModes` (Classic/Narrow/Narrow3/Hard/LaserLimit/SkyPlatforms plus every
chapter-owned mode: Jungle, Sakura, Neon, Giza, …). The real campaign is untouched.
