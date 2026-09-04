# JUICE.md — Game Feel & The Coin Economy

Status: **BINDING, as-built (July 2026).** This documents what exists and — just as
important — what was tried and rejected. Read PHYSICS.md §1 before touching anything here;
every effect must respect its invariants.

---

## 1. Principles (non-negotiable, playtest-hardened)

1. **Physical, simulation-grounded feedback fits this game; gamified fanfare does not.**
   A full celebration layer (chimes, pitch ladders, white flashes, hit-stops, punch-zooms,
   shock rings for judged placements) was built, playtested, and rejected wholesale —
   "over the top, didn't fit the game." Never reintroduce it without Nick.
2. **Clean landings are the default, not an achievement** — they get physical feedback only
   (sound/dust/squash/shake/haptic). No coins, no praise.
3. **The earn RATE is scheduled, not geometry-emergent.** Target: a roughly consistent coin
   total per 100 bricks. Geometry-emergent earners (rows, gap fills, pair interlocks) paid
   wildly with tower shape (rows alone: every ~5 bricks on a wide tower) and were cut.
   The golden-brick scheduler is the metronome.
4. **The coin flight IS the celebration.** Coins bursting from the brick and flying to the
   counter, plus one reward sheen, plus ONE soft muted clink per batch. Nothing else fires.
5. **Attribution** — every effect traces 1:1 to a specific player action. The reward sheen
   exists to answer "why did I get gold?" wordlessly.
6. **Physics is sacred** — block-side visuals animate only the collider-less **PieceSkin
   child**, never the rigidbody transform (PHYSICS.md I1). Camera, particles, audio,
   haptics are always safe.

## 2. Tier 0 — every landing (shipped, commit 39b4627)

Fires for ALL landings, scaled by descent speed at touchdown (`ComputeLandingHardness01`:
normal steer ≈ 0, held fast-drop ≈ 0.5, flick = 1), orchestrated by `Core/LandingFx`.
One carve-out (2026-08-22): a landing UNDER the Flood's surface keeps the thud, squash,
trauma and haptic — mass is mass — but drops both DUST layers (the `LandingDustFx` puffs
and the `land_tail` settle sound): kicked-up dust read as smoke under water. Decided once
in `LandingFx.Play`, never per leaf FX.

- Velocity-scaled impact sound (layered `land_*` clips once generated; `impact_heavy`
  fallback until then) — volume and pitch ride hardness.
- `LandingDustFx` — pooled dust puffs squirting from the bottom edge.
- `LandingSquashFx` — squash-&-stretch on the PieceSkin only, bottom edge pinned.
- Trauma camera (`TowerCameraController.AddTrauma`, amplitude = trauma², Perlin offsets +
  slight roll, unscaled-time decay; legacy `Impact(amplitude)` maps to the same peak).
- `Haptics` transient (Android VibrationEffect; iOS no-op until Nice Vibrations import),
  gated by `SettingsService.HapticsEnabled`.

Nudge body language (2026-09-04, `Core/NudgeLungeFx`): a successful corner-tap dash tilts the
PieceSkin ~8° leading-edge-down, lags it a beat behind the column the body already jumped to
(elastic catch-up with ~10% overshoot) and smears 3 fading ghost copies along the jump. Skin
only - the body still moves the exact grid column in one physics step, so the animation can
never change where the piece lands. A landing squash cancels a running lunge (touchdown owns
the skin). Wind streaks (`DashWindFx`) and the `nudge` swoosh are unchanged.

## 2b. The death beat (shipped 2026-09-04)

The results card used to pop the same frame the last life went — the fatal brick was under
the modal before it registered. Now `LevelRuntimeController.HandleGameOver` plays the sting
at the death, starts `DeathBeatFx`, and shows the card after `GameOverCardDelaySeconds`
(0.7 s, any tap after 0.25 s skips). The beat, all on unscaled time:

