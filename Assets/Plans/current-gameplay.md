# Current Gameplay

This document describes the prototype as it works now. It is a practical reference for the current game loop, not a future roadmap.

## Core Loop

1. A tetromino shape is selected from the configured block bag.
2. The block spawns at the spawn point near the top of the camera view.
3. While the block is active, the player can move, rotate, and fast-drop it.
4. The active block is steered on a column grid as a Kinematic `Rigidbody2D`.
5. On first support, the block makes one placement decision: an exact, supported cell pose becomes grid-stable; every other pose is handed to Unity 2D physics.
6. Score and max height update as soon as player control is released.
7. The next block spawns after the configured spawn delay, even if the previous block/tower is still wobbling.
8. Grid-stable blocks remain exact and motionless. Rejected or structurally released Dynamic blocks may settle and sleep wherever physics leaves them.
9. Game over occurs only when a block falls into the loss zone below the floor.

## Movement And Input

Active blocks use `StackingInputs`.

Current controls handled by `BlockController`:
- Horizontal movement changes the target column by one grid column.
- Holding left/right uses delayed auto shift through `dasDelay` and `dasRate`.
- Rotation changes the target Z angle in 90-degree increments.
- Fast drop multiplies fall speed by `fastDropMultiplier`.

While the block is still falling:
- The body is a Kinematic `Rigidbody2D`.
- Gravity is temporarily disabled.
- Vertical fall and horizontal column movement are applied by explicit cast-and-position steps.
- Linear velocity is kept at zero during controlled falling, so the active block is not a physical projectile.
- Horizontal steering is side-contact limited: if the falling piece would hit a landed block from the side this frame, sideways velocity is capped or stopped instead of pushing the tower.
- Rotation is snapped to the target 90-degree angle while the piece is still controlled.
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

The current model deliberately separates exact placement from physical failure:
- `BlockController` steers the Kinematic falling piece until a downward cast finds valid support.
- While it is still Kinematic, the piece is seated once onto its exact column, row, and quarter-turn.
- The exact pose is accepted only when it has no meaningful overlap and has a row-aligned terrain, frozen-block, or grid-stable-block support beneath a bottom cell.
- An accepted piece remains grid-stable: Kinematic, zero gravity/velocity, and `FreezeAll`. Later landings cannot create small solver tilts or gaps.
- A rejected piece restores its honest contact pose and becomes an unconstrained Dynamic body with its normal physics material. It is never snapped or pulled toward the grid later.
- After a good placement, load propagates down the connected support graph. Every block's combined self/load-above resultant must remain within its direct contacts with a 0.15-cell edge reserve; a distant wide foundation cannot rescue a local cantilever.
- A genuine ledge hook remains exact when one supported top cell connects outward to a same-row cell and then down beside the support. S/Z and J/L hooks therefore stay attached without leaning; flat overhangs with no cell below the ledge still fail the normal load test. A hook preserves its load's original horizontal line of action and is limited to 0.40 cell beyond its real contact, so accumulated weight can overwhelm it instead of creating an infinite anchor.
- A failed interface releases that block with its normal physics material, then revalidates the remaining graph in the same frame. Unsupported pieces above follow while independently supported pieces can remain exact.
- Removing support, tremors, and failed nudges also release affected stable structures before applying physical motion.
- Dynamic wreckage alone uses the settle/sleep watchdog; it sleeps without changing pose.
- Score, height, and the next-block spawn event still fire immediately at lock.

## Physics Tuning

Physics feel is currently controlled by data and collider setup:
- `BlockData.mass`
- `BlockData.physicsMaterial`
- `BlockData.gravityScaleMultiplier`
- `BlockController.defaultBlockFriction`
- `BlockController.defaultBlockBounciness`
- `BlockController.restingLinearDamping`
- `BlockController.restingAngularDamping`
- `BlockController.colliderFootprintScale`
- `PlayAreaController.floorFriction`
- `PlayAreaController.floorColliderEdgeInset`
- `ProjectSettings/Physics2DSettings.asset`

Important control handoff settings:
- `groundedCheckDistance`
- `landingSupportNormalY`
- `landingMinSupportWidthFraction`
- `maxLandingImpactSpeed`
- `GridSeatMaxCorrectionFraction`
- `GridPenetrationToleranceFraction`
- `GridStructuralEdgeReserveFraction`
- `GridHookMaxOverhangFraction`
- `GridBalanceToleranceFraction`
- Grid hook topology rule
- `settleLinearThreshold`
- `settleAngularThreshold`
- `settleTime`
- `sleepSettledBlocksOnLock`
- `maxControlTime`

Grid-stable structures propagate mass and torque through each direct support interface after placement or support removal. Once an interface fails, Unity 2D physics owns every released body pose permanently.

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
- Applies current base fall speed from `GameManager` (delegated to `DifficultyController`) plus ability fall-speed factors.
- Emits the next-block label through `GameEvents`.

Current spawn delay:
- Controlled by `GameModeConfig.spawnDelay`
- Default: `0.5`

## Speed And Difficulty

`DifficultyController` owns the current base fall speed and ramp rules; `GameManager` exposes the current value to spawns. Landed gravity stays normalized by the block physics contract.

Current default config:
- `initialFallSpeed = 2`
- `difficultyScalingMode = PerBlock`
- `difficultyAdjustmentMode = Additive`
- `speedIncreasePerBlock = 0.1`

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
