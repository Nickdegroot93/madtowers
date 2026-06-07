# Current Gameplay

This document describes how the game works right now. It is not a future roadmap; it is a practical reference for the current prototype behavior.

## Core Loop

1. A tetromino shape is selected from the configured block bag.
2. The block spawns at the spawn point near the top of the camera view.
3. While the block is active, the player can move, rotate, and fast-drop it.
4. The active block falls under controlled kinematic movement, not normal gravity.
5. When the block lands, it is snapped to the grid and checked for stability.
6. Stable blocks are frozen into the logical tower grid.
7. Unstable blocks or unstable tower chunks are released back into Dynamic Rigidbody2D physics.
8. Score and max height update when the active block locks.
9. The next block spawns after the configured spawn delay.
10. Game over occurs only when a block falls into the loss zone below the floor.

## Movement And Input

Active blocks use `StackingInputs`.

Current controls handled by `BlockController`:
- Horizontal movement is discrete, one grid column at a time.
- Holding left/right uses delayed auto shift:
  - `dasDelay`
  - `dasRate`
- Rotation snaps to 90-degree increments.
- Fast drop multiplies fall speed by `fastDropMultiplier`.

While controlled by the player:
- The block Rigidbody2D is Kinematic.
- Gravity is disabled.
- Rotation is locked to grid-right angles.
- X position is snapped using the configured grid spacing.
- Horizontal movement is limited to the current playable envelope: floor segments plus already landed block bounds plus the configured placement buffer.
- The active block must also remain inside the current camera width, so it cannot be moved off-screen while falling.

Current default:
- `horizontalPlacementBufferColumns = 3`

The falling block does not expand the camera. It can only move within the allowed placement area.

## Grid And Floor

The grid spacing is configured by `GameModeConfig.gridSpacing`.

The current default is:
- `gridSpacing = 1`
- One centered floor segment
- `centerColumn = 0`
- `columnCount = 9`

That means the floor supports columns `-4` through `4`, with visual edges at `-4.5` and `4.5`.

Floor data is used in two places:
- `PlayAreaController` sizes and positions the visible floor.
- `PlayAreaController` calculates the logical row-0 height from the floor collider top.
- `TowerGrid` uses the same floor segment columns to decide which grid cells are supported.
- `TowerGrid` also treats floor side cells as lateral braces, so pieces can hook around the edge of the floor.

This is already prepared for future levels with smaller floors, wider floors, or multiple floor segments with gaps.

## Landing And Physics

The default landing mode is `StrictGrid`.

In `StrictGrid`:
- The falling block moves down on the grid.
- Side/corner contact with frozen blocks does not count as a landing.
- The block lands only when its grid cells reach logical floor columns, static support islands, or occupied cells below.
- On landing, it snaps to row and column grid positions.
- The block is checked for center-of-mass support.
- If center-of-mass support is not enough, side contact can still stabilize the block as a lateral brace.

If stable:
- The Rigidbody2D is frozen.
- Gravity is disabled.
- The block's cells are registered in `TowerGrid`.

If unstable:
- The block is released into Dynamic physics.
- Rotation constraints are removed.
- Gravity is restored.
- A small directional angular kick and impulse are applied so unstable pieces do not balance forever on a mathematical edge.

After a block is registered, `TowerGrid` also checks connected tower chunks. If a connected chunk's combined center of mass is outside its support footprint, that chunk is released into Dynamic physics too.

## Stability

Stability uses:
- The grid cell positions of the current block or connected tower component.
- The support footprint underneath those cells.
- Optional lateral brace contacts against existing tower cells, static support islands, and floor sides.
- `GameModeConfig.stabilityMargin`.

Current default:
- `stabilityMargin = 0.08`
- `lateralBraceStabilityEnabled = true`
- `lateralBraceMinimumContacts = 1`
- `connectedComponentLateralBraceEnabled = true`
- `connectedComponentLateralBraceMinimumContacts = 1`
- `connectedComponentLateralBraceMaxCells = 4`

The margin means a center of mass must sit slightly inside the support edge. Being exactly on the edge does not count as stable.

Lateral brace stability is the current Tricky Towers-style forgiveness rule:
- The newly landed block still needs real bottom support.
- If its center of mass is outside the bottom support footprint, side contact with existing tower cells, static support islands, or floor sides can keep it grid-locked.
- Pure side-touching with no bottom support does not count.
- This allows hooked/cornered placements without making unsupported overhangs float.