- **Hit-stop** 0.12 s at timescale 0.02, then **half speed** until the card. `DeathBeatFx`
  owns `Time.timeScale` for the beat with HitStop's discipline (restore only if still ours;
  `HitStop.Cancel()` hands over a running micro-stop). It is the one sanctioned exception to
  GameManager's "1 or 0" clock rule.
- **Camera** eases in 4 % ABOUT the death point (`TowerCameraController.SetDeathFocus`) — the
  fatal brick stays put, the world expands. Focus comes from `LifeLossFx`/`FloodSplashFx`
  via `DeathBeatFx.SuggestFocus` (the GameOver event carries no position); timeouts zoom
  about the centre. Held under the card.
- **Colour** drains to grey and the vignette closes (`PostFxController.SetDrain`), held until
  the next scene load resets it. Post never touches overlay UI, so every root overlay canvas
  under the modal tier (sorting ≤ 6999) fades out with it — otherwise one coloured HUD
  hairline survives in a grey world.
- **Achievement losses stay bright**: a game over that banked a new tier or a new best keeps
  hit-stop, slow beat and push-in but skips the drain + HUD fade (`drain: false`). The sting
  rule is separate: victory sting only for a NEW TIER; a plain new best keeps the game-over
  sting and gets its gold NEW BEST pill on the card.

## 3. The coin economy (shipped)

### Earning — the complete table (`CoinLedger` constants)

| Event | Coins | Rate control |
|---|---|---|
| Perfect stack — same shape, same orientation, exactly on its twin | +5 | skill-rare (~4–8 per 100 bricks) |
| Golden brick landed upright (drift ≤ 0.6 cell, tilt ≤ 8°) | +10 | scheduled |
| Golden brick landed as a perfect stack | +40 (replaces, not adds) | scheduled × skill |
| Level completion (win bonus) | +25 | once per run |

Expected total: **~70–95 coins per 100 bricks**, a tight band by design.

**Cut earners (do not re-add without Nick):** perfect fit (+10), pair interlock (+8),
row complete / multi-row (15+). All were geometry-emergent and violated principle 3.
An even earlier speed-chain reward paid the default way of playing. Git history has the
detection code (PlacementJudge, row walks, rectangle-merge) if ever needed.

### The golden brick (`GoldenBlockDirector`)

- Spawns every **25–40 locked bricks** (uniform roll inside the window): fixed rate,
  unpredictable moment — the variable-ratio element, quarantined to timing.
- Only plain bricks goldify; special variants (behaviour subclasses, custom looks, hazards,
  non-counting) are skipped and the director waits for the next plain spawn. NOTE: ordinary
  spawns carry the base `BlockData` named "Normal", NOT null — test for specialness, not null.
- Look: a **gold overlay renderer** cloning the skin sprite (a tint fails: gold × green art
  stays green) + a gold reward sheen glinting across it every 0.9 s while it falls. A landed
  golden brick keeps its overlay — trophies stay visible in the tower.
- One chance: judged once; toppled = pays nothing; destroyed mid-fall = scheduler re-arms.

### Detection (`PlacementScout`)

Silent. Runs on `BlockLocked` with ComboDetector's revalidate-after-settle pattern
(PHYSICS.md I5): drift/tilt between lock pose and settled pose gate every reward
(stack needs ≤ 0.35 cell / ≤ 3° / column-true; golden needs ≤ 0.6 cell / ≤ 8°).

### Feedback (`RewardSheenFx` + `CoinHud`)

- **Reward sheen**: one soft reflection band (ability-card sheen family) sweeping
  lower-left → upper-right across every brick that earned, clipped to the brick sprites via
  temporary SpriteMasks (custom sorting range so nothing else clips). White for stacks,
  gold for golden. Single-brick sweeps follow the brick (falling golden glints).
