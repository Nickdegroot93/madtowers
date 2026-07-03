# PROGRESSION.md — the campaign difficulty curve

How the campaign gets harder across chapters: the numbers to start each mode at, where they
end, when each brick is introduced, and the fairness rules that keep it honest. This is the
**curve plan**; the *dial reference* (what every setting does, exact field names) is
[LEVELS.md](LEVELS.md), the brick catalog is [BLOCKVARIANTS.md](BLOCKVARIANTS.md), and the
physics contract (never tune impact for difficulty) is [PHYSICS.md](PHYSICS.md).

> The numbers below are a **proposed starting curve to bracket with playtesting**, not gospel.
> Monotonic and fair beats "correct on paper." Expect to nudge with Nick playing.

---

## 1. Design philosophy

- **One pressure + one relief per tier.** Every chapter turns *one* screw (faster, narrower,
  a nastier brick, a tighter clock) and often loosens one elsewhere (more power-up choices, a
  positive brick, a wider floor). Turning everything at once is what makes a curve feel unfair.
- **Difficulty is reaction + planning, never impact.** Speed, leniency, floor width, hostile
  bricks, and the clock are the levers. `maxLandingImpactSpeed` stays at 2 (PHYSICS.md) — a
  block never "thumps harder" to be hard.
- **Teach, then test.** A brick debuts at a *low* ambient chance in a level built to showcase
  it kindly (slow, wide, forgiving), then recurs at higher chance / as a bag default once the
  player knows it.
- **Every chapter's first level is beatable cold.** The opener of a chapter is the gentlest
  level in it; the third is the wall. Never open a chapter on its hardest idea.
- **Positives can be common; hazards stay rare.** Anchor/Vine may sit at 10–20% or be the bag
  default. Bomb/Maw/Tremor stay in the single digits — one is a spike of tension, a swarm is a
  coin-flip.

---

## 2. The difficulty axes (grouped dials)

| Axis | Dials (LEVELS.md §2) | Harder = |
|---|---|---|
| **Pace** | `initialFallSpeed`, `maxFallSpeed`, `speedIncreasePerBlock`, `towerPeakScreenY` (leniency), `startingLives`, `spawnDelay` | faster start/cap, steeper ramp, higher peakY (less reaction room), fewer lives |
| **Space** | `floorSegments` width, `staticSupportIslands*`, `horizontalPlacementBufferColumns` | narrower floor, fewer/no islands |
| **Puzzle** (Laser Limit) | `HeightLimitWavesModifier.waves[]` (count + `lineHeightAboveFloor`), `lineRiseSeconds` | lower starting line, bigger waves, smaller rises |
| **Goal** | `targetType` + `targetValue`; timed variants add a clock | more blocks / higher climb / tighter time |
| **Hazards** | `ambientBlockVariantChances`, bag `defaultData` | nastier brick, higher % |
| **Relief** ↓ | `powerUpChoiceEveryBlocks`, positive bricks, wider floor, islands on, more lives | (loosen these to offset a pressure bump) |

Baseline to escalate *from* — `GameMode_Classic` ("works perfectly"): fall **2 → cap 5** at
**+0.025/block**, leniency **0.50**, floor **9**, power-up every **10**, islands **on**.

---

## 3. The 10-chapter ladder

Themes 1–3 are the shipped chapters; 4–10 are suggestions to rename. Each chapter carries a
**tier** (the intended wall height) and a **signature brick** (debuts here at a low ambient %).

