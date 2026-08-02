# PROGRESSION.md — the campaign difficulty curve

How the campaign gets harder across chapters: the chapter order, each chapter's speed band,
which brick debuts where, and the fairness rules that keep it honest. This is the **curve
plan**; the *dial reference* (what every setting does, exact field names) is
[LEVELS.md](LEVELS.md), the brick catalog is [BLOCKVARIANTS.md](BLOCKVARIANTS.md), and the
physics contract (never tune impact for difficulty) is [PHYSICS.md](PHYSICS.md).

> The numbers below are a **starting curve to bracket with playtesting**, not gospel.
> Monotonic and fair beats "correct on paper." Expect to nudge with Nick playing.
>
> **Status (July 2026):** chapter order + speed system agreed; per-level layouts deliberately
> NOT designed yet — the old per-level tables were removed on purpose. The chapter reorder
> is **applied in the project**: every chapter asset's `chapterNumber`/`sortOrder` matches
> §3 (slot × 10), and the leftover `alwaysUnlocked` flags on Jungle Depths and Crimson Core
> were cleared so chapter 1 gates the campaign and the finale is earned (use
> `MADTOWERS_UNLOCK_ALL` or Custom Game for testing). Liquefy/MagmaSpawn's
> `minChapterNumber` moved 7 → 9 to stay "just before the Magma chapter" (Burning Steppes,
> now 10) — revisit in the ability-gating pass. **Chapters 1–3 are authored to this plan** —
> Ch1 Jungle Depths: Place 100 / 5 waves / Reach 75m on 4 columns, tutorial attached to
> the opener, Temple Sprint retired. Ch2 Sakura Ridge: Feather @6%, chapter-owned mode
> copies, three pagoda-profile floors, 6 waves / 72 (final line 24). Ch3 Neon Nightfall:
> Void Zones opener, hard 5-wave puzzle (60), trio-pillar climb to 60m; no signature brick
> yet. Ch4 Frozen Peaks: Ice debuts at volume (28% everywhere — playtest-bracketed: 10–15%
> too easy, 50% too rough), mountain floors, summit-spike wall climb. Ch5 Kvartal 4:
> Locked debuts (10/8/10% on the first three levels, incl. the first true 3-column climb),
> Airtight debuts as the Locked-free wall; panelka floors carry the campaign's first
> (shallow) pockets. Ch6 Barren Lands: Sandstone @5% (12% playtested too frequent.
> The tuning rule that fell out: judge a brick by WHEN its hazard is live — Locked is
> hazardous only before landing, Ice mostly at landing, but Sandstone stays an OPEN problem
> whose risk compounds with every later placement, so each one on the board adds permanent
> mental load; open-problem bricks tune single-digit like destructive ones), deep-pocket
> canyon floors (the pocket showcase), dust weather parked for later. Ch7 Sector Isla: Airtight opener, Anchor debut @2% (3% nerfed 2026-08-02 — "WAY too powerful"; AnchorSpawn ability Rare→Epic), **Curse @4% signature** (assigned 2026-08-02; puzzle mode untouched — a life-stealer inside endless waves needs its own call), tough puzzle, first timed level (40/180 s). Ch8 Fangkuai District: Vortex debut @8% (4% on the 4th level; 6% felt too scarce), lantern-post/alley/staircase floors, Void Zones II at full standard strength (new Level_FD4_VoidGate). Ch9 Lost City: Boulder @5/4/4% + Tremor @6/5/5% (3-4% playtested invisible; Tremor's burst resolves on landing, so it tunes above the open-problem band) (the double-debut exception), levitating-ruin floors, first sky platforms (sparse: 6m interval @35%, off in the climb). 4th level Hollow Moon: Airtight III on stepped terrain (Place 100). Ch10 Burning Steppes: Magma @10% + Bomb @4/3/4%, rift/vent/crater floors, first flat-rise wave set (older sets due a sweep — late waves played too easy because rises accelerated). **Magma inflates counted blocks ×4 per piece** — scale PlaceBlocks/wave targets by (1+3×rate) wherever magma rides the bag, keep wave lines unchanged, derive ramps from inflated targets; height goals unaffected (Nick's catch, ch10 scaled ×1.3). Ch11 Giza Dusk: Void Zones III opener (9m/7m/90%), broken-obelisk + great-pyramid floors, Reach 90m, no Airtight (pyramid faces + sealing = too much), Giza laser scaling fixed. Ch12 Amber Tide: Vine recurs @12%, signature TBD (Nick); interim identity = nothing-touches-the-ground floors (floating slabs / archipelago) + the most island-forward sky-platform tuning (4m @50%, off in the climb), Reach 95m. Ch13 Monsoon Sector: Airtight opener + Locked @3% on every level (first hostile brick inside an Airtight — the poison combo), catenary/skyline floors, first Reach 100m, menuTopIsLight fixed. Ch14 Hallow's End: Maw debut @3% (opener only), the Maw Sort climb (50% maw, two 4-wide pillars, 2 lives), gallows-platform waves, void showcase at Ch14 pace. Ch15 Crimson Core (finale): Bomb @4/3/4% (fuse glow pierces the blackout), Reactor/circuit/core-shaft floors, Reach 110m; Blackout's 0.90 stacks on every level's multiplier. ALL 15 CHAPTERS AUTHORED.
> Note this makes Ice the precedent that MILD hazards can run far past the single-digit
> band — the single-digit rule stays for destructive bricks (Bomb/Maw/Tremor). (Details in LEVELS.md.) **Wave achievability rule**: pieces are 4 cells, reachable
> width = floor + buffer columns both sides; keep the final squeeze at ≤ ~90% of max-width
> capacity (Jungle's endgame ≈ the hard edge; standard ≈ the comfy edge). **Laser modes
> must set `difficultyScalingMode: PerBlock`** — the shipped standard/Giza laser modes use
> None (constant speed), so a copied laser mode silently ignores its authored ramp/cap
> (caught in the July 2026 review; Jungle/Sakura/Neon laser modes now scale). Wave heights
> are **integers only**: a .5 height plus the half-cell zap grace re-creates the flush-row
> trap the grace exists to prevent. Note: mode assets also carry a small
> time-based ramp (`speedIncreasePerInterval` 0.1 per 60 s) on top of the per-block ramp —
> negligible at current values, fold into the formula if it ever grows.
>
> **Waves REBUILT (2026-07-26)**: all hand-authored wave tables and per-chapter wave/block
> targets above are HISTORICAL. Puzzle waves are now endless and generated (counting = live
> standing blocks; goal = `ClearWaves` with waves-to-win 5/6/7 by chapter third; difficulty =
> `difficultyRank` on the modifier asset — ALL shipped at rank 5, per-chapter differentiation
> comes from brick variants + speed, not looser lines). The wave
> achievability rule, flat-rise principle, integer-heights rule and magma ×4 scaling are all
> now enforced BY the wave engine's math instead of by authoring discipline — see LEVELS.md
> "Height-Limit Waves details".

---

## 1. Design philosophy

- **One pressure + one relief per tier.** Every chapter turns *one* screw (faster, narrower,
  a nastier brick, a tighter clock) and often loosens one elsewhere. Turning everything at
  once is what makes a curve feel unfair.
- **Difficulty is reaction + planning, never impact.** Speed, leniency, floor width, hostile
  bricks, and the clock are the levers. `maxLandingImpactSpeed` stays at 2 (PHYSICS.md) — a
  block never "thumps harder" to be hard.
- **Every chapter looks nothing like the one before it.** Palette/mood alternates hard
  between neighbours (no two "red looks" in a row) — the order in §3 was chosen partly for
  this.
- **Teach, then test.** A brick debuts at a *low* ambient chance in a level built to
  showcase it kindly, then recurs at higher chance / as a bag default once the player knows
  it.
- **Every chapter's first level is beatable cold.** The opener of a chapter is the gentlest
  level in it; the last is the wall. Never open a chapter on its hardest idea.
- **Positives can be common; destructive hazards stay rare.** Anchor/Vine may sit at 10–20%
  or be a bag default. MILD hazards (Ice, Feather, Locked — annoying, not destructive) can
  run as high as a level's identity demands (Frozen Peaks ships Ice at 28%; 50% playtested as too rough). Bomb/Maw/Tremor
  stay in the single digits — one is a spike of tension, a swarm is a coin-flip.

---

## 2. The speed system

Three dials exist per mode asset: `initialFallSpeed`, `maxFallSpeed`,
`speedIncreasePerBlock`. **Only the first two are authored** — the ramp is always derived so
the cap is hit at 90% of the level's goal and the last 10% is a sprint at cap:

```
speedIncreasePerBlock = (maxFallSpeed − initialFallSpeed) / (0.9 × blockTarget)
```

Change a level's block target and the ramp re-tunes itself (Place 100 at 3→7 = +0.044/block;
Place 200 at the same band = +0.022/block). For ReachHeight goals use *expected placements
to reach the height* (calibrate blocks-per-meter from playtest runs once, reuse it).

