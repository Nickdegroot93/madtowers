# TUTORIAL.md — first-run gesture tutorial

How the very first level teaches the controls: the step flow, the gesture gating, how each step
detects that the player did it, where the ghost hand comes from, and how completion is
remembered so it never repeats. **Built** — code in
`Scripts/Levels/Modifiers/TutorialModifier.cs`, attached to `Level_TW1_Foundations` via
`Assets/Data/Modifiers/Tutorial_GestureBasics.asset`.

Built from what already exists: the tutorial is a **`LevelModifier`** (LEVELS.md §1) attached to
one level, remembered in **`ProgressStore`** (DATA.md), drawn on a **`RuntimeUiKit` overlay
canvas** (RESPONSIVE.md). Engine additions: five gesture events on `GameEvents`, a run-local
**gesture gate** on `BlockController`, and small hooks on `Spawner`/`UIManager`.

---

## 1. Principles (from mobile-onboarding research)

- **Teach by doing, never by reading.** One caption per step, ≤ 8 words ("Tap to rotate");
  the looping ghost-hand demo is the real instruction, the text is a caption.
- **One mechanic at a time, forced-order but cumulative.** During each step only the gestures
  already taught (plus the one being taught) work — a stray flick can't dump the piece
  mid-lesson — but a gesture, once rewarded, is never taken away again.
- **The demo plays where the gesture happens — and from the first frame.** Hand positions
  derive live from the actual piece / the real corner nudge zones, not fixed screen spots, and
  the demo (plus the lit nudge pills) starts with the pre-roll itself, tracking the descending
  piece — the caption is never on screen without its demonstration. The demo hides the instant
  a finger goes down and returns after ~2.8 s of idling.
- **Success must register.** Each step ends with a sound (`pop_01`), a ring burst at the piece,
  the step dot filling, and a ~0.7 s beat before the next ask.
- **Make the invisible visible.** The corner nudge pills light up fully while nudge is taught,
  stay faintly lit for the rest of the tutorial, and fade with the coda (hidden controls decay,
  never cut).
- **Never trap the player.** Steps wait indefinitely; an always-tappable **Skip** rides the
  strip's right edge (with a gesture-exclusion rect so tapping it can't also rotate the piece,
  and the safe-area right inset so it never hides under a curved edge). Settings → Account has
  "Reset tutorial" for replays. Self-healing backstops: a piece whose variant refuses rotation
  auto-passes the rotate lesson; a pre-roll that *lands* (tower outgrew the settle line) releases
  the input lock and arms the next piece promptly; a game over mid-lesson tears the tutorial
  down instantly (modifier updates stop on game over); `GameManager.Awake` re-clears the input
  lock / gesture gate / nudge spotlight per run as a final safety net; and `EndModifiers`
  isolates each modifier's teardown so one exception can't rob the tutorial of its restore.
- **End on a quick win.** After the last gesture a short coda ("You're ready!" + the level's own
  goal line) fades out and the level plays on as a normal, easy Place-N free build. **Skip plays
  a shorter coda that still shows the goal** — the runtime's banner was suppressed
  (`LevelModifier.SuppressesGoalBanner`), so the objective must still be handed over.

*(Sources at the bottom.)*

---

## 2. The flow

**Opening.** Nothing shows during the opening camera pan (the pan holds spawning; the overlay is
built lazily on the first spawn). Each teaching piece then **pre-rolls**: it descends briskly
(normal-speed factor ×2.2, so it reads as arriving, not dropping) to a working height with all
input locked (`TouchGestureInput.Suspended` + gesture gate `None`), then hovers
(`SetDescentSuspended`) and the step arms. There is **no short time cap** — arming waits for the
piece to actually clear the instruction strip (a generous 8 s safety cap remains), so the strip
can never cover the piece.

**Layout & juice.** The instruction strip is anchored directly under the *real* HUD bottom
(`UIManager.TryGetTopHudBottomWorldY`, so notches and the NEXT card are accounted for), and the
piece settles ~a quarter-screen below the strip's bottom edge. Strip anatomy: a letterspaced
**TUTORIAL** tag, the caption at the optical centre, a subline (rep counter / coda goal), step
dots at the bottom, an accent hairline along the bottom edge, and a ghost-pill **Skip** riding
the right edge (outside the fading group, always tappable). Motion: the strip slides down out
of the HUD as it fades in (and retreats with the coda), new text pops in with an ease-out-back
overshoot, the current step dot breathes, an earned dot lands with a 2×→1× pop, and a ring
burst fires at the piece on every step completion. A gentle full-screen dim (α 0.24) focuses
attention; everything except Skip sits in one `CanvasGroup` that fades as a block.

