# Custom Game (the testing setup screen)

Binding for the dev/testing entry point. The hand-made "test levels" are gone; to test, open
**Level Select → Custom Game** and dial in a run. Editor-only (it discovers content via
`AssetDatabase`), so the button shows and the toggles populate **only in the Unity editor**.

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

## The maintenance rule (read this before adding content)

- **New ability or block → nothing to do.** [ContentCatalog](Assets/SourceFiles/Scripts/Levels/ContentCatalog.cs)
  loops over *every* `AbilityDefinition` / `BlockDefinition` in the project, so anything you author
  appears as a toggle automatically (abilities grouped by rarity, dummies excluded). This was the
  whole point — no list to keep in sync.
- **New tweakable FIELD on `GameModeConfig` that you want exposed → three edits.** Add the field to
  `CustomGameSettings` (+ seed it in `FromConfig`), add a control for it in the matching
  `Build*Section` of `CustomGameMenu`, and write it through in `GameModeConfig.ApplyCustomGameOverrides`.
  If you skip this, the new field simply keeps the preset's value (safe, just not adjustable here).

## Curated scope (intentionally not everything)

Exposed: goal (Endless / Place Blocks / Reach Height + target), starting lives, initial/max fall
speed, difficulty ramp (None / Per Block / Over Time + amounts — "Over Time" is the time-based
difficulty), floor width, spawn delay, power-up frequency, static islands (+ chance), block bag,
ability pool (per-rarity All/None). The fiddly physics/camera tuning (settle thresholds,
micro-align, camera smoothing, ambient variants) is **left at the preset's values** — change those on
the preset asset if you need to. Widen the curated set by following the maintenance rule above.

## Removed

The `Chapter_TestingGrounds` sandbox chapter and its levels (Classic, Narrow, SkyPlatforms, Hard,
LaserLimit, Test10Blocks, TestAbilities) and the `GameMode_AbilityTest` bench were deleted. The mode
*configs* (Classic/Narrow/Hard/LaserLimit/SkyPlatforms/Spire) are kept and reused as Custom Game
**presets**. The real campaign (Training Wheels, Desert) is untouched.
