# JUICE.md — Game Feel, Placement Rewards & In-Run Coins

Status: **DRAFT — design agreed in principle, not yet implemented.** Once implementation
starts, this document is binding for all game-feel / placement-reward / coin work, same as
PHYSICS.md and BLOCKS.md. Read PHYSICS.md §1 before touching anything here — every effect
in this file must respect its invariants.

---

## 1. Principles (non-negotiable)

1. **Attribution** — every effect traces 1:1 to a specific player action and scales with the
   quality of that action. If a celebration can fire without the player having done something
   readable to cause it, cut it. (CHI 2024 finding: indiscriminate juice destroys the
   competence signal.)
2. **Deterministic core, variable magnitude** — whether feedback fires is deterministic and
   skill-readable. Randomness lives only in celebration *size* (golden blocks, bonus rolls),
   never in whether a good placement is acknowledged.
3. **Tiered ceiling** — feedback intensity is a strict ladder (Tier 0 → 3). A tier may only
   use the effects listed for it. Over-juicing a common event is as bad as silence.
4. **Clean landings are the default, not an achievement** — the baseline placement gets
   *physical* feedback only (sound/dust/haptic). No coins, no chimes, no praise. Coins and
   musical feedback start where skill starts (Tier 1+).
5. **Physics is sacred** — all block-side visual effects animate the **sprite/visual child**,
   never the rigidbody transform (PHYSICS.md I1: "towers shimmer" = someone scaled/nudged a
   landed body). Camera, particles, post-fx, audio, haptics are always safe; block squash,
   flash, shine are visual-child-only.

## 2. PlacementJudge — event detection

New system listening to `GameEvents.BlockLanded` (the same hook `BlockLedger` scores from),
classifying each placement after the settle window (reuse `ComboDetector`'s
revalidate-after-settle pattern so toppled placements never reward).

| Event | Detection rule | Tier |
|---|---|---|
| **Clean landing** | Settled within normal settle time, no knife-edge defer, final overhang < ~0.25 cell. | 0 |
| **Sloppy landing** | Large overhang, long wobble, knife-edge defer engaged, or triggers a block loss. **Resets the combo ladder.** No negative feedback beyond the physics itself. | — |
| **Flush placement** | Both vertical edges align with support below (grid columns match, no overhang). | 1 |
| **Perfect stack** | Same shape, same orientation, directly stacked — reuse `ComboDetector.FindMatch/Matches` logic, generalized out of the ability gate. | 1 |
| **Speed chain** | Locked within `SpeedChainWindow` (default 3.0 s) of the previous lock. Stacks with other events. | 1 |
| **Gap fill** | Placed block has filled-cell contact on **both** left and right sides at its row (closed a pocket/hole between existing blocks/terrain). | 2 |
| **Perfect fit** | Gap fill **and** the block's top surface ends flush with both neighbors — the 1×4-into-a-1×4-slot moment. | 2 |
| **Close call** | Block displaced significantly after landing (slid/teetered, knife-edge grace consumed) but survived and settled. Near-miss marking — small "phew" beat. | 1 |
| **Row complete** | See §3. | 3 |
| **Record height** | `MaxHeight` exceeds the profile's previous best for this level. Once per run. | 3 |

