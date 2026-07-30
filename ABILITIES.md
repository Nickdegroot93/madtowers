# MadTowers Abilities — Architecture & Authoring Guide

The ability system behind the every-N-blocks picker. This document is the contract:
read it before adding abilities, triggers or status effects. Physics rules in
PHYSICS.md remain binding for anything an ability does to the world.

Code: `Assets/SourceFiles/Scripts/Abilities/` · Assets: `Assets/Data/PowerUps/`
(folder name kept for asset-history stability).

---

## 1. The four kinds

| Kind | Class | Lifecycle | Example |
|---|---|---|---|
| **Instant** | `InstantAbility` | `Apply()` once at pick, then gone | Extra Life, Slow Motion |
| **Consumable** | `ConsumableAbility` | Held in one of 2 HUD slots; player taps to `Activate()` | Freeze, Extract, Shrink |
| **Passive** | `PassiveAbility` | Always on from pick; `charges` makes it one-shot | Recovery (permanent), Sacrifice (charges = 1) |
| **Combo** | `ComboAbility` | Fires `OnComboFired()` when its trigger pattern lands | (logic retained; no combo ability currently shipped — see §6) |

A **one-shot passive is not a separate kind**: set `charges = 1` on a `PassiveAbility`
asset. A handler that returns true ("I triggered") consumes a charge; at zero the
ability leaves the inventory. Same convention on `ComboAbility` (0 = fires forever).
**Stacking a charged ability adds its charges** (two non-unique one-shot passives = two
saves); infinite stays infinite. Author abilities where a second copy means nothing as
`unique`.

## 2. Presentation (every ability carries all four)

`AbilityDefinition` has a standard presentation block: **title** (`displayName`),
**icon** (`Sprite`, generated per ability later - empty falls back to the title text),
**short description** (one line; cards + swap dialog), **long description** (`TextArea`;
the details view). Short and long fall back to each other, so half-authored assets
degrade gracefully.

A stackable ability may also author an **owned short description**: the card swaps to it
once the player already owns ≥1 (`ShortDescriptionFor(ownedStacks)`, fed the stack count the
card already computes for the "Owned ×N" badge), so a second+ pick reads as *what another
stack adds* (e.g. Magma → "Drops molten blocks more often") instead of repeating the
first-time intro. Blank falls back to the short description; the long/detail text is left
alone — it stays the full mechanic explainer at any stack count. This is the lightweight
alternative to a separate self-gating "More X" booster asset (the Pip pattern, §13): use the
owned line for a plain stack-the-knob passive, a booster only when the upgrade deserves its
own card identity/rarity.

There is also a player-facing **type badge** (`AbilityDefinition.Type`), shown small at
the top of every card. It is **derived, never authored** - kind from the class,
"one-time" from charges. The `Type` enum still distinguishes `OneTimePassive`, but the
badge **text** collapses it to "PASSIVE" (a one-shot passive IS a passive; the one-time
distinction is intentionally not surfaced to players), so the labels shown are
**CONSUMABLE / PASSIVE / INSTANT**. Labels/colors in `AbilityTypeInfo`.

Consumption today: choice cards are procedural **neon slabs** (no authored frame art): a
NEAR-BLACK rounded body with only a whisper of rarity tint (`RuntimeSprites.CardGradient`)
wrapped in a bright neon edge with a real outer bloom (`RuntimeSprites.CardNeonRing`) -
the rarity colour lives in the EDGE, never the body, and is never written as a word. Each
card: Archivo Black title (`RuntimeUiKit.TmpDisplayFont`), a solid **type chip** tinted by
the DERIVED type colour (`AbilityTypeInfo`), the authored icon on a white rounded tile
lifted by a soft accent glow, an "OWNED ×N" gold tag when stacked, short description
(Inter), and a full-width dark **DETAILS** pill (mobile touch height) with a bright
outline. The rarity ladder: common = faint silver edge; rare = bright blue edge; epic =
hot violet edge + extra halo + slow shine sweep; legendary = gold, breathing halo + fast
warm sweep. Sweeps/pulse in `AbilityCardShine` (+ `UiGlowPulse`), unscaled time. Details
opens the matching detail panel (`AbilityCardView.CreateDetailPanel`: same chrome, big
icon, LONG description, filled-accent Choose / ghost Back - Back returns to the same
three cards, no reroll). HUD slots show the icon (title text if none); the swap dialog
shows title + short. The detail view is the future home of per-ability explainer videos -
the icon and long text it needs are already authored.

### Delivery layer split

`AbilityChoiceController` owns offer timing, pause/phase handoff, pick routing, reroll, details,
and consumable-slot swap flow. It deliberately delegates the other two heavy jobs:

- `AbilityOfferRoller` is the headless balance policy: availability filtering, rarity-profile
  weighting, run-progress escalation, and sampling without replacement.
- `AbilityCardView` owns all UGUI card rendering: the offer cards, the Vault collection
  cards, the shared detail panel, the "CHOOSE AN ABILITY" header, the rarity tier styling
  ladder, and the modal/button restyle helpers the swap dialog and reroll button use.

Runtime targeting/sequence effects derive from `AbilitySessionBase` when they own a temporary
mode (Fission, Overdraw, Zap, Magma melt, Extract — all five sessions use it; none hand-roll the
lifecycle). The base enforces one active session per type, runs destroy-time cleanup through
`CancelSession()`, and shares the common pointer-picking / easing helpers (`TryGetSelectionPoint`,
`IsPointerOverUi`, `Smooth01`). Whether a session seizes the active falling piece is declared once
as the abstract `SeizesActivePiece` property — the base reads it to pair `ActivePieceSession.Enter`
with `Exit`, so the pairing is **safe by construction**: a new session can't forget it or pass the
wrong value. New sessions only implement `SeizesActivePiece` + `CancelSession()` and call
`BeginSessionLifecycle()` / `CompleteSessionLifecycle()`; never hand-roll `IsActive`, `OnDestroy`,
or Enter/Exit.

## 3. The state rule (never violate)

Definitions are **immutable assets**. On acquisition, `AbilityRuntime` stores
`OwnedAbility { Source, Instance = Instantiate(Source), Stacks, ChargesLeft }`:

- **Identity** (unique checks, stack caps, bans, "Owned ×N" on cards) compares `Source`.
- **Callbacks** go to `Instance` — its plain instance fields are safe per-run state
  (the clone-per-run pattern; an ability that must remember something across its own
  callbacks stores it there).
- Stacking never re-clones: `Stacks++` then `OnStackAdded(ctx, newTotal)` on the same
  instance. An instance that needs its stack count records it from those calls.