### 2.1 Base speed table (Classic endurance = the reference mode)

Chapter 1 anchors at **today's shipped cap with a hotter start** — the current game's 2.0
opening crawl is what reads "too easy," so the notch-up went into the start speed. Both
columns grow ~+4% per chapter (multiplicative, because perceived speed is relative: 6→7
feels smaller than 3→4).

| Ch | Start | Cap |
|---|---|---|
| 1 | 2.5 | 5.0 |
| 2 | 2.6 | 5.2 |
| 3 | 2.7 | 5.4 |
| 4 | 2.8 | 5.6 |
| 5 | 3.0 | 5.8 |
| 6 | 3.1 | 6.0 |
| 7 | 3.2 | 6.2 |
| 8 | 3.4 | 6.4 |
| 9 | 3.5 | 6.7 |
| 10 | 3.7 | 6.9 |
| 11 | 3.8 | 7.2 |
| 12 | 4.0 | 7.4 |
| 13 | 4.2 | 7.7 |
| 14 | 4.3 | 7.9 |
| 15 | 4.5 | 8.2 |

Speeds attach to the **slot number, not the theme** — reshuffling chapters never touches
this table. There is deliberately no training-wheels tier below today's baseline; the
tutorial plus the mode multipliers below are the onboarding. If cold players faceplant,
nudge Ch1's *start* back toward 2.3 — don't flatten the curve. The 8.2 ceiling is slightly
past the previously judged "edge of reactable" (8.0) and arrives with the tighter late
camera (§2.5) — it's the single number to watch hardest in playtests; if it tips over, pull
the cap to 8.0 and keep the start.

