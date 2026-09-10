# First-launch tutorial checks

Copy `TutorialChecks.cs.txt` to `Assets/SourceFiles/Scripts/Editor/TutorialChecks.cs`,
refresh Unity, then invoke `TutorialChecks.Run()` through the Editor MCP execute-code
command (outside Play Mode). Remove the temporary `.cs` and `.meta` afterward.

The fixture replaces in-memory progress and manager references, disables the online
layer and audio-pool creation, then restores everything in `finally`. It does not
reset or write the real progress file. Completion scenarios pre-mark the temporary
progress as learned so the existing persistence function performs no disk write.

Coverage includes independent camera/welcome spawn holds, both welcome choices,
no-pan scenes with an existing piece, controls during gentle arrival, success timing,
soft-drop release, the general and tutorial hover watchdogs, replacement bricks after
slams, optional nudge, teardown ownership, existing saves, 24/25/collapse qualification,
live practice counts and six responsive TMP layouts with safe-area insets.

`TutorialChecks.RenderReview()` exports welcome and controls PNGs under ignored
`ArtReviews/SurfaceRestyle/TutorialWelcome/`. These render the actual runtime UI over
the existing Jungle menu still; they are composition previews, not captured gameplay.
Physical-device touch comfort and a complete first-run playthrough remain manual checks.

2026-09-10: Unity 6000.4.10f1 compiled the implementation. **199 tutorial assertions**
passed, alongside **143 progression** and **69 HUD objective** assertions. Both UI
previews were rendered and inspected. Temporary Editor fixtures were removed.
