# MadTowers Physics — Grid-Stable Placement With Physical Failure

This document records the arcade-physics architecture introduced on `floor-test` in September
2026. Correctly seated pieces are exact grid structures; bad placements and failed structures
are ordinary physics bodies. The ownership boundary is explicit and one-way, preventing delayed
snap/solver fights while preserving real falls and tower collapses.

Sister file locations:
- [BlockController/](Assets/SourceFiles/Scripts/Blocks/BlockController/) — descent, landing, grid stability, Dynamic-debris settling, and sleep. One class split into focused partials: core (fields/lifecycle), Input, Setup, Steering, Placement, Landing, GridStability, Settling, PlacementBeam — all the same `BlockController`, so everything in this document applies across them.
- [StaticSupportIslandManager.cs](Assets/SourceFiles/Scripts/World/StaticSupportIslandManager.cs) — sky platforms
- [PlayAreaController.cs](Assets/SourceFiles/Scripts/Levels/PlayAreaController.cs) — floor
- [GameModeConfig.cs](Assets/SourceFiles/Scripts/Levels/GameModeConfig.cs) + `Assets/Data/GameModes/` + `Assets/Resources/GameModes/` — per-level tuning
- `ProjectSettings/Physics2DSettings.asset` — solver settings

---

## 1. The Five Invariants

### I1 — Landing makes one ownership decision
The active piece remains kinematic through contact cleanup. `TryEnterGridStablePlacement()` may
snap X, Y and quarter-turn rotation exactly once while the incoming piece is still kinematic.
It succeeds only when the resulting cell pose has no real overlap and at least one exact support
cell beneath it. Dynamic debris and `LandableSlope` surfaces are never grid supports.

If the test fails, the contact pose is restored and the piece becomes an unconstrained Dynamic
body with its normal authored/fallback material immediately. There is no later retry.

### I2 — Grid-stable means exact and motionless
A grid-stable block is Kinematic, has zero velocity/gravity, and uses `FreezeAll`. The lattice is
the authoritative gameplay state, so later landings cannot nudge, tilt, compress or separate it.
Do not add springs, settle timers, velocity pulls, temporary joints or repeated pose correction to
this state.

### I3 — Every support interface must carry its real load
Vertically adjacent grid-stable pieces form a support graph. Weight and torque propagate from its
top downward. At every block, the resultant of its own mass plus loads received from above must
project inside the exact contacts immediately beneath that block, with a 0.15-cell structural edge
reserve. Kinematic ownership removes the tiny impact/compliance that would topple a mathematically
possible but visibly precarious tower, so the reserve deliberately makes cumulative structures fail
before their resultant reaches the literal contact edge. A broad foundation at ground level cannot
legalize a one-cell cantilever higher in the tower.

`GridBalanceToleranceFraction` is only a 0.005-cell floating-point tolerance around those policy
boundaries. A 2x2 supported beneath only one of its two bottom cells has its COM outside the real
narrowed contact and must release. When an interface fails, that block becomes Dynamic and the
remaining graph is revalidated in the same frame. Unsupported pieces above follow, while an
independently supported base may remain exact.

A genuine ledge hook is a separate exact support case: one cell rests on top, a connected cell
extends past that edge on the same row, and another cell continues down beside the support. That
geometry can react against the vertical ledge, so S/Z and J/L hooks remain grid-owned rather than
being released into a shallow physical lean. A flat 1x4, 2x2 or L overhang with nothing wrapping
below the ledge receives no exemption and must still release. Hook load propagates into a supporting
tower block while preserving the load's original horizontal line of action, so the lower structure
still receives the full added moment and can fail. A hook is also bounded to 0.40 cell beyond its
real top contact. That is enough for the intended standalone L/S/Z hook poses, but it is not an
infinitely strong anchor for accumulated tower load. **Never clamp a hooked resultant to the ledge
edge or remove the hook bound.** Either change turns hooks into torque sinks that can hold an
arbitrarily lopsided, gravity-defying tower.

Support destruction uses the same revalidation process. No structural-release impulse or special
low-friction material exists.
Tremors and failed nudges intentionally release the affected connected structure before applying
their normal velocity/impulse. A released block never becomes grid-stable again.

