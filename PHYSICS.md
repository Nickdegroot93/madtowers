# MadTowers Physics — Why It Works & What Must Never Change

This document records the physics architecture and every load-bearing setting as of the
"best it's ever been" state (June 2026). The system went through many broken iterations
before this one. **Read the Invariants section before touching anything** — every one of
them was learned by shipping the bug it forbids.

Sister file locations:
- [BlockController/](Assets/SourceFiles/Scripts/Blocks/BlockController/) — descent, landing, settling, sleep. One class split into focused partials: core (fields/lifecycle), Input, Setup, Steering, Placement, Landing, Settling, PlacementBeam — all the same `BlockController`, so everything in this document applies across them.
- [StaticSupportIslandManager.cs](Assets/SourceFiles/Scripts/World/StaticSupportIslandManager.cs) — sky platforms
- [PlayAreaController.cs](Assets/SourceFiles/Scripts/Levels/PlayAreaController.cs) — floor
- [GameModeConfig.cs](Assets/SourceFiles/Scripts/Levels/GameModeConfig.cs) + `Assets/Data/GameModes/` + `Assets/Resources/GameModes/` — per-level tuning
- `ProjectSettings/Physics2DSettings.asset` — solver settings

---

## 1. The Five Invariants (NEVER violate these)

### I1 — Never write position/rotation on a landed block. Velocity only.
Once a block goes Dynamic, **no code may set its transform, `rb.position`, or `rb.rotation`
— ever.** Teleporting a resting body creates penetration with its neighbours; Box2D answers
with depenetration impulses; those wake and shove the stack. Every "vibrating tower" and
"infinite twitch" bug in this project's history was some form of this:
- a per-frame maintenance pull that teleported blocks 50×/s (towers shimmered like a living organism);
- a grid-snap at sleep time that teleported blocks whose physical equilibrium was off-grid
  (solver pushed them back, snap reapplied → metronomic infinite twitch).

Corrections on landed blocks are allowed **only as velocities** (the solver mediates them,
contacts stay honest, interpolation stays intact). See `PullQuietBlockTowardGrid()`.

### I2 — Going to sleep must never move the body.
`SleepSettledBody()` is exactly: zero velocities + `Sleep()`. No snap, no alignment.
A block that physics holds 2° tilted sleeps 2° tilted. Grid registration comes from honest
sources only: pieces *land* exactly on-grid (kinematic snap before handoff), and flat
blocks get a gentle velocity pull while awake. Re-adding "just a tiny bounded snap" at
sleep will re-create the infinite twitch — bounded in size is not bounded in repetition.

### I3 — The stillness watchdog: no net movement → asleep within 0.75 s.
Velocity-based settle detection can be defeated by marginal contact configurations (a block
pivoting on a corner gets a solver kick every cycle, so instantaneous velocity never stays
quiet). The watchdog (`UpdateStillnessWatchdog()`) measures **net displacement against an
anchor** instead: < 0.005 units and < 0.5° for 0.75 s → force sleep. Oscillation has zero
net movement by definition, so persistent twitching is structurally impossible. Genuinely
tipping/sliding blocks keep re-anchoring and stay live. Do not remove this thinking the
settle path covers it — the settle path alone provably does not.

**Bounded knife-edge refinement (June 2026):** a quiet block whose centre of mass lies
horizontally *outside* its supporting contact span is mid-tip; force-sleeping it froze a
coin on its rim. Which side of the floor that happened on was decided by sub-millimetre
float noise, so identical-looking half-on-edge placements survived on the left and fell on
the right (the original bug report). `ShouldDeferSleepForKnifeEdge()` defers **both** sleep
paths for such blocks so gravity resolves the balance honestly — but only for
`KnifeEdgeGraceSeconds` (2 s) of continuous quiet-but-unsupported state. Anything still
quiet after the grace (leaning, wedged, vine-jointed) sleeps normally, and the watchdog
timer keeps accruing during the defer, so the I3 guarantee is *delayed* for marginal
blocks, never lost. If you ever see a block twitching, check this defer first (then I1/I2).

### I4 — Physics footprint is SMALLER than the visual cell (0.94).
A piece must be able to slide into a gap exactly its own size. With a footprint of exactly
1.0 cell that is mathematically impossible (any sub-pixel neighbour drift pinches the slot
→ the piece wedges on the corners → depenetration shoves the walls apart). The 6% total
clearance also means side-by-side blocks don't touch at rest, so the contact graph splits
into independent columns: a landing wakes one column, not the whole tower. The sprite stays
full size; only collision shrinks, **uniformly** (so it survives 90° rotations).
This is how Tricky Towers does it. If footprint goes back to 1.0, everything regresses at once.

### I5 — Grid owns X/rotation during descent; physics owns Y from first contact.
The falling piece is kinematic, column-snapped, rotation-snapped. At first valid contact:
snap X to grid → cast to the *real* contact Y (never a computed grid Y) → resolve any
residual overlap by moving **the incoming piece only** → go Dynamic with a capped downward
velocity. Never compute a landing Y from grid math (drifted/compressed neighbours make it
wrong by ±0.01 → depenetration pop or micro-freefall). Never derive a piece's column from a
neighbour's live position (drift would propagate). Control handoff ("locked", spawns next
piece) is a separate concept from "settled" (quiet for a sustained window) — don't merge them.

