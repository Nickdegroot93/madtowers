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

## 2b. Speed escalation per rung (Nick 2026-08-30)

On levels where the fall-speed ramp IS the difficulty — pure **BLOCK COUNT** (incl. timed;
`WinCondition.SpeedCapChasesTiers`) — the speed CAP scales with the **armed** rung:
×1.0 bronze, ×1.15 silver, ×1.30 gold (`LevelRuntimeController.TierSpeedCapStep`). The
authored caps land right at the bronze target, so silver/gold otherwise played at bronze
speed forever. Only the CEILING moves: the per-block ramp climbs into it at its authored
slope (`DifficultyController.SetCapScale` never touches the current speed), so earning a
rung never jolts the falling piece, and a replay chasing silver/gold ramps toward its own
cap from the first brick. Levels whose game type is claimed by a modifier (Void Zones,
Airtight, The Flood, Puzzle Waves) and height goals are untouched — they carry their own
difficulty. Keep Playing after gold holds the gold cap.

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
- Lower rungs celebrate in-run (MedalHud's debut fly-in + `ui-star-earned`, §10 — no text
  toast); only the top rung shows the victory card. A loss card after any rung earned
  this run gets the full celebration treatment (badge + chip + confetti, victory sting) —
  a collapse after earning a medal is a completion with a bruise, never a failure screen.

## 6. Adding a tier (platinum)

Start at `LevelTiers.MaxTier` / `TierCount` — every loop and terminal check derives from
them. The full list:

1. `MedalTier`: add the enum entry (ordered).
2. `LevelTiers`: bump `MaxTier`; give `Threshold` the new rung's rule (multiplier or
   wave step) and, if formula-driven, a `LevelDefinition` override field.
3. `MedalStyle`: color + display name (the switches are exhaustive on purpose — a
   missing entry logs an error and renders gold).
4. Re-check the fixed-size medal layouts: the results-card medal row
   (`RunResultsScreen.BuildMedalRow`, 110px cells) needs a width pass for four columns.
   The level-summary progress track (2026-08-29 redesign) needs nothing — stops derive
   their positions from threshold/top-rung, so a fourth cube lays itself out.

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

Two persistent surfaces keep the chase distinct from what this run has banked:

- **Earned-this-run row** (`MedalHud`): open text and the rendered tier cube below the
  lives group. Same stretched bounds and 52-unit row height as `CoinHud`. It starts
  hidden even on replays and appears only when `TierEarned` fires. A wave countdown or
  timed-goal clock takes the first row; the medal then occupies the second row.
- **Objective tier cue** (`UIManager`): a small tier cube beside the objective caption
  names the next unearned rung. The leading icon now identifies the challenge (blocks,
  height/Flood, Puzzle, Airtight or Void). Block and height goals display **remaining to
  the next unearned tier**, clamped at zero, rolling forward only after that rung banks.
  Height remaining is rounded upward from the exact threshold. A collapse increases
  remaining. After gold, and in Endless, the readout shows the live total. Puzzle shows
  its current wave, with the existing block debt in the separate NEXT WAVE row.
- The new HUD uses `HudVisualStyle` chapter ink and Manrope; the medal icons keep
  `MedalStyle` art/tints. No gold tint on ordinary captions. See [HUD.md](HUD.md).

**Medal art landed 2026-08-29**: Nick's rendered block icons live at
`Assets/Resources/Menu/medal_{bronze,silver,gold}.png` (256px, downscaled from the 2048px
renders — keep PNG, the transparency is load-bearing). `MedalStyle.Sprite` serves them on
every surface (level cards, summary modal, results card, in-run pill, objective badge);
the procedural circle badge survives only as the fallback for a tier whose render hasn't
landed. One art per tier: EARNED state is a tint — pair every `Sprite()` call with
`MedalStyle.IconTint(earned)` on the Image (unearned = dark ghost).

## 9. Celebration cards & modal restyle (updated 2026-09-06)

The results-card redesign (Nick's mockup + `unity_tier_modal_handoff.md`, adapted to the
runtime-UI architecture — the handoff's camera-space canvas / Shuriken / DOTween /
ScriptableObject configs were all replaced with our own idioms):

