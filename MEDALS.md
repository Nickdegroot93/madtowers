# MEDALS — per-level bronze/silver/gold tiers (BINDING)

Approved 2026-08-28 (design), hardened 2026-08-29 (post-review). This document is the
contract for medal-ladder work, like PHYSICS.md is for physics.

## 1. The model

- **Bronze IS completion.** The bronze threshold is exactly the authored `targetValue`;
  earning it does everything the old single-target win did: unlocks the next level, pays
  the win bonus, raises `LevelCompleted`. The medal system never changed completion
  semantics.
- **Tiers DERIVE, never persist.** `LevelTiers.IsEarned` derives every tier at read time
  from the level's stored `bestVerifiedValue` (the highest value that ever survived a
  hold-steady verification, in target units) against the CURRENT thresholds. No tier
  booleans anywhere — lowering a threshold later retroactively upgrades every player
  already above it, with zero migration. Legacy rule: a level completed before medals
  existed reads as bronze (`completion IS bronze`), bestVerifiedValue 0.
- **Thresholds**: silver = ⌈bronze × 1.25⌉, gold = ⌈bronze × 1.6⌉, per-level overridable
  (`silverTargetOverride` / `goldTargetOverride`). ClearWaves instead steps whole waves
  (+1 / +2) and ignores the overrides; the wave engine freezes quota/density growth past
  the bronze wave (`WaveSolver` overtime) so those waves stay feasible.
- **Monotone by construction**: `LevelTiers.Threshold` clamps every rung to at least the
  rung below it, so a partial override can never invert the ladder; `LevelDefinition.
  OnValidate` warns the author when it had to flatten.
- Boosted runs earn medals (board-agnostic). Endless has no ladder.

## 2. The rung-per-hold rule

**Each rung banks exactly its own hold.** The armed tier (lowest unearned,
`LevelRuntimeController._armedTier`) is the only one the 5-second hold-steady verifies —
`IsStillHeld` enforced only that threshold, so a higher threshold the tower happened to
cross at the hold's last frame was NEVER held and must not bank. A tower already above
the next rung's goal re-arms immediately and holds again (5s per rung, worst case).
`ReportVerified` therefore always records exactly the earned threshold. Do not re-add a
"measure what the hold ended at" value (the removed `WinCondition.VerifiedValue`) without
solving the last-frame-spike problem it had.

## 3. Run adjudication (attempts / XP / server)

- **The run's FIRST newly earned rung — ANY tier — adjudicates it as a win**: local bests
  (`ReportResult`), XP (`AwardRunXp(won:true)`), and `RunGate.ReportFinish(won:true)` go
  out at that hold. `finish_run` refunds the attempt PER-RUN (BACKEND.md §6.2), so a
  replay that newly silvers/golds is exactly as free as a first completion (SHOP.md §7
  wins are free). Win XP is bounded: at most `TierCount` won runs per level, ever.
- Later rungs in the same run are **score improvements** (`improve_run_score`), reported
  at run end — never a second finish.
- `RunSuppliesApplier` adjudicates off `GameEvents.TierEarned` (any rung → `NoteWin` +
  local refund), not `LevelCompleted`.
- A replay that reaches the bronze target but earns NO new rung stays a loss (pre-medal
  behaviour, unchanged).

## 4. Coins

- Skill-coin earning stays OPEN through the whole medal chase and closes when the ladder
  completes (`GamePhase.Completed`) — the victory card's Keep Playing earns nothing, as
  post-win always did (JUICE.md economy unchanged in rate).
- The once-per-level win bonus persists the moment bronze completes (crash-safe); skill
  coins bank once at ladder completion / game over / teardown.

## 5. End-of-run cards

- The record comparison uses `_preRunBest`, a field COPY captured at `Start` — the ladder
  banks results MID-run, and a card comparing the run against its own mid-run banked
  score could never say NEW BEST. Never hand the live `LevelBest` to a card.
- Lower rungs celebrate in-run (toast + `ui-star-earned`); only the top rung shows the
  victory card. A loss card after any rung earned this run headlines
  "LEVEL COMPLETE — {TIER}" with the victory sting — a collapse after earning a medal is
  a completion with a bruise, never a failure screen.

## 6. Adding a tier (platinum)

Start at `LevelTiers.MaxTier` / `TierCount` — every loop and terminal check derives from
them. The full list:

1. `MedalTier`: add the enum entry (ordered).
2. `LevelTiers`: bump `MaxTier`; give `Threshold` the new rung's rule (multiplier or
   wave step) and, if formula-driven, a `LevelDefinition` override field.
3. `MedalStyle`: color + display name (the switches are exhaustive on purpose — a
   missing entry logs an error and renders gold).
4. Re-check the two FIXED-HEIGHT medal layouts: the level-summary TARGETS card
   (`MainMenuRuntime.LevelSummary`, modal heights 976/904 = 3-tier delta 136) and the
   results-card medal row (`RunResultsScreen.BuildMedalRow`, 110px cells) — four columns
   need a width/height pass.

Everything else (controller ladder, HUD roll-over, events, persistence, adjudication) is
tier-count agnostic.

## 7. Deferred / known limitations (documented 2026-08-29)

- **ClearWaves medals follow the standing-count contract, not staged waves**: `IsMet` for
  wave N is `placedBlocks >= StandingTargetForWave(N)` (the wave engine's own win
  contract), so a very dense packer can bank the next rung while the staged wave lags. As
  of the rung-per-hold rule this is bounded to one rung per hold; per-level bounds live
  with the §11 BACKEND open item if it ever matters.
- **ClearWaves overtime is time-not-skill**: frozen quota/density means silver/gold waves
  are "same difficulty, longer" (psych-review flag). If playtests call gold boring on
  wave levels, let quota creep slightly in overtime instead of freezing.
- **Crash between bronze and run end loses the run's skill coins** (the win bonus is
  safe). Pre-medal, a crash after the win lost nothing; accepted as minor.
- ~~Medal art is a procedural placeholder~~ — real renders landed 2026-08-29 (§8); the
  circle badge remains only as the missing-art fallback.

## 8. In-run medal HUD & medal art (as-built 2026-08-29)

Two persistent surfaces so the chase always reads (Nick 2026-08-29):

- **Earned-this-run pill** (`MedalHud`, installed by GameSystemsInstaller): top-right
  under the lives card, the CoinHud pill's EXACT size (150×52) mirrored to the right
  edge. Shows only tiers earned THIS RUN (Nick 2026-08-29): a replay chasing silver
  starts with no pill — the objective tier badge already names the chase, and a pill for
  a previous run's bronze would read as this run's trophy. Appears when a rung lands
  (`TierEarned`), settle-pops per rung. On a wave run it sits one row below the
  wave-countdown pill (WaveHud outranks — live survival state owns the corner slot).
- **Objective tier badge** (`UIManager.BuildObjectiveTierIcon`): a small medal at the
  objective card's right edge naming WHICH rung the "/target" denominator belongs to —
  "60/75" alone doesn't say if 75 is silver or gold. Rolls with the denominator on
  `TierEarned`; full tier colour on purpose (it labels the target, earned state is the
  pill's job).

**Medal art landed 2026-08-29**: Nick's rendered block icons live at
`Assets/Resources/Menu/medal_{bronze,silver,gold}.png` (256px, downscaled from the 2048px
renders — keep PNG, the transparency is load-bearing). `MedalStyle.Sprite` serves them on
every surface (level cards, summary modal, results card, in-run pill, objective badge);
the procedural circle badge survives only as the fallback for a tier whose render hasn't
landed. One art per tier: EARNED state is a tint — pair every `Sprite()` call with
`MedalStyle.IconTint(earned)` on the Image (unearned = dark ghost).

## 9. TODO — celebration & framing pass (Nick 2026-08-29, not yet built)

- **Replace the in-game banner look everywhere.** The current win/message strip
  (`LevelRuntimeController.ShowBanner` + the hold-steady "Hold steady!" strip) is a black
  bar with opacity and text — bland. Redesign the whole family: level instruction, "tower
  fell", tier toasts, hold-steady countdown.
- **Bronze/silver dopamine popup**: when a rung lands, a proper dopamine-hitting popup
  with the medal block icon (not just the text toast) — the moment should slam, then
  settle into the persistent pill. Build it around the rendered icons when they land.
- **Pause-menu quit relabels to "Finish Run"** once any rung is earned this run
  (psych review: quitting at a medal must feel like choosing to stop winning).
- ~~Swap `MedalStyle.Sprite` to the rendered block icons~~ — done 2026-08-29 (see §8).
