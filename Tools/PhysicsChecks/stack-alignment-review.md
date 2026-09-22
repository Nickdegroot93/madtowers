# Stack drift and alignment investigation — September 22, 2026

A support-load calculation could release a correctly aligned stack into free physics merely
because more centered blocks were added. The correction is limited to
`BlockController.GridStability.cs`, in `DistributeGridLoad`.

## Symptoms and scope

The supplied screenshots show widening horizontal seams in Neon Nightfall and small height /
angle mismatches in the jungle tower. A still image cannot establish which placement first
caused the movement, whether a hazard acted earlier, or which build produced it.

The current branch already distinguishes exact grid-owned blocks from free Dynamic bodies.
An exact block is Kinematic with FreezeAll, so solver drift cannot move it while that ownership
holds. Investigation therefore concentrated on valid placements losing grid ownership.

The isolated reproductions below establish a current-code cause of unwanted movement and
alignment loss. They do **not** reconstruct the exact photographed towers or prove this is
the only cause of the reported outward spreading. In particular, their largest measured
movement is vertical separation, with smaller horizontal displacement and angular error.
Released debris and stacks affected by intentional hazards can still move physically.

## Reproduction and cause

Use real I prefabs, rotated vertically, with their cell columns at X=-1 and X=0, occupying
rows 0–3. Place O prefabs across both columns, occupying rows 4–5, 6–7, and so on. Floor top
is Y=-0.5. All columns, cells and quarter-turn rotations begin exactly aligned.

Both bases support their load evenly. Nevertheless, the original algorithm releases one
base and the supported branch after eight O pieces on sharp floor columns; rounded columns
fail after four. A simple exact-pose sharp-floor reproduction developed about 0.099 cell of
vertical offset at the eighth square after 30 simulated seconds. The first square leaned
about 0.326 degrees. One base remained grid-owned while the other became Dynamic.

`DistributeGridLoad` selected the *nearest inner edges* of support patches when the load
resultant fell between them. A centered load at X=-0.5 was applied near X=-0.59 to the left
column and X=-0.41 to the right column, instead of at their centers X=-1 and X=0. Each base
therefore received an artificial eccentric load. Adding centered weight moved each computed
resultant toward its tipping boundary until the existing edge reserve rejected a base.
Support revalidation then released the blocks above it. Contact solving produced small
height differences and tilts even though the geometry had been placed correctly.

This also explains why a defect can appear only after several later placements, and why
merely increasing friction or solver iterations would not fix the incorrect release.

## Plan executed

1. Inspect placement, collider sizing, contact cleanup, support/load propagation and settling.
2. Connect to the installed Unity MCP plugin's TCP bridge on localhost:6400. The configured
   HTTP MCP endpoint at :8080 was unavailable. Use the plugin's `execute_code` handler.
3. Preserve the open Gameplay scene and previous play-mode start scene. Run fixtures in a
   temporary empty `__PocketPhysicsReview` scene without a GameManager or player run.
4. Reproduce a valid stack's false release before editing runtime code.
5. Correct load distribution and add repeatable prefab-based regressions.
6. Compile and run the same regression suite with both original and corrected code.
7. Restore the corrected implementation, rerun checks, and restore editor scene settings.

## Change and physical limits

When supporting contact centers bracket the resultant, use those centers for the reactions
and divide weight according to the lever arms. This conserves both total force and moment:

- left reaction + right reaction = incoming weight;
- left reaction × left X + right reaction × right X = incoming weight × resultant X.

Outside that center span, the original eccentric-load handling remains. Hooks still transmit
their full moment; their overhang allowance is unchanged. Existing support eligibility,
structural edge reserve, collider sizes, materials, gravity, damping, solver settings, landing
snaps and one-way transition to Dynamic physics are unchanged.

A support graph with several possible reactions is statically indeterminate; this remains an
arcade load model rather than an elastic structural solver. Contact-center distribution is a
better default for centered supports, but some complex multi-support collapse thresholds may
change. The existing tested overhang and overload examples retain their behavior.

## Controlled before/after evidence

Unity version: 6000.4.10f1. All tests ran through the Unity MCP plugin, using the real prefab
colliders, authored masses and runtime landing methods. The new suite drives
`SteerWhileFalling` until landing, advances 0.02-second physics steps, and invokes the
production landed-body maintenance path. Long cases add 20 squares with simulation between
placements, followed by 120 simulated seconds.

| Check | Original | Corrected |
|---|---:|---:|
| New stack/alignment suite | 35 pass, 11 fail | 46 pass, 0 fail |
| Sharp floor: first false release, both placement orders | 8th O | None through 20 O |
| Rounded floor: first false release, both placement orders | 4th O | None through 20 O |
| Additional position drift during 120-second observation, four fixtures | 0.0563–0.0686 cells | 0 |
| Final maximum quarter-turn angle error, four fixtures | 0.337–0.349 degrees | 0 |
| Existing terrain, hook, ice and overload suite | 38 pass | 38 pass |
| Existing blocked/legal rotation and narrow-slot suite | Prior documented baseline: 46 pass | 46 pass |

The new failures in the original were eight repeated-placement/drift checks, the 40-row I
bridge stack, supported Boulder load and supported Ice load. The corrected run recorded zero
runtime errors. Existing physical failure checks include J/S cumulative overload, J/T/Z/L
branch failure, overloaded hooks, unsupported O/L overhangs, force release and ice hook slip.
The new suite also verifies a real fall/tip after either base support is removed.

Reproducible runner: [stack-alignment.cs.txt](stack-alignment.cs.txt).
Instructions: [README.md](README.md).
Local ignored JSON evidence: `Library/stack-alignment-before.json`,
`Library/stack-alignment-after.json`, `Library/stack-alignment-checks.json`,
`Library/terrain-pocket-checks.json`, and `Library/rotation-checks.json`.

## Follow-up play testing

The user subsequently tested the fix and reported that things work properly. The device,
build and individual scenarios were not specified, so this feedback is recorded separately
from the measured editor results above. The following checklist remains useful for future
release testing; it is not a list of scenarios individually confirmed by that feedback:

- Play Neon Nightfall and jungle on a build containing this fix. Record the
  first placement where an apparently stable stack starts moving, if it occurs. The screenshot
  build version was not established during this investigation.
- Build tightly packed towers, insert vertical bars into narrow slots, and bridge separate
  supports. Observe both idle periods and another 15–20 placements for widening seams.
- Build intentionally offset towers until their accumulated weight tips them. Check that the
  timing and feel remain appropriate, particularly structures with several support branches.
- Exercise tremors, failed nudges, ice hooks and support destruction; verify that their
  intended disturbance still occurs and that fallen pieces do not snap back to the grid.
- Test on representative mobile frame rates. The editor checks explicitly simulate 0.02-second
  steps, matching the project timestep at the time of testing. They do not measure mobile frame
  pacing or reproduce full level UI/hazard sequences.