---

## 2. Load-Bearing Settings — BlockController script defaults

The block prefabs do **not** override these fields (they were added after the prefabs were
saved), so the script defaults are authoritative. If you ever edit a value in a prefab
inspector, that prefab silently stops following the script default — check with the
Inspector in debug mode if behaviour diverges between pieces.

| Setting | Value | Why this value / what breaks if changed |
|---|---|---|
| `colliderFootprintScale` | **0.94** | Invariant I4. Lower (→0.90) = more forgiving + bigger visible seams; 1.0 = game-breaking wedging. Must equal the island footprint scale. |
| `colliderCornerRadiusFraction` | 0.06 | Rounded corners turn "catch and tip" into "shave past and slide in". Box is shrunk by 2r so radius adds **no** size (edgeRadius expands outward!). |
| `defaultBlockFriction` | 0.95 | Engine default 0.4 is "wood on ice" — towers shear sideways. Box2D mixes friction as √(a·b), so **every** surface (blocks, floor, islands) must be ~0.95. |
| `defaultBlockBounciness` | 0 | Any bounce amplifies stack ringing. |
| `restingLinearDamping` | 0.5 | Wobble dies in a beat or two but towers still lean/topple honestly. This is the "feel" dial — not lock/freeze logic. |
| `restingAngularDamping` | 3 | Same. |
| `maxLandingImpactSpeed` | 2 | Handoff velocity cap. Decouples landing force from fall speed: late-game fast drops land as softly as early ones. Difficulty = reaction time, never impact. (Also set per-mode in assets.) |
| `settleLinearThreshold` / `settleAngularThreshold` | 0.08 / 8 | "Quiet" thresholds; deliberately far looser than Unity native sleep tolerance (0.01) — native sleep is unreachable on tall stacks, so we sleep blocks ourselves. |
| `settleTime` | 0.35 | Sustained-quiet window before the clean-settle sleep. |
| `stillnessPositionTolerance` | 0.005 | Invariant I3 watchdog. |
| `stillnessRotationToleranceDegrees` | 0.5 | Invariant I3 watchdog. |
| `stillnessTime` | 0.75 | Watchdog window. Also makes phantom wake-ups cheap (re-sleep without re-earning velocity quiet). |
| `KnifeEdgeGraceSeconds` (const) | 2 | I3 refinement: sleep is deferred this long for quiet blocks whose COM is outside their support span, letting them tip honestly. Bounded so I3 still always wins. |
| `SupportSpanEpsilon` (const) | 0.01 | COM-outside-support margin for the knife-edge test. Larger values defer sleep for more borderline blocks. |
| `quietGridPullFactor` | 0.15 | Strength of the awake-time ease toward grid X. **Velocity-based** (I1). |
| `quietGridPullMaxSpeedFraction` | 0.02 | Pull speed cap = 0.02 u/s — must stay well under `settleLinearThreshold` (0.08) so the pull can never keep a block awake. |
| `QuietPullMaxTiltDegrees` (const) | 1 | Pull only touches blocks that seated flat. Nudging a tilted block engages/releases its lean contact every frame → rocking limit cycle. |
| `microAlignMaxColumnFraction` / `microAlignMaxRotationDegrees` | 0.08 / 4 | ε caps: corrections only ever apply within these; beyond them the block belongs to physics. (Used as the quiet-pull drift bound.) |
| `sleepSettledBlocksOnLock` | true | The whole settle architecture assumes self-managed sleep. |
| `landingSupportNormalY` | 0.7 | A cast hit only counts as landing if the surface is actually upward-facing — rejects corner/side grazes (diagonal normals). |
| `landingMinSupportWidthFraction` | 0.15 | A landing also needs ≥15% of a cell of horizontal overlap. Stops 0.5 mm corner grazes from being treated as a floor (the original "block lands on nothing and tips" bug). Too high and valid narrow placements get rejected. |
| Lateral placement assist | **removed** | The magnetic placement assist caused historical chaos and was deleted. If ever rebuilt, it is polish on top of verified geometry, never a bug fix. |
| `groundedCheckDistance` | 0.03 | Small so last-second tucks stay possible. |
| `maxControlTime` | 12 | Safety lock for pieces that never find a landing. |
| Collision detection | Continuous while falling, **Discrete once landed** | CCD on resting bodies only adds speculative-contact noise and cost; descent is cast-driven anyway. |