- **Both tier-celebration cards** (gold mid-run victory AND bronze/silver newly earned on
  death) share one treatment: the tier cube badge half-in/half-out over the card's top
  edge (210-unit cube drops, compresses on impact, then catches a single light sweep —
  NO header outside the card, Nick cut it: badge +
  chip carry the story), a "{TIER} TIER REACHED" gradient chip as the card's first row,
  the hero number in a
  cream→tier vertex gradient, and `ResultsCelebrationFx` behind the card: a slowly
  rotating per-tier ray fan + a 40-piece confetti burst of tumbling UI-Image paper.
  Both start at the medal impact; the rays fade away within three seconds of impact. New-best-only
  cards use a chapter-accent pill and cream hero, without confetti, rays or gold chrome.
  Everything runs on UNSCALED time (the victory card opens under timeScale 0 — a
  ParticleSystem would freeze; UI Images on the card's own overlay canvas, the CoinHud
  flight precedent). Chip capsule + ray sprite are procedural (`MedalStyle.ChipSprite`,
  `MedalStyle.RayBurstSprite`), tier gradients are code-owned data on `MedalStyle`.
- The three-slot ladder row now shows only on the PLAIN game-over card (motivation to
  retry); celebration cards tell the story with badge + chip alone.
- **Game-over celebration rule (Nick 2026-08-29)**: ANY rung newly earned this run puts
  the badge treatment on the game-over card - including a topple after the gold victory
  card already showed (only its coin line stays 0: those coins were already advertised
  and banked). A run that earns nothing new but sets a record gets the NEW BEST pill;
  a run with neither is the only plain card. One hold that finishes multiple
  clamped-equal rungs announces the HIGHEST one (a golding run must never show a silver
  pill).
- **All modals borderless + opaque NEUTRAL near-black `#0E0E10`** (`GameMenuStyle.
  PanelColor`, StylePanel no longer adds an outline; the mockup's `#121016` was rejected —
  its purple cast read as "a weird color" at full opacity): results card, pause-menu
  sheets, block-debut modal, level
  summary, boost picker, leaderboard, identity/sign-in, vault detail ×2, dev letter,
  refill + notification offers. The results NEW BEST chip is also borderless;
  the Settings frosted side panel and inline message panels are screen layout, not modals.
- **Reading order and responsive framing:** 80% safe-area width, capped at 860 reference
  units; weighted sheet arrival, followed by headline, counting hero, record, details,
  coins and equal-height actions. Full choreography is in JUICE.md §2c. Tap-to-fast-forward
  still settles the entire card and enables actions immediately. Retry still calls
  `RestartGame`; ad/regen rebuilds retain `muted: true`. Medal derivation, first-attempt
  record exclusion, coin-line suppression and sting selection are unchanged.

## 9b. Menu surfaces: trophy row + chapter medal strip (as-built 2026-09-04)

- **Profile identity card → trophy row** (`MainMenuRuntime.BuildTrophyRow`): hairline, then
  four cells — CLEARED (green check + count) and one per tier (Nick's cube + count of levels
  whose HIGHEST tier is that one; buckets sum to cleared). Zero counts ghost the cube and mute
  the number. PSN-trophy-row precedent: identity, not a stats dashboard — no new card, the
  Unlimited pitch keeps the shine. Found-counts only, never "of N" (ambiguity rule).
- **Chapter cards → medal strip** (`BuildChapterMedalStrip`): the capsule bar became one cube
  per level, tinted by its highest medal (ghost = uncleared, green check = cleared Endless).
  Cubes shrink to fit 330 u for long chapters. "x / y LEVELS" text kept.
- Both derive at read time via `LevelTiers.HighestEarned` (no persisted counts); the menu
  rebuilds on every return from a run, so no live refresh hook.
- Not done (optional, Nick to decide): gold chapter badge when every level in it is gold.

## 10. TODO — remaining framing pass

- The hold-steady overlay and abort banner now share the open HUD's Manrope and chapter
  ink (September 2026). HOLD STEADY sits above the tier cube with thin draining lines,
  and a restrained large digit below. No panel, gradient wordmark or shadow twin.
- `MedalHud`'s existing debut starts at the countdown composition's safe-area-aware
  origin, settles, holds, then flies into its live corner slot. The enlarged row is the
  same icon/text composition as the destination, so the handoff remains seamless.
  No mid-run confetti or extra toast; banking and sting rules are unchanged.
- **Pause-menu quit relabels to "Finish Run"** (implemented 2026-09-06) once any rung is earned this run
  (psych review: quitting at a medal must feel like choosing to stop winning).