### I4 — Physics footprint is NARROWER than the visual cell (0.94 world-width, 1.0 world-height).
A piece must be able to slide into a gap exactly its own width. With a collider width of
exactly 1.0 cell that is mathematically impossible (any sub-pixel neighbour drift pinches
the slot → the piece wedges on the corners → depenetration shoves the walls apart). The 6%
total horizontal clearance also means side-by-side blocks don't touch at rest, so the
contact graph splits into independent columns: a landing wakes one column, not the whole
tower. The sprite stays full size; only collision width shrinks.

Collider height stays 1.0 cell so perfect stacked supports remain grid-true vertically.
Shrinking height as well made each separate block-on-block support settle slightly low,
which accumulated into tiny height mismatches and tilted later flat pieces placed across
multiple supports. Because block roots rotate in 90° steps, the shrink axis must follow
the snapped rotation: local X is narrowed at 0°/180°, local Y at 90°/270°, so the collider
is always narrow in world-X and full-height in world-Y.

### I5 — Dynamic means physics owns the pose forever
Once released or rejected, code never writes the block's position/rotation and never attempts to
re-register it to the grid. Dynamic bodies may tilt, slide, tumble and later sleep at any pose.
`SleepSettledBody()` only zeros velocities and calls `Sleep()`. The stillness/knife-edge logic is
retained solely to stop dynamic wreckage from twitching indefinitely; it is not placement logic.

---

## 2. Load-Bearing Settings — BlockController script defaults

The block prefabs do **not** override these fields (they were added after the prefabs were
saved), so the script defaults are authoritative. If you ever edit a value in a prefab
inspector, that prefab silently stops following the script default — check with the
Inspector in debug mode if behaviour diverges between pieces.

| Setting | Value | Why this value / what breaks if changed |
|---|---|---|
| `colliderFootprintScale` | **0.94 world-width only** | Invariant I4. Lower (→0.90) = more forgiving + bigger visible side seams; 1.0 = game-breaking horizontal wedging. Height stays 1.0 to preserve support height. The local axis is swapped at 90°/270° rotations. Must equal the island width scale. |
| `colliderCornerRadiusFraction` | 0.06 | Rounded corners turn "catch and tip" into "shave past and slide in". Box is shrunk by 2r so radius adds **no** size (edgeRadius expands outward!). |
| `defaultBlockFriction` | 0.95 | Normal fallback material for every freely Dynamic block. Grid-owned bodies do not rely on friction, but released/rejected pieces retain it so surfaces never feel like ice. |
| `defaultBlockBounciness` | 0 | Any bounce amplifies stack ringing. |
| `restingLinearDamping` | 0.5 | Damping for rejected/released Dynamic pieces only. Grid-stable blocks never wobble. |
| `restingAngularDamping` | 3 | Same. |
| `maxLandingImpactSpeed` | 2 | Velocity cap for the Dynamic fallback. Grid-stable placements keep zero velocity. |
| `GridSeatMaxCorrectionFraction` (const) | 0.12 | Maximum one-time Y correction accepted while the incoming piece is still kinematic. It absorbs contact slop, not a wrong ledge. |
| `GridPenetrationToleranceFraction` (const) | 0.03 | Unity's two default contact skins measure as -0.02 penetration at an exact touching pose; 0.03 accepts that without accepting visible overlap. |
| `GridStructuralEdgeReserveFraction` (const) | 0.15 | Required support kept inside the outer contact edge. Replaces the small impacts/compliance removed by grid ownership and makes precarious cumulative towers topple before the mathematical knife edge. |
| `GridHookMaxOverhangFraction` (const) | 0.40 | Maximum resultant distance beyond a genuine hook's real top contact. Preserves intended standalone L/S/Z hooks without creating an unlimited anchor. |
| `GridBalanceToleranceFraction` (const) | 0.005 | Numerical tolerance only. It must remain below the physical gap from a cell edge to the narrowed contact edge, or one-cell 2x2/1x4/L overhangs become falsely stable. |
| Grid hook topology (code rule) | supported top cell + outside same-row cell + outside lower cell | This exact form-lock remains grid-owned even when its COM lies beyond the top contact. Merely overhanging pieces do not qualify. |
| `settleLinearThreshold` / `settleAngularThreshold` | 0.08 / 8 | Quiet thresholds for Dynamic wreckage only. |
| `settleTime` | 0.35 | Sustained-quiet window before a Dynamic body sleeps. |
| `stillnessPositionTolerance` / `stillnessRotationToleranceDegrees` | 0.005 / 0.5 | Net-motion watchdog for Dynamic wreckage only. |
| `stillnessTime` | 0.75 | Dynamic-body watchdog window. |
| `KnifeEdgeGraceSeconds` (const) | 2 | Lets a quiet Dynamic body finish tipping before the watchdog may sleep it. |
| `SupportSpanEpsilon` (const) | 0.01 | COM-outside-contact margin for that Dynamic knife-edge test. |
| `sleepSettledBlocksOnLock` | true | Prevents rejected/released Dynamic debris from twitching forever. |
| `landingSupportNormalY` | 0.7 | A cast hit only counts as landing if the surface is actually upward-facing — rejects corner/side grazes (diagonal normals). |
| `landingMinSupportWidthFraction` | 0.15 | A landing also needs ≥15% of a cell of horizontal overlap. Stops 0.5 mm corner grazes from being treated as a floor (the original "block lands on nothing and tips" bug). Too high and valid narrow placements get rejected. |
| Lateral placement assist | **removed** | The magnetic placement assist caused historical chaos and was deleted. If ever rebuilt, it is polish on top of verified geometry, never a bug fix. |
| `groundedCheckDistance` | 0.03 | Small so last-second tucks stay possible. |
| `maxControlTime` | 12 | Safety lock for pieces that never find a landing. The timer does **not** accrue while `_descentSuspended` (Fission hover, tutorial lessons) — a deliberately suspended piece isn't stuck, it's waiting on the player; force-locking it mid-air dropped the piece out from under the lesson (July 2026). Suspension has its own watchdog instead: `SuspendedResumeSeconds` (90) of continuous hover auto-RESUMES the descent (the same thing the commit gesture does, never a mid-air force-lock) — without it a never-resumed hover stranded `ActiveControlled` and the whole spawn loop forever. |
| Collision detection | Continuous while falling, **Discrete after landing** | Applies to both grid-stable and Dynamic outcomes; descent is already cast-driven. |