- **Coin flight**: 3–7 small coins (menu `coin` art) burst from the brick, hang a beat,
  curve on staggered arcs into the counter pill; the pill (hidden until the first earn,
  under the top bar's left card) ticks up per arrival with a ~10% elastic pulse and ONE
  soft `coin_settle_01` clink per batch (synthesized, deliberately un-chime-like).
- First-earn origin bug to remember: the HUD canvas must be built at scene start — a canvas
  created mid-frame has no layout yet and world→canvas math lands at screen centre.

### Accounting (`CoinLedger`, `PlayerProfileStore`)

- `CoinLedger` (per-run, on the GameManager host): run total banks exactly once — on
  LevelCompleted (+ win bonus), on GameOver, or on teardown (mid-run quit keeps earnings).
- `PlayerProfileStore.Coins` is PlayerPrefs-backed (`profile.coins`), persisted on every
  change; the menu top bar shows the real balance. `RunResult.CoinsEarned` carries the run's
  skill coins (excludes win bonus). Cloud sync is live (BACKEND.md §10.5) — the wallet
counters ride the progress payload.

## 3b. Menu unlock reveals (shipped, July 2026)

The one sanctioned "celebration" outside gameplay: returning to the menu after a FIRST-time
level completion plays the unlock it earned instead of showing it silently pre-unlocked.
Three beats — scroll-into-view + delay (anticipation), lock-badge rattle (strain), flash-covered
swap to the unlocked look with a scale punch + sparkle burst (payoff). The next level card and
the next-chapter card share the sequence; the chapter one is grander (radial preview sweep —
the locked card is a deliberate MYSTERY: no name, no thumbnail). Tap fast-forwards the
anticipation; the payoff always plays. Driver: `MenuUnlockRevealRunner` (all knobs), armed by
`MainMenuRuntime.UnlockReveal.cs`, carried across the scene reload by `UnlockRevealPending`
(PlayerPrefs).

**Sound rule (Nick, playtested): reward reads through WEIGHT, not brightness.** The first
unlock stingers used "rising magical sparkle" prompts and came out chimey — rejected. The
shipped `unlock_*` clips are knock/clunk/grind character (see SOUNDS.md + the prompt-table
lesson in `Tools/generate_elevenlabs_sfx.py`). Don't add chimes/pitch-up arpeggios here.

## 4. Tuning knobs

| Knob | Where | Default |
|---|---|---|
| Earn amounts | `CoinLedger` consts | 5 / 10 / 40 / 25 |
| Golden window | `GoldenBlockDirector` | 25–40 bricks |
| Golden look | `GoldenBlockDirector.GoldTint`, overlay alpha | (1, .84, .35), 0.8 |
| Glint interval | `GoldenBlockDirector` | 0.9 s |
| Sheen speed / brightness / band width | `RewardSheenFx` | 0.4 s / 0.6 / 0.55 u |
| Stack / golden exactness gates | `PlacementScout` | see §3 |
| Coin flight timing, coin count | `CoinHud` | hang 0.26 s, fly 0.38 s, 3–7 coins |
| Clink volume | `CoinHud.Arrive` | 0.22 |
| Landing feel (Tier 0) | `LandingFx`, `LandingDustFx`, `LandingSquashFx`, shake consts in `TowerCameraController` | — |

## 5. Open items

- Landing clips: the ElevenLabs `land_body/transient/tail` set WAS generated 2026-09-04 and
  rejected outright by Nick (as were pack/ElevenLabs coin clinks) - files deleted, nothing wired.
  Next attempt should be RECORDED foley (Sonniss GDC / freesound CC0), not generated.
- Nice Vibrations (open-source) import for iOS haptics.
- End-of-run coin tally + the store that gives coins meaning (goal-gradient display).
- Haptics settings-screen toggle (service key exists: `SettingsService.HapticsEnabled`).

## 6. Pitfalls checklist (before touching this system)

- PHYSICS.md I1–I3: visual-child only; don't keep bodies awake.
- `SettingsService` gates every effect: ScreenShake / VisualEffects / EffectiveSfx / HapticsEnabled.
- Editor testing: gates + pause traps in the play-mode memory; the editor auto-pauses on
  pre-existing missing-script errors — check `EditorApplication.isPaused` when a run freezes.
- Spawner raises `BlockSpawned` for the NEXT piece inside the current piece's `BlockLocked`
  dispatch — scheduler-style systems may see events one piece "late"; design for it.
