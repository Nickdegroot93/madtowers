# Current Gameplay

This document describes the prototype as it works now. It is a practical reference for the current game loop, not a future roadmap.

## Core Loop

1. A tetromino shape is selected from the configured block bag.
2. The block spawns at the spawn point near the top of the camera view.
3. While the block is active, the player can move, rotate, and fast-drop it.
4. The active block is steered on a column grid while using a real `Rigidbody2D`.
5. When support is detected below the block, player steering stops and Unity 2D physics takes over.
6. Score and max height update as soon as player control is released.
7. The next block spawns after the configured spawn delay, even if the previous block/tower is still wobbling.
8. Settled blocks later run lightweight micro-alignment/sleep maintenance when they are calm enough.
9. Game over occurs only when a block falls into the loss zone below the floor.

## Movement And Input

Active blocks use `StackingInputs`.

Current controls handled by `BlockController`:
- Horizontal movement changes the target column by one grid column.
- Holding left/right uses delayed auto shift through `dasDelay` and `dasRate`.
- Rotation changes the target Z angle in 90-degree increments.
- Fast drop multiplies fall speed by `fastDropMultiplier`.

While the block is still falling:
- The body is a dynamic `Rigidbody2D`.
- Gravity is temporarily disabled.
- Vertical fall and horizontal column movement are applied by explicit cast-and-position steps.
- Linear velocity is kept at zero during controlled falling, so the active block is not a physical projectile.
- Horizontal steering is side-contact limited: if the falling piece would hit a landed block from the side this frame, sideways velocity is capped or stopped instead of pushing the tower.
- Rotation is driven toward a target 90-degree angle.
- The block can land while still rotating, and that remaining spin can carry into physics.
- Landing handoff happens very close to support, so quick last-second horizontal tucks are still possible before physics takes over.
- If support is detected while the piece is still sliding into its target column, landing is delayed briefly so it does not stand on a tiny corner instead of entering the gap.
- Upward contacts near a cell's left/right edge are ignored as landing support, so a block needs real support under the body of a cell instead of balancing on a corner scrape.
- Fast-drop movement still casts ahead for any physical contact. If the nearest contact is not valid landing support, the block stops just above it instead of moving through it and letting the physics solver push bodies apart.
- If that invalid contact is a corner scrape while the piece is already tucking horizontally, the controlled piece gets a small one-frame sideways nudge off the corner.
- If the corner cannot be resolved after a short timeout, control releases to physics and the next block can spawn instead of trapping the current block as active forever.
- Before the physics step can create a downward impact, the script predicts the landing distance and places the active block flush on the support.
- If the landing rotation is already very close to the target 90-degree angle, tiny spin is cleared at handoff so a clean placement does not slide sideways from leftover rotation.
- The active block cannot expand the camera and cannot be moved outside the current playable horizontal envelope.

The playable horizontal envelope is based on:
- Configured floor segments.
- Already landed block bounds.
- `horizontalPlacementBufferColumns`.
- The current camera width.

Current default:
- `horizontalPlacementBufferColumns = 3`

## Grid And Floor

The grid spacing is configured by `GameModeConfig.gridSpacing`.

The current default is:
- `gridSpacing = 1`
- One centered floor segment
- `centerColumn = 0`
- `columnCount = 9`

That means the floor visually spans columns `-4` through `4`, with visual edges at `-4.5` and `4.5`.

Floor data is used by:
- `PlayAreaController`, which sizes and positions the visible floor.
- `BlockController`, which uses the configured floor segments to limit the active block's horizontal placement bounds.

The floor uses a real 2D collider and a generated friction material. Physics support comes from the actual collider, not from a separate logical grid.

## Landing And Physics

The old custom `TowerGrid` stability system has been removed.

The current landing model is Unity 2D physics:
- `BlockController` steers the falling piece until support is detected beneath it.
- Support is detected using a downward `Rigidbody2D.Cast`.
- The contact must have an upward normal of at least `minimumLandingNormalY`.
- On first support, gravity is restored and the block's center of mass returns to its real value.
- Downward landing speed is capped by `maxLandingImpactSpeed`; the default is `0`, so falling speed itself does not shove the tower.
- Horizontal controlled velocity is cleared at landing so a late left/right input does not push the tower sideways.
- Angular velocity can still carry into physics, so a piece caught mid-rotation can settle naturally.

