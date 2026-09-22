# Tilted branch investigation — September 22, 2026

The user's second report was inspected in the actual paused Gameplay scene, after commit
`fae31fa`. That commit's support-load correction does not address the separate Dynamic-body
settling defect reproduced here. The first investigation preserved the paused run; after the
user authorized ending Play mode, the general settling fix below was implemented and tested.

## Captured state

The run was paused at frame 17565, with 50 landed blocks and one controlled block in flight.
39 landed blocks were grid-owned and 11 were Dynamic. All 11 Dynamic blocks were on the
right-hand branch and had almost zero linear/angular velocity, but all remained awake.

Selected Rigidbody poses (world units; a cell is one unit):

| Piece | Instance ID | Position | Rotation | Observation |
|---|---:|---|---:|---|
| Lower green S | -359200 | (4.15032, -6.99101) | 0.26386° | About 0.15 cell right of its original column |
| O above it | -359612 | (4.25935, -4.98160) | 1.06187° | About 0.26 cell right of its original column |
| First upright orange L | -361070 | (5.19556, -3.94951) | 91.03690° | Lean of about 1.04° |
| Outer upright orange L | -361830 | (6.13310, -2.91387) | 91.18974° | Lean of about 1.19° |

The live contacts connect the lower S, O, orange Ls and the displaced pieces above them.
The S also contacts exact grid-owned blocks both above and below its left-hand lower cell.
This is a mixed Kinematic/Dynamic contact network, not an entirely free stack.

The snapshot proves their current poses and ownership, not the order in which they lost grid
ownership. In particular, it does not establish that the outer L was the first cause of the
shift. A slight physical lean of an unsupported hook is allowed, as the user clarified.

## Confirmed settling defect

`HandleLandedMaintenance` calls `SleepSettledBody` for each quiet body independently, through
either the settle timer or the stillness watchdog. `SleepSettledBody` zeros that body's
velocities and calls `Rigidbody2D.Sleep`. Connected Dynamic bodies and awake Kinematic grid
supports can immediately wake it again. The watchdog's elapsed time remains above its
threshold, so this can repeat every physics step.

The paused run already contained contact impulses far larger than the ordinary resting
reactions: for example, the S/O contact reported a normal impulse around 172.7. The comparison
below demonstrates growth caused by the current maintenance path in the copied fixture.
Contact impulses are solver output, not the same quantity as body speed or a stored external
force, so their magnitude alone is not a measurement of visible displacement.

## Isolated reproduction

All simulation ran in temporary scenes created with `LocalPhysicsMode.Physics2D`. Only each
local `PhysicsScene2D` was stepped. The live game's default physics world was never stepped,
and its frame remained 17565. Copies contained bodies and colliders; tests using production
maintenance also used disabled BlockController components with the captured body properties
and poses restored after Awake. They were removed from live tracking before simulation.
No landing events, player-run callbacks or save operations were invoked.

A final readback matched all 51 live blocks against the initial snapshot: maximum position,
rotation and linear-velocity differences were all zero. Unity remained playing and paused at
frame 17565.

The copy starts with fresh solver contacts, so it cannot reproduce the original run's internal
warm-start cache exactly. Comparisons demonstrate the defect and candidate behavior from the
same exported geometry; they are not a replay of the player's complete input history.

### Idle copied branch

Using the **actual production `HandleLandedMaintenance` method** on copied controllers:

| Simulated time | Largest contact normal impulse | Awake Dynamic blocks |
|---|---:|---:|
| 1 second | 19.0 | 11 |
| 10 seconds | 257.3 | 11 |
| 60 seconds | 1897.9 | 11 |

A separate raw-body native-physics control stayed below a maximum impulse of 0.82 over
60 seconds. Removing only the per-body forced sleep likewise prevented the growing impulses
in that control; native sleeping by itself did not put the mixed contact network to sleep.
Changing copied collider heights did not fix the sleep/wake cycle and is not the proposed fix.

### Later landings, prototype comparison

Four additional O-shaped bodies were seated by a downward cast and handed to Dynamic physics
at the game's capped landing speed of 2 units/second, at 10, 15, 20 and 25 simulated seconds.
These are controlled landing probes, not a full replay of placement assistance, abilities or
structural-grid registration.

| At 60 simulated seconds | Current production maintenance | Coordinated-sleep prototype |
|---|---:|---:|
| Largest contact normal impulse | 1970.6 | 1.33 |
| Awake Dynamic bodies | 15 | 4 |

The coordinated-sleep prototype allowed the original quiet branch to sleep, preserved its
nonzero tilt, and allowed it to respond to later landings. The additional four bodies still
reported awake at the end; their final poses stopped changing between the 40- and 60-second
samples. This is not evidence that every possible contact network now sleeps correctly.

Both versions had some additional movement when weight was added. This investigation does
**not** establish that all of the original 0.15–0.26-cell displacement came from the sleep bug,
or that a sleep fix alone will eliminate all horizontal movement or alignment differences
when stacking on an actually tilted support.