Code-level details that are part of the contract (not inspector values):
- The High Friction ability may raise the runtime shared fallback material used by
  standard blocks. It must not mutate `BlockData` or authored `PhysicsMaterial2D`
  assets; explicit-material variants such as Ice keep their authored surface.
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
  Y is descent-authored, so this does not touch I5), bounded to ~half a cell. Per-contact
  pushes combine as **extremes, never sums** (two cells on the same island row need the
  push once; opposing pushes mean "doesn't fit", not zero), and a tucked step must end
  fully clear of rock AND bricks or the whole step is reverted — the tuck may never hand
  the solver a brick interpenetration it created itself. History: with only the continuous probe,
  a one-cell pocket between island cells demanded ~0.13-cell vertical alignment and was
  effectively unenterable, while an identical pocket between tower blocks allowed half
  a cell ("islands act differently from blocks", June 2026). Block-vs-block stays
  grid-based (I5); bricks overlapped by a step are still mediated by the solver.
- A failed **nudge** (the corner-zone dash refused by bricks or islands) shoves the
  blocking landed bricks with a horizontal **velocity impulse** (`SlamBlockingBricks` —
  I1-sanctioned: never positions) and arms a 0.5 s nudge lockout (`NudgeFailLockoutSeconds`,
  static across pieces). Anchored/cemented (non-Dynamic) bricks and islands never move.
  Drag steps stay silent on refusal — only the nudge is high-stakes.
- Cast/overlap buffers are reused instance arrays — no per-FixedUpdate allocations
  (GC spikes read as physics stutter).
- Anchored/cemented blocks (`FreezeInPlace()`, used by the anchor-brick variant and the
  cement-tower power-up) become Static bodies; landed maintenance skips any non-Dynamic
  body. Static blocks are allowed to violate grid registration — they freeze as-is by design.

## 3. Floor & Sky Platforms (must match the blocks)

| Where | Setting | Value | Why |
|---|---|---|---|
| PlayAreaController | `floorFriction` | 0.95 | Friction mixing — see above. |
| PlayAreaController | `floorColliderEdgeInset` | 0.03 | Collision edge sits just inside the visual floor so pieces don't snag its corners. |
| StaticSupportIslandManager | `_islandFriction` | 0.95 | Islands are the tower's anchors; the prefab itself has **no** material, the manager applies it at spawn. |
| StaticSupportIslandManager | `_islandFootprintScale` | 0.94 | **Must equal** the blocks' footprint scale or pieces wedge beside/between islands. |
| StaticSupportIslandManager | `_islandCornerRadiusFraction` | 0.06 | Match blocks. |
| StaticSupportIslandManager | spawn-clearance check | (code) | A platform never materializes intersecting the falling piece / tower / another island — an overlapped piece can't land on it and ghosts through (the original "fall through platforms" bug). Platforms must also spawn **below the spawn line** to be usable (see camera settings). |

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
| Sleep tolerances | 0.5 s / 0.01 / 2 | Native sleep is effectively unreachable on stacks — irrelevant because sleep is self-managed (I3). |

Block data: Normal mass 1, Heavy mass 3. A 3:1 ratio is fine for Box2D at these iteration
counts (mushiness starts ~10:1). Don't "fix" stability by changing masses.

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
| `settle*`, `microAlign*`, `sleepSettledBlocksOnLock` | mirror §2 values | — | These exist per-level but should normally stay identical to the script defaults. |

## 6. Symptom → Cause Cheat-Sheet (when someone reports a regression)

| Symptom | First thing to check |
|---|---|
| Towers shimmer / everything moves constantly | Someone is writing positions on landed blocks (I1). Search for `SetPosition`/`transform.position` reachable after `HasLanded`. |
| One block twitches forever in place | Something moves bodies at sleep time (I2), or the watchdog (I3) was weakened/removed, or the knife-edge defer (I3 refinement) lost its grace bound. |
| Half-on-edge block survives on one floor side, falls on the other | Knife-edge defer (I3 refinement) removed or its support test broken — COM-on-edge outcomes degrade to float noise without it. |
| Piece won't fit a gap it should fit; placements shove neighbours | Footprint scale crept back toward 1.0, or islands/blocks scales diverged (I4). Verify in Physics Debugger: collider outlines must sit visibly *inside* sprites. |
| Blocks land on invisible corners and tip | Landing filter weakened (`landingSupportNormalY`, `landingMinSupportWidthFraction`). |
| Blocks slide off platforms/floor | A surface lost its 0.95 friction material (remember √-mixing punishes one bad surface). |
| Tower collapses by itself late game | Escalating load came back (gravity scaling per block) or landing impact got coupled to fall speed again. |
| Falls through sky platforms | Platform spawned overlapping the piece (clearance check), or spawn-ahead vs spawn-line relation broke (§5). |
| Landings detected at wrong column edge | A `SyncTransforms()` call before a cast was removed (AutoSyncTransforms is off). |
| Can't nudge/steer into a pocket between islands, or a sidestep wedges the piece inside an island | The snapped-row forgiveness or `TuckIntoStaticPocket()` was removed/weakened (§2 code details). |
| Stutter under load | Per-frame allocations returned, or CCD re-enabled on landed bodies. |

---

*Maintained by hand — update this file when any of the above changes, and record the
symptom you were fixing. The history matters: every forbidden thing in here was once tried.*
