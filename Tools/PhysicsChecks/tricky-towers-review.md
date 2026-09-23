# Tricky Towers comparison and load-sharing correction — September 23, 2026

The reference supports the requested distinction: carefully packed blocks provide a steady
surface for later placements, while poorly supported pieces can tip and fall without taking
the sound part of the tower with them. Another false structural release was reproduced in our
game and corrected. This does not claim to reproduce Tricky Towers' proprietary physics settings.

This report records the first load-sharing correction, tested with the original 0.15-cell
reserve. The subsequent [sky-platform correction](sky-platform-alignment-review.md) reduced
that reserve to 0.03, improved numerical pivot selection, and brought the complete regression
baseline to **265 passes / zero failures / zero captured test errors**. The user accepted the
updated behavior on September 23. Counts and timings below describe the earlier stage.

## Reference video

Source: `/Users/Nick/Downloads/trickytowers.mp4`, 1280×720, approximately 29.97 fps, 812.61 seconds.
The first roughly 6 minutes 40 seconds contain building; the latter portion holds the results
screen. Analysis used timestamped frames across the clip and closer 0.2-second sampling from
1–40 seconds. Frames and contact sheets are local evidence in `Library/TrickyTowersReview/`.

- **00:02–00:13:** the packed opening stack stays broadly aligned as different shapes arrive;
  small gaps and individual overhangs do not make every subsequent row visibly wander.
- **00:14.2–00:16.0:** an outboard square rotates, moves off its support and falls. The packed
  lower tower stays aligned. A new T and upright corner piece can still be placed above it.
- **00:32–00:36:** a green binding effect appears. This section cannot establish ordinary
  unaided stability, so it was not used to justify making normal blocks adhesive.
- Later samples show leaning and lost pieces as well as a very tall surviving structure.
  The reference is forgiving, but does not eliminate physical failure.

Camera movement, image resolution and visual effects prevent recovering exact friction,
gravity, mass or hidden stabilization rules from these observations. The goal here is the
visible placement behavior, not an inferred numeric copy of the other game's engine.

## Relationship to the earlier investigations

The full September 22 “Fix block sliding and alignment” conversation and both saved reports
were reviewed. The earlier contact-centre correction fixed artificial eccentric loads on equal
columns. Coordinated sleep then fixed repeatedly waking a quiet mixed Kinematic/Dynamic branch.
Both fixes remain useful, but neither could repair the decision that initially released a
well-packed branch into free physics.

Once that happens, small physical lean is retained. Pieces supported only by those Dynamic
bodies cannot enter grid ownership, so later placements inherit the imperfect geometry.
Increasing friction or sleeping sooner would not correct an erroneous ownership decision.

## Reproduction and cause

The saved `Fixtures/tilted-branch.json` contains 50 landed blocks. Rounding their poses back to
intended lattice cells and quarter turns reconstructs a placement layout. These poses are fed
through the real prefab landing/support code. This is **not** a replay of the player's original
input history or Unity's old contact cache; it is a controlled reconstruction from the snapshot.

On the original code, the 34th placement—the outer upright L—releases the lower S and its upper
branch. Ultimately 39 of 50 blocks remain grid-owned, matching the affected branch in the saved
report. A ten-second diagnostic develops about 1.49° maximum quarter-turn error among the
released pieces. The exact displacement differs from the original live run.

The nearest-contact load path gives the lower S a 4.25-unit load at X≈4.4118. Its right support
patch ends around X=4.41, with the existing 0.15-cell reserve inside that. That **particular**
distribution fails. It does not establish that the whole contact network cannot balance:
other compressive reaction distributions conserve every body's force and moment while satisfying
the same margins and hook limits. The former code never checked those alternatives.

## Implementation

`BlockController.GridStability.cs` retains the original top-down calculation as the fast path.
Only when that path identifies a release candidate does `BlockController.GridEquilibrium.cs`
try an alternative distribution for the connected structure.

