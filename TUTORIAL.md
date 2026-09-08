# First-run controls tutorial

`Assets/SourceFiles/Scripts/Levels/Modifiers/TutorialModifier.cs` runs once on
`Level_JD1_TheUndergrowth`, through `Tutorial_GestureBasics.asset`. Completion stays in
`ProgressStore`; Settings → Account → Reset tutorial allows a replay.

## First launch and the introduction level

A fresh installation selects The Undergrowth before the first scene loads and enters
the controls tutorial directly. It skips the menu and launch splash. This is a local,
unranked introduction: no account/network wait, attempt charge, supplies, random special
bricks or ability-choice interruptions. Normal bricks fall at a constant gentle speed.

The level is marked `IsIntroduction`, with one goal: **30 standing blocks**. The HUD shows
blocks remaining without a medal suffix. At 30, the existing **5-second HOLD STEADY**
verification runs; falling below the goal cancels it. Success stops the run and shows
the gold celebration with **TUTORIAL COMPLETE**, the next level's name and one highlighted
**Back to Menu** button. There is no bronze/silver ladder or Keep Playing action.
The normal first-completion award unlocks **Canopy Trial**, whose puzzle waves are unchanged.
Replaying the introduction still ends at 30; the first-completion bonus cannot repeat.

The monotonic `firstLaunchHandled` flag is separate from both learned controls and level
completion. It is saved before direct entry. Existing saves open normally, and leaving an
unfinished first run does not force another automatic launch. Reset tutorial resets the
control tips only. The original level asset ID is preserved for existing progress.

## Continuous practice

The TUTORIAL card, first action and Skip appear immediately in `OnLevelStart`. Tutorial
runs finish the scenery pan immediately and update the camera/spawn point before releasing
its spawn gate. The first brick takes about 0.45 seconds to reach its practice position,
with rotation already available during that arrival. Ordinary runs retain their camera pan.
There are no “Get ready”, “Next lesson”, arrival captions or intermediate screens.

| Order | Prompt | Completion |
|---|---|---|
| 1 | Tap to rotate | One real rotation |
| 2 | Drag left or right | Two successful column steps |
| 3 | Drag down and hold | Engage soft drop, then release it or land |
| 4 | Flick down to slam | One committed hard drop |
| 5 | Tap a bottom corner | One real nudge attempt; tagged TUTORIAL · OPTIONAL NUDGE |

The same brick carries the main controls where possible. Soft drop demonstrates the speed
change, then the brick pauses again on release so the flick can use that same brick.
If a player holds until landing, the next brick continues the current prompt. Replacement
arrivals accept the current cumulative gesture gate. Gestures also count during the short
0.28-second success feedback.

Nudge comes last as an optional extra for normal play. Its helper says: “Nudge adds a
sideways shove. Unlike dragging, it can push other bricks.” The existing physical impulse,
collision rules and rebound cooldown own the effect; the tutorial does not manufacture a shove.
The slam caption stays while its brick falls. The nudge prompt, gesture gate and corner reveal
arrive together on the next controllable brick, without an intermediate instruction screen.
A learned slam used during nudge resumes that prompt on another brick and never re-freezes
the committed one.

Trying nudge hands straight into normal play: “Keep stacking” and the level goal fade while
play continues. There is no blocking recap modal or tutorial-owned spawn hold. Skip uses a
shorter goal handoff. Both mark tutorial completion immediately, rather than waiting for a win.

## Presentation

The tutorial keeps the HUD's Manrope typography, with consistently pale text on a dark
rounded card at 92% opacity. The backing is local to the instructions and does not intercept
gameplay gestures. The action line uses 46–50-unit type; helper text is 36 units, with room
for two lines. The 280-unit card has side padding, a TUTORIAL label, five thin progress marks
and a Skip link with a 72-unit hit area and published gesture-exclusion rectangle.

The composition follows the actual top-HUD bottom, canvas scale and device safe area.
The piece settles below the composition. Its arrival speed is derived from the distance
and uses the existing collision-checked descent path, capped at 30x for this scripted
arrival. Normal ability-owned descent retains its 3x cap, and the scripted pin is released
on practice, any player-initiated drop, completion or teardown.

The existing hand artwork follows the actual piece/corner targets; it hides while the player
is touching and returns after 2.8 seconds of inactivity. During nudge it points downward into
the corner so its palm stays on screen.

