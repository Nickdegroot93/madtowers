# Gameplay HUD

The September 2026 HUD follows Nick's open Egyptian reference. No filled top bar,
status cards, bevelled hearts, glow or outline around a panel. Chapter brick art, neutralized and tinted to the HUD ink,
sits inside thin open NEXT brackets, with the label below the preview.

## Typography and colour

`HudVisualStyle` owns Manrope Medium (500) for primary numbers and Semibold (600) for
labels and secondary values. These are static instances of the Google Fonts OFL release,
not the differently licensed Manrope v5 download. Source:
https://github.com/google/fonts/tree/main/ofl/manrope. The license ships beside the two
TTFs in `Resources/Fonts`. TMP creates and caches dynamic SDF font assets at runtime;
no platform-installed font is required. The menu and modal pass now shares Manrope; see MENU.md.

At the existing 1080×1920 reference scale, the main value is 60 units, captions/NEXT
20, secondary values 30 and pause hitbox 72×72. Heights account for Manrope's line
metrics. No automatic shrinking below the font floor.

Each chapter has explicit main/secondary ink and a muted red-family heart colour.
Light skies use dark ink; dark skies use pale ink. Lost City's pale moving planet crosses
a dark sky, so a continuous pale atmospheric fade behind the header maintains contrast.
It has no bounded card edge and accepts no input. Ordinary text never borrows medal gold.

## Readouts

- Blocks: remaining standing blocks to the next unearned rung, caption BLOCKS · {TIER}.
- Height: ceil(exact tier threshold − live height), clamped at zero, caption HEIGHT · {TIER}.
- Puzzle: current wave; NEXT WAVE keeps the actual standing-block debt beneath lives.
  The objective caption reads WAVE · {TIER}.
- After gold, or Endless: live total, caption HEIGHT or BLOCKS. No stale zero countdown.
- The objective caption names the rung being chased: BRONZE, SILVER or GOLD, in the
  same chapter ink as the objective name, separated by a middle dot. No separate tier
  cube. The tier suffix disappears after gold and in Endless. The leading glyph
  identifies the challenge, with one matching family for block count, height, Flood,
  Puzzle, Airtight and Void.
- Coins stay hidden until earned. Banked medals show only tiers earned this run.
  The introduction has one 30-block remaining goal with an unsuffixed BLOCKS caption;
  its gold celebration appears only after the final hold, without intermediate medals.
  Wave/timer occupies the first right-side row; the medal moves below it.
- Hearts retain all three sockets; missing lives are outlined silhouettes. Existing
  gain/loss/shatter events and pause action remain attached to the same owners.

## Nudge corners

The bottom-corner hit areas and guides share 22%-wide, 9%-high screen fractions, also
used by the tutorial hand and Controls editor. Idle guides use pale chapter-tinted ink and the
saved opacity (hidden by default). Both corners smoothly reveal for 1 second on the first
playable brick; each corner tap reveals only that side for 0.5 seconds, even during rebound
cooldown. Pause freezes the reveal clocks. These cues never change the saved setting.
The tutorial temporarily spotlights the corners; see TUTORIAL.md for the continuous flow.

## Preview and motion

The NEXT preview preserves chapter stone detail in a neutral value range, tinted to the
same chapter ink as its label/brackets. Each aspect-preserved image has a centre pivot
and sits vertically centred in its preview slot, including the short I piece. Uneven
transparent sprite bleed is trimmed from preview copies so the visible brick is centred. Generated
preview copies are cached per shape and destroyed with the view. Foresight adds its smaller,
dimmer second preview below the first and extends the same brackets. Overdraw hides the
entire preview and restores the spawner's real queue afterward. No queue or spawn changes.

Hold-steady uses the same chapter ink, Manrope, real tier cube and thin draining lines.
Its compact composition follows safe-area/top-HUD geometry and leaves a 24-unit gap
below the Airtight label even with Foresight active; the banked medal's existing
settle and flight start there too. See JUICE.md §2c and MEDALS.md §8/10 for choreography.
The five-second verification, pause ownership, aborts, rung derivation and sounds stay
with their existing rule owners. The abort banner also uses the new typography.

## Layout and validation

All groups use the existing canvas scale and safe-area helpers (RESPONSIVE.md). The
header tracks the full safe rect and canvas scale; secondary rows follow the same side
anchors. The complete pause hit area remains at least 72 reference units tall.

Review screenshots belong in ignored `ArtReviews/SurfaceRestyle/Hud`, never Resources.
They are real ScreenCapture captures. Some busy-state captures deliberately invoke view
handlers for earned coins/medals/hold without banking synthetic progress; they are UI
review evidence, not screenshots of a completed playthrough.