### 2.2 Mode multipliers

One fixed multiplier per mode, applied to **both** start and cap of the chapter's row. Hard
modes are hard because of their rule, not their pace — speed shouldn't crowd out the
thinking they demand.

| Mode | × | Why |
|---|---|---|
| Classic / block count | 1.00 | the reference |
| Narrow climb | 0.95 | precision on 3 columns |
| Laser Limit (puzzle waves) | 0.88 | a thinking mode |
| Blackout | 0.90 | |
| Airtight | 0.85 | seam-free stacking is the pressure |
| Void Zones | 0.85 | the zones are the pressure |
| Timed variants | 1.00 | the clock is already the screw — don't also slow it |

This is what makes gentle openers free: Chapter 1's puzzle-wave level lands at 2.2→4.4 and
its height challenge at ~2.4→4.75 without authoring special numbers. Airtight and Void
Zones are **recurring modes across the whole campaign** (not tied to one chapter); the
multiplier keeps them fair wherever they appear.

### 2.3 The brick tax

A level whose bag carries a nasty brick doesn't get a new multiplier — it **drops back one
chapter row** (two rows for the truly cruel: Vortex-heavy, Bomb, Maw). Pressures are
exchangeable in one currency, the chapter-step. The tax fades as the player internalizes
the brick: debut = −1 row, later recurrences = −0 or −½.

### 2.4 Within a chapter

Opener uses the *previous* chapter's row; the closing wall may nudge halfway toward the
*next*. No level ever jumps more than one step from its neighbour.

### 2.5 The camera axis (held in reserve)

Less vertical headroom = less reaction time; the dials are `towerPeakScreenY` (up = less
room) and `minimumCameraSize`. **Keep it out of chapters 1–8** (hold ~0.50), then tighten
across 9–15 (toward ~0.62) as the third-act pressure. It stacks multiplicatively with
speed, and the fairness guardrails (§4) need leniency free as the safety valve — don't
spend it early.

---

## 3. The 15-chapter order & signature bricks

The agreed order (July 2026). Look-contrast between neighbours was part of the choice.
Speeds come from §2.1 by slot number.