| # | Theme (suggest) | Tier | Signature brick (debut %) | Pace: init→cap @ ramp / leniency | Floor | Lives* |
|---|---|---|---|---|---|---|
| 1 | Sakura Ridge | tutorial | none (Normal only) | 2.0→5.0 @ .020 / .50 | 9 | 3 |
| 2 | Barren Lands (desert) | gentle | **Anchor** ＋ (learn platforms) | 2.1→5.3 @ .025 / .51 | 9 | 3 |
| 3 | Jungle Depths | gentle+ | **Vine** ＋ (welds) | 2.3→5.6 @ .028 / .52 | 9 | 3 |
| 4 | Windswept cliffs | rising | **Feather** − (mild wobble) | 2.4→6.0 @ .030 / .53 | 9 | 2 |
| 5 | Frozen caverns | rising+ | **Ice** − (slippery) | 2.6→6.3 @ .033 / .54 | 8 | 2 |
| 6 | Volcano / magma | mid | **Magma** ~ (melts to terrain) | 2.7→6.6 @ .036 / .55 | 8 | 2 |
| 7 | Ancient ruins | mid+ | **Locked** − (no rotate) | 2.9→7.0 @ .038 / .56 | 8 | 2 |
| 8 | Warped / neon | hard | **Vortex** − (inverted steer) | 3.0→7.3 @ .041 / .58 | 7 | 2 |
| 9 | Quake fault | hard+ | **Tremor** − + **Boulder** − | 3.2→7.6 @ .044 / .60 | 7 | 1 |
| 10 | The Maw / void | wall | **Bomb** −− + **Maw** hazard | 3.4→8.0 @ .048 / .62 | 7 | 1 |

\* **Lives is a decision, not a fact** — `GameMode_Classic` currently ships `startingLives 0`.
Decide whether early chapters grant a 2–3 buffer (recommended for "training wheels") or the
whole campaign is one-miss-tense with slow speeds. The column above assumes a buffer.

Read the pace column against the baseline: Ch1 is *slightly gentler* than today's Classic; Ch10
is ~1.6× the start speed and 1.6× the cap, with ~half the reaction room — steep but not
twitch-impossible, because the cap keeps late game from running away.

---

## 4. Per-mode ramps (each chapter runs ~3 flavours)

Keep the shipped structure: each chapter ≈ **one Classic endurance + one Laser puzzle + one
Narrow climb**, each flavour a notch meaner than the same flavour last chapter. Timed variants
join from the mid-tiers as a fourth flavour on some chapters.

