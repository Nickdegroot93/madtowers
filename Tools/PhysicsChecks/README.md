# Physics regression checks

`terrain-pocket.cs.txt` is a Unity MCP `execute_code` method body, not a shipping script.
It runs 38 assertions using real block prefabs, `FloorTerrain`'s collider construction,
`BeginPhysicsLanding`, support revalidation, and 150 explicit 0.02-second physics steps.
It exercises accounting-free physics in an empty scene: no GameManager or player run exists.
It cleans up its fixture roots and restores time scale / simulation mode in `finally`.
Failures throw after writing `Library/terrain-pocket-checks.json` (ignored local evidence).

## Running

1. Keep the current editor scenes. Create an additive empty scene, save it temporarily as
   `Assets/__PocketPhysicsReview.unity`, then close it. Temporarily assign that SceneAsset to
   `UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene`, remembering the old value.
2. Enter play mode. Run `prepare-terrain-pocket.cs.txt` through MCP first: the game's static
   bootstrap still creates its menu in an empty scene. Preparation disables that scene's
   EventSystem (its fallback uses legacy input) and adds the otherwise-missing AudioListener.
   These are temporary scene objects/settings, discarded on exiting play mode.
   Execute the contents of `terrain-pocket.cs.txt` through Unity MCP's
   `execute_code`. The script refuses any other scene, a running GameManager, or existing blocks.
3. Inspect the returned pass/fail counts and the JSON report. Exit play mode, restore the original
   `playModeStartScene`, and delete the temporary scene asset and its meta. Do not save fixtures.

The test runner explicitly steps the default 2D world because BlockController's overlap/cast
queries use that world. The empty-scene guard prevents stepping a live player's tower.

## Controlled rotation regression

Run `rotation.cs.txt` in the same prepared empty scene, before or after the terrain checks.
It restores its simulation mode, time scale, gesture gate and fixture objects in `finally`,
and writes `Library/rotation-checks.json`. No player run or progress is involved.

The 46 assertions cover rejected I turns in a one-column slot (both directions), exact
restoration of the rejected pose/collider sizes/pending column, repeated taps, continued
descent and landing between real grid-stable towers, and unchanged blocker poses/velocities.
They also cover all seven standard shapes in open air, solid bodies of each type, tilted
debris, floors/ceilings, contact-skin tolerance, triggers/layers, stale queued rotations,
and refusal of control commands on landed or frozen pieces.

The pre-fix reproduction rotated a clear vertical I to -180 degrees with real overlap.
The same setup after the fix keeps it at 90 degrees with no overlap. All 46 rotation
assertions and the existing 38 terrain/support assertions pass with no runtime errors.
Rotation uses the existing discrete quarter-turn pivot and collision footprint; the
support, collider, tuck and Dynamic-settling rules are unchanged.

`rotation-parity.cs.txt` compares the original rotation setter against the public queued-input
and guarded-apply path in 2,016 clear layouts: seven shapes, four starting angles, three X
columns, four Y offsets, and six single/rapid tap sequences. It checks positions, angles,
cell centres, pending columns, collider sizes and velocities within 0.00001. All comparisons
pass with zero differences or runtime errors. Its temporary local physics scene never steps
simulation, and it restores fixture tracking in `finally`; results go to
`Library/rotation-parity-checks.json`. Use the same empty-scene preparation above.

The review caught an input-preview side effect: rebuilding collider shapes could perturb
half-cell bounds rounding and shift a legal turn by a cell. The final implementation checks
clearance only when applying the queued angle. Clear turns take the original rotation path
once; rollback and rejection happen only for an occupied destination. Successful rotation
sound/tutorial events now fire at application, so refused turns do not report success.

## Coverage and reproducible layouts

- Horizontal I inserted into a one-row static socket, both directions, including an O/T load.
- Same I with no ceiling, two-row clearance, a moving/kinematic ceiling, a frozen playable-block
  ceiling, or a marked slope must release. A ceiling without a floor cannot anchor a brick.
- Exact island-style socket, 180-degree I, and an actual authored `FloorTerrain` pocket.
- Removing either boundary releases the arm; removing the ceiling releases its supported branch.
- Normal S/Z/J/L hooks hold; Ice versions retain their original 0.5-cell/second outward slip and
  authored friction. Current normal data restores hook hold. Supported Ice, Freeze and Anchor
  preserve their existing ownership rules.
- Flat O/L overhangs release, centered O-on-O stays exact, a Boulder overload releases an S hook,
  explicit force releases a pocket arm, and a released arm never becomes grid-owned again.
- J/S: terrain columns -3 through 0, top Y=-0.5; J root (0,0), S root (1,1), both 0 degrees.
  J starts exact, then both pieces release when S adds its load.
- J/T/Z/L: same columns, top Y=0.5; J (0,1,180 degrees), T (0,2,0), Z (-1,3,90),
  then L (-2,5,90). The first three remain exact; the last addition releases Z and L while
  J and T stay exact. Positions are prefab roots, all pieces use their normal authored mass.

## September 2026 result and scope

The missing brace was restored from `3c49173`, which remained on `floor-test` after that
branch's earlier merge point. Reproduction with the real I prefab and pocket collider builder:

| Same fixture, three seconds of Unity physics | Original current-branch code | Restored brace |
|---|---|---|
| Ownership | Dynamic | Kinematic / FreezeAll |
| Rigidbody rotation | -1.633739 degrees | 0 degrees |

All 38 scoped assertions pass with the restored brace. The original support decision was also
recompiled and tested to compare surrounding behaviour: the existing overhang/hook cases passed
unchanged, while the braced pocket cases failed as expected. No collider, friction, gravity,
solver, steering, or settling settings were changed.
The final assertion run captured no runtime errors. Earlier empty-scene runs exposed unrelated
bootstrap EventSystem legacy-input exceptions and missing-listener warnings; the preparation
script isolates those test-environment issues without modifying production UI code.

**Separate existing entry limitation:** off-row insertion attempts at Y offsets -0.4 and +0.4
were refused by `TuckIntoStaticPocket` both before and after the restoration, with identical
resulting positions. They appear as observations in the report, not passing entry assertions.
The on-row insertion succeeds and is asserted. PHYSICS.md's broad entry-window claim is therefore
not fully met by the existing code. This patch intentionally does not change tuck/overlap logic.