Two pieces, five gestures, cumulative gate per step:

| # | Teaches | Caption | Waits for (`PieceGesturePerformed`) | Reps | Gate while armed (cumulative) |
|---|---|---|---|---|---|
| 1 | **Rotate** | "Tap to rotate" | `Rotate` | 1 | Rotate |
| 2 | **Move** | "Drag left or right to move" | `Move` | 3 | + Move |
| 3 | **Soft drop** | "Drag down and hold" | `SoftDrop` (engage edge) | 1 | + SoftDrop → *ends piece 1* |
| 4 | **Nudge** | "Tap a corner to nudge" | `Nudge` | 1 | + Nudge (pills lit) |
| 5 | **Hard drop** | "Flick down to slam!" | `HardDrop` | 1 | + HardDrop = Everything → *ends piece 2* |
| ✓ | **Coda** | "You're ready!" + level goal | — | — | Everything (free build) |

Gestures are credited while a step is armed, during the previous step's 0.7 s success beat (its
gate is already open), and on the still-falling previous piece between lessons — a fast player
is never asked to redo something the game just accepted.

- **Teaching shapes are forced** from the level's own bag (`Spawner.RequeueDefinition` +
  `QueueVariantOverride` with the shape's default data). Candidates must pass
  `IsVisiblyRotatable` — the default variant allows rotation AND the cell layout is not
  4-fold symmetric (a 2×2 square or single Pip rotates invisibly), read from the prefab's
  colliders so renamed/themed content still qualifies; the L → J → T → S → Z → I → Domino name
  list is only a preference order. The NEXT preview follows automatically — the queue *is* the
  preview, and `AnnounceUpcoming` caps the preview at the visible depth so the transiently
  longer queue never reads as a Foresight-style double preview.
- **Drops end pieces on purpose:** soft drop is taught by riding piece 1 to the floor, hard drop
  by piece 2's instant plunge. Because the gate is cumulative, a learned drop used "early"
  (e.g. soft-dropping during the nudge step) just lands the piece — the current step re-arms on
  the next spawn, so nothing can soft-lock.
- **The goal banner is suppressed on tutorial runs** (`LevelRuntimeController` checks
  `LevelModifier.SuppressesGoalBanner` after `StartModifiers`); the coda shows the goal itself.

---

## 3. The step machine

One phase enum drives everything (`Inactive / PreRoll / Armed / Beat / AwaitPiece / Coda`):

```
BlockSpawned            -> BeginPreRoll: lock input, boost descent, caption shows upcoming step
piece clears the strip  -> ArmStep: hover piece, open the step's cumulative gesture gate,
                           demo loops at the live target (hides on touch, returns after idle)
matching gesture event  -> reps++; done -> CompleteStep: pop_01 + ring burst + dot fill,
                           NEXT step's gate opens immediately (no dead window), 0.7s beat
beat over               -> same piece: arm next step   |   piece dropped: AwaitPiece
last step done          -> BeginCoda: MarkTutorialCompleted() IMMEDIATELY, gate = Everything,
                           "You're ready!" + goal line, fade out, Teardown
Skip (any time)         -> MarkTutorialCompleted() + Teardown (no coda)
OnLevelEnd              -> Teardown (restores Suspended / gesture gate / nudge boost)
```

## 4. Engine pieces (all run-local, all restored on teardown)

- **`PieceGestures` gate** (`BlockController.AllowedGestures`, default `Everything`): every
  input path funnels through the gated `BlockController` entry points — touch and mouse via
  `TouchGestureInput` → `StepColumn`/`RotateLeft`/`RotateRight`/`SetFastDrop`/`StartAutoDrop`/
  `Nudge`, keyboard via the gated `_moveInput` axes + FastDrop read in `Update`. Narrowing the
  gate truly disables a control, not just its touch gesture. `SetFastDrop(false)` always passes
  (releasing is never blocked). Reset in `ResetRuntimeState` (per run via `GameManager.Awake`)
  and on tutorial teardown. **System-initiated moves bypass the gate**: the magma melt's
  committed plunge uses `ForceAutoDrop`, which neither checks the gate nor reads as a gesture.