Connected tower chunks are stricter by default:
- After a block is registered, `TowerGrid` checks the combined connected chunk.
- Small connected chunks can still use lateral brace forgiveness.
- Once a connected chunk grows beyond `connectedComponentLateralBraceMaxCells`, it must satisfy center-of-mass support.
- This allows one hooked tetromino to stay, while adding enough mass to one side can make the connected tower fall.

Block variants can currently opt out of stability failure with:
- `BlockData.ignoresStabilityFailure`

That is intended for future sturdy/anchor blocks.

## Block Shapes

Block shape data is represented by `BlockDefinition`.

Current shape responsibilities:
- Display name
- Prefab reference
- Default `BlockData`
- Bag copies

Current default shapes:
- I
- J
- L
- O
- S
- T
- Z

The spawner uses the configured `GameModeConfig.blockBag` first. Legacy prefab arrays still exist as fallback support.

## Block Variants

Block variant data is represented by `BlockData`.

Current variant responsibilities:
- Display name
- Mass
- Physics material
- Gravity multiplier
- Color tint
- Optional sprite override
- Optional material override
- Optional landing mode override
- Optional stability-failure ignore flag

Current variant assets:
- Normal
- Heavy

Variants are applied by `BlockController.ApplyData`.

Future examples that fit this model:
- Icy: low-friction physics material.
- Heavy: high mass.
- Light: low mass or gravity multiplier.
- Sturdy: ignores stability failure.
- Bouncy: bouncy physics material.
- Themed: sprite/material override.

## Spawning

`Spawner` prepares one next block and then spawns it when the current block locks.

Current behavior:
- Uses the configured block bag from `GameModeConfig`.
- Refills the bag when empty.
- Randomly removes entries from the bag so each bag cycle is fairer than pure random.
- Applies the block definition's default variant.
- Falls back to configured fallback variants if a definition has no default.
- Applies current fall speed and gravity from `GameManager`.
- Emits the next-block label through `GameEvents`.

Current spawn delay:
- Controlled by `GameModeConfig.spawnDelay`
- Default: `0.5`

## Speed And Difficulty

`GameManager` owns current fall speed and current gravity scale.

Current default config:
- `initialFallSpeed = 2`
- `initialGravityScale = 1`
- `difficultyScalingMode = PerBlock`
- `difficultyAdjustmentMode = Additive`
- `speedIncreasePerBlock = 0.1`
- `gravityIncreasePerBlock = 0.05`

Supported scaling modes:
- `None`: no automatic speed scaling.
- `PerBlock`: increase difficulty when a block locks.
- `OverTime`: increase difficulty after a configured number of seconds.

Supported adjustment modes:
- `Additive`: add the configured amount.
- `Percent`: multiply by `1 + amount`.

Example:
- Additive `0.1` means speed increases from `2.0` to `2.1`.
- Percent `0.1` means speed increases from `2.0` to `2.2`.

## Camera

`TowerCameraController` follows the tower upward.

Current default config:
- `minimumCameraY = 0`
- `towerPeakScreenY = 0.64`
- `spawnPointScreenY = 0.88`
- `cameraSmoothTime = 0.28`
- `minimumCameraSize = 15`
- `maximumCameraSize = 24`
- `horizontalCameraPadding = 1.5`
- `horizontalCameraSafeArea = 0.78`
- `cameraZoomSmoothTime = 0.35`

The camera only moves upward. It does not move back down if the tower collapses.

The spawn point follows the camera so new blocks continue appearing near the top of the visible play area.

The background transform follows the camera vertically so the visible background stays filled while the floor and loss zone remain fixed in world space.

The camera also adjusts orthographic size horizontally:
- Blocks register with `BlockController.AllBlocks`.
- The camera calculates bounds for landed blocks inside the current vertical focus band.
- If that focused tower section gets close to the horizontal safe area, the camera zooms out.
- The zoom uses the farthest block edge from the camera center, so one-sided expansion triggers zoom correctly.
- If wide blocks fall below the focus band and the visible tower becomes narrow again, the camera can zoom back toward the minimum size.
- The zoom calculation uses the normal minimum camera height as its vertical focus band so old wide foundations do not keep the camera zoomed out forever.
- Active falling blocks are ignored by camera zoom. This prevents players from holding left/right and forcing the camera to zoom out before anything has been placed.

