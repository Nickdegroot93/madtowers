# Menu and overlay presentation

September 2026: extend the approved open gameplay HUD across the surrounding UI.
The chapter-selection screen follows the original concept artwork: translucent themed
cards, a glowing current-level frame and rail, and a polished Play medallion. Other
overlays retain the open HUD styling and Manrope typography. Gameplay,
progression, economy, purchase/ad delivery, and the in-game HUD layout remain owned by
the existing systems.

## Design and research

The chapter artwork provides the atmosphere. Controls should make it easier to read
and navigate, not compete with it. Manrope Medium / Semibold is shared with the HUD;
Archivo remains available to the explicitly styled ability-card artwork.

Research informed the hierarchy and checks, rather than a claim of proven conversion:

- [Apple tab bars](https://developer.apple.com/design/human-interface-guidelines/tab-bars):
  stable destinations with descriptive labels. Keep the familiar five destinations,
  with a prominent Play destination. The user’s follow-up favours a raised Play
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
  relief, a framed status bar, inset resource compartments and a raised Play medallion.
  Level cards use full rounded frames; the current unfinished level adds a chapter-colored
  glow with a crisp light edge. Earned medals are standalone cubes with their tier material,
  without a colored circular backing or halo; navigation arrows and locks retain their circles.
- Titles 38–64, primary labels 26–36, captions 18–24 reference units. Body type normally
  23–28. Use real Semibold, not synthetic bold. Reserve sufficient line-box height for
  Manrope's metrics; truncation can hide a whole line when the box is too short.
- All decoration ignores raycasts. Existing button listeners, loading states, gates,
  dismissal routes and async callback lifetime checks remain attached to their owners.

## Surfaces

Play keeps the swipeable chapter and level list, with a compact framed status bar,
outlined diamond level numbers, a luminous rail, and darker previous/next previews.
The chapter title uses responsive 48–84-point Semibold with a tracked chapter eyebrow.
Previous and next chapter cards stay in the bottom left and right corners. The center
navigation button says **PLAY**, uses a triangle icon, and returns to level selection;
starting a level still opens its summary and boost choices. Decorative glow sprites
are cached, ignore raycasts, and the Play glow follows the pager's chapter-color blend.
The list mask leaves room around the active frame, including when the first row is active.
The status bar sits 8 reference units below the device safe area, with no full-width
dark gradient; chapter artwork continues uninterrupted behind the camera cutout.
Its chapter-tinted fill is 38% opaque, with slightly darker translucent resource chips
for contrast. The pager uses the same fill formula so swiping never makes the bar opaque.
Locked chapters stay mysterious and unlock through the existing reveal sequence.
On the first return after completing a chapter, its next-chapter card reveals for about
one second, then the normal pager slide opens that newly unlocked chapter automatically.
Navigating away cancels the automatic advance. Level unlocks within a chapter keep the
current page, so finishing the introduction reveals Canopy Trial in Chapter 1.

Fresh installations start the introduction directly, before any menu or splash. Its
25-standing-block hold-steady win shows a gold **TUTORIAL COMPLETE** card with one highlighted
**Back to Menu** action. Existing saves retain normal menu entry. Introduction cards say
**TUTORIAL** and use a single goal/completion check; their level sheet has a full-width
Play action without supplies or Ranks. See TUTORIAL.md for first-launch state handling.

Profile keeps identity, earned trophies, Unlimited, and the online-play message. The
Unlimited symbol describes the purchase without a decorative coin pile. Chapters
form a vertical journey on a quiet atlas background: isolated chapter landmarks,
a connected route, cleared markers, earned medal strips and one highlighted current
destination. Only unlocked chapters appear; an unnumbered continuation teaser keeps
future destinations undisclosed. Opening the page scrolls near the current chapter.

Vault shares the quiet gallery background and typography. Underlined Bricks / Abilities tabs
keep discovery counts in the header. Bricks use open 360-unit rows with studio posters capped
at 300 units, sentence-case names, summaries, and a full-row detail action. Abilities use two
columns of 400-unit tiles with larger icons, plain type labels and restrained rarity accents.
Unseen bricks remain hidden behind one continuation message; unseen abilities retain silhouettes
and cannot open. New finds have a small text accent until inspected. Detail sheets use the actual
looping demonstrations or ability icons, a short entrance, content-sized descriptions, and chapter
corner ornaments; brick statistics sit beneath quiet rules. No extra image downloads or live
collection cameras are introduced. Preview workflow: Tools/VaultReview/.

Settings replaces the narrow side rail with six categories in a compact three-column,
two-row selector. The active category has a pale fill and dark label. The full-width
body scrolls above its footer; every control and persistence callback is retained.

Chapter-specific isolated corner decorations frame modal edges, with no repeated chapter
image stamped over the Home background or modal headers. The menu passes the chapter
being viewed; in-game sheets use the active run. Decorations remain behind content,
ignore layout and cannot intercept taps. Art sources/import settings: Tools/ChapterArt/.

The level sheet keeps artwork, challenge, progress ladder, instructions, supplies,
and Play/Ranks in reading order. Run-life pips use the HUD heart masks; attempts remain
summit flags because they are a different resource. Play and the boost tray share the
same sheet height, including the room reserved for larger main actions.

The developer letter keeps Nick's copy and live store price verbatim. Its left-aligned
heading, small chapter mark, measured body, and one Keep playing action form a personal
letter. It still dismisses outside, via its button, and on Android Back.

Pause is an open composition over the existing frozen blurred shroud. Confirmations
and the out-of-attempts explanation use a sheet. Resume removes the shroud immediately,
then keeps physics, timers and controls paused for 0.5 seconds before releasing its pause
ownership. Touch/mouse gestures are discarded while paused, so Resume cannot rotate the
brick or carry a held drag into play. An app interruption during that gap cancels the
resume and reopens the pause menu. Other pause owners remain respected.
Results retain the medal landing,
light sweep, count-up, new-best rule, tier-only celebration, and fast-forward. The tier
caption is open type rather than a gradient capsule. See JUICE.md §2c.

Resume follow-up validation: Unity compiled and 17 focused runtime assertions passed,
including paused mouse input, held-touch cleanup, fresh taps after resuming, duplicate
Resume clicks, cancellation and overlapping pause owners. The measured delay was 0.519 s in the integrated rotation-fix rerun.
Temporary fixtures were removed and Edit Mode restored; evidence is in ignored
`ArtReviews/SurfaceRestyle/PauseResume/`.

## Implementation and review

First-clear game-over actions (2026-09-09): a newly earned bronze leads with
**Back to Main Menu**, with **Try Again** secondary, and names the unlocked level
or chapter. This also applies if that first clear reaches silver or gold. The player
chooses boosts and lives in the next level's menu sheet; results never launch the next
level directly. Chapter boundaries still reveal the next chapter and slide there
automatically on returning to the menu. Tutorial completion has only **Back to Main
Menu**, so a new player visits the menu before continuing. A loss before bronze, or a replay of an already-cleared level,
keeps **Try Again** as primary (with the existing attempt-refill actions when empty).
First-clear menu exits never require attempts; the secondary retry is disabled while
attempts are empty and becomes available in place when an attempt arrives. Results
retire immediately on exit, so another pointer or late refill cannot act on the old
card. Old unlock-all saves never advertise already-cleared content as newly unlocked,
and the campaign-complete message requires every campaign chapter to be cleared.

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