As soon as the block lands:
- The script disables player control.
- The `Rigidbody2D` remains dynamic.
- Rotation is not frozen.
- Gravity remains active.
- Score, height, and the next-block spawn event fire immediately.

Once a landed block settles:
- If the block is already very close to a clean column and right-angle rotation, tiny X/rotation drift is corrected before sleeping.
- Blocks that are visibly tilted or too far off-grid are not corrected; those remain messy physics failures.
- Tiny residual velocity and spin are cleared.
- The body is put to sleep so clean placements do not drift by millimeters after they have genuinely settled.
- Locked block scripts keep running lightweight maintenance. If a later contact wakes a clean block and it settles again near the grid, the same tiny correction/sleep pass runs again.
- The block is not made kinematic or frozen; future contacts can wake it and make it tilt, slide, or fall.
- Unity physics continues to decide whether the block stays, tilts, slides, or falls.

## Physics Tuning

Physics feel is currently controlled by data and collider setup:
- `BlockData.mass`
- `BlockData.physicsMaterial`
- `BlockData.gravityScaleMultiplier`
- `BlockController.defaultBlockFriction`
- `BlockController.defaultBlockBounciness`
- `BlockController.restingLinearDamping`
- `BlockController.restingAngularDamping`
- `BlockController.horizontalColliderInset`
- `PlayAreaController.floorFriction`
- `PlayAreaController.floorColliderEdgeInset`
- `ProjectSettings/Physics2DSettings.asset`

Important control handoff settings:
- `maxColumnMoveSpeed`
- `columnApproachSpeed`
- `horizontalSteeringContactSkin`
- `rotationApproachSpeed`
- `maxRotationSpeed`
- `groundedCheckDistance`
- `landingColumnToleranceFraction`
- `landingCornerInsetFraction`
- `cornerSlideSpeed`
- `invalidContactReleaseTime`
- `maxLandingImpactSpeed`
- `settleLinearThreshold`
- `settleAngularThreshold`
- `settleTime`
- `sleepSettledBlocksOnLock`
- `microAlignSettledBlocks`
- `microAlignMaxColumnFraction`
- `microAlignMaxRotationDegrees`
- `maxControlTime`

The game no longer performs mathematical center-of-mass stability checks. If a tower is too heavy, too far overhanging, too slippery, or badly supported, that should emerge from the 2D physics simulation itself.

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

Current variant assets:
- Normal
- Heavy

Variants are applied by `BlockController.ApplyData`.

Future examples that fit this model:
- Icy: low-friction physics material.
- Heavy: high mass.
- Light: low mass or gravity multiplier.
- Bouncy: bouncy physics material.
- Themed: sprite/material override.

Future sturdy/anchor blocks should be designed as a new explicit gameplay rule or component, not by reintroducing hidden grid stability logic.

## Spawning

`Spawner` prepares one next block and then spawns it when the current block releases player control on landing.

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
- `initialGravityScale = 0.85`
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

The camera:
- Follows max tower height upward.
- Never moves back below the highest camera Y reached.
- Moves the spawn point with the camera.
- Zooms horizontally based only on already landed blocks in the current vertical focus window.
- Ignores the currently falling block for zoom decisions.

## Game Over And Lives

`LossZone` ends the round when a block's collider enters the trigger below the floor.

Current behavior:
- If lives are available, `GameManager.GameOver()` consumes one life and continues.
- If no lives remain, the game over UI appears.
- The old top-of-screen game-over rule is not part of the current game.

Current default:
- `startingLives = 0`

## Static Support Islands

`StaticSupportIslandManager` can spawn floating static support islands as the tower gets higher.

Current data-driven settings:
- `staticSupportIslandsEnabled`
- `staticSupportIslandHeightInterval`
- `staticSupportIslandSpawnChance`
- `staticSupportIslandFirstHeight`
- `staticSupportIslandSpawnAheadHeight`
- `staticSupportIslandMinColumn`
- `staticSupportIslandMaxColumn`
- `staticSupportIslandCenterClearColumns`
- `staticSupportIslandShapes`

Support islands:
- Spawn only after the configured height thresholds.
- Use weighted shape configs.
- Stay out of the configured center clear lane so a block falling straight down does not hit them.
- Use real static colliders from the spawned prefab, so blocks can physically land on them.