Code-level details that are part of the contract (not inspector values):
- The High Friction ability may raise the runtime shared fallback material used by
  standard blocks. It must not mutate `BlockData` or authored `PhysicsMaterial2D`
  assets; explicit-material variants such as Ice keep their authored surface.
- **The reach guarantee:** a falling piece can always reach ≥ `BlockController.WidestBlockColumns`
  (4 = the widest piece, the horizontal 1×4) of clear grid past the outermost obstacle —
  tower block **or** sky island — on each side, so any piece can drop down a structure's
  outer side and fall off. Horizontal movement is gated **only** by the gameplay reach bounds,
  never by the camera: `TryGetGameplayHorizontalBounds` folds in island extent
  (`AddStaticIslandHorizontalBounds`, fed by `StaticSupportIslandManager.TryGetWorldHorizontalExtent`)
  and expands by `max(designerBuffer, WidestBlockColumns)`; both `IsColumnTargetWithinBounds`
  (grid step) and `ClampHorizontalToReachBounds` (continuous steering) clamp to those bounds.
  `WidestBlockColumns` is a **code constant** tied to block geometry, not a designer dial — a
  correctness floor, separate from the aesthetic `horizontalPlacementBufferColumns`. The bounds are
  **cached per piece** against a static `_reachGeometryVersion` (`InvalidateReachGeometry`), bumped
  only when the placed geometry changes — a block lands (`LockBlock`), a landed block leaves tracking
  (`DetachFromTracking`/`OnDestroy`), or an island spawns (`SpawnCluster`) — so the hot-path clamp and
  legality checks don't rescan every block + island each tick. Grid-stable geometry never drifts;
  Dynamic debris invalidates placement occupancy when it moves. The
  floor-edge math is shared with the camera via `HorizontalBounds` so the two can't drift apart.