The fallback represents each support patch by nonnegative endpoint reactions. Equal and
opposite reactions at the same horizontal coordinate connect the two bodies. Separate constraints
enforce force balance, moment balance and the existing total-resultant support margin at every
block. Verified hooks preserve their bounded eccentric reaction; verified static terrain sockets
retain their existing ground brace. Ice cannot gain normal hook support.

The bounded solver uses double arithmetic, local moment coordinates, deterministic lattice/contact
ordering and final checks against the original equations. A failed or numerically unresolved
proof conservatively retains the old release behavior. This is not a complete elastic-structure
simulation, and it can decline layouts it cannot certify.

No Dynamic block is snapped back into alignment. No grid spring, permanent adhesive joint,
friction increase, weaker gravity, collider edit or earlier sleep was added. The correction
prevents an unwarranted release while the stack is still exactly placed.

## Validation

Unity 6000.4.10f1, accessed through the installed Unity MCP plugin's TCP bridge at localhost:6400.
The configured HTTP endpoint at :8080 was unavailable. Controlled regressions run in the
temporary `__PocketPhysicsReview` Play-mode scene, using real prefabs and production methods.

| Check | Before | Corrected |
|---|---:|---:|
| Grid-owned blocks in the 50-piece reconstruction | 39/50 | 50/50 |
| First false release | Placement 34 | None through all 50 |
| Further drift over 120 seconds, original/mirrored/translated layouts | Not asserted by the initial diagnostic | 0 |
| Added well-supported pieces on the existing foundation | Not measured in initial diagnostic | 30; all 80 stay exact |

`load-sharing.cs.txt` adds 28 checks: original and mirrored layouts, a 200-cell translation,
reversed component order, simulation between placements, two-minute holds, 30 additional
cast-driven placements, large outboard loads, explicit jolts, and removal of all terrain.
The further stack uses a supported foundation; simply growing a column over a cantilever is
not assumed safe merely because the upper squares line up.

The existing 212 checks cover alignment, terrain sockets, rotations and Dynamic settling,
including hooks, ice slipping, cumulative overload, support removal, joints and repeated jolts.
That stage's result was **240 passes / 0 failures / 0 captured test errors** (28 new + 212 existing).
Counts and timing are recorded in the JSON reports listed below.

A live custom-game smoke test in the real Gameplay scene also used the normal hard-drop command
through MCP to stack 24 normal squares: all 24 remained grid-owned, with zero measured angular
error and 47.5 reported tower height. The next square was moved one column outward and dropped;
it was lost, lives changed from three to two, and all 24 correctly placed blocks stayed grid-owned.
This simple run checks the spawner/control/camera/landing integration; the mixed-shape regression
is the evidence for the new load-sharing fix.

The dense fallback allocates and runs at structural validation, not every physics frame. Its
80-block editor timing in the final run was approximately 27–33 milliseconds; ordinary successful load paths
skip it. A representative mobile-device profile and play test are still needed for worst-case
landing latency and subjective feel. This work does not establish that every source of sliding,
every level, or every intentionally disturbed stack is now identical to the reference game.

## Evidence and reproduction

- [Runner](load-sharing.cs.txt); use the prepared-scene procedure in [README.md](README.md).
- `Library/load-sharing-checks.json`: 28 new assertions and fallback timing.
- `Library/stack-alignment-checks.json`: 46 alignment assertions.
- `Library/terrain-pocket-checks.json`: 38 terrain/support assertions.
- `Library/rotation-checks.json`: 46 rotation assertions.
- `Library/settling-checks.json`: 82 settling assertions.
- `Library/TrickyTowersReview/replay.json` and `replay-after.json`: initial controlled comparison.
- `Library/TrickyTowersReview/load-trace.json`: the original greedy reaction distribution.
- `Library/TrickyTowersReview/live-play.json`, `live-stable.json`, `live-overhang.json`, and PNGs:
  live custom-game observations.

Library evidence is ignored and local. The source fixture and runner preserve the reproduction.
Current behavior, final timings and test totals are recorded in the linked sky-platform report.
