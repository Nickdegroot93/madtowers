# BLOCKPREVIEWS.md — "how this brick works" demos (BUILT)

The little looping clips that *show* a brick's behaviour: the one-time **debut modal** when a
never-seen variant first drops in play, and the **Vault** collection screen's detail views and
showcase posters. They read like short videos but are **live in-engine loops**, not video files.

Sister contracts: [BLOCKVARIANTS.md](BLOCKVARIANTS.md) (the bricks; §4's recipe now includes the
demo/Vault step), [DATA.md](DATA.md) (the discovery persistence), [PROGRESSION.md](PROGRESSION.md)
(which chapter debuts which brick). The first-run gesture tutorial ([TUTORIAL.md](TUTORIAL.md))
is a separate feature: it teaches *gestures* interactively; a block preview teaches a *brick*,
passively, in a watchable loop.

---

## 1. Decision (held): in-engine real-time loops, not video files

Baked video fights everything here: app size, aspect ratios (RESPONSIVE.md), per-chapter theming,
and staleness against living shaders. The demo renders with the actual game instead. Video remains
a fallback for a behaviour too messy to script — none of the current bricks needed it.

## 2. Architecture (`Assets/SourceFiles/Scripts/Discovery/`)

| Piece | Role |
|---|---|
| `BlockDemoStage` | The diorama: builds a sandbox at a far offset (`(1000,200)` + a per-stage slot so two stages' physics can never meet; GameObject layer 3), its own orthographic camera → RenderTexture (shown via a UI RawImage), chapter ground art + accent glow, a full-width **static floor collider** (top at stage-local y = 0), the loop driver and helpers (Spawn/SpawnPhysical/WaitForLand/Settle/Reveal/FadeCut/SetView/Shatter/Dust/CameraKick). `Open()` = looping demo; `OpenPose()` = static poster framing; everything is destroyed and the RT released on `Close()`. Renders only while visible — zero idle cost. |
| `BlockDemoPuppet` | Prefab→puppet factory: instantiates the REAL block prefab under an inactive host and strips `BlockController` **before activation** (a real controller would register in `BlockController.AllBlocks` — the LossZone sweep would charge lives — and hijack `ActiveControlled`; behaviours mutate real game state). Two flavours: KINEMATIC (posters, control-story demos — body/colliders stripped, moved by script) and **PHYSICAL** (`physical: true`) — the prefab's own dynamic `Rigidbody2D` with the variant's real mass / friction material / gravity, colliders live, a `DemoContactRelay` for land/touch signals, plus `DemoFallGovernor` capping descent to the game's controlled fall speed until first contact (freefall momentum would make every impact read far too violent). |
| `BlockDemoCatalog` | The data table (in CODE, like `TutorialModifier.Steps`, so it can't go stale): variant id → scenario + fallback caption. `HasDemo()` is the debut gate — no entry ⇒ no modal, silent discovery. |
| `BlockDemoScenarios` | One demo per variant, built as a **TEMPLATE + REAL PHYSICS** (Nick's rule): an exact starting structure of physical puppets (spawned asleep at their poses), ONE dropped variant piece, and the simulation plays out the consequences — weight, balance, sliding, toppling are genuine Box2D, never animated. Only the variant MOMENT is a small shim doing what the real behaviour does, minus game-state writes: Anchor → body Static on contact; Vine → `FixedJoint2D` welds + `GrowFrom`; Bomb → fuse then blast-radius removal (survivors fall by physics); Tremor → radial velocity kicks; Maw → devour on prey contact; Magma → cells replaced by real stone pips that pour into the hollow. Boulder/Feather/Ice need NO shim — mass and friction do it (the Feather demo is an A/B on the same rig: a normal brick tips the cantilever, the feather doesn't — same drop, only the mass differs). Vortex/Locked stay scripted (they teach an INPUT rule). Skins' real cue methods carry the drama plus the game's own FX. |
| `BlockDiscoveryController` | Installed by `GameSystemsInstaller`. Watches `GameEvents.BlockSpawned` (+ `Spawner.ApplyVariantToNextBlock`'s direct-apply notify) for never-seen demo-worthy variants, waits until the piece is in view (viewport y ≤ 0.75 **and** ≥0.35s old, else a 2.5s timeout / already-landed), then freezes and presents. Marks discovery **at modal-open** (quit-safe). FIFO queue for multiple debuts; tears down instantly on GameOver. |
| `BlockDebutModal` | The debut card: `GameMenuStyle`-styled panel, rounded RawImage demo, name + description (authored `vaultDescription`, catalog caption fallback), one Continue. Sort 6100 (above the ability offer). |
| `VaultPosterService` | Caches one ~360² RT poster per discovered variant for the Vault grid (BLOCKPREVIEWS' "pre-bake first frames as posters"). See §4 for the timeScale wrinkle. Released in the menu's `TearDownRoot`. |

## 3. The freeze (debut modal) — world-alive, NOT timeScale 0

`PushPause` would freeze the demo itself: every skin animates on **scaled** time (PHYSICS.md).
The debut instead copies the tutorial's freeze: `RequestPhase(GamePhase.Discovery)` (priority
between AbilityChoice and Paused — a system pause still outranks it) + `SetDescentSuspended(true)`
on the debuting piece (it hovers alive behind the dimmed backdrop) + `TouchGestureInput.Suspended`
+ `AllowedGestures = None`. The phase alone holds spawning, freezes the timed-goal clock
(`TickTimedGoal` gates on `Playing`) and defers pending ability offers. Continue restores all of
it; the piece resumes falling instantly.

## 4. The timeScale wrinkle (menu surfaces)

The main menu idles at `Time.timeScale = 0`, and skins animate on scaled time — so:
- the **Vault brick detail modal** sets `timeScale = 1` for its lifetime (safe: the menu only
  exists while level selection is pending and covers the whole screen) and restores 0 on close;
- **posters** are captured by `VaultPosterService` inside the same short scaled-time window
  (~0.65s warm-up so grow-in looks — Vine, the Maw's waking grin — settle before the frame is
  detached and the diorama destroyed).

## 5. Adding a demo for a new brick (the recipe)

A demo is a **template + one drop**: an exact starting structure, the new brick released over
it, real physics resolving the consequences. Until the demo exists the brick simply debuts
silently (first spawn marks it discovered + Vault-unlocked, no modal) — so ship the brick first,
author the demo when ready.

1. **Write the scenario** in `BlockDemoScenarios` (copy the closest existing one):
   ```csharp
   public static IEnumerator MyBrick(BlockDemoStage stage)
   {
       stage.SetView(3.2f, 2.1f);                                    // frame the action
       stage.SpawnPhysical("O", null, new Vector2(-0.5f, 0.5f), asleep: true); // the template
       yield return stage.Settle(0.5f);                              // settles BEHIND the curtain
       yield return stage.Reveal();                                  // video opens on a calm scene
       yield return stage.Hold(0.3f);
       GameObject piece = DropIn(stage, "T", stage.Variant, new Vector2(0.5f, 5.8f));
       MyBrickSkin skin = Dress<MyBrickSkin>(piece); skin.Apply();   // the real look
       BlockDemoPuppet.Relayer(piece);                               // after ANY skin attach
       yield return stage.WaitForLand(piece);
       // ... the variant's MOMENT (see the shim menu below), then let physics finish:
       yield return stage.Hold(2.0f);
   }
   ```
   **Grid discipline:** columns are integers (a cell = `[n, n+1]`, centre `n+0.5`); the pivot
   cheat-sheet is in the file header. Structures on exact columns; gaps are exact column counts;
   drops on columns. Off-grid = the misaligned-pocket / mid-air-clip bugs, every time.
   **Shim menu** (each mirrors the real behaviour, no game-state writes): `FreezeSquare(piece)`
   (Anchor/settled Maw), `FixedJoint2D` + `GrowFrom` per `PiecesNear(...)` neighbour (Vine),
   fuse loop + `Shatter` victims in `PiecesNear` radius (Bomb), radial `linearVelocity` kicks
   with height shear (Tremor), `SnapToGrid` + replace with `SpawnPhysical("Pip", ...)` stones
   (Magma), `Shatter` the prey on contact (Maw). Boulder/Feather/Ice need NO shim — the asset's
   mass/friction does everything. Control-rule bricks (Vortex/Locked) use kinematic `Spawn` +
   scripted steering instead.
2. **Register it** in `BlockDemoCatalog` with a one-line fallback caption (this gates the debut
   modal).
3. **Poster**: automatic. The pose is always a resting T and the skin attaches by the
   `<Name>BlockSkin` naming convention — a new brick's poster needs zero code. Touch
   `PosterPose` only for an extra cue (the Maw poses with its grin awake).
4. **Copy**: author `behaviourSummary` + `vaultDescription` on the variant's `.asset`.
5. **Verify**: play mode → `BlockDemoStage.Open(variant, chapter, 700, 700)` via MCP
   `execute_code` with a debug `RawImage`, watch two loops; or unlock it and open the Vault
   detail. Templates are data — expect one or two physics-informed nudges (the simulation is
   honest about marginal balances; give static margins ≥ 0.5 columns).

*Update this file when the demo system or a surface changes.*
