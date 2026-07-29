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
| `BlockDemoScenarios` | One demo per variant, built as a **TEMPLATE + REAL PHYSICS** (Nick's rule): an exact starting structure of physical puppets (spawned asleep at their poses), ONE dropped variant piece, and the simulation plays out the consequences — weight, balance, sliding, toppling are genuine Box2D, never animated. Only the variant MOMENT is a small shim doing what the real behaviour does, minus game-state writes: Anchor → body Static on contact; Vine → `FixedJoint2D` welds + `GrowFrom`; Bomb → fuse then blast-radius removal (survivors fall by physics); Tremor → radial velocity kicks; Maw → devour on prey contact; Magma → cells replaced by real stone pips that pour into the hollow. Boulder/Feather/Ice need NO shim — mass and friction do it — and Pyramid needs none at all (its slope IS the behaviour: an O tips off the peak, a bridged I see-saws away) (the Feather demo is an A/B on the same rig: a normal brick tips the cantilever, the feather doesn't — same drop, only the mass differs). Vortex/Locked stay scripted (they teach an INPUT rule). Skins' real cue methods carry the drama plus the game's own FX. |
| `BlockDiscoveryController` | Installed by `GameSystemsInstaller`. Watches `GameEvents.BlockSpawned` (+ `Spawner.ApplyVariantToNextBlock`'s direct-apply notify) for never-seen demo-worthy variants, waits until the piece is in view (viewport y ≤ 0.75 **and** ≥0.35s old, else a 2.5s timeout / already-landed), then freezes and presents. Marks discovery **at modal-open** (quit-safe). FIFO queue for multiple debuts; tears down instantly on GameOver. |
| `BlockDebutModal` | The debut card: `GameMenuStyle`-styled panel, rounded RawImage demo, name + description (authored `vaultDescription`, catalog caption fallback), one Continue. Sort 6100 (above the ability offer). |
| `VaultPosterService` | Supplies the Vault grid's posters, **baked first**: a committed `Resources/VaultPosters/poster_<id>.png` (see §4a) assigned in the same frame the card is built. Falls back to the old live capture when a poster is missing — one ~360² RT per variant, all stages opened at once sharing ONE warm-up window (slots keep the physics apart). Live RTs are released in the menu's `TearDownRoot`; baked textures are assets and are only forgotten there, never destroyed. |
| `VaultPosterBaker` | Editor-only, **Play mode required**: renders every variant's pose once and writes the committed PNGs the service loads (§4a). Re-run it after skin/shader/pose changes or a new variant; it reports posters that match no current variant instead of deleting them. |

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
- **posters** no longer pay this at runtime at all — they are **baked** (below). The live capture
  survives only as a fallback, and it still needs the same short scaled-time window (~0.65s
  warm-up so grow-in looks — Vine, the Maw's waking grin — settle before the frame is detached
  and the diorama destroyed).

### 4a. Vault posters are BAKED (2026-07-29)

`Tools ▸ MadTowers ▸ Bake Vault Posters` (`VaultPosterBaker`, editor-only) renders every variant's
pose once and commits a PNG to `Assets/Resources/VaultPosters/poster_<id>.png`. `VaultPosterService`
loads those, so the Vault grid fills **in the frame it is built** — no cameras, no render textures,
no timeScale flip. **Requires Play mode**: the poses must animate before capture (the Maw's grin,
Vine's grow-in, the time-driven shaders) and `Update` does not tick in edit mode — the tool says so
if you forget.

**Why it exists.** The live path rendered one diorama *per discovered variant* — on a full save ~14
cameras and ~7 MB of render textures built at once, gated behind a hard warm-up, all popping in the
same frame, and re-paid after every run because the cache is released with the menu. It read as a
broken image, not as loading (Nick, 2026-07-29: *"it takes quite a while… it seems like it's
generated on the spot"*). The `timeScale = 1` warm-up was a second hazard: any scaled-time menu
animation mid-flight (unlock reveals, the chapter cross-fade) lurched forward during the window.

**Re-run the tool** when a variant's skin, shader or `PosterPose` changes, or when a variant is
added — the same discipline the ability icons have (ICONS.md). Forgetting is not fatal: a variant
with no baked poster falls back to the live render and the editor logs which ids are missing.
Baked posters are project assets, so `ReleaseAll` must never destroy them — it only forgets them.

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
   **Real-board staging (Nick, July 2026):** props and weights are REAL piece shapes (O, I, T,
   L, Domino, ...) — never a stack of Pips (1×1s are special blocks and read wrong). Frame wide,
   like a real board: defaults are ortho 3.4 / centre-y 2.4, scenarios run ~3.8–4.6. Compose
   the stack deliberately (a flush O, an I laid as a bridge) rather than piling random shapes —
   and NO decorative clutter: every piece on stage must serve the story (Boulder's cantilever,
   Vine's gap). Nick cut the Sandstone demo's flanking scenery: "the tower IS the story".
   **Shim menu** (each mirrors the real behaviour, no game-state writes): `FreezeSquare(piece)`
   (Anchor/settled Maw), `FixedJoint2D` + `GrowFrom` per `PiecesNear(...)` neighbour (Vine),
   fuse loop + `Shatter` victims in `PiecesNear` radius (Bomb), radial `linearVelocity` kicks
   with height shear (Tremor), `SnapToGrid` + replace with `SpawnPhysical("Pip", ...)` stones
   (Magma), `Shatter` the prey on contact (Maw). Boulder/Feather/Ice need NO shim — the asset's
   mass/friction does everything. Control-rule bricks (Vortex/Locked) use kinematic `Spawn` +
   scripted steering instead.
2. **Register it** in `BlockDemoCatalog` with a one-line fallback caption (this gates the debut
   modal).
3. **Poster**: automatic, then BAKE it. The pose is always a resting T and the skin attaches by
   the `<Name>BlockSkin` naming convention — a new brick's poster needs zero code. Touch
   `PosterPose` only for an extra cue (the Maw poses with its grin awake). Then run
   `Tools ▸ MadTowers ▸ Bake Vault Posters` in Play mode (§4a) and commit the PNG, or that one
   brick makes the Vault slow again.
4. **Copy**: author `behaviourSummary` + `vaultDescription` on the variant's `.asset`.
5. **Verify**: play mode → `BlockDemoStage.Open(variant, chapter, 700, 700)` via MCP
   `execute_code` with a debug `RawImage`, watch two loops; or unlock it and open the Vault
   detail. Templates are data — expect one or two physics-informed nudges (the simulation is
   honest about marginal balances; give static margins ≥ 0.5 columns).

*Update this file when the demo system or a surface changes.*
