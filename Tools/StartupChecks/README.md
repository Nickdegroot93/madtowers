# Startup, offline progress, and Unlimited checks

Copy `StartupChecks.cs.txt` temporarily to `Assets/SourceFiles/Scripts/Editor/StartupChecks.cs`,
refresh Unity, stop Play Mode, and run `StartupChecks.Run()`. Remove the script and its
`.meta` afterward. Checks use temporary files and in-memory state; they do not write
fabricated progress to a player save, create online accounts, or charge a store.

Coverage: complete startup readiness, three-second grace period, free-player connection
failure, cached Unlimited fallback, one-time reveal, malformed cloud responses, four
completed offline levels in the upload payload, saves made during a merge, atomic local
save replacement, and duplicate purchase callbacks / distinct restore confirmation.

Run the tutorial, progression, and HUD fixtures too. In Play Mode verify the launch art
covers startup, a successful connection reveals a populated menu, and entering the
introduction follows resolution of the gate. Physical-device checks still required:
slow/no connectivity, background/foreground while retrying, reinstall/restore, and real
purchase/sign-in flows once their platform integrations are configured.

Supabase public auth settings checked 2026-09-10: anonymous and email enabled; Apple
and Google disabled. Client Apple/Google methods remain scaffolds. See GOLIVE.md Phase 2.
Cloud sync alone does not recover a lost anonymous account after uninstalling.

Validation on 2026-09-10: 81 startup/save/purchase checks, 207 tutorial assertions,
69 HUD assertions and 143 progression assertions passed. Player-save checksum verified
unchanged by isolated fixtures. Six connection-error/purchase/restore captures at phone
and tablet sizes passed text-fit checks and were visually inspected. The one-off rendering
script and generated images were removed after review; no captures are committed.

Review follow-up adds authenticated-recovery checks, full ownership vs count-only snapshot
races, rejection of structurally valid regressing cloud saves, and completion callbacks when
UI subscribers throw. The combined startup/tutorial/HUD/progression suites pass 500 assertions.

For the opt-in scene-transition test, temporarily compile `StartupPlayChecks.cs.txt`, set
`UnityEditor.SessionState` key `HazardHeights.StartupReview.SavePath` to a unique temporary
`progress.json` path, then enter Play Mode. The fixture disables networking and uses only that
save. Verify the introduction welcome is visible after the splash, stop Play Mode, erase the
SessionState key, remove the fixture and its `.meta`, and refresh. Always hash/back up the real
player save before tests and verify its hash afterward. Never use an actual save path here.

Generated captures have been removed after review. Keep the regression fixtures for future
changes, and retain paid source artwork under the art tooling's raw folder rather than
treating it as disposable review output.
