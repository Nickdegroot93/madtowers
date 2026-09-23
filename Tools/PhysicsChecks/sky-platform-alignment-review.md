# Sky-platform alignment investigation — September 23, 2026

The paused example exposes an overly conservative support-acceptance rule. The sky
platform is on the exact cell lattice. A lower branch has already lost grid ownership,
and its Dynamic contact offsets raise the yellow O enough to tilt the red Z bridging
the tower and the platform.

**Final status:** implemented with a 0.03-cell support reserve and robust equilibrium
pivots; all 265 regression checks pass. The user accepted the resulting forgiving behavior
on September 23. The investigation below records the original failure; the implementation
and validation section records the final behavior.

## Measurements from the paused run

Captured at frame 8313, with 19 landed pieces and one active I. Fourteen landed pieces
are grid-owned; the following five are Dynamic. Cell spacing is 1 world unit.

| Piece, from bottom of affected branch upward | Root Y | Intended root Y | Angle error |
| --- | ---: | ---: | ---: |
| Blue J at X ≈ -4 | -5.982484 | -6 | -0.37312° |
| Purple T at X ≈ -4 | -3.967459 | -4 | -0.37582° |
| Orange L at X ≈ -3 | -1.956714 | -2 | -0.02441° |
| Yellow O at X ≈ -3 | 0.058238 | 0 | -0.02688° |
| Red Z at X ≈ -2 | 1.038338 | 1 | -3.36540° |

The relevant island cell is centered exactly at (-2, 0). Its collider core is
0.82 × 0.88, with edge radius 0.06: its intended physical top is Y = 0.5.
The yellow O's upper cells are centered around Y = 1.0585 instead of 1. Its top
therefore sits approximately 0.0585 above the intended ledge height. The Z bridges
two support levels separated by a one-cell step, making this small excess visible
as a tilt. Island spawning already rounds its rows to the shared grid.

## Exact-placement reproduction

Temporary copies of the real prefabs and captured terrain were constructed 100 rows
above the paused game, then removed without advancing physics. Positions were rounded
to intended integer cells and angles to quarter turns. Pieces were added in allocation
order, inferred from instance IDs. This reconstructs a plausible placement sequence;
it does not replay the original inputs, discarded pieces or historical contacts.

Each copy used the production `TryEnterGridStablePlacement`, load-path checker and
`CanBalanceGridStructure`. The diagnostic deliberately retained each accepted grid pose
to examine successive proposed structures, rather than applying releases and stepping
their physics. All 19 proposed placements passed initial grid seating. The first 16
stages admitted balanced reactions. Adding the orange L was the first rejected stage:
both support solvers rejected the blue J at (-4, -6). The later O and Z stages also
failed structural acceptance.

This is a separate limitation from the previous alternative-load-distribution fix:
both solvers enforce the same edge reserve, so finding another load distribution
could not rescue this branch under the original policy.

## Why the checker releases a neatly fitted branch

At the time of capture, `GridStructuralEdgeReserveFraction` was 0.15. The support checker
took the supporting collider's bounds and removed another 0.15 cell at each outside edge.

The blue J's support interval is [-4.41, -3.59]. Its accepted resultant interval,
before the 0.005 numerical tolerance, is [-4.26, -3.74]. The J, upper T and orange L
have equal masses and centers X = -3.75, -4 and -3.25. Their combined center is
-3.666667: inside the measured support span, but outside the reserved span.

With the yellow O added, that branch's combined center becomes -3.625. The orange L
and O also have a combined center at -3.375; their supporting span is [-3.41, -2.59],
but the reserved left edge is -3.26. Both interfaces fail the deliberate margin even
though the vertical resultant remains inside the unreserved support interval.

These figures explain the rejection without requiring imperfect player placement.
They do not prove stability under arbitrary impacts or reproduce every side contact.
The support model primarily reasons about vertical reactions, with specific hook and
static-pocket exceptions; it is not a complete frictional-contact solver.

After a release, the game intentionally never re-registers a Dynamic piece on the grid.
Later landings therefore build on its physical contact heights. The mismatch seen at
the island is the downstream symptom of the earlier ownership decision.

## Initial correction hypothesis