**Combo ladder**: Tier 1+ events advance the ladder by 1 (multiple qualities on one placement
still advance it by 1, but pay each event's coins). Clean landings neither advance nor reset.
Sloppy landings and block losses reset to 0. The ladder drives the pitch ladder (§5) and the
coin multiplier (§6).

**Occupancy**: judge computes row/side occupancy at judge time via physics overlap queries per
cell column (robust to real settled positions), not a bookkept grid — blocks move after landing.

## 3. Row Complete — definition

There are no predefined rows (this isn't Tetris): the player decides tower width. A **row** is
a *contiguous* horizontal run of filled cells at one cell-Y, spanning **≥ `RowMinColumns`**
(default **8** — calibrated to the 9-column classic flat floor and the twin-pillar bridge span;
per-level override allowed for narrow levels).

- **Fires when a run at some Y first reaches `RowMinColumns`**, checked only at rows the
  just-placed block occupies.
- **Once per run**: the run's cells are marked consumed; extending a celebrated row further
  never re-fires. A second, disjoint run at the same Y can fire independently.
- **Bridge bonus ×2**: if the completing placement was a *gap closure* (filled-cell contact both
  sides at that row — the "fill the hole in the middle" moment), the row pays double. Closing a
  gap to complete a row is the game's line-clear; merely growing a row outward to 8 still counts,
  but the bridge is the moment we celebrate hardest.
- Reward scales with run length: `RowCoins × (length − RowMinColumns + 1)` before the bridge
  multiplier — superlinear celebration for wide, solid construction.

## 4. Feedback tiers

Upgrades to shared systems first:
- **Trauma camera** — convert `TowerCameraController` to a trauma model: events add trauma
  (0–1), shake amplitude = trauma², Perlin-noise-driven offsets + slight rotation, linear decay.
  Landing trauma scales with block mass × impact velocity. Honors `SettingsService.ScreenShake`.
- **Haptics** — integrate open-source Nice Vibrations (github.com/Lofelt/NiceVibrations); wire
  to the existing "SOUND & HAPTICS" settings label. Transient haptic on the same frame as the
  sound transient.
- **Permanence** — faint dust scuff decals where blocks land, persistent for the run.

| Tier | Events | Feedback stack (cumulative with lower tiers) |
|---|---|---|
| **0** | every landing | Velocity-scaled layered impact sound (§5) · dust puff at contact edge (pooled `LandingDustFx`, house style of `NudgeImpactFx`) · squash-&-stretch pop on **visual child** · trauma bump · light transient haptic. **Fires for ALL landings** — removes the current `_autoDrop`-only gate (`BlockController.Landing.cs:31-37`). |
| **1** | flush · stack · speed chain · close call | Block flash white 1–2 frames (visual child) · musical "snick" note at current pitch-ladder degree · small coin drip → HUD flight · medium haptic. |
| **2** | gap fill · perfect fit | Hit-stop 6–8 frames (`HitStop.Trigger`) · camera punch-zoom 2–3% eased back ~0.3 s · radial shine/shockwave particle on block · ascending arpeggio stinger · coin fountain × combo multiplier · strong haptic. |
| **3** | row complete · record height | Time-scale ramp 0.4× for ~0.4 s · bloom/chromatic pulse via `PostFxController`, decay < 0.5 s · light wave traveling up the tower · chord/cadence stinger in chapter key · large coin burst. Keep rare. |

Golden block (§6) celebration = Tier 2 stack regardless of placement quality achieved, on top
of whatever the placement itself earned.

## 5. Sound

Current SFX are synthesized (`Tools/generate_sfx.py`); new placement audio is generated with
the **ElevenLabs SFX API** and converted per the WAV→OGG recipe (chunked SoundFile writes,
compression_level 0.4 — no ffmpeg on this Mac).

**Layered impact (Tier 0)** — three independently mixed layers, 4–5 round-robins each, played
through `SfxPlayer` with existing pitch jitter:
- *Transient* — tight click/snap; volume scales with placement precision.
- *Body* — deep thud, more bass than realistic; volume/pitch scale with impact velocity.
- *Tail* — dust settle / creak sweetener; quiet, drops out entirely on hard slams.

**Pitch ladder (Tier 1)** — the Peggle system: each chapter's soundtrack gets a declared key;
combo ladder degree *n* plays the *n*-th note of a **pentatonic scale** in that key, via pitch
transposition of 1–2 base chime samples (`pitch = 2^(semitones/12)` in `SfxPlayer` — Peggle
Blast ran its entire system off 5 WAVs). Ladder resets → pitch resets.

**Stingers** — perfect-fit arpeggio · row-complete cadence · close-call "phew" · coin tick
(rising pitch per coin arriving at counter) · level-win jingle (fills the existing
`MusicPlayer.HandleGameOver` gap).

No coin sound on Tier 0. Ever.

## 6. Coins (in-run economy)

`PlayerProfileStore.Coins` is currently a hardcoded placeholder — this makes it real
(persistence + setter). `RunResult` gains a `CoinsEarned` field. Backend sync stays deferred
to Phase E (BACKEND.md).

**Earning — skill only, no participation drip:**

| Source | Coins (defaults, all tunable) |
|---|---|
| Clean landing | **0** — physical feedback only |
| Tier 1 event | +2 |
| Tier 2 event | +8 × combo multiplier |
| Row complete | +25 × length scaling (§3) × 2 if bridge |
| Record height | +25 |
| Golden block placed | ×3–5 on that placement's total (variable roll — the only randomness) |
| Level completion payout | Sized so a skilled run totals ~1.5–2× a scraped-through run |

Combo multiplier = `1 + ladder/4` (capped, tune in playtest).

**Golden block** — occasional queue piece with a golden skin. Fixed-look rule applies
(chapter-independent material, like Magma). Spawn chance low and variable-ratio; this is the
designed slot-machine element, quarantined to magnitude only.

**The flight** — coins burst outward with slight physics → hang a beat → accelerate along
*staggered curved (bezier) arcs* to the HUD coin counter → counter pulses (`FxKit.Elastic`)
with a rising-pitch tick per arrival. Many small coins > one big icon. End-of-level tally
screen counts the run's coins up a second time (second pass over the same dopamine).

## 7. Tooling decision

**Feel asset: rejected** (July 2026). Inspector/prefab-centric workflow clashes with this
code-first project, and we already own its primitives (`HitStop`, `TowerCameraController`,
`ImpactFx`, `FxKit`, pooled procedural FX, `SfxPlayer` pitch variants). Instead:
- Haptics: **Nice Vibrations open-source** (the thing Feel bundles).
- Tweening (if needed beyond `FxKit`): **PrimeTween** (free, zero per-frame allocations).
- Everything else: extend the existing in-house kit.

## 8. Implementation phases

1. **Foundation** — Tier 0 for all landings (remove `_autoDrop` gate, velocity-scaled layered
   sound, `LandingDustFx`, visual-child squash, trauma camera, haptics). Ship/playtest first;
   biggest ROI, no new mechanics.
2. **PlacementJudge** — detection system, combo ladder, ElevenLabs sound set, pitch ladder,
   Tier 1–2 stacks.
3. **Coins** — real `PlayerProfileStore`, earning table, coin flight, HUD counter,
   end-of-level tally.
4. **Big moments** — row complete, record height, golden blocks, Tier 3 stack, win jingle.

## 9. Tuning defaults

| Knob | Default |
|---|---|
| `RowMinColumns` | 8 (per-level override) |
| `SpeedChainWindow` | 3.0 s |
| Hit-stop (Tier 2) | 6–8 frames (~100–130 ms) |
| Punch-zoom | 2–3 %, ease back 0.3 s |
| Slow-mo (Tier 3) | 0.4× for 0.4 s |
| Pitch jitter (impacts) | ±10 % (≈ ±2 semitones) |
| Trauma exponent | shake = trauma² |
| Sloppy overhang threshold | ≥ 0.25 cell (tune) |

Pitfalls to re-read before coding: PHYSICS.md I1–I3 (visual child only; don't keep bodies
awake), `HitStop` refuses when `timeScale ≠ 1` (fine — never stack slow-mo + hit-stop),
`SettingsService` gates (ScreenShake / VisualEffects / EffectiveSfx) must gate every new effect.