- **The camera is a follow camera, decoupled from movement** (`TowerCameraController`): it does
  NOT clamp the piece and does NOT statically reserve the reach margin (an earlier version did,
  via `GetTargetCameraSize` reach-padding — that read as permanently zoomed-out with dead space
  on the pushed side). Instead `GetTargetFraming` frames the horizontal span of the content —
  floor (always), nearby landed tower (vertical window), nearby islands
  (`StaticSupportIslandManager.TryGetWorldHorizontalExtentInRange` — a windowed query that advances a
  monotonic low-water index, so it scans only the in-view island cells, not the whole climb's
  history), and the active piece (`BlockController.ActiveControlled`, no window) — with a fixed
  `HorizontalCameraPadding` margin,
  then **pans (X) and zooms** to fit. Normal play keeps the active piece over the tower → span =
  tower → camera sits still and tight; pushing a piece out past the tower grows the span on that
  side → the camera glides to follow, so reaching the drop lane stays possible without permanent
  zoom-out. When the content is wider than `MaximumCameraSize` can show, the centre is biased to keep
  the **active piece** fully framed (the far tower side crops, never the piece being steered), so a
  reachable column is never off-screen. Framing is snapped on the first frame that has content (held
  at Awake values until then) so the first piece causes no zoom pop. No movement is
  camera-bounded anymore (the Edge Portal ability, which wrapped targets across the
  visible screen edges, was removed from the game entirely — Nick 2026-08-01).