## Score, Height, And Game Over

Score:
- Increases when a block locks.

Height:
- Tracks the highest actual block cell Y value.
- The UI shows max height through `GameEvents.HeightChanged`.

Game over:
- No longer happens at the top of the screen.
- Happens only when a Rigidbody2D belonging to a block enters `LossZone`.
- `LossZone` destroys the fallen block object after triggering game over.

Restart:
- The restart button asks `GameManager` to reload the current scene.
- Before the scene reloads, `BlockController.ResetRuntimeState` clears the static tower grid and tracked block list.
- `GameManager.Awake` also clears block runtime state, so scene reloads from other entry points do not inherit invisible logical tower cells.
- `GameManager` and `UIManager` clear their singleton references when destroyed.

Lives:
- `GameManager` supports lives.
- If lives are above zero, a loss-zone event consumes one life instead of ending the game.
- Default current lives: `0`.

## Power-Ups

Power-ups are still prototype-level.

Current types:
- SlowMotion
- ExtraLife

Current spawning:
- `PowerUpManager` watches max height.
- Every configured height interval, it spawns a power-up target.
- X position is randomly rounded to an integer column.
- Spawn interval and range come from `GameModeConfig`.

Current collection:
- Power-ups are collected when a landed block overlaps them.
- Collection is driven by `BlockController.CollectOverlappingPowerUps`.

Current effects:
- SlowMotion changes `Time.timeScale` temporarily.
- ExtraLife increments lives.

Known future cleanup:
- Power-ups should become ScriptableObject data before adding many more effects.

## Static Support Islands

`StaticSupportIslandManager` owns static support island spawning.

Current behavior:
- Watches max tower height.
- Every configured height interval, rolls once to decide whether an island appears.
- If the roll succeeds, chooses one weighted island shape from `GameModeConfig`.
- Spawns one static 1x1 support prefab per shape cell.
- Snaps island cells to the same column/grid spacing as falling blocks.
- Keeps the configured center lane clear, so an untouched falling block will not hit a support island in the default drop path.
- Registers island cells with `TowerGrid`, so blocks can land on them and count them as real stability support.

Current default config:
- `staticSupportIslandsEnabled = true`
- `staticSupportIslandHeightInterval = 8`
- `staticSupportIslandSpawnChance = 0.35`
- `staticSupportIslandFirstHeight = 6`
- `staticSupportIslandSpawnAheadHeight = 8`
- `staticSupportIslandMinColumn = -5`
- `staticSupportIslandMaxColumn = 5`
- `staticSupportIslandCenterClearColumns = 5`

Current default weighted shapes:
- Single: weight `6`
- Two Wide: weight `3`
- Two Tall: weight `2`
- Corner: weight `1`

Frequency is controlled by both interval and chance. For example, an interval of `8` and chance of `0.35` means the level rolls about every 8 meters of tower height, and roughly 35% of those rolls create an island.

## Events And UI

`GameEvents` is a static event hub for current runtime events:
- Score changed
- Lives changed
- Height changed
- Next block changed
- Game over

`UIManager` listens to these events instead of polling gameplay state every frame.

## Current Data Assets

Current level wrapper:
- `Assets/Data/Levels/Level_01.asset`

Current mechanical game mode:
- `Assets/Data/GameModes/DefaultGameMode.asset`

Current block definitions:
- `Assets/Data/BlockDefinitions/Block_I.asset`
- `Assets/Data/BlockDefinitions/Block_J.asset`
- `Assets/Data/BlockDefinitions/Block_L.asset`
- `Assets/Data/BlockDefinitions/Block_O.asset`
- `Assets/Data/BlockDefinitions/Block_S.asset`
- `Assets/Data/BlockDefinitions/Block_T.asset`
- `Assets/Data/BlockDefinitions/Block_Z.asset`

Current block variants:
- `Assets/Data/Blocks/Normal.asset`
- `Assets/Data/Blocks/Heavy.asset`

## Known Prototype Edges

- Power-up effects are still hardcoded with an enum.
- Obstacle spawning is inactive and not yet fully level-authored.
- `Spawner` still has legacy fallback prefab/variant arrays for safety.
- There is no level-selection flow yet.
- Presentation fields exist in `LevelDefinition`, but the active scene still references `GameModeConfig` directly.
