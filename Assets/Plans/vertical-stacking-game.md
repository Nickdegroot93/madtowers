# Project Overview
- Game Title: Vertical Block Stacking (Tricky Towers inspired)
- High-Level Concept: A physics-based stacking game where players drop Tetrominoes onto a platform, trying to build as high as possible without the tower collapsing.
- Players: Single player
- Inspiration / Reference Games: Tricky Towers, Tetris
- Tone / Art Direction: Stylized, vibrant. Level-specific themes are deferred until the core stacking game is stable.
- Target Platform: Mobile (iOS/Android)
- Screen Orientation / Resolution: Vertical Portrait (9:16 / 10:19)
- Render Pipeline: Universal Render Pipeline (URP)

# Game Mechanics
## Core Gameplay Loop
1. **Spawn**: A random Tetromino spawns at the top of the screen.
2. **Control**: Player moves and rotates the block while it is in a "Kinematic" state (unaffected by gravity).
3. **Drop**: Upon manual drop or contact with the stack, the block switches to a "Dynamic" state, enabling gravity and full physics simulation.
4. **Stack**: Blocks settle based on physics (friction, mass, center of gravity).
5. **Score**: Height is tracked; loss condition occurs if a block falls past the bottom boundary.

## Controls and Input Methods
- **Touch/Drag**: Move block horizontally.
- **Buttons/Tap**: Rotate block (Left/Right).
- **Double Tap/Swipe Down**: Fast drop trigger.
- **Input System**: Using Unity Input System Package with a dedicated Action Map for "StackingControls".

# UI
- **HUD**: Height meter, current block count, "Next Block" preview.
- **Game Over**: Summary of height achieved, "Try Again" button.
- **Mobile Buttons**: Semi-transparent overlays for rotation and drop if touch gestures are not preferred.

# Key Asset & Context
- **Scripts**: 
    - `BlockController.cs`: Handles Kinematic/Dynamic state switching and movement logic.
    - `Spawner.cs`: Manages random block instantiation and "Next Block" logic.
    - `GameManager.cs`: Tracks height, loss conditions, and score.
- **Prefabs**: 
    - `Tetromino_[Shape]`: Prefabs for each Tetris shape (I, J, L, O, S, T, Z) with Rigidbody2D and Colliders.
    - `Boundary_Sensor`: Trigger zone at the bottom of the screen.
- **Materials**: Utilizing existing URP simple color materials (`Material_Simple_Red`, etc.).

# Implementation Steps

## Phase 1: Core Prototype (MVP)
1. **Setup Scene**: 
    - **Description**: Configure `Gameplay` scene with an Orthographic camera (Size ~10 for Portrait). Create a static "Base Platform" and a "Loss Zone" (trigger) at the bottom.
    - **Assigned role**: developer
    - **Dependencies**: None
2. **Input Actions**:
    - **Description**: Create `StackingInputs.inputactions` with maps for Move (Vector2/Axis), Rotate (Button), and FastDrop (Button).
    - **Assigned role**: developer
    - **Dependencies**: None
3. **Block Prefabs**:
    - **Description**: Create the 7 standard Tetromino shapes using 2D sprites or 3D cubes (Z-locked). Each needs a `Rigidbody2D` (Kinematic initially) and `BoxCollider2D` or `CompositeCollider2D`.
    - **Assigned role**: developer
    - **Dependencies**: None
4. **Block Control Logic (`BlockController`)**:
    - **Description**: Implement script to handle horizontal movement and rotation via Input System. Switch to `RigidbodyType2D.Dynamic` upon manual drop or collision.
    - **Assigned role**: developer
    - **Dependencies**: Step 2, Step 3
5. **Spawning System (`Spawner`)**:
    - **Description**: Implement a spawner that instantiates a random block prefab at the top of the screen. Wait for the current block to become "Dynamic" before spawning the next.
    - **Assigned role**: developer
    - **Dependencies**: Step 4
6. **Loss Condition**:
    - **Description**: Implement `GameManager` to detect when any block enters the "Loss Zone" trigger. Trigger Game Over UI.
    - **Assigned role**: developer
    - **Dependencies**: Step 1

## Phase 2: Configuration & Progression
7. **Difficulty Scaling**:
    - **Description**: Add parameters to `GameManager` for `fallingSpeed` and `gravityScale` that increase as the stack grows higher.
    - **Assigned role**: developer
    - **Dependencies**: Step 5
8. **Level Presentation Config**:
    - **Description**: Later, define authored level presentation data for background image, music, block colors, and level-specific visual elements. Do not switch themes during a run based on score or height.
    - **Assigned role**: developer
    - **Dependencies**: Core gameplay stability
9. **Static Obstacles**:
    - **Description**: Create a system to spawn static "Anchor" blocks at fixed vertical intervals to help stabilize the tower.
    - **Assigned role**: developer
    - **Dependencies**: Step 5
10. **Block Variants**:
    - **Description**: Create ScriptableObjects for block types (Heavy, Bouncy, Fragile) and modify `BlockController` to apply these physical traits.
    - **Assigned role**: developer
    - **Dependencies**: Step 3, Step 4

## Phase 3: Power-ups & Polish
11. **Power-up Spawning**:
    - **Description**: Implement floating targets that spawn at various heights.
    - **Assigned role**: developer
    - **Dependencies**: Step 6
12. **Collection Logic**:
    - **Description**: Add logic to power-ups to detect when a "Dynamic" block overlaps them, triggering effects like "Slow Time" or "Extra Life".
    - **Assigned role**: developer
    - **Dependencies**: Step 11
13. **Mobile UI & HUD**:
    - **Description**: Implement touch-friendly buttons and a height tracker UI.
    - **Assigned role**: developer
    - **Dependencies**: Step 2, Step 6

# Verification & Testing
- **Physics Test**: Verify blocks stack naturally and don't "tunnel" through each other (check Collision Detection mode: Continuous).
- **Control Test**: Ensure rotation and movement feel responsive on mobile (simulated in Editor).
- **Loss Test**: Drop a block intentionally and verify the loss screen appears.
- **Scaling Test**: Verify falling speed increases after 10 blocks are placed.