## Implemented general fix

The user confirmed that the L was an example, and authorized ending the paused session to
compile and test. The fix has no shape-specific branches and does not alter support ownership,
colliders, materials, gravity, masses, solver settings or pose alignment.

- `BlockController.Settling.cs` tracks eligibility without independently sleeping each body.
  The existing motion thresholds, soft damping, stillness window and knife-edge grace remain.
- `BlockController.SleepGroups.cs` traverses complete contact lists plus block joints in both
  directions. Vine/Maw joints may disable collisions, so contacts alone are insufficient.
- Every awake Dynamic member must be eligible. Motionless grid-owned Kinematic supports join
  the sleep operation; Static terrain terminates traversal. Controlled or external bodies,
  moving Kinematic bodies, NeverSleep and disabled automatic settling prevent forced sleep.
- All velocity writes finish before any group member is slept. Poses, rotations, Dynamic body
  types and constraints are preserved. Timers reset after group sleep and explicit jolts so
  old watchdog eligibility cannot immediately cancel the next disturbance.
- Reusable lists and sets avoid steady-state allocations and contact-buffer truncation.

## Production validation

`settling.cs.txt` rebuilds the saved geometry in the isolated review scene and invokes the
actual production maintenance method. New scenarios use real block prefabs and real joints.
The initial 12-check reproduction on the original code produced **5 passes / 7 failures**.
The completed suite now produces **82 passes / 0 failures**, with no captured runtime errors.

| Captured idle branch at 60 seconds | Original production code | Coordinated production code |
|---|---:|---:|
| Awake Dynamic blocks, either update order | 11 | 0 |
| Largest normal impulse, forward order | 1946.2 | 0.80 |
| Largest normal impulse, reverse order | 2842.4 | 1.09 |
| Further position drift after 10 seconds, forward order | 0.000956 cell | 0 |
| Further position drift after 10 seconds, reverse order | 0.0000031 cell | 0 |

The branch retains its nonzero tilt and Dynamic body types; grid supports keep their poses.
Exact impulses vary slightly with contact ordering between fixture runs. The assertions check
bounded behavior, sleep and drift, rather than a particular floating-point impulse value.

In the later-load scenario, the added blocks fall off the narrow upper branch. This is allowed:
all four stay Dynamic and continue falling, while the surviving original branch returns to
sleep with zero further drift during the final 20-second observation window. The final sampled
contact impulse was about 2.4 rather than the thousands seen with independent sleep. The
fixture has no gameplay loss-zone cleanup, so fallen bodies continue below the terrain.

Twenty additional jolt/settle cycles followed by a further minute of physics left the branch
asleep, with maximum sampled resting contact impulse about 5.2. The 82 checks also cover:

- All seven shapes at all four quarter turns on fully supported Kinematic foundations.
- Unsupported falls and genuine tipping from both sides of narrow support edges for every shape.
- A 4-degree resting slope, support destruction, jolts and subsequent settling.
- Fixed joints with collisions disabled, attached in both directions, including break/removal
  and an impact transmitted to another connected body.
- Active and external Kinematic bodies retaining movement/control; settling opt-out; separate
  piles sharing static terrain settling independently.
- Zero managed bytes allocated across 1,000 warmed blocked-sleep attempts (about 2 ms total on
  this editor machine; this is a focused allocation check, not a mobile-device benchmark).

The existing **46 alignment + 38 terrain/support + 46 rotation checks** also passed with no
captured runtime errors. These cover full landing decisions, hooks, cumulative overload,
Ice variants, support release and long-duration exact stacks.

The measurements establish that coordinated sleep removes the reproduced artificial cycle.
They do not prove that all displacement accumulated before the paused snapshot came from it.
A physically tilted support can still produce tilted upper pieces or collapse under added
weight. An on-device play test remains necessary to judge feel and any remaining visible drift.
No changes have been committed or pushed.

## Evidence files

- [Physics-only fixture](Fixtures/tilted-branch.json): 50 landed bodies and terrain geometry;
  no player account or save data. Contains poses, colliders, materials and mass properties,
  not internal solver caches or a serialized resumable game.
- `Library/live-sliding-snapshot.json`: full initial block state, including the active piece.
- `Library/live-sliding-contacts.json`: contacts and settling fields read from the paused run.
- `Library/live-sliding-production-probe.json`: production-maintenance copy measurements.
- `Library/live-sliding-clone-probe.json`: native / per-body-sleep / damping-only controls.
- `Library/live-sliding-loaded-probe.json`: later-landing prototype comparison.
- `Library/settling-before.json`: initial production reproduction before the change.
- `Library/settling-checks.json`: completed 82-check production regression results.
- `Library/stack-alignment-checks.json`, `Library/terrain-pocket-checks.json`, and
  `Library/rotation-checks.json`: the existing 130 checks rerun with the new implementation.

The Library files are local, ignored diagnostic evidence. The fixture and MCP method-body
runner under Tools preserve the reproduction without requiring the original live session.
