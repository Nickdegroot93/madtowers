# Code Architecture

## Current Runtime Shape

- `Scripts/Blocks`: Tetromino control, grid snapping, stability, and block/variant definitions.
- `Scripts/Levels`: Level/game-mode data, floor segment definitions, play-area sizing, and difficulty scaling.
- `Scripts/Spawning`: Block selection and spawning.
- `Scripts/Camera`: Tower-following camera logic.
- `Scripts/Core`: Global game state, events, and small reusable runtime helpers.
- `Scripts/World`: Loss zone, power-ups, and static support islands.
- `Scripts/UI`: UI display and game-over presentation.
- `Scripts/_Legacy`: Starter/tutorial scripts that are not part of the stacking game loop.

## Data Model

- `LevelDefinition` is the future authored level asset: name, rules config, background, tint, and music.
- `GameModeConfig` is the level rules asset: lives, fall speed, scaling mode, block bag, variants, grid spacing, floor segments, camera composition, power-up timing, and static support island rules.
- `BlockDefinition` is the shape asset: prefab, display name, and shape-specific default variant.
- `BlockData` is the variant asset: mass, physics material, gravity multiplier, visual overrides, landing behavior override, and future sturdy/anchor behavior.

## Extension Rules

- Add new levels by creating/duplicating data assets, not by hardcoding scene values.
- Add new block variants by creating `BlockData` assets, then including them in a level's variant pool or a shape's default data.
- Keep per-frame logic in runtime controllers; keep authored balancing numbers in ScriptableObjects.
- Keep scene objects thin. Prefer assigning data assets over adding special-case MonoBehaviour fields for each level.