**Classic endurance** (PlaceBlocks / ReachHeight) — pace from the ladder; the *goal* grows:
`Place 60 → 80 → 100 → 110 → Reach 45m → 55m → Place 140 → Reach 70m → Place 160 → Reach 90m`.
(Today's "Foundations: Place 100" is a big ask for Ch1 — 60 is a truer opener.)

**Laser Limit** (Height-Limit Waves — the puzzle mode). Higher starting line = easier; bigger
waves + smaller rises = harder. Default asset = `6@5m → 10@8m → 15@13m → 21@20m` (52).

| Debut→late | Start line | Waves (count@m) | Total | Lives | Hazard in mix |
|---|---|---|---|---|---|
| First laser (~Ch2) | 6 m | 5@6 · 8@10 · 12@15 | 25 | 3 | none |
| Mid (~Ch5) | 5 m | 6@5 · 10@8 · 15@13 · 21@20 (default) | 52 | 2 | chapter's positive/mild |
| Late (~Ch8) | 4 m | 8@5 · 12@8 · 18@13 · 24@19 | 62 | 1 | one negative @ ~5% |
| Wall (Ch10) | 3.5 m | 8@4 · 14@7 · 20@12 · 26@18 | 68 | 1 | Bomb/Maw @ low % |

**Narrow climb** (ReachHeight on a narrow floor). Rising Dunes/Vine Ascent are the template
(`Narrow3`, 3 columns). Ramp: reach target grows, islands thin out, camera tightens.

| Debut→late | Floor cols | Islands | Reach | Notes |
|---|---|---|---|---|
| First (~Ch2) | 3 | dense (relief) | 40 m | islands are the platforms you live on |
| Mid | 3 | medium | 60 m | leniency +, slightly faster |
| Late | 3 | sparse | 80 m | fewer platforms, stricter framing |
| Wall | 3 | off | 100 m | pure narrow climb, no crutches |

**Timed** (TimedPlaceBlocks / TimedReachHeight) — introduce ~Ch5; the *seconds-per-block* (or
seconds-per-meter) budget tightens. Loosen another dial (wider floor, more choices) since the
clock alone is a big pressure.

| Tier | TimedPlaceBlocks | ≈ budget | TimedReachHeight |
|---|---|---|---|
| intro (~Ch5) | 40 blocks / 200 s | 5.0 s/block | 40 m / 160 s |
| mid | 50 / 175 s | 3.5 s/block | 55 m / 165 s |
| late (Ch10) | 60 / 150 s | 2.5 s/block | 70 m / 140 s |

---

## 5. Brick-introduction schedule

One signature brick per chapter, easiest-to-live-with first. Debut at a **low ambient %** in a
showcase level; a brick may then recur in later chapters at higher % or as a bag default.

| Ch | Brick | Polarity | Why here | Debut ambient % |
|---|---|---|---|---|
| 1 | — | — | learn plain stacking | — |
| 2 | Anchor | ＋ | first "special" is a *helper* — teaches that bricks vary, kindly | 12–15% |
| 3 | Vine | ＋ | still positive (welds), themed to jungle; rewards clustering | 10–15% |
| 4 | Feather | − | first hazard is *mild* — light bricks wobble, teaches load | 6–8% |
| 5 | Ice | − | slippery — punishes uneven placement | 5–8% |
| 6 | Magma | ~ | melts to conform to terrain — interesting, not cruel | 8–12% |
| 7 | Locked | − | can't rotate — a *planning* hazard, not a reflex one | 5–8% |
| 8 | Vortex | − | inverted steering — a control hazard; keep rare | 4–6% |
| 9 | Tremor / Boulder | − / − | stability + weight; the tower fights back | 4–6% each |
| 10 | Bomb / Maw | −− / hazard | the destroyers — Maw *costs a life*, so keep it a rare spike | 3–4% each |

Rules: never debut two negatives in the same chapter (Ch9 is the intentional exception, and it
gives up a life-buffer as the trade). A brick's % can climb ~2–3× over the chapters after its
debut, but hostile bricks never become the *bag default* (that's a "cursed" level's job, not a
whole chapter's baseline). Positives (Anchor/Vine) may become defaults on a relief level.

---

## 6. Fairness guardrails

- **Cap the runaway.** `maxFallSpeed` keeps late chapters bounded — long games plateau instead
  of accelerating into un-reactable. Never remove the cap to make something "harder."
- **Leniency is the safety valve.** If a chapter tests as unfair, raise reaction room
  (`towerPeakScreenY` down toward 0.5, `minimumCameraSize` up) *before* slowing the bricks — it
  keeps the fantasy of speed while restoring fairness.
- **One screw per level, not per dial.** A single level shouldn't stack faster + narrower +
  fewer lives + a new hazard. Pick the theme of *that* level.
- **Relief between walls.** After a wall level, the next chapter's opener eases off (that's why
  each chapter opens gentle) — players need to exhale.
- **The Maw and Bomb are seasoning.** They read as "oh no" moments. At >~5% ambient they turn a
  skill test into a lottery; keep them a rare, memorable threat.
- **Validate cold.** A player who has never seen a brick should clear the level that debuts it.

---

## 7. Open decisions (settle before authoring 10 chapters)

- **Lives model** — is there a per-level life buffer (recommended 3 early → 1 late), or is the
  campaign built around `startingLives 0`? This changes every tier's feel. (§3 note.)
- **Chapter count & themes** — 10 is the target; lock the theme/brick pairing (§3, §5) so art
  and skins can be produced against it.
- **Timed modes' place** — a fourth flavour on select chapters, or their own late "gauntlet"
  chapter? They pressure differently (clock vs collapse).
- **Endless as a per-chapter sandbox?** An `alwaysUnlocked` free-build level per chapter (no
  goal) as a low-stakes place to try the new brick before the graded levels.
- **Power-up cadence as a relief curve** — hold `powerUpChoiceEveryBlocks` at ~10 early and
  stretch to ~15 late (fewer rescues) as an additional, gentle pressure axis.

---

*Update this when the curve shifts. LEVELS.md owns the dials; this owns the plan for where they
sit per chapter. PHYSICS.md wins every conflict.*
