# Menu and overlay presentation

September 2026: extend the approved open gameplay HUD across the surrounding UI.
This supersedes older presentation prescriptions for glass, gradient buttons, glowing
selection glows and Archivo modal typography. Gameplay,
progression, economy, purchase/ad delivery, and the in-game HUD layout remain owned by
the existing systems.

## Design and research

The chapter artwork provides the atmosphere. Controls should make it easier to read
and navigate, not compete with it. Manrope Medium / Semibold is shared with the HUD;
Archivo remains available to the explicitly styled ability-card artwork.

Research informed the hierarchy and checks, rather than a claim of proven conversion:

- [Apple tab bars](https://developer.apple.com/design/human-interface-guidelines/tab-bars):
  stable destinations with descriptive labels. Keep the familiar five destinations,
  with a prominent Home destination. The user’s follow-up favours a raised Home
  medallion and inset side-tab selections over five visually equal tabs.
- [Android accessibility](https://developer.android.com/guide/topics/ui/accessibility/apps):
  large simple controls, minimum 48 dp targets, 4.5:1 contrast for small text and 3:1 for
  larger text. Unity reference units are not dp; inspect the rendered device geometry.
  Main actions in this pass are 112–128 reference units high, navigation 140. Smaller
  existing utility actions remain and need physical-device accessibility testing.
- [AdMob rewarded-ad guidance](https://support.google.com/admob/answer/7313578):
  disclose the action and reward before opt-in, preserve a clear way to decline, and
  deliver the reward through the existing verified callback. The offer shows Unlimited,
  its localized price and one-time benefits, an explicit ad/+2 attempts alternative,
  daily availability, and free regeneration. No new prompts or reward rules.
- [Monument Valley interface reference](https://interfaceingame.com/games/monument-valley/):
  visual reference for letting the environment lead and keeping labels/actions quiet.
  Its chapter selection is inspiration, not a replacement for this game's campaign rules.

## Shared language

- Opaque neutral near-black modal sheets; pale chapter-tinted primary actions with dark
  type; open secondary actions; hairline separators instead of enclosing borders.
- Small corner radius. Modal actions stay flat; menu chrome uses restrained material
  relief, a framed status bar, inset resource compartments and a raised Home medallion.
  no selection glows on level/chapter rows. Earned medal art retains its tier material.
- Titles 38–64, primary labels 26–36, captions 18–24 reference units. Body type normally
  23–28. Use real Semibold, not synthetic bold. Reserve sufficient line-box height for
  Manrope's metrics; truncation can hide a whole line when the box is too short.
- All decoration ignores raycasts. Existing button listeners, loading states, gates,
  dismissal routes and async callback lifetime checks remain attached to their owners.

## Surfaces

Home keeps the swipeable chapter and level list, with a compact framed status bar,
plain level numbers, restrained current markers, and darker previous/next previews.
The status bar sits 8 reference units below the device safe area, with no full-width
dark gradient; chapter artwork continues uninterrupted behind the camera cutout.
Locked chapters stay mysterious and unlock through the existing reveal sequence.
On the first return after completing a chapter, its next-chapter card reveals for about
one second, then the normal pager slide opens that newly unlocked chapter automatically.
Navigating away cancels the automatic advance. Level unlocks within a chapter keep the
current page, so finishing the introduction reveals Canopy Trial in Chapter 1.

Fresh installations start the introduction directly, before any menu or splash. Its
30-block hold-steady win shows a gold **TUTORIAL COMPLETE** card with one highlighted
**Back to Menu** action. Existing saves retain normal menu entry. Introduction cards say
**TUTORIAL** and use a single goal/completion check; their level sheet has a full-width
Play action without supplies or Ranks. See TUTORIAL.md for first-launch state handling.

Profile keeps identity, earned trophies, Unlimited, and the online-play message. The
Unlimited symbol describes the purchase without a decorative coin pile. Chapters
retain large environment previews and earned medal strips. Vault retains the real
brick posters/ability cards, with the shared type and borderless collection rows.

Settings replaces the narrow side rail with six categories in a compact three-column,
two-row selector. The active category has a pale fill and dark label. The full-width
body scrolls above its footer; every control and persistence callback is retained.

The level sheet keeps artwork, challenge, progress ladder, instructions, supplies,
and Play/Ranks in reading order. Run-life pips use the HUD heart masks; attempts remain
summit flags because they are a different resource. Play and the boost tray share the
same sheet height, including the room reserved for larger main actions.

The developer letter keeps Nick's copy and live store price verbatim. Its left-aligned
heading, small chapter mark, measured body, and one Keep playing action form a personal
letter. It still dismisses outside, via its button, and on Android Back.

Pause is an open composition over the existing frozen blurred shroud. Confirmations
and the out-of-attempts explanation use a sheet. Results retain the medal landing,
light sweep, count-up, new-best rule, tier-only celebration, and fast-forward. The tier
caption is open type rather than a gradient capsule. See JUICE.md §2c.

## Implementation and review

`MainMenuRuntime.Style` supplies menu separators/actions. `RuntimeUiKit` owns the shared
Manrope text. `ModalSafeFrame` fits authored menu sheets within a live safe-area parent,
scaling a separate wrapper so it cannot fight entrance animation. Results/pause retain
`ModalPresentationFx`'s flow layout and safe-area width. `MenuRowsFit` measures the
positioned Settings rows once they have been built for its scroll content extent.

Local ScreenCapture evidence and runtime-check logs live in ignored
`ArtReviews/MenuRestyle/`; they are review artifacts, not shipped assets.

Validation for this pass: all five navigation destinations and all six Settings categories
passed GameView raycast/pointer checks. Settings, the level sheet, and the boost sheet
passed the final text-bound check. Pause opened at time scale zero, restart confirmation
opened/cancelled, and Resume restored time scale one. Muted result fixtures exercised
victory/game-over presentation, fast-forward, and primary callback delivery. Phone captures
cover a compact SE layout and a notched iPhone layout. Refill previews covered available
and unavailable store/ad states using a temporary in-memory fixture, restored immediately.
The saved profile payload was unchanged after testing. Real purchases, ad completion,
account deletion and external authentication were not executed.

Unity compiled successfully. Two existing native memoryless-depth load/store diagnostics
also occur in the previous Editor log; no new C# exception was observed. Device Simulator
and dynamic MCP code use different screen coordinate spaces, so interaction checks use
normal GameView; simulator PNG dimensions are the authoritative layout evidence. Physical
device usability and store sandbox transactions remain separate release checks.


Follow-up: menu chrome retains game-like hierarchy. Home is a raised, chapter-coloured
hexagonal button with a subtle bevel; side destinations use larger icons and an inset
selected plate. The player level sits in a matching badge, beside identity/XP and resource
compartments. Frames are quiet material edges, with no bloom. Modal panels retain their
borderless treatment. All themed chrome blends through the existing chapter swipe.

Chapter-card tint invariant: the Image stays white and Button ColorBlock owns the dark
fill in normal, selected and disabled states. The incoming chapter is temporarily inside
a non-interactable CanvasGroup, so disabled white would flash across that entire page.
Initialize the renderer tint immediately when building level and previous/next cards;
do not animate from Unity's initial white tint. Interaction gates remain unchanged.

Follow-up validation: sampled chapter transitions before/after showed the incoming disabled
cards reaching RGB 1.0 before the correction, and remaining at maximum RGB .065 afterwards
(205 samples, zero white samples). All five navigation destinations and the raised Home
overhang passed raycasts. Cancelling a partial swipe preserved the chapter and removed every
chrome blend twin; selected/disabled level-card fills remained dark. Sakura, Neon and Jungle
captures are in the same ignored review directory. The two existing graphics diagnostics
remain; no new C# errors were observed.

Badge swipe regression: the top status bar owns exactly one level slot and one live level
label. Its decorative BadgeFace is a separate child; only that artwork is cross-faded.
Chrome twins ignore layout and are hidden immediately before deferred destruction. A full
badge clone would duplicate the text and enter the HorizontalLayoutGroup as a second slot,
shifting the entire row. Repeated forward/back transitions, reversal and cancellation were
checked with one label throughout, zero status-slot displacement and no surviving twins.