- **One gesture event on `GameEvents`**: `PieceGesturePerformed(BlockController, PieceGestures)`,
  raised once per performed gesture from the gated entry points — including the keyboard/DAS
  move step and the keyboard soft-drop (the soft-drop edge is detected on the *combined*
  keyboard+touch value in `BlockController.Update`, so both report identically). Every real
  corner tap counts as a Nudge, even an out-of-bounds dash that stays silent in gameplay terms.
  The tutorial only credits gestures raised by the piece it is teaching.
- **Control-timer pause while hovering:** `_controlElapsed` (the `maxControlTime` 12 s safety
  lock) does **not** accrue while `_descentSuspended` — a player thinking 30 s on a lesson (or a
  Fission hover) must not have the piece force-lock mid-air. Noted in PHYSICS.md §2.
- **`Spawner.ConfiguredBlockBag`** exposes the level's shape set for the teaching-shape pick.
- **`UIManager.SetNudgeGuideBoost(0..1)`** blends the corner pills toward a clearly visible
  version of themselves regardless of the player's Nudge Guides opacity setting.
- **Skip's gesture-exclusion rect** via `TouchGestureInput.RegisterUiExclusionRect` — the same
  publish-your-rect contract the ability slots use.

## 5. The ghost hand

Procedural, in `RuntimeSprites.Hand()` (fist + index finger + thumb, fingertip at the top),
animated in code per gesture: alternating **tap** with a fingertip ripple (rotate: either side of
the piece; nudge: both real corner zones), a horizontal **swipe** with a leading chevron (move),
a **press-drag-hold** with a throb (soft drop), and a fast ease-in **down-swipe** (hard drop).
The chevron sprite points left: 180° = right, +90° = down. A real hand sprite can swap in behind
the same animation driver.

## 6. Persistence

`ProgressStore.tutorialCompleted` (schema v2): monotonic false→true, so cloud-merging devices is
an OR. Marked the instant the last gesture completes (or on Skip) — never at level win — so
quitting during the coda/free build still never re-shows it. `ResetTutorial()` (Settings →
Account) clears just this flag. Because the flag is checked in `OnLevelStart`, the modifier is
standalone: attach it to any level and it teaches exactly once.

## 7. Open / deferred

- Level goal is Place **100**; the tutorial brief suggested ~50 for a faster first win —
  one-line change on `Level_TW1_Foundations` when tuning.
- Haptics on step success (no haptics helper exists in the project yet).
- A scripted "tight spot" obstacle to *motivate* the nudge, and an adaptive re-hint in early
  levels if the player never nudges (research: one exposure is not enough for hidden controls).

---

## Sources
- [Best practices for mobile game onboarding — Adrian Crook](https://adriancrook.com/best-practices-for-mobile-game-onboarding/)
- [Mobile-app onboarding — Nielsen Norman Group](https://www.nngroup.com/articles/mobile-app-onboarding/)
- [Instructional overlays and coach marks — NN/g](https://www.nngroup.com/articles/mobile-instructional-overlay/)
- [Onboarding for games — Apple Developer](https://developer.apple.com/app-store/onboarding-for-games/)
- [10 tutorial tips from Plants vs Zombies' George Fan — GDC 2012](https://www.gamedeveloper.com/design/gdc-2012-10-tutorial-tips-from-i-plants-vs-zombies-i-creator-george-fan)
- [Clash Royale's sticky FTUE — Matt Le](https://medium.com/@Matthewwspencerr/clash-royale-creating-a-sticky-first-time-user-experience-113e17b18f36)
- [Misused mobile UX patterns (invisible gestures) — Zoltan Kollin](https://medium.com/@kollinz/misused-mobile-ux-patterns-84d2b6930570)
- [Video-game onboarding takeaways — UserGuiding](https://userguiding.com/blog/video-game-onboarding)

*Update when the control scheme changes. Control detection lives in `TouchGestureInput` +
`BlockController`; the flow lives in `TutorialModifier`.*
