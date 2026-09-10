# First-launch welcome, controls and practice

`TutorialModifier.cs` and `TutorialModifier.Welcome.cs` run once through
`Tutorial_GestureBasics.asset` on `Level_JD1_TheUndergrowth`. Completed controls make
this modifier inert. Development builds expose Settings → Account → Reset tutorial;
this is a development replay control, not a shipping onboarding requirement.

## Arrival

A fresh installation enters the local, unranked introduction directly, skipping the
menu and launch splash. No account/network wait, attempt charge, supplies, special
bricks or ability choices interrupt it. The scenery's existing camera pan plays in full.

A welcome composition fades over the scenery while the gameplay HUD is hidden:

> YOUR FIRST TOWER
>
> Welcome to Hazard Heights
>
> Every great tower starts with one brick. Let’s learn the controls.
>
> **Let’s build** · Skip tutorial

The welcome waits indefinitely for a choice. It owns a spawn hold independently of
the camera gate, so finishing the pan cannot start gameplay before the player is ready.
An early button press waits for the pan, then the welcome fades out. Gameplay remains
suspended through the button's release so the same touch cannot rotate a brick. A
piece that was already present in a no-pan scene is retained under an explicit hold.
Let’s build starts the controls; Skip tutorial hands directly into practice.

## Guided controls

| Order | Prompt | Completion |
|---|---|---|
| 1 | Drag left or right | Two successful column steps |
| 2 | Tap to rotate | One real rotation |
| 3 | Drag down and hold | Engage soft drop, then release or land |
| 4 | Flick down to slam | One committed hard drop |
| Optional | Tap a bottom corner | One nudge attempt, or **Try later** |

Movement and rotation are available from the first controllable frame. Gates are
cumulative; learned controls stay available. The first two queued shapes visibly
change when rotated, using the existing bag/variant override mechanism so NEXT
matches the actual queue. A non-rotatable replacement auto-passes rotation.

Bricks descend at their gentle normal speed. There is no accelerated arrival and no
short timeout that teleports or rushes a brick into its lesson. As a brick approaches
the practice height, normal descent eases down over 1.5 world units, then pauses.
That height also respects the real tower top and the rotated brick's lower bounds.
The first assisted hold explains: “Take your time — we’ll hold your brick.” Steering
and rotation remain live. Completing a lesson during arrival advances the prompt
without forcing a stop.

Tutorial holds explicitly bypass the general 90-second hover watchdog. Skip,
completion, game over and teardown release them. Other gameplay hover users retain
their existing watchdog. Tutorial descent uses the same swept collision path and
normal speed cap as other controlled descent.

The same brick carries several lessons when possible. Holding down releases the
hover and shows actual fast descent; releasing demonstrates the slowdown before
the next safe hover. Landing early continues on a replacement. A learned soft drop
used during a later lesson regains assisted arrival on release. A committed slam is
never frozen again. Its caption remains until the next controllable brick introduces
nudge and reveals the corner guides. Nudge is explicitly optional via Try later.

Success gets a quiet sound, a light haptic through the existing settings-aware wrapper,
and a 0.55-second beat before the next caption appears. Input remains live during
feedback. A fast player can earn the next action during that beat.

## Practice and completion

The introduction has one goal: **25 standing blocks**. Tutorial placements already
count. The HUD shows the live standing count out of 25 with a TUTORIAL caption while
learning, then PRACTICE when guidance ends. A collapse reduces the numerator.

Learning the controls or choosing Try later shows:

> You’ve got the basics!
>
> Build a tower of 25 standing bricks to get the hang of things.

Skipping uses “Let’s get stacking” with the same goal. The message holds briefly and
fades while play continues; the HUD keeps the practice objective visible. The learned
controls flag is saved at this handoff, independently of finishing the level.

At 25, the existing five-second HOLD STEADY verification runs; dropping below 25
cancels it. Success ends the run, shows the gold TUTORIAL COMPLETE celebration,
names the next challenge and offers the highlighted Back to Main Menu action.
The first completion unlocks Canopy Trial. Replays still finish at 25 and cannot
repeat the first-completion bonus.

The separate `firstLaunchHandled` flag prevents an abandoned opening from forcing
automatic tutorial entry on every launch. Existing saves and the level's asset ID
remain compatible.

## Presentation and layout

The welcome uses the existing Manrope fonts, warm pale ink, a dark rounded panel,
a primary pale button and a quieter skip action. Its width stretches inside the
safe area and its height fits shorter screens. The scenery stays visible beneath
a light full-screen wash. Isolated Jungle foliage straddles the lower corners, half inside and half outside the card and has no repeated chapter-image badge.
The HUD is bound again if it awakens after the modifier. There is no video dependency.

The gameplay tutorial is a compact 248-unit panel under the actual HUD bounds:
46–50-unit action text, 32-unit helper text, four core progress marks, TUTORIAL step
labels and an explicit OPTIONAL label for nudge. Skip/Try later has a 72-unit hit
area and publishes its live gesture-exclusion rectangle. Layout follows canvas
scale, device safe areas and screen changes.

The existing hand artwork follows the brick and real nudge zones. It hides while
the player touches or a success beat runs and returns after 2.8 seconds of inactivity.
Corner zones remain 22% of screen width and 9% of height. Tutorial reveals never
write the user's saved guide-opacity setting.

## Verification

`Tools/TutorialChecks/` holds the repeatable Editor fixture for welcome/spawn ownership,
control progression, soft-drop release, committed-slam recovery, skip/optional exits,
25-block qualification, live practice counters and responsive TMP layout. It swaps
in-memory state and restores it without resetting or writing the player's save.

Unity 6000.4.10f1 passed 199 tutorial assertions, 143 progression assertions and
69 HUD objective assertions on 2026-09-10. Welcome and controls composition previews
were rendered and inspected under `ArtReviews/SurfaceRestyle/TutorialWelcome/`.

Physical-device touch comfort, perceived pacing and first-time comprehension still
need a playthrough with people who have not learned the controls.
