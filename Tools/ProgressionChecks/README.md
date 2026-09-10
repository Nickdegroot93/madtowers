# Progression and first-clear checks

`ProgressionChecks.cs.txt` is an Editor-only batch fixture. Copy it to
`Assets/SourceFiles/Scripts/Editor/ProgressionChecks.cs`, then run Unity with this
project and `-batchmode -nographics -executeMethod ProgressionChecks.Run -quit`.
Remove the temporary `.cs` and generated `.meta` afterward.

The fixture swaps in temporary in-memory progress and disables the online layer,
restoring both afterward. It does not save player progress, start runs, call the
backend, or purchase/refill anything. Run outside Play Mode.

Coverage: every authored chapter's fresh-save locks, sequential level gates,
Chapter 3 developer-letter timing and one-shot behavior, below-bronze/replay
results, first-clear bronze/silver/gold results, actual result-button construction
and styling, the tutorial's single menu exit, chapter-boundary/finale exits, and
secondary-retry availability with an empty-attempt meter, old unlock-all saves,
results retirement, and late attempt updates without reopening/rebuilding a card.

2026-09-09: 143 assertions passed in Unity 6000.4.10f1. Editor and Android C#
compilation also passed. Scene reloads, server run grants, and the animated menu
transition still need an on-device playthrough; this fixture does not simulate them.

## HUD objectives

`HudObjectiveChecks.cs.txt` uses the same temporary Editor-script workflow, with
`-executeMethod HudObjectiveChecks.Run`. It builds the actual objective readout
against in-memory level/progress/wave fixtures and restores the original state.

2026-09-09: 69 assertions passed for waves remaining (including singular/plural),
bronze/silver/gold handoffs, immediate post-gold wave numbering, saved-medal replays,
endless puzzles, block/height countdowns and live totals, and text width at the
1080 reference layout. No gameplay rules, progress saves, or backend calls are exercised.