## Corner guides

The input zones, gameplay guides, tutorial targets and layout-editor previews all use
`TouchGestureInput.NudgeZoneWidthFraction` and `NudgeZoneHeightFraction`: 22% of screen width
and 9% of height. Height was reduced from 14.4%, making the zones 37.5% shorter while keeping
them attached to the bottom corners.

Idle opacity remains the player's setting, defaulting to zero. `UIManager` adds:

- One smooth 1-second reveal of both corners on the first controllable brick. The camera
  intro, menus and pause do not consume it; a tutorial introduces it with the nudge control.
- A 0.5-second reveal of only the pressed corner, including taps during the rebound cooldown.
- The existing tutorial spotlight while nudge is taught, followed by a quieter guide until
  the final goal reminder fades.

The reveals never write the visibility setting. Pause freezes their clocks. A new run
creates new reveal state, and a saved nonzero opacity remains after each transient ends.
Settings → Controls adjusts idle visibility and explains that corner taps apply force.

## State and recovery

The phases remain `Inactive / PreRoll / Armed / Beat / AwaitPiece / Coda`. `PreRoll` is an
internal positioning phase, never an interstitial screen. Input is live from the first
prompt, including during arrival. Gestures are cumulative: a control already taught stays
available. A real soft-drop release is read from `BlockController.IsFastDropping`, the same
combined touch/keyboard flag the physics movement uses.

The first two queued shapes are chosen from the level's bag with visibly different quarter
turns, using existing variant overrides. NEXT follows the actual queue. A non-rotatable
variant auto-passes rotation. Missing/landed pieces re-arm on replacements; a tall tower
uses the existing relaxed settle line and timeout. There is no timer on player practice.

Skip, completion, game over and level end all restore the input lock, gesture gate,
piece descent/fall-speed ownership and nudge spotlight. The shared run reset remains the
final safety net. The tutorial suppresses the normal goal banner while it owns messaging.

## Research used for this revision

- [Game Accessibility Guidelines: interactive tutorials](https://gameaccessibilityguidelines.com/include-interactive-tutorials/): practise controls in the context where they are used.
- [NN/g: onboarding tutorials and contextual help](https://www.nngroup.com/articles/onboarding-tutorials/): show help alongside the current action, avoid relying on memorised instruction screens, and make help dismissible.
- [Apple: onboarding for games](https://developer.apple.com/app-store/onboarding-for-games/): game-specific onboarding guidance.

These inform the design choices; pacing and touch comfort still benefit from physical-device
playtesting. An authored obstacle demonstrating a nudge collision remains a possible future
exercise, rather than adding another stage to this short control sequence.

## Validation for this revision

Unity compiled the changes. 151 isolated runtime/layout assertions passed, covering
immediate tutorial visibility and input, the reordered controls, soft-drop release and
landing recovery, committed-slam recovery, skip/teardown, nudge feedback, and text bounds.
All five prompts and the actual first level's goal fit at layout sizes corresponding to
320×568, 360×800, 390×844, 521×973 and 768×1024, without shrinking action text below 46 units.

A separate check used a real L-brick prefab, the scene camera and the real fixed-step
collision/descent path: arrival settled in 0.46 seconds, with the tutorial visible and
rotation enabled throughout. A rendered portrait preview was inspected over a flat bright
green review background to check contrast; this is a UI fixture, not recorded gameplay.
Evidence is in ignored `ArtReviews/SurfaceRestyle/Tutorial/`. Temporary fixtures were removed,
Play Mode was stopped, and the saved tutorial-completion flag was preserved. Physical-device
touch comfort and a full first-run playthrough remain manual review items.

Introduction follow-up: Unity compiled, and 48 isolated checks passed for first-launch
routing, old saves, 29/30-block verification and aborts, completion/unlocks, replays,
the gold modal and automatic chapter navigation. The 151 tutorial/layout checks also
passed with the new goal text. Six additional checks cover free retries with an empty
attempt meter and ignoring stale paid supplies. The gold card was rendered for portrait review. These
checks used temporary progress snapshots restored immediately, with cloud-save events
suppressed and the editor's unlock-all override temporarily disabled. Evidence is in
ignored `ArtReviews/SurfaceRestyle/Onboarding/`; the editor override was restored.