- The Fission ability (and the first-run tutorial's lesson hover — TUTORIAL.md) may **suspend
  the controlled descent** of the active piece
  (`BlockController.SetDescentSuspended`): while suspended, `SteerWhileFalling` still runs
  the horizontal grid step and rotation but skips the Y advance and the landing cast, so the
  shard hovers and is steerable but does not fall. Any descent intent (flick / held fast-drop /
  down) auto-clears it, so the normal commit gesture starts the drop. The body stays Kinematic
  and never-landed throughout — this only **defers** the one-time landing decision (I1).
  Active-piece-only.
- `Physics2D.SyncTransforms()` is called before every landing cast (`SteerWhileFalling`,
  `SettleOntoContact`) because **AutoSyncTransforms is off** project-wide. Without it,
  casts see last step's collider poses → landings measured at the wrong X.
- `ResolveIncomingOverlaps()` moves **only the incoming kinematic piece**, never a resting
  neighbour, before handoff.
- The landing restore value `_originalCenterOfMass` is **computed from the cell layout**
  (`ComputeUniformCellCenterOfMassLocal`), never read back from the body during steering:
  the body is Kinematic then, and **kinematic bodies report centre of mass (0,0)**. Reading
  it pinned every landed piece's weight to its grip cell (the body origin) — edge overhangs
  balanced on one side of the floor and toppled hard on the other (June 2026 bug).
- Landed `gravityScale` is normalized to a constant 1.0 (`ResolveLandedGravityScale`).
  The escalating-gravity difficulty path was deleted; do not reintroduce it — tower load
  must not grow with block count or collapse becomes a function of time, not skill.
- Sideways steps are blocked by static obstacles (islands) via an overlap probe
  (`IsCellBlockedByStaticObstacle`), but with the same half-cell row forgiveness the
  landed-block grid check grants: a destination cell is blocked only if BOTH its
  continuous (descent) Y and its grid-snapped row are obstructed
  (`ClassifyGridPlacementAtColumn`; drag/DAS steps take an early-out fast path — only
  the nudge pays for collecting blockers). The off-row seating this permits is resolved
  by `TuckIntoStaticPocket()` right after the sidestep: the still-kinematic piece slides
  **vertically only** until clear of static geometry (grid keeps owning X; pre-landing
  Y is descent-authored, so this is still pre-landing control), bounded to ~half a cell. Per-contact
  pushes combine as **extremes, never sums** (two cells on the same island row need the
  push once; opposing pushes mean "doesn't fit", not zero), and a tucked step must end
  fully clear of rock AND bricks or the whole step is reverted — the tuck may never hand
  the solver a brick interpenetration it created itself. History: with only the continuous probe,
  a one-cell pocket between island cells demanded ~0.13-cell vertical alignment and was
  effectively unenterable, while an identical pocket between tower blocks allowed half
  a cell ("islands act differently from blocks", June 2026). Block-vs-block stays
  grid-based; bricks overlapped by a step are still mediated by the solver.
- A failed **nudge** (the corner-zone dash refused by bricks or islands) shoves the
  blocking landed bricks with a horizontal **velocity impulse** (`SlamBlockingBricks`) and
  arms a 0.5 s nudge lockout (`NudgeFailLockoutSeconds`, static across pieces). A grid-stable
  component is released first (I3), then physics owns the hit (I5). Anchored/frozen bricks
  and islands never move.
  Drag steps stay silent on refusal — only the nudge is high-stakes.
- Cast/overlap buffers are reused instance arrays — no per-FixedUpdate allocations
  (GC spikes read as physics stutter).
- Anchored/frozen blocks (`FreezeInPlace()`, used by the anchor-brick variant and the
  Freeze power-up) become Static bodies; landed maintenance skips any non-Dynamic
  body. Static blocks are allowed to violate grid registration — they freeze as-is by design.
- **Sloped colliders and the landing gate (the Pyramid brick).** A descending kinematic
  piece only stops on a surface `IsValidLandingSupport` accepts
  (`hit.normal.y ≥ landingSupportNormalY` 0.7 ≈ slopes ≤ ~37°); a rejected surface is
  simply descended *through* — there is no other stop. Steeper surfaces must opt in via
  the **`LandableSlope`** marker component on the collider (static registry, no hot-path
  `GetComponent`): marked surfaces accept landings down to a 0.3 normal.y sanity floor,
  and the support-width check still rejects sub-cell corner clips. The Pyramid uses this
  for its ~42/44° faces (normals ≈0.71/0.75 — too close to the 0.7 gate to trust
  unmarked): a piece touching them locks, goes Dynamic, and gravity/friction slide it
  off — that IS the behaviour (Nick approved the exception 2026-07-11; the gate is
  untouched for every unmarked surface). Its apex is a
  single point **offset 0.07 u off-centre** (invisible: the sprite's apex stays centred).
  Both symmetric apexes failed (July 2026 tests): a 0.12 u apex flat let perfectly
  column-aligned pieces see-saw balance into an ever-wobbling tip-to-tip tower that
  never slept; a centred point was worse — zero torque at perfect alignment, so pieces
  balanced, went quiet, and the Dynamic-debris watchdog slept a 17-pyramid tip-to-tip tower solid.
  Perfect alignment is the DEFAULT alignment (grid columns), so it must not be an
  equilibrium: the offset guarantees contact at +0.07 with COM at 0 → always torque →
  the knife-edge defer tips it, every time, to the same side. Non-cell shape anatomy
  (the pattern): keep REAL 1×1 box cells wherever the shape has full cells — they are
  the grid anchor (`GetPrimaryWorldX` snaps the first collider, which must sit ON a
  column), the placement-beam footprint, the COM sample points, and the column
  alignment (the Pyramid's v2 without them sat half a column off every other brick and
  its beam collapsed to one column). The polygon part lives on a named child ("Body")
  positioned at its centroid, authored pre-inset (forgiveness only resizes
  `BoxCollider2D`s), listed AFTER the cells.

## 3. Floor & Sky Platforms (must match the blocks)

| Where | Setting | Value | Why |
|---|---|---|---|
| PlayAreaController | `floorFriction` | 0.95 | Friction mixing — see above. Applied by `FloorTerrain` to every column-run collider. |
| PlayAreaController | `floorColliderEdgeInset` | 0.03 | Collision edge sits just inside the visual floor so pieces don't snag its corners. Applied per run, both sides. |
| FloorTerrain | terrain build | (code) | The floor is built by `FloorTerrain` from `FloorSegmentConfig`s (July 2026): one STATIC BoxCollider2D per column (grid − 2·inset wide, 24 u deep so nothing passes underneath), split vertically around carved 1×1 **pockets**. Column heights are always ≥ 0 above the **datum** (the legacy Base Platform's top = `floorOriginY`), so the datum stays the lowest landable surface and every single-floor-Y consumer (tower height, islands, backdrop anchors, the `floorOriginY − 3` steering bailout) keeps its meaning. Landing/casts/reach/camera are collider-generic and need no registration. All terrain visuals are collider-free children. |
| FloorTerrain | pocket leniency | rounded + 0.05 | Boxes around a pocket get the ISLAND collider prescription (shrink by 2r, `edgeRadius` r = 0.06·grid — "shave past and slide in"), and the pocket **ceiling is raised 0.05** so the full-height piece cell has real clearance while the pocket floor stays grid-exact to rest on. Without these, the snapped-row forgiveness allowed the step but `TuckIntoStaticPocket`'s final overlap check reverted it (sharp corners + zero clearance = always still overlapped) — pockets classified as enterable yet were physically unenterable, the same bug the island pockets had in June 2026. Plain floor spans keep sharp full boxes: coplanar tops, no rounding dips. Verified: entry succeeds across the full ±0.4-cell misalignment window. |
| StaticSupportIslandManager | `_islandFriction` | 0.95 | Islands are the tower's anchors; the prefab itself has **no** material, the manager applies it at spawn. |
| StaticSupportIslandManager | `_islandFootprintScale` | 0.94 width only | **Must equal** the blocks' width scale or pieces wedge beside/between islands. Height stays 1.0 to preserve support height. |
| StaticSupportIslandManager | `_islandCornerRadiusFraction` | 0.06 | Match blocks. |
| StaticSupportIslandManager | spawn-clearance check | (code) | A platform never materializes intersecting the falling piece / tower / another island — an overlapped piece can't land on it and ghosts through (the original "fall through platforms" bug). Platforms must also spawn **below the spawn line** to be usable (see camera settings). |
| StaticSupportIslandManager | reachable-column guardrail | (code) | A platform never spawns where it could **never** be reached, even at full zoom-out: each side band is clipped (`TryGetReachableColumnRange`) to leave ≥ `BlockController.WidestBlockColumns` (4 = the horizontal 1×4) of clear grid between an island's outer edge and the viewport edge at `MaximumCameraSize`, anchored to the **floor centre** (stable) rather than the live, panning camera X. The follow camera pans/zooms to keep that margin past the farthest obstacle; this clip is the backstop for islands past what even max zoom could ever show. With the default ±6 band on a normal phone aspect it clips nothing — it bites only on very narrow screens or an over-wide band. |

The StaticBlock prefab is intentionally bare (Static body, plain 1×1 collider) — all
physics dressing happens in `ConfigureIslandCellPhysics`, idempotently (pooled cells are
configured once, detected via `edgeRadius > 0`).

## 4. Project Physics2D Settings

`ProjectSettings/Physics2DSettings.asset`:

| Setting | Value | Note |
|---|---|---|
| Velocity / Position iterations | 16 / 8 | Already generous. **Do not crank higher to mask jitter** — jitter means something is injecting impulses (see Invariants); find the source. |
| Gravity | −9.81 | Plain. |
| `m_AutoSyncTransforms` | 0 | Why the manual `SyncTransforms()` calls exist. If you ever flip this to 1, the manual calls become redundant but harmless. |
| `m_DefaultContactOffset` | 0.01 | Far smaller than the 0.06 inter-block clearance, so neighbours don't generate phantom contacts. |
| Sleep tolerances | 0.5 s / 0.01 / 2 | Native sleep can be effectively unreachable for Dynamic wreckage, so that state has a bounded stillness watchdog (I5). Grid-stable structures do not rely on solver sleep. |

Block data: Normal mass 1, Boulder mass 4, Feather mass 0.25. Normal↔Boulder (4:1) is
comfortably within Box2D's tolerance at these iteration counts (mushiness starts ~10:1).
Feather's 0.25 keeps the worst case — a Boulder resting directly on a Feather — at ~16:1,
just past that threshold rather than well beyond it (it was 0.1 → 40:1 before being raised
for exactly this reason); if that *specific* pairing ever reads mushy or penetrating, the
mass ratio is the cause, not the solver. Don't "fix" general stability by changing masses.

## 5. Per-Level Tuning (GameModeConfig assets)

These are the *designer* dials — safe to vary per level. Current defaults:

| Setting | Default / Classic / Sky | Narrow | Purpose |
|---|---|---|---|
| `initialFallSpeed` | 2 | 2.2 | Base descent speed. |
| `speedIncreasePerBlock` | 0.025 | 0.03 | Slow ramp (~80 blocks to double). 0.1 made long games impossible. |
| `maxFallSpeed` | 5 | 5.5 | Hard ceiling — endless games stay physically playable. |
| `maxLandingImpactSpeed` | 2 | 2 | See I-section; difficulty must never make landings harder. |
| `towerPeakScreenY` | 0.5 | 0.58 | **The leniency dial.** Lower = more room between tower peak and spawn = more reaction time. Range widened to 0.35–0.9; raise for hard levels. |
| `spawnPointScreenY` | 0.9 | 0.9 | Where pieces spawn on screen. |
| `staticSupportIslandSpawnAheadHeight` | 6 / 8 (Sky) | — | Lead above the **tower peak** at which islands materialize, with the pop reveal (June 2026: was camera-top-relative, which kept islands permanently littering sky nobody could build to yet). Keep **below** the spawn-line offset ((spawnY−peakY)·2·cameraSize ≈ 12 at min zoom) so revealed platforms appear under the falling piece and are immediately landable. |
| Sky platform frequency | interval 4, chance 0.9, first at 4 | — | Sky mode: wide shapes (Two/Three Wide) dominate the weights — platforms are "floor pieces", not pebbles. |
| `spawnDelay` | 0 | 0 | Correct. Don't gate spawning on settling — fix ringing at its source instead (that's what the geometry work was for). |
| `settle*`, `sleepSettledBlocksOnLock` | mirror §2 values | — | These are code-owned profile values shared by every mode and apply only to Dynamic wreckage. |

## 6. Symptom → Cause Cheat-Sheet (when someone reports a regression)

| Symptom | First thing to check |
|---|---|
| Correctly placed tower blocks tilt, separate or shimmer | A grid-stable block was made Dynamic, or code/animation is moving its Rigidbody/transform after the I1 landing decision. Check `IsGridStable`, body type, and every landed pose write. |
| A rejected/released block twitches forever in place | Something moves the body at sleep time, the Dynamic stillness watchdog was weakened/removed, or its knife-edge grace lost its bound (I5). |
| Half-on-edge Dynamic debris survives on one floor side, falls on the other | The Dynamic knife-edge sleep defer or its support test is broken; COM-on-edge outcomes otherwise degrade to float noise. |
| A visibly good placement becomes Dynamic | Inspect the one-time seat rejection, then the failing support interface: row correction, meaningful overlap, exact support cells, propagated load, and resultant contact span (I1/I3). |
| A badly overhanging structure remains rigid | The local support edge was omitted from the graph, the authored cell COM is wrong, `GridBalanceToleranceFraction` bridges the real contact gap, or hook propagation clamped away the original load moment (I3). |
| A genuine S/Z or J/L ledge hook tilts or slides away | Verify `HasGridHookAnchor` sees the supported top cell, outside same-row cell, and outside lower cell in the load-resultant direction (I3). |
| A flat overhang remains rigid as though it were hooked | The hook test accepted a piece without an own cell below the ledge, or the ordinary resultant/contact-span test was bypassed (I3). |
| A hooked multi-piece sculpture carries unlimited weight | Hook torque propagation lost its original line of action, or `GridHookMaxOverhangFraction` is no longer enforced. A hook is a bounded placement affordance, never an infinite anchor (I3). |
| Piece won't fit a gap it should fit; placements shove neighbours | Footprint width crept back toward 1.0, or islands/blocks width scales diverged (I4). Verify in Physics Debugger: collider outlines must sit visibly *inside* sprite sides while remaining full-height. |
| Blocks land on invisible corners and tip | Landing filter weakened (`landingSupportNormalY`, `landingMinSupportWidthFraction`). |
| Dynamic blocks feel like they are on ice | Their authored/normal material or the surface lost its expected friction. Rejected and released bodies must retain normal material; there is no collapse material. |
| Tower collapses by itself late game | Escalating load came back (gravity scaling per block) or landing impact got coupled to fall speed again. |
| Falls through sky platforms | Platform spawned overlapping the piece (clearance check), or spawn-ahead vs spawn-line relation broke (§5). |
| Landings detected at wrong column edge | A `SyncTransforms()` call before a cast was removed (AutoSyncTransforms is off). |
| Can't nudge/steer into a pocket between islands, or a sidestep wedges the piece inside an island | The snapped-row forgiveness or `TuckIntoStaticPocket()` was removed/weakened (§2 code details). |
| Stutter under load | Per-frame allocations returned, or CCD re-enabled on landed bodies. |

Mandatory I3 regression layouts before a physics change is accepted: standalone Z hook stays exact;
standalone L hook stays exact; flat O on one narrowed cell releases; centered O-on-O stays exact;
the documented four-piece J/T/Z/L edge sculpture releases at the Z interface; the documented
two-piece J/S edge stack releases at the base; a cumulatively overloaded hook releases its support
and the unsupported branch follows in the same validation pass.

---

*Maintained by hand — update this file when any of the above changes, and record the
symptom you were fixing. The history matters: every forbidden thing in here was once tried.*