Recalibrate the support edge reserve for the intended forgiving placement behavior.
For the two measured interfaces through the yellow O stage, a reserve around 0.03 cell
would admit their vertical resultants. At this stage it was an analytical candidate;
the subsequent implementation and testing are recorded below. A global change also
affects other narrow supports and must be checked
against intentional overhang, hook-overload, Ice, support-removal and jolt failures.
The full layout should be replayed through landing and release, followed by later
placements and a long hold. Simply offsetting islands would misalign the fourteen
pieces that currently remain exact.

No production physics changes were made during this investigation. The paused run was
preserved: frame 8313, every original body position/rotation and grid-ownership flag
unchanged, twenty original blocks remaining, and the original active I restored.

## Local evidence

- `Library/SkyPlatformReview/paused-snapshot.json`: original block and terrain geometry.
- `Library/SkyPlatformReview/reconstruction.json`: nineteen proposed placement stages,
  seating decisions, solver decisions and affected-branch support intervals.
- `Library/SkyPlatformReview/preservation-check.json`: unchanged-frame and pose comparison.

These are ignored local diagnostic files. The measurements and reproduction geometry
above are recorded here so the cause remains reviewable after Library cleanup.

## Implemented correction and regression results

After approval to end the paused run, the support reserve was reduced from 0.15 to
0.03 cell. This changes both the fast load-path test and the equilibrium fallback through
their shared constant. Exact placement ownership continues to enforce motionless rows;
the change accepts more supported structures before they enter free physics. Real contact
eligibility, hook reach, overlap rejection and Dynamic settling retain their existing rules.

The smaller reserve exposed a numerical weakness in the equilibrium solver on the earlier
50-piece fixture. Mathematically relaxing the constraint cannot invalidate its existing
solution, but nearly tied constraint ratios selected tiny pivots that amplified float
rounding. The solver now finds the limiting step with a 1e-9 numerical allowance, chooses
the strongest qualifying pivot, excludes already-basic variables from entry, and explicitly
maintains zero/one pivot columns. Every accepted result still passes the original independent
force/moment residual checks. No artificial force or torque is accepted to make a tower hold.

The reproducible [fixture](Fixtures/sky-platform-alignment.json) contains the captured geometry.
The [runner](sky-platform-alignment.cs.txt) uses the normal landing path for all pieces and
cast-driven descent for the upper T/L/O/Z, with real physics/maintenance between landings.
It runs only in the isolated empty physics scene.

| Verification | Result |
| --- | --- |
| New layout runner before the change | 6 passed / 14 failed; first release at orange L |
| New layout runner after the change | 20 passed / 0 failed |
| Nineteen-piece layout and mirror | All pieces remain grid-owned and exactly aligned |
| 120-second hold of each layout | Zero measured position drift; no angular misalignment |
| Twenty more vertical I placements above the island column, then 30-second hold | All 39 pieces remain exact, both directions |
| Eccentric L mass increased to 20 before the island bridge | J/T/L release and physically move; lower foundation remains exact |
| Ordinary J/S edge stack followed by an O overload | Two supported pieces hold; third piece moves resultant outside support and all three fall, both directions |
| Earlier 50-piece layouts and 80-piece extensions | All load-sharing checks pass, including mirrors and translated fixtures |

The final six suites report **265 passed, zero failures, zero captured runtime errors**:
sky-platform alignment 20, terrain/support 43, stack alignment 46, load sharing 28,
rotation 46 and Dynamic settling 82. The former two-piece J/S release assertion was
intentionally replaced with supported J/S and genuine J/S/O overload cases; this is the
requested forgiveness policy, supported by measured resultants rather than a suppressed failure.

One settling assertion depended on Unity's native auto-sleep timing after three seconds
and failed once before passing on rerun. It now wakes the quiet contacting pair and invokes
the production group-sleep method without stepping physics, verifying the intended external
Kinematic veto directly. No production sleeping behavior changed.

Final reports are `Library/*-checks.json`; the old-margin baseline is retained at
`Library/SkyPlatformReview/before-checks.json`. Desktop editor proofs for the 80-piece
structures measured approximately 14–16 ms in this run. The user accepted the updated feel;
on-device performance profiling remains outstanding. The regression reconstructs intended geometry rather than
recovering the old game's solver caches or replaying the exact player inputs.