| Slot | Chapter | Signature brick | Notes |
|---|---|---|---|
| 1 | Jungle Depths | **Vine** ＋ | first special is a helper (welds); learn plain stacking + one kindness |
| 2 | Sakura Ridge | **Feather** − | first hazard is mild (light, wobbly) |
| 3 | Neon Nightfall | *TBD* | |
| 4 | Frozen Peaks | **Ice** − | slippery |
| 5 | Kvartal 4 | **Locked** − | can't rotate — a planning hazard |
| 6 | Barren Lands | **Sandstone** ~ | load-bearing limit; desert-native |
| 7 | Sector Isla | **Curse** − | bury-me countdown hazard @4% (BLOCKVARIANTS.md; assigned by Nick 2026-08-02); Anchor also ambient here @2% (nerfed from 3% same day - "WAY too powerful") |
| 8 | Fangkuai District | **Vortex** − | inverted steering; keep rare |
| 9 | Lost City | **Boulder** − | 4× mass strains the tower |
| 10 | Burning Steppes | **Magma** ~ | melts to terrain; volcano-native |
| 11 | Giza Dusk | **Pyramid** | 3-wide monument shape, ~3% of spawns (see BLOCKVARIANTS.md / LEVELS.md) |
| 12 | Amber Tide | *TBD* (Vine recurs) | needs more ideas |
| 13 | Monsoon Sector | *TBD* | |
| 14 | Hallow's End | *TBD* | |
| 15 | Crimson Core | *TBD* | built (sortOrder 150) — the Blackout home chapter: all three levels run `ScheduledStatus_Blackout_Standard`; its gimmick is the dark, so its signature may stay the mode rather than a brick |

**Unassigned brick pool** (in the catalog, no slot yet): **Tremor** −, **Bomb** −−,
**Maw** hazard. (Anchor turned out to already be ambient in Sector Isla's modes — now @2%
with the Curse as that chapter's signature; Curse left this pool 2026-08-02.)
Natural-feeling homes to consider — Tremor for Monsoon Sector
(the shaking fits), Maw for Hallow's End (it *is* a monster), Bomb for Crimson Core (the
finale's destroyer). Not decided — Nick assigns.

Rules: never debut two negatives in the same chapter. A brick's ambient % can climb ~2–3×
over the chapters after its debut, but hostile bricks never become the *bag default* (that's
a "cursed" level's job). Positives (Anchor/Vine) may become defaults on a relief level.

**Per-level layouts are intentionally not designed yet.** The old per-level tables were
removed; when levels are authored, they read their speeds off §2 (row × mode multiplier −
brick tax) and their goals off a block-count ladder still to be agreed.

---

## 4. Fairness guardrails

- **Cap the runaway.** `maxFallSpeed` keeps late chapters bounded — long games plateau
  instead of accelerating into un-reactable. Never remove the cap to make something
  "harder."
- **Leniency is the safety valve.** If a chapter tests as unfair, raise reaction room
  (`towerPeakScreenY` down toward 0.5, `minimumCameraSize` up) *before* slowing the bricks —
  it keeps the fantasy of speed while restoring fairness.
- **One screw per level, not per dial.** A single level shouldn't stack faster + narrower +
  fewer lives + a new hazard. Pick the theme of *that* level.
- **Relief between walls.** After a wall level, the next chapter's opener eases off (that's
  why the opener uses the previous chapter's row) — players need to exhale.
- **The Maw and Bomb are seasoning.** At >~5% ambient they turn a skill test into a lottery;
  keep them a rare, memorable threat.
- **Validate cold.** A player who has never seen a brick should clear the level that debuts
  it.

---

## 5. Open decisions

- **Signature bricks for slots 3, 7, 12, 13, 14, 15** — pool in §3; Nick decides later.
  (Crimson Core may not need one — Blackout is already its identity.)
- **Ability chapter-gates** — with chapters renumbered, `minChapterNumber` values need a
  deliberate pass (only Liquefy/MagmaSpawn use it today, provisionally moved to 9).
- **Block-count ladder** — how the per-level goals grow across 15 chapters (the speed ramp
  auto-adjusts to whatever is chosen, §2).
- **Lives model** — per-level life buffer (3 early → 1 late) or campaign built around
  `startingLives 0`? Changes every tier's feel.
- **Timed modes' place** — a fourth flavour on select chapters, or their own late "gauntlet"
  chapter?
- **Power-up cadence as a relief curve** — hold `powerUpChoiceEveryBlocks` ~10 early,
  stretch to ~15 late.

---

*Update this when the curve shifts. LEVELS.md owns the dials; this owns the plan for where
they sit per chapter. PHYSICS.md wins every conflict.*
