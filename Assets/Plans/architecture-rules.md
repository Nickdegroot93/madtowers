# Architecture Rules

These rules are intentionally practical. They exist to keep the stacking game easy to extend as levels, block variants, power-ups, obstacles, and presentation grow.

## Source Of Truth

- Author gameplay balance in ScriptableObject assets.
- Keep scene objects thin. Scene objects should reference data assets, transforms, prefabs, and UI widgets; they should not contain per-level rules.
- Do not hardcode level-specific values in runtime scripts when the value belongs to a level, game mode, block variant, power-up, or spawn table.
- Preserve `.meta` files when moving assets. Unity references scripts and assets by GUID.

## Runtime Code

- MonoBehaviours should coordinate Unity lifecycle and scene references.
- Plain C# helper classes should hold reusable logic that does not need Unity lifecycle callbacks.
- Avoid `FindObject*` in gameplay loops. Prefer serialized references, events, or explicit initialization.
- Avoid direct UI polling of gameplay state every frame. Raise events when score, lives, height, next block, or game-over state changes.
- Keep physics/grid decisions centralized in block/grid systems. Do not duplicate placement rules in managers, UI, or power-ups.
- If a future feature needs special block behavior, prefer adding a data flag/strategy field to `BlockData` before adding shape-specific conditions.

## Data Rules

- `LevelDefinition` describes an authored level: presentation plus a rules config.
- `GameModeConfig` describes mechanical rules: lives, grid spacing, floor segments, block bag, variants, speed scaling, camera composition, power-up timing, and static support island rules.
- `BlockDefinition` describes shape identity and prefab.
- `BlockData` describes a block variant: physics, visuals, and placement behavior.
- Future power-ups should become data assets before the number of effects grows beyond the current prototype.
- Future level spawns should be authored as level data, not hardcoded into manager scripts.

## Folder Rules

- Keep stacking-game runtime scripts under feature folders in `Assets/SourceFiles/Scripts`.
- Keep old tutorial/starter scripts under `_Legacy`.
- Keep authored gameplay data under `Assets/Data`.
- Keep design notes and architecture rules under `Assets/Plans`.

## Extension Checklist

Before adding a new feature, ask:

1. Is this a new rule for a level or game mode? Put it in `GameModeConfig`.
2. Is this a new block shape? Add or duplicate a `BlockDefinition`.
3. Is this a new block behavior/material/visual variant? Add or duplicate a `BlockData`.
4. Is this new level presentation? Add it to `LevelDefinition`.
5. Is this runtime orchestration? Add it to the relevant manager.
6. Is this reusable math/state logic? Prefer a plain C# helper class.