This is the LevelModifier clone-per-run pattern; SO fields written without cloning leak
state across runs (documented bug class — don't reintroduce it).

## 4. Ordering rules (deterministic, documented here on purpose)

- Inventory is ONE list in **acquisition order**.
- **Intercepting hooks** (`TryInterceptLoss`): highest `LossInterceptPriority` gets
  first refusal, ties resolve in acquisition order. The first armed ability to return
  true handles the event and SHORT-CIRCUITS; later abilities stay armed. Multiple lost
  blocks in one sweep resolve in block-spawn order. Keep the default priority unless
  the UI/FX implies a physical order (Hardline's catch beam sits above Sacrifice's
  destroy beam, so it wins first). `LossInterceptLineOffset` can raise the sweep line
  while an upper catch beam is armed, keeping the visual trigger and gameplay trigger
  aligned.
- **Beam-drawing interceptors trigger at `LossZone.InterceptLineY`**, never at the
  charge line: the charge line's datum − 4 clamp (pocket protection) sits BELOW the
  screen at tight zoom, so a beam drawn there is invisible for the whole ground game
  (July 2026). While any armed passive reports `ShowsLossInterceptLine` (Sacrifice,
  Hardline), the sweep intercepts landed blocks at the raised line — ~8% of the screen
  height above the bottom edge at EVERY camera height, no datum cap — and the laser
  visuals + Hardline's catch height read the same value. Pocket landings are protected
  by a TRIGGER veto, not the line height: while the charge line is terrain-clamped
  (floor still in play) a falling block whose centre is over the floor's X span is
  never intercepted — the terrain catches everything above the charge line, so only
  blocks beyond the floor edges can be condemned there (a depth-2/3 pocket settle used
  to read as "lost" at the raised line and cost a Sacrifice + the tower top; and the
  old datum cap left the beam under the screen for the whole mid-game band).
  At altitude the charge line itself is camera-relative (screen bottom + 2; the datum
  clamp releases once the floor is well out of play — LossZone.FloorRegimeCeiling), so
  the intercept line rides it and stays on screen for the whole climb. The lasers render
  at −40 (Sacrifice) / −39 (Hardline): the whole beam (layer offsets −2..+3) clears the
  ENTIRE floor stack (fill −50 through wisps −43) and the front particles (−45), behind
  bricks (0) — an armed line is a status light and must never hide behind or z-fight the
  floor. Hardline's order is serialized on `Hardline.asset`; keep it in sync with the
  script default. Invisible interceptors (Rebound) leave the flag false and keep the deep
  charge line; a run with no interceptor armed is untouched.
- **Notification hooks** (`OnLifeLost`, `OnBlockSpawned`, combo fan-out): EVERY
  subscriber runs, in acquisition order; a charge is consumed right after the owning
  handler returns. Handlers observe live state mutated by earlier handlers.
- Consumable slots resolve synchronously: the slot empties BEFORE `Activate` runs
  (double-tap safe).

## 5. Status effects — reusable timed game states

"For N seconds, X is true" is never implemented inside an ability. It is a
`StatusEffectDefinition` **asset** applied via `context.Status.Apply(status)` — so two
different abilities can grant the same state, and a combo can re-trigger what a
consumable also grants. Rule of thumb: *if it lasts N seconds and another ability could
conceivably want it, it's a status asset.*

| Kind | Consulted by | Meaning of `magnitude` |
|---|---|---|
| `LifeLossImmunity` | `GameManager.GameOver()` skips the charge | — |
| `FallSpeedMultiplier` | folded into the per-block NORMAL-descent factor (fast drops immune; never `Time.timeScale`) | the multiplier (0.5 = half) |
| `ScorePerBlockBonus` | `BlockLedger` adds it per counted `BlockLanded` grant | extra score (+1 = double progression) |
| `Custom` | nothing built-in; abilities query `IsActive(def)` | yours |

Stack policies: `RefreshDuration` (timer restarts), `ExtendDuration` (durations add),
`StackMagnitude` (magnitudes add, timer refreshes). Timers tick on scaled time —
pauses freeze every state for free. A new shareable state = one new asset; new code
only when a new KIND needs a new consult point in a core system.

**Surfacing a state on screen** (so the player knows it's active): the state carries its own
look — `StatusEffectDefinition.screenEffect` is an optional prefab. `StatusFieldController` (on
the GameManager object) is **fully data-driven**: it shows the prefab of *any* active state that
has one and tears it down when the state ends, driven by the STATE not the ability. So surfacing
a new state, or pointing a second ability at an existing one, is **zero code here** — author the
status asset, drop a prefab on it. Today only `LifeLossImmunity` (opened by Brace) carries one:
the **Hovl "Screen buff" overlay** (`Assets/Hovl Studio/Fullscreen effects`), a camera-parented
particle quad whose `HS_ScreenEffect` sizes it to the view, tuned to a warm edge-weighted smoke
haze (arrow sub-effect disabled, smoke recoloured). The controller loops every system for the
window and stops them to fade out.

Note: `ScorePerBlockBonus` amplifies score-based rewards/progression such as picker
milestones and personal-best score. It does not create extra physical pieces: live
`PlaceBlocks` goals, height checks, and `ScheduledStatusModifier` block-count schedules use
real placed blocks instead. The picker milestone check is crossing-based so jumps can't skip
offers, and the difficulty ramp deliberately uses the UNAMPLIFIED amount.

### Scheduled level/chapter states

Abilities are not the only source of statuses. A level can apply the same
`StatusEffectDefinition` assets through `ScheduledStatusModifier` (see **LEVELS.md**):
for example, a snowstorm every 60 seconds, a sandstorm every 30 physical placed blocks,
or a cyberpunk "all blocks glow the same neon color" window. The status asset still owns
the timed state and screen overlay; the modifier only owns the schedule.

Use this split for chapter-flavoured pressure events:

- Status asset = "what state is active, for how long, and what overlay appears."
- Optional listener component = "what custom gameplay changes while this specific status is active."
- Scheduled modifier asset = "when this level applies the state."

Block-count schedules use `GameEvents.BlockPlaced`, not score, so score-amplifying effects do
not accidentally accelerate "every N blocks" hazards.

## 6. Combo triggers — patterns separate from effects

> **No combo ability ships today** (Overdrive was retired — players found the
> stack-a-pattern triggers hard to read). The combo SYSTEM below is intact and zero-cost
> while no `ComboAbility` is owned, so combos can return as assets without code.

`ComboTriggerDefinition` (asset): required `BlockDefinition` (reference match via the
`BlockIdentity` component the Spawner attaches — never name strings), orientation
(judged from collider-bounds aspect, robust to 180° symmetry), relation
(`StackedDirectlyOn` today; extend the enum + `ComboDetector.Matches` for new ones).

The `ComboDetector` runs **only on block lock**, only for triggers owned combo
abilities subscribe to, and only against landed blocks (locality). Two correctness
rules it owns:

- **Lock ≠ settled** (PHYSICS.md I5): a candidate match is revalidated ~0.4 s after
  lock before firing — a pair that topples immediately never rewards.
- **Consumption**: blocks that participated in a match are consumed for that trigger
  (a 3-stack fires once, a 4-stack twice). Consumption happens once per match, before
  fan-out — every subscribed ability fires from the same match.

`ComboMatch` carries the two blocks, combined bounds and `TopY` (e.g. a catch-line
height). **Never retain block references** from a match — zaps/bombs/losses can
destroy them at any time.

Authoring gotchas (learned the hard way, keep them):
- `Vertical`/`Horizontal` orientation only distinguishes shapes whose bounds aspect
  exceeds **1.5** (the I piece). Near-square shapes (T, S/Z, O) never pass either test —
  use `Any` for them or the trigger silently never fires.
- Tolerances are in grid cells and exist because collider footprints are 0.94 of the
  cell (PHYSICS.md I4): visually-touching blocks have a real ~0.06-cell gap.
  `StackContactTolerance` 0.2, `MinHorizontalOverlap` 0.3. A new relation needs its own
  tolerances honoring the same fact.
- `Matches(trigger, newBlock, existingBlock)` is also the **revalidation predicate**
  (it re-runs after the settle delay) — keep relations evaluable from bounds alone, and
  asymmetric relations written against that argument order.
- The settle-revalidation delay derives from the mode's `settleTime` + margin (it is a
  per-mode tunable; never hardcode the delay).

## 7. Availability — when an ability may be offered

`AbilityDefinition.IsAvailable(context, ownedStacks)` filters the pool before every
roll. The default enforces, in order:

1. `unique` and already owned → out (uniques are pickable exactly once).
2. `maxStacks` reached → out (0 = unlimited; unique implies 1).
3. Level bans (`LevelDefinition.bannedAbilities`) → out. **Manual** design lockouts.
   The same check also consults every modifier's `LevelModifier.BansAbility` — a game
   TYPE's lockouts ride the modifier into all its levels with no per-level authoring
   (Height-Limit Waves bans every ability granting an `AnchorBlockData` variant: a brick
   that freezes into permanent terrain wherever it lands collapses the wave puzzle).
4. `requiresVariantsInLevel`: every listed `BlockData` must exist in the mode's spawn
   tables (ambient chances or fallback variants) → **automatic** content conditions
   ("no Vortex bricks in this level → don't offer the anti-Vortex ability").
5. `minChapterNumber`: offered only from that chapter onward (vs
   `GameManager.CurrentChapterNumber`) → gate an ability to the chapter that introduces
   its brick, so players are never offered an ability for a brick they've never seen
   (e.g. Magma Drops from the volcano chapter). `0` = ungated; a level with no resolved
   chapter (custom/endless, chapter `0`) is **never** gated, so the sandbox sees everything.

Exotic conditions: override `IsAvailable`, call `base` for the standard rules. An
offer whose candidates all filter out is quietly skipped (by design).

> **TODO — gate every brick-variant ability by chapter (pending level design).**
> Any ability that *introduces or boosts a block variant* must eventually carry a
> `minChapterNumber` matching the chapter where that brick is first taught, so players
> are never offered an ability for a brick they have never seen. Brick-intro chapters
> aren't pinned yet, so most ship **ungated** for now. The live per-ability checklist
> (and the icon backlog) lives in **`ABILITIES_TODO.md`** — keep it updated as levels
> get designed.

**Offers are SINGLE-RARITY**: the roll first picks the offer's rarity, then samples
all three cards uniformly (without replacement) from that rarity's available
candidates — a mixed common/legendary offer would be a non-choice. Rarity odds come
from an `AbilityRarityProfile`: stages of (progress threshold → 4 weights), where
progress = score-or-height vs the level target (0 on endless). Built-in defaults
escalate toward the goal: base 100/40/15/5, past 50% → 70/40/25/12, past 80% →
45/35/30/25. A level can override with its own profile asset
(`LevelDefinition.abilityRarityProfile`) for anything from gentle retuning to a
"legendaries only" gimmick (weights 0/0/0/1). Rarities with no available candidates
are excluded from the roll, so an offer never comes up blank while others exist.

Where things live: pools on `GameModeConfig` assets (`Assets/Resources/GameModes/`),
bans on `LevelDefinition` assets (`Assets/Resources/Levels/`). Bans require a selected
level — direct-scene/quick play has no ban list (conditions still apply).

## 8. Consumables — slots and gates

Two HUD slots (bottom-center buttons; they register gesture-exclusion rects with
`TouchGestureInput` so taps never steer/rotate). Picking a consumable with both slots
full opens the swap dialog (replace either slot, or discard the new one) — the game
stays paused until resolved. Blanket activation gates, checked before the ability's
own `CanActivate`: `GameManager.CurrentPhase == Playing`, not paused, and no
active-piece session owns the field (a freeze during the hold-steady countdown would
cheat the sturdiness test). Slots dim
when unusable, same affordance as the nudge pills.

## 9. How to add things (recipes)

**New instant/consumable/passive/combo ability**
1. Reuse an existing class with new field values if possible — these cover most ideas
   with **zero code**:

   | Class | Kind | Fields | Covers |
   |---|---|---|---|
   | `StatusConsumableAbility` | Consumable | status | "activate: enter state X" |
   | `TransmuteAbility` | Consumable | targetShape (+ transformEffect) | "activate: active piece becomes shape X" |
   | `FlipAbility` | Consumable | - | "activate: swap active shape with next queued shape" |
   | `SlowWindowConsumable` | Consumable | slowFactor, blocks | "activate: next N blocks fall slower" |
   | `OverdrawAbility` | Consumable (unique) | choiceCount | "activate: hold three shapes and choose drop order" |
   | `ScrapAbility` | Consumable | shatterColor | "activate: destroy the last placed block" |
   | `SuspensionAbility` | Consumable | - | "activate: select one placed block and freeze it in place" |
   | `RecoveryWindowAbility` | Passive (unique) | slowFactor, blocksPerTrigger | "on life lost: next N blocks fall slower" |
   | `StatusPassiveAbility` | Passive | triggerEvent, status (+ charges) | "on life lost / on spawn: enter state X" |
   | `StatusComboAbility` | Combo | trigger, status (+ charges) | "pattern lands: enter state X" |
   | `BlockVariantChancePowerUp` | Passive (stackable) | variant, chancePerBlock | "% chance blocks spawn as variant V" |
   | `BlockDefinitionChancePowerUp` | Passive (stackable) | definition, firstStackChance, additionalStackChance | "shape X appears a little more often" |
   | `BlockFrictionPowerUp` | Passive (stackable) | firstStackIncrease, additionalStackIncrease | "standard blocks grip a little harder" |
   | `FallSpeedReductionPowerUp` | Passive (stackable) | reductionPerStack | "future pieces fall a little slower" |
   | `SlowburnPowerUp` | Passive (unique) | slowSeconds, slowFactor | "each new piece falls slow for its first ~1s, then full speed (per-piece thinking beat; fast-drop bypasses)" |
   | `TitanPowerUp` | Passive (unique) | frictionIncrease, massMultiplier | "future blocks heavy + grippy: planted, topple/Tremor-resistant, but land harder" |
   | `PurifierPowerUp` | Passive (unique) | reductionPerStack, minHazardTypesInLevel | "drastically cuts ALL hazard spawns (data-driven off `BlockData.IsHazard`); offered only when the level features >= N hazards (custom IsAvailable)" |
   | `WardPowerUp` | Passive (charges = 1) | — | "neutralises the NEXT hazard brick to spawn into a plain brick of its shape, once (hazards via `BlockData.IsHazard`)" |
   | `LastStandAbility` | Passive (unique) | reductionFraction | "on the last life: flat speed cut" |
   | `ReboundAbility` | Passive (unique) | saveChance (+ cellBurstEffect) | "% chance a lost landed block is saved back to the queue" |
   | `BlockDropChancePowerUp` | Passive (unique) | definition, dropChance | "introduce an out-of-bag brick at a rare drop rate" |
   | `QueueVisibilityPowerUp` | Passive (unique) | visibleDepth | "see N upcoming shapes instead of 1" |
   | `EdgePortalAbility` | Passive (unique) | — | "active pieces wrap across screen edges" |
   | `PocketCacheAbility` | Passive (unique) | — | "unlocks a Tetris-style hold/swap cache" |
   | `RerollPowerUp` | Passive (unique) | rerollCharges | "banks N rerolls; a Reroll button on the choice panel redraws all three cards" |
   | `HardlineAbility` | Passive (unique, charges = 1) | laserColor, laserYOffset, settleSeconds | "first lost landed block becomes an airborne platform" |
   | `ApplyVariantConsumable` | Consumable | variant, count (+ transformEffect/Scale) | "tap: the falling brick (and the next count-1) become variant V" — Anchor Brick (1), Vine Bricks (2) |
   | `SanitizeConsumable` | Consumable | (transformEffect/Scale) | "tap: strip the falling hazard's look IN PLACE (same piece, no shift) and reset it to its shape's plain DefaultData" |
   | `ExtraLifePowerUp` | Instant | lives | flat life grant |
   | `SlowMotionPowerUp` | Instant | slowStatus | timed normal-descent slow (applies a `FallSpeedMultiplier` status; not `Time.timeScale`) |

   Otherwise subclass the kind in `Definitions/` — one file.
2. Create the asset (Create > Stacking > Abilities > …) under `Assets/Data/PowerUps/`.
   Set rarity, unique/maxStacks, charges, conditions.
3. Add it to a mode's Power Up Choice Pool.
4. **Presentation is part of done** (the §13 juice standard is the reference): card icon via
   `Tools/generate_ability_icons.py` per ART.md §12, and the §13 juice standard
   (slot punch is free; the ability owes its own transform/impact moment — pick
   Cartoon FX `CFXR…` prefabs into serialized effect fields and play them via
   `Vfx.Spawn`, layered with hit-stop / camera kick / a custom sfx).

**Transform-style consumable** — the generic `TransmuteAbility` (Consumable) already
covers "the active piece becomes shape X": set its `targetShape` to any `BlockDefinition`
and it swaps via `Spawner.ReplaceActivePiece` (Shrink → the 1×1 Pip is just an asset; a
1×2 Domino Shrink would be another asset, no code). Reach for the recipe below only when
the replacement needs its OWN behaviour on lock (e.g. a piece that detonates where it lands): the
replacement is a full block variant — `BlockData`
subclass + behaviour in `Blocks/Variants/`, a 1-cell prefab cribbed from an existing 1×1
prefab (`Block_Pip.prefab`), a `BlockDefinition` + data asset, a `piece_<Name>.png`
skin sprite in `Skins/Classic/` (or ApplyBlockSkin warns per swap) — wired into
the ability asset and swapped in via `Spawner.ReplaceActivePiece` (validates
before destroying; by default `DefaultData` + does NOT re-raise `BlockSpawned`,
i.e. the same logical turn — pass `asNewSpawn: true` only when the result is a
genuinely new piece entering play, which rolls variants and raises `BlockSpawned`,
as the Pocket Cache bank does).
`CanActivate` must pre-check every way `Activate` could fail (the slot is
consumed first): config wired, piece in the air and not already transformed,
piece not below `LossZone.CullY`. Never replace the piece outside the Spawner —
the lock→spawn chain has no retry.

**New shared effect helper:** project-wide impact/destruction juice used by abilities AND block
variants (ImpactPunch / BurstFromEveryCell / DestroyBlockWithShatter) is a static method in
`Core/ImpactFx.cs`; ability-specific guards (e.g. CanTransmuteActivePiece) stay in
`Effects/AbilityEffects.cs`. Effects touching the world follow PHYSICS.md: velocity or
lifecycle only on landed blocks; spawned static geometry matches the world contract
(friction 0.95, footprint 0.94, corner radius 0.06, never materialize intersecting
anything).

**New status effect**: a `StatusEffectDefinition` asset. New kind = enum member + its
consult point in the relevant core system.

**New scheduled chapter/level effect**: usually no new scheduler code. Create a
`StatusEffectDefinition`, add visuals via `screenEffect`, add a listener component only if
the state needs custom gameplay, then schedule it with `ScheduledStatusModifier` on the
`LevelDefinition`.

**New combo trigger**: a `ComboTriggerDefinition` asset; new relation = enum member +
one case in `ComboDetector.Matches`.

**New trigger-able game event**: add to `GameEvents` (with its `Reset()` entry), raise
at the source, add a virtual handler on `PassiveAbility`, fan out in `AbilityRuntime`.
Fan-outs iterate a per-call snapshot of the inventory, so a handler MAY safely re-raise a
dispatched event (e.g. spawn a piece) without corrupting the in-flight loop.

## 10. Rules for ability EFFECTS (hard constraints)

- **Never** write position/rotation on landed blocks (PHYSICS.md I1). Velocity
  (`ApplyJolt`) and lifecycle (`FreezeInPlace`, `Destroy`) are the legal verbs.
- Loss interception receives **landed blocks only** — the active piece always takes
  the normal loss path (saving it would strand the spawner's control gate). An
  interceptor that returns true must leave the block non-lost (frozen or destroyed),
  or the 10 Hz cull sweep re-fires and drains every armed charge.
- Abilities may grant score (it's the progression currency), but the per-block
  difficulty ramp must stay tied to real placements — `BlockLedger` sends the
  unamplified physical placement count to `DifficultyController`.
- Timed windows are status assets (§5), not private coroutines.
- State pushed into OTHER systems (e.g. the Spawner's variant-chance registry) must be
  applied as **deltas** (the registry accumulates) and is **irreversible** — there is no
  unregister path, so never combine registry pushes with `charges > 0`. Prefer
  pull-style hooks (`GetFallSpeedFactor`) that vanish with the ability.
- Known quirk (pre-existing scoring semantics, accepted): a piece lost off-screen still
  scores +1 on its forced lock. During `LifeLossImmunity` that loss is also free, so
  deliberately dumping pieces progresses at normal pace with zero risk for the window's
  duration — bounded by the status duration; revisit only if scoring semantics change.

## 11. Runtime reference

- **`AbilityContext`** (one type for picking/availability/activation/handlers — extend
  it, never method signatures): `GameManager`, `Spawner`, `Runtime` (AbilityRuntime),
  `Status` (StatusEffects), `Config` (active GameModeConfig), `Level` (null in quick
  play), plus `LevelHasVariant(BlockData)`.
- **Rarity odds** live in `AbilityRarityProfile` stages (see §7); `AbilityRarityInfo`
  owns only the rarity colors. Offers are single-rarity: rarity rolled by profile
  weights, then 3 uniform picks without replacement within it. An offer earned during
  a pause/win-verification is deferred, not dropped; the milestone check is
  crossing-based so bonus score can't skip one.
- **Offer cadence** (2026-07-30, "abilities are a fun little extra"): every 20 placed
  blocks (`GameModeConfig.powerUpChoiceEveryBlocks`, all non-wave configs). Wave modes
  author it 0 (no block cadence) and instead grant ONE offer per cleared wave —
  `HeightLimitWavesModifier` calls `AbilityChoiceController.QueueOffer()` on the
  confirmed clear, presented under the same gates as milestone offers. Quick Study's
  early-offer threshold still fires when the cadence is 0.
- **Consumable gates** (blanket, before per-ability `CanActivate`): `GameManager.CurrentPhase == Playing`,
  not paused, and no active-piece session owns the field.
- All ability components live on the GameManager's object (added in `GameManager.Awake`,
  order matters: StatusEffects → AbilityRuntime → ComboDetector → AbilityHud →
  AbilityChoiceController).

## 12. Testing abilities (Custom Game)

The dedicated "Ability Range" bench (the `Chapter_TestingGrounds` sandbox, `GameMode_AbilityTest`,
and the inert dummy assets) has been **removed**. Test abilities through the **Custom Game** screen
instead: it auto-discovers every ability asset via `ContentCatalog`, lets you enable any subset, and
runs them with the equal-odds `RarityProfile_TestEqual` and a fast picker cadence — so a new ability
needs no list maintenance to appear. Unique abilities filter out once picked (restart to re-test);
non-unique ones reappear until `maxStacks`. See CUSTOMGAME.md. (Custom Game is editor/dev-only.)

## 13. Shipped abilities

> Highlights, not an exhaustive registry — it documents the abilities with notable mechanics or
> reusable patterns. The authoritative list is the asset set under `Assets/Data/PowerUps/` (and the
> auto-discovered Custom Game screen). Simple data-only variants (extra-life, block-chance boosters,
> combo/status assets, etc.) may not each have an entry here.

### Recovery (Common, passive, unique) & Slo-Mo (Common, consumable)
A shared **slow window** on `AbilityRuntime` (`GrantSlowWindow(blocks, factor)`): the next
N *normal-descent* spawns fall at `factor` of base speed, counted down per spawn (not a
timer — follows the player's pace). `RecoveryWindowAbility` (Recovery) grants 3 blocks @
0.5 on life lost; `SlowWindowConsumable` (Slo-Mo) grants 5 @ 0.5 on activate. Overlapping
grants take the stronger slow + longer window.

**Fast drops are immune (the key rule).** The slow is applied as a *normal-descent-only*
factor: the block is stamped at spawn with the un-factored `BaseFallSpeed` plus the
`AbilityFallSpeedFactor`, and `BlockController.GetActiveFallSpeed` applies the factor **only**
when the player isn't fast-dropping — hold / down / flick all use `base × fastDropMultiplier`
with no slow. A player who chose to go fast is never fought. **This also routes Air Brake's
multiplier through normal-descent-only** (its ~8% no longer touches fast drops — intended).
**No slow-time ability touches `Time.timeScale`** — a global slow (the old `SlowMotionPowerUp`
behaviour) dragged fast drops and the whole simulation into slow motion too, which was wrong.
Slow Time (`SlowMotionPowerUp`) now applies a 15 s `FallSpeedMultiplier` status instead, which
`AbilityRuntime` folds into this same per-block normal-descent factor; the block-count window
(Slo-Mo/Recovery) and the duration status both ride the one fast-drop-immune path.

### Rebound (Rare, passive, unique)
`ReboundAbility` is a loss interceptor (the Sacrifice pattern, gentler): when a LANDED block
crosses the loss line, a `saveChance` (0.2) roll teleports it back to the FRONT of the
spawn queue (`Spawner.RequeueDefinition`) instead of charging a life - and unlike Sacrifice,
nothing else is destroyed. Only the **shape** returns; its variant is re-rolled on respawn.
On the 80% miss it returns false and the normal loss proceeds. Landed-only by contract:
the active piece driven into the abyss is never offered to interception (it would strand the
spawner), so it still costs a life. Permanent (charges 0) - always armed, 20% each time.

The save plays `RescueLift`: the block is removed from accounting (`BlockDestroyed`),
neutralised (physics off so it can't fall or shove the tower), then beamed up on a soft cyan
light and dissolved into a per-cell CFXR magic burst (`cellBurstEffect`, swappable) before
it's destroyed. Moving the block's transform here is allowed - it is no longer a live
gameplay block, and the loss guard is already consumed upstream so the cull sweep never
re-fires on it.

### Pocket Cache (Rare, passive, unique)
`PocketCacheAbility` just calls `context.Hold.Enable()`; all behaviour lives in `HoldCache`
(a run-local component on the GameManager object) and the `HoldButton` HUD. The cache holds one
block **shape** (variant re-rolls on respawn, like the queue) and a circular bubble button
appears on the left, just above mid-height:
- **Bank** (cache empty): the current shape is stored and the **next queued** piece spawns from the
  top as a genuinely fresh piece — `Spawner.TakeNextQueued` + `ReplaceActivePiece(next, SpawnPosition,
  asNewSpawn: true)`, which rolls variants and raises `BlockSpawned` so the banked-in piece joins
  combos / slow windows / on-spawn passives exactly like a normal spawn. A white ghost of the banked
  shape flies from the field into the bubble.
- **Swap** (cache full): the cached shape returns **in place, lifted slightly** (`ReplaceActivePiece`
  at the active piece's position + up, `asNewSpawn: false` — it's the same turn, like a transmute:
  `DefaultData`, no `BlockSpawned`). It rises then falls again, buying a beat, and the shape you were
  driving takes its slot. The bubble snaps to the new shape instantly with a pop.

Both build on the Spawner's transmute primitive (`ReplaceActivePiece`, now with an `asNewSpawn` mode),
so there's little new lifecycle code — `HoldCache` owns only the cached shape, the bank-vs-swap
decision, and a **one-hold-per-piece lockout**. The lockout resets on `GameEvents.BlockLocked(block)`
(raised at the block when a piece LANDS) rather than `BlockSpawned` — that's deliberate, because the bank raises a
fresh `BlockSpawned` and keying the lockout off spawns would let it reset itself and re-hold for free.
So you must LAND a piece before holding again, and a just-swapped-in piece can't be swapped straight
back out. `CanHold` also gates on a live controllable piece (`CurrentPhase == Playing`, not paused; other phases have
nothing to swap), and both paths check `ReplaceActivePiece`'s return before committing cache state.
The held shape shows as a **white** silhouette — luminance normalised by the piece's brightest pixel
then gamma-lifted, so cell seams survive while it reads white — with a very slight idle wave.
`unique = true`, charges 0. The circular button uses a reusable, theme-neutral `RuntimeSprites.Bubble()`
(glassy disc + thin rim).

### Edge Portal (Common, passive, unique)
`EdgePortalAbility` toggles run-local horizontal wrapping on `BlockController`. While a
piece is still actively controlled (not touched down, not landed, not flick-dropping),
a sideways step that crosses the current camera edge wraps the target column to the
opposite camera edge. The wrapped target is then classified through the normal
side-step collision checks, so the portal cannot intentionally place the piece inside
blocks or static islands. The camera bounds are live, so the portal width follows the
current zoom. `unique = true`.

### Sacrifice (Rare, one-shot passive, unique)
`SacrificeAbility` uses the intercepting `TryInterceptLoss` hook: the first **landed**
block that falls below the loss line is destroyed before it can charge a life, then the
current topmost other landed block is destroyed as the cost. Both blocks use the shared
impact composition (`BurstFromEveryCell` with the authored `impactEffect`,
`impact_shatter_01`, `ImpactPunch`) so it reads as a real detonation, not silent cleanup.
While armed, a layered blue laser line follows `LossZone.InterceptLineY` (the always-
on-screen raised line — see §4). `charges = 1`, `unique = true`.

### Hardline (Epic, one-shot passive, unique)
`HardlineAbility` is the constructive sibling of Sacrifice. While armed, it renders a
purple laser line slightly above Sacrifice's blue line and has a higher
`LossInterceptPriority`, so if both are owned the visible upper catch line gets first
refusal. The first **landed** block lost below the screen is immediately neutralised
as kinematic, eased into a platform pose, and left as a `Static` Rigidbody2D that
remains in `BlockController.AllBlocks` as real stackable terrain. The platform pose is
**grid-snapped** (July 2026): its bottom sits on the first row boundary (datum + n·grid)
at/above the laser and its cells sit in real columns, with whole-column overlap nudges —
stacks grown on the platform line up exactly with stacks grown from the floor.

The platform pose is computed from the block's cell colliders, not hardcoded per shape:
it tests the four cardinal rotations, maximises horizontal width, then breaks ties by
the number of cells forming the upper surface, lower height, and smallest rotation.
That makes I/domino pieces lie flat and favours L/T-style orientations with the broad
side on top. A small overlap nudge tries to keep the rescued platform out of the tower
without teleporting it far from where it fell. `charges = 1`, `unique = true`.

Juice: the catch flashes the laser line, plays a swoosh + `ImpactPunch`, and bursts a
swappable per-cell `catchEffect` (a serialized CFXR prefab, base prefabs only per the §13
gotcha) across the block — unassigned by default, so it degrades to the flash+punch until
an effect is dragged in.

### Brace (Epic, passive, unique)
A `StatusPassiveAbility` asset (`triggerEvent = LifeLost`) that applies the shared
`LifeLossImmunity` status (the same 10 s `Status_LifeImmunity10s` the old Stasis consumable
used). Losing a life opens a 10 s window in which `GameManager.GameOver()` absorbs every
further charge, so a whole-tower collapse during it costs exactly the one life that opened it.
`charges = 0` (permanent — re-arms every life loss), `unique = true`. No new logic: it's a pure
status grant, and during the window no life is actually lost, so the next loss *after* it
expires re-opens it. Replaces the Stasis consumable (removed). The active window is surfaced by
the reusable status presenter (§5): a warm orange edge-haze (the Hovl "Screen buff" overlay with
its arrows disabled, smoke recoloured), held for the 10 s, plus a soft "shield up" swoosh on
engage.

### High Friction (Common, passive)
`BlockFrictionPowerUp` adds a run-local multiplier delta to the shared standard-block
fallback physics material. Existing and future standard blocks share that runtime
material, so each stack immediately makes ordinary block-on-block contact grippier.
Variants with explicit physics materials keep their authored behaviour (for example,
Ice stays slippery). Front-loaded like the supply passives: `firstStackIncrease` (0.3)
is a perceptible first pick, `additionalStackIncrease` (0.1) tops up later stacks.
Friction governs sliding/shearing, not tipping, so even maxed it can't trivialize a
top-heavy tower; and the floor/islands stay un-boosted (0.95), so √-mixing halves the
benefit on the base layer — a natural governor. `maxStacks = 3`.

### Last Stand (Rare, passive, unique)
`LastStandAbility` slows normal descent while on the **last life** (`lives == 0` — the next
lost block ends the run) by a **flat offset**: `reductionFraction` (0.2) of the level's
*initial* speed, held constant as the difficulty ramp climbs (100%→80%, 200%→180%). It is
NOT a multiplier — it deliberately does not slow acceleration; its *relative* help fades as
the game speeds up (−20% at start → ~−4% near max). Implemented through `GetFallSpeedFactor`
as the multiplier that yields that flat offset at the current speed, recomputed per block,
which also makes it normal-descent-only (fast drops stay full speed). Turns off on the next
block spawned after a life is regained (the factor is restamped per spawn). Note: most modes currently start at `lives == 0` (sudden
death), so there it's active for the whole run once picked — a permanent flat cushion; in
life-granting modes it's the genuine late-game clutch.

### Air Brake (Common, passive)
`FallSpeedReductionPowerUp` overrides `GetFallSpeedFactor` to return `1 − reductionPerStack
× stacks` (0.08/stack), which `AbilityRuntime` folds into `GameManager`'s spawn-speed
multiplier. It is the *pull-style* hook the fall-speed getter was built for ([GameManager](Assets/SourceFiles/Scripts/Core/GameManager.cs)
comment: ability effects compose as a multiplier IN THE GETTER, never by mutating the
ramp). It scales the whole speed curve down, so a given speed is reached at a later block
AND the effective top speed comes down — the brake persists on long/endless runs instead
of evaporating at the cap. It is deliberately NOT a `FallSpeedMultiplier` status (those are
timed; this is permanent and stack-scaled). `maxStacks = 3` (×0.76 at full, ~24% slower).

### Foresight (Common, passive, unique)
`QueueVisibilityPowerUp` raises the Spawner's visible look-ahead depth on acquire
(`context.Spawner.SetVisibleQueueDepth(2)`). The Spawner keeps a **stable** depth-N queue
(`_upcoming`): shapes are pre-rolled and held, never re-rolled on advance, so the second
preview is exactly the shape that spawns. `NextBlockChanged` carries the whole visible
list (front first), so the data layer supports any depth; the HUD renders up to
`Spawner.MaxVisibleQueueDepth` slots. The NEXT card grows **downward** to a smaller,
dimmer second slot — the top slot is byte-identical to the single-preview layout, and the
relayout only fires when the slot count actually changes (`UIManager.EnsureSlotLayout`).
`unique = true`; resets per run with the fresh Spawner. Note for any future shape-bias ability:
one picked up *after* a shape is already queued won't bias that already-locked shape (the bias
applies going forward) — acceptable.

### Flip (Common, consumable, max stack 1)
`FlipAbility` swaps the active falling shape with the front of the Spawner's stable
look-ahead queue via `Spawner.SwapActiveWithNextQueued`. The incoming queued shape is
instantiated at the active piece's current world position, wired through the same path as
a normal spawn, and raises `BlockSpawned` because it is a queued piece entering play early.
The outgoing active shape becomes the new front of `_upcoming`, so the NEXT preview updates
immediately to show the piece the player just traded away; activation is refused if the
next shape is identical to the active one. This is shape-only, matching
Hold/Rebound queue semantics: any variant on the outgoing piece is not preserved and will
reroll when that shape later spawns. Activation is refused unless there is a live controlled
piece, a valid queued next shape, and the active piece has not fallen below the loss cull.
Like other consumables, Flip is locked out while Fission/Overdraw-style consumable sessions
own the active-piece state. The asset uses `maxStacks = 1`, not `unique`, so it can be
offered again after spending it.

### Vector Guide (Common, passive, unique)
`VectorGuideAbility` toggles a run-local landing ghost on `BlockController`. The active
piece still shows the normal placement beam; once owned, the beam also gets a
translucent copy of the current piece at the first straight-down support contact. The
preview uses the active block's real colliders and the same landing-support filter as
controlled landing, but it is visual-only and intentionally does not simulate
post-contact tipping. `unique = true`, so the picker filters it out after one pickup.

### ~~Spike Supply~~ / ~~Cube Supply~~ — REMOVED (2026-07-29)
Two `BlockDefinitionChancePowerUp` assets (targeting `Block_I` and `Block_O`) that nudged the
shape odds a few percent per stack. **Deleted at Nick's call: "really stupid"** — a passive
+8%/+5% shape bias is invisible in play, so the card read as a wasted pick in a 3-card offer.
Gone are the assets, their icons, and their entries in every `powerUpChoicePool`, the
`ContentManifest` and the icon-gen roster. The `BlockDefinitionChancePowerUp` **class survives**
(and `Spawner.AddDefinitionChance` with it) — it is a sound mechanism, just not at that
magnitude; any future shape-bias ability should be a big, legible effect rather than a nudge.
Do not re-add either card without a much stronger dose.

### Shrink (Common, consumable)
`TransmuteAbility` with `targetShape` = the **Pip** (1×1) `BlockDefinition`. Activating
swaps the active falling piece for a Pip via `Spawner.ReplaceActivePiece` (same lock→spawn
chain, re-reports flags), with a `swoosh` + a CFXR transform burst. The Pip is a **normal
brick** (counts + costs a life — `Normal` data), so a shrunk
piece scores and is at risk exactly like any block; it's just small enough to slot into a
tight gap. `CanActivate` uses the shared `AbilityEffects.CanTransmuteActivePiece` guard (piece in
the air, not mid-lock, not already a Pip, not past the loss line, target wired). `TransmuteAbility`
is generic, so a future 1×2 "Shrink" targeting the **Domino** is one more asset, no code.

**The Pip (1×1) and Domino (1×2) bricks** are standalone `BlockDefinition`s
(`Data/BlockDefinitions/`) skinned in Classic + Desert (other themes inherit Classic).
They are in **no** spawn bag — they only enter a run when an ability introduces them:
Shrink swaps one in (`ReplaceActivePiece`), and the Pip/Domino abilities inject them into
the spawn roll (`AddInjectedDefinition`). Bag membership and chance-injection stay
*separate* paths — never add these to a mode's `blockBag` expecting a rare drop (that
injects `bagCopies` natural copies every refill); use the injection registry.

### Pip (Common, passive, unique)
`BlockDropChancePowerUp` targeting the **Pip** (1×1) `BlockDefinition`. On acquire it calls
`Spawner.AddInjectedDefinition(Pip, 0.05)`: the brick is marked run-spawnable and registered
in the definition-chance roll, so ~5% of spawns become a Pip — about one every ~20 pieces,
roughly a third the rate of a normal shape in a 7-bag — without ever touching the authored
bag (the other shapes shave proportionally, ×0.95). It is an *average*, not a strict cadence
(the roll is per-piece independent, not a bag slot). `unique = true`. Because
`AddInjectedDefinition` makes the brick pass `CanSpawnDefinition`, a future "more Pips"
booster is simply a `BlockDefinitionChancePowerUp` targeting Pip — its `IsAvailable` gate
(`CanSpawnDefinition`) is false until this ability introduces the brick, so it self-gates to
"only offered after Pip is owned." That's conditional availability with **no new
infrastructure**; for an arbitrary "requires ability X" prerequisite, override `IsAvailable`
and query `context.Runtime.GetOwnedStacks(prereq) > 0`.

### Domino (Common, passive, unique)
Same `BlockDropChancePowerUp` pattern targeting the **Domino** (1×2) brick at `0.05`. Two
injected bricks just sum their chances in the roll (Pip + Domino owned = ~10% forced, split
between them; the bag fills the remaining ~90%). `unique = true`.

### Scrap (Rare, consumable, max stack 1)
`ScrapAbility` deletes the latest counted placed block. `BlockLedger` records
`LastPlacedBlock` when a block successfully scores/enters the live placed-block count, and
clears that reference when the block is destroyed or resolved by the loss system. This is
deliberately not a tower scan: "last placed" means the last piece the player added, even if
physics has already pulled it away from the tower and it is falling toward the loss line.
Activation calls `ImpactFx.DestroyBlockWithShatter`, which removes the block from
the live count and destroys the object before `LossZone` can charge a life. It cannot undo
a loss that has already been resolved. Like other consumables, Scrap is locked out while
a consumable-driven piece sequence such as Fission or Overdraw is active. The asset uses
`maxStacks = 1`, not `unique`, so the player can hold one Scrap at a time but may be offered
another after spending it.

### Suspension (Rare, consumable, max stack 1)
`SuspensionAbility` reuses the Extract targeting presentation: visible landed tower blocks
are hidden behind floating visual proxies, the game pauses, and the player taps one proxy.
Instead of deleting the chosen block, the shared `ExtractTargetingSession` runs in Suspension
mode and, on the real block, applies the **Anchor** `BlockData` variant (`ApplyData`, wired as
the ability's `anchorVariant` field) and then calls `BlockController.FreezeInPlace()`. Applying
the anchor data re-tints the existing skin so the block visually *becomes* an anchor brick (it
adopts whatever look the Anchor variant carries — today the bluish `colorTint` — so future anchor
styling flows to suspended blocks for free); the freeze turns it into a `Static` Rigidbody2D at
its current world coordinates, so it remains as permanent anchor-like terrain even if every
supporting block underneath later disappears.

Suspension only offers/selects landed blocks that are not already frozen/static, so a held
charge cannot be wasted on an existing anchor brick, Freeze target, or previous Suspension
target. **Maws are never offered or selectable** (`ExtractTargetingSession.CanTarget` excludes
anything with a `MawBlockSkin`): they stay put and stacked while the rest of the tower flies
out, never proxied, never moved — a maw welds into one rigid cluster and draws through a
vertex-colour-ignoring shader, so spreading it would both break the weld illusion and leave the
real maw visible behind its proxy. `CanActivate` mirrors this exclusion, so a screen of only
maws leaves the charge unusable instead of no-op'ing on activation. It uses the same
visible-screen filter as Extract and the normal consumable lockout while Fission/Overdraw-style
sessions own active-piece state. The asset uses `maxStacks = 1`, not `unique`, so the player can
hold one Suspension at a time but may be offered another after spending it.

The fly-out hides each real brick by **disabling its renderers**, not by zeroing their colour
alpha — procedural brick shaders (Maw, Magma) ignore the SpriteRenderer vertex colour, so an
alpha-0 hide left them fully visible behind the moving proxy. Disabling is shader-independent
and never touches RGB, so a Suspension recolour on the chosen block survives teardown; the
selected block is the one renderer set left alone on restore (its `ApplyData`→Anchor re-skin
already owns it).

### Overdraw (Rare, consumable, unique)
`OverdrawAbility` replaces the current active falling piece with a three-shape draft.
Activation destroys the active piece without locking/scoring, suspends the Spawner's
automatic lock-to-next-bag-piece path, and draws three upcoming definitions via
`Spawner.TakeDistinctQueued` (preferring different shapes; duplicates only fill if the
mode cannot supply enough distinct draws). `OverdrawSession` presents those choices as
world-space, chapter-skinned previews just below the top HUD. The player clicks or taps
the first two choices; the final remaining choice auto-commits. A selected preview glides
into the spawn lane, then `Spawner.SpawnControlledPieceAt(..., asNewSpawn: true)` creates
the real controllable piece so `BlockSpawned` passives, variants, slow windows and scoring
all treat each chosen shape as a fresh piece. The NEXT preview is hidden only while the
draft UI is active; it returns as soon as the final Overdraw piece starts falling. When
that last chosen piece locks, the session clears auto-spawn suspension before the Spawner
continues, so normal play resumes on the next bag piece. Audio currently reuses
`swoosh_01` for activation and manual choice commits; the final auto-commit stays silent
so the third-piece handoff does not double-hit the ear.

### Zap (Common, consumable)
`ZapAbility` — the active falling piece **vanishes** (no lock, no score) and `ZapSession` (the
`AbilitySessionBase` active-piece sequence created via `Begin`) takes over. It captures the column X
and the first **dynamic, landed `BlockController`** straight below the
piece — the target; the floor, support islands and frozen (Static-body) blocks are zap-proof, the
shot is wasted — then withholds bag pieces (`Spawner.SetAutoSpawnSuspended`) so the field holds
still, and summons a vertical `ZapBeam` from the top of the screen down to the target. Over **exactly
3 seconds** the beam charges from a wide glow to a thin needle; on full charge the target detonates
through the shared shatter path (`ImpactFx.DestroyBlockWithShatter` — removes it from the live
count, [BLOCKS.md](BLOCKS.md), plus a per-cell `detonateEffect` burst + `ImpactPunch`), or a soft dud
plays for an empty column. On finish it just **clears the auto-spawn hold** (`SetAutoSpawnSuspended(false)`):
that republishes spawn availability and the next bag piece spawns on its own — no explicit kick, no
`ResumeSpawning`. The charge runs on
**scaled time** and is held unless `GameManager.CurrentPhase == Playing` and unpaused, so a Zap can never fire behind
those screens (PHYSICS.md). It is **not a block variant** — nothing falls; the laser does the work.

The beam is `ZapBeam`: layered soft **vertical** bars (outer glow → blue body → cyan filament →
white needle, via `RuntimeSprites.SoftVerticalBar`) that glow through the global bloom — no shader, a
crib of `SacrificeLaserLine`. Colours are HDR-bright (`beamColor`/`accentColor` on the asset) and it
draws IN FRONT of the tower (a momentary dramatic overlay, unlike Sacrifice's persistent warning
line). Lockout is one line: `AbilityRuntime.ConsumablesUsable` gates on `!ActivePieceSession.AnyActive`
(the shared registry every active-piece session joins — Zap included), so no consumable can fire mid-charge. `CanActivate` refuses without a piece in the air, on a
landed piece, or on a doomed below-screen piece.

**Juice standard (applies to every future ability):** activating must FEEL
like something happened. Zap, Bomb and Fission set the bar:
- **Authored VFX prefabs**, played through `Vfx.Spawn(prefab, position, scale)`.
  The effects come from the **Cartoon FX Remaster** packs (`Assets/JMO Assets/`,
  prefab prefix `CFXR…`); each ability references its effects as **serialized
  prefab fields** on its asset (e.g. Zap's `detonateEffect`, Bomb's blast
  prefab) so swapping a look is a
  drag-and-drop in the Inspector, never a code change. Prefer **Unlit + HDR**
  CFXR variants (consistent in 2D, glow through the global bloom). CFXR prefabs
  self-destroy (`CFXR_Effect`), so `Vfx.Spawn` only instantiates.
  - **Break from the whole body, not one point.** A block shatters across all its
    cells: `ImpactFx.BurstFromEveryCell` spawns the effect at each cell
    collider's centre (a 1×4 I-piece erupts from four origins, a square from one),
    each burst sized to a single cell. A single centre-burst scaled up reads as a
    detached explosion in the middle — spawn per cell instead. The asset's
    `effectScale` is a *multiplier* on the per-cell size (1 ≈ cell-sized). This is
    code, not the prefab: **swapping the effect keeps the per-cell behaviour**.
  - **Gotcha — base vs variant prefabs:** many CFXR effects (the colored
    recolors: `SLASH`/`FIRE`/`ICE`/… sword hits, etc.) are prefab *variants*.
    A variant's root isn't a real object in its own file, so a hand-authored
    YAML reference to it resolves to null and the effect silently doesn't play.
    Assign variants **only by drag-drop in the editor** (Unity computes the
    reference). Effects wired by editing the `.asset` text must be **base
    prefabs** (e.g. `CFXR4 Sword Hit PLAIN (Cross)`, not the `SLASH` variant).
- **The bits the pack doesn't do are still ours:** the slot-button elastic
  punch (`AbilityHud` via `FxKit.Elastic`), and `ImpactFx.ImpactPunch` —
  the shared "this hit had weight" combo of a pause-safe micro hit-stop
  (`HitStop.Trigger`) + a camera kick (`TowerCameraController.Impact`). Layer
  these + a custom sfx *with* the prefab — that combination is what reads as
  "expensive."
- Never scale or move the physics piece for effect — spawn overlays/prefabs at
  its position instead (PHYSICS.md).

**Shared building blocks (use these, don't re-roll them).** The reusable verbs
behind the standard, so the next consumable composes instead of copy-pasting:

| Helper | Use |
|---|---|
| `Vfx.Spawn(prefab, position, scale)` | play any authored effect prefab (null-safe; forces z=0) |
| `ImpactFx.BurstFromEveryCell(block, prefab, scale)` | shatter an effect across every cell of a block |
| `ImpactFx.ImpactPunch(stop?, shakeAmp?, shakeDur?)` | hit-stop + camera kick |
| `ImpactFx.DestroyBlockWithShatter(block, tint)` | destroy a block with the standard shatter |
| `BlockQuery.SupportBlockBelow(block)` | nearest dynamic, landed block beneath (statics/frozen excluded) — `BlockQuery` lives in `Blocks/` |
| `BlockQuery.IsOnScreen(block)` | is the block within the camera viewport (shared by the targeting abilities) |
| `FxKit.Elastic(t, amp, damp, freq)` | the game's one elastic settle curve |

> **These honour the player's Graphics settings for free** (see [GRAPHICS.md](GRAPHICS.md)):
> `Vfx.Spawn` no-ops when **Visual Effects** is off, and `ImpactPunch` /
> `TowerCameraController.Impact` no-op when **Screen Shake** is off. So compose with these
> helpers and your effect is toggle-able automatically — never `Instantiate` an effect prefab
> or move the camera directly.

`ZapSession.Fire` and `BombBlockBehaviour` show the composition: find the target →
`BurstFromEveryCell` + `ImpactPunch` + sfx → `DestroyBlockWithShatter`. Every reusable mechanic is
one of the calls above; the ability owns only its own decisions (guards, which sounds).

### Fission (Epic, consumable)
`FissionAbility` shatters the active falling piece into one independent **1×1 Pip shard per
cell** (a tetromino → 4, a domino → 2). `Activate` plays the per-cell shatter (`BurstFromEveryCell`
+ `ImpactPunch` + `impact_shatter_01`) then hands off to `FissionSession`, a runtime-only driver
(an `AbilitySessionBase` sequence, created via `Begin`):
- Shard #1 reuses `Spawner.ReplaceActivePiece(pip, SpawnPosition)` (cleanly disposes the original,
  no lock/score) lifted to the **top spawn line**, then `BlockController.SetDescentSuspended(true)`
  so it **hovers** — steerable L/R but not falling. A downward **flick** (the normal commit
  gesture) auto-clears the suspension and the shard plummets and lands through the ordinary
  landing/lock path. See PHYSICS.md for why the suspension is I1/I5-safe (Kinematic, contact
  merely deferred).
- The remaining shards float above as a small **queue** of ghost sprites (cloned from the live
  shard's renderers, so they match the theme skin), with an idle hover bob. Each time the active
  shard locks (`GameEvents.BlockLocked(block)`, like `HoldCache`), the next shard is fed via the new
  `Spawner.SpawnControlledPieceAt(pip, SpawnPosition, suspended:true)` and the front ghost glides
  into the drop slot (smooth lerp, no teleport snaps); the row recentres.
- The session **withholds bag pieces** for its duration via `Spawner.SetAutoSpawnSuspended(true)`
  (a spawn hold in `GameManager`, so `CanSpawnBlocks` is false while held); it does **not** pause
  `Time.timeScale` (that would freeze the controllable shard — the "kinda paused" feel comes from no
  bag pieces + hovering shards). On the last shard's lock it clears the suspension, which republishes
  spawn availability and the next bag piece spawns on its own (serialized against the lock→spawn chain
  by the `ActiveControlled` guard — never two pieces); game-over mid-session tears down cleanly.

Each shard is a real `Block_Pip` (Normal data — counts +1, costs a life), so a tetromino that
normally scores +1 places **four counting blocks** (BLOCKS.md): the power, and the cost. The
original piece is destroyed without locking, so it was never counted (no `−1`). `CanActivate`
reuses `AbilityEffects.CanTransmuteActivePiece(context, pip)` (live piece in air, not mid-lock,
not past the loss line, Pip wired, **not already a Pip**) plus cell-count ≥ 2 and no session
already active. `splitEffect` is a swappable serialized CFXR field (base prefabs only; degrades to
flash+punch until assigned). **Deferred:** the spec's "infinite stacks / +1 charge" — consumables
don't stack today; ships single-use, revisit with a general stackable-consumable pass.
