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
| **Instant** | `InstantAbility` | `Apply()` once at pick, then gone | Extra Life, Slow Motion, Next-Block Variant |
| **Consumable** | `ConsumableAbility` | Held in one of 2 HUD slots; player taps to `Activate()` | Cement Tower (Flash Freeze), Stasis |
| **Passive** | `PassiveAbility` | Always on from pick; `charges` makes it one-shot | Recovery Window (permanent), Sacrificial Safety (charges = 1) |
| **Combo** | `ComboAbility` | Fires `OnComboFired()` when its trigger pattern lands | Overdrive (two upright I-pieces) |

A **one-shot passive is not a separate kind**: set `charges = 1` on a `PassiveAbility`
asset. A handler that returns true ("I triggered") consumes a charge; at zero the
ability leaves the inventory. Same convention on `ComboAbility` (0 = fires forever).
**Stacking a charged ability adds its charges** (two Sacrificial Safeties = two saves);
infinite stays infinite. Author abilities where a second copy means nothing as `unique`.

## 2. Presentation (every ability carries all four)

`AbilityDefinition` has a standard presentation block: **title** (`displayName`),
**icon** (`Sprite`, generated per ability later - empty falls back to the title text),
**short description** (one line; cards + swap dialog), **long description** (`TextArea`;
the details view). Short and long fall back to each other, so half-authored assets
degrade gracefully.

There is also a player-facing **type badge** (`AbilityDefinition.Type`), shown small at
the top of every card: Instant / Consumable / Passive / One-Time Passive. It is
**derived, never authored** - kind from the class, "one-time" from charges - so the
badge can never contradict what the ability does. Labels/colors in `AbilityTypeInfo`.

Consumption today: choice cards are the mockup chrome - a dark cut-corner plate + a
rarity-tinted glowing frame (both SDF sprites in `RuntimeSprites.AbilityCards`), title,
a badge plate with a generated type glyph (infinity = passive, ring-and-one =
one-time, flask = consumable, spark = instant), the icon (a spark placeholder until
real icons land), "Owned xN", short description and an outlined rarity-tinted
**Details** button; titles render in Rajdhani (Resources/Fonts, OFL) best-fit to one
line; legendary cards get an animated
shine sweep (`AbilityCardShine`). Details opens the detail view (type + rarity, icon,
title, LONG description, Choose/Back - Back returns to the same three cards, no
reroll). HUD slots show the icon (title text if none); the swap dialog shows title +
short. The detail view is the future home of per-ability explainer videos - the icon
and long text it needs are already authored.

## 3. The state rule (never violate)

Definitions are **immutable assets**. On acquisition, `AbilityRuntime` stores
`OwnedAbility { Source, Instance = Instantiate(Source), Stacks, ChargesLeft }`:

- **Identity** (unique checks, stack caps, bans, "Owned ×N" on cards) compares `Source`.
- **Callbacks** go to `Instance` — its plain instance fields are safe per-run state
  (see `RecoveryWindowAbility._remainingSlowBlocks`).
- Stacking never re-clones: `Stacks++` then `OnStackAdded(ctx, newTotal)` on the same
  instance. An instance that needs its stack count records it from those calls.

This is the LevelModifier clone-per-run pattern; SO fields written without cloning leak
state across runs (documented bug class — don't reintroduce it).

## 4. Ordering rules (deterministic, documented here on purpose)

- Inventory is ONE list in **acquisition order**.
- **Intercepting hooks** (`TryInterceptLoss`): first armed ability to return true
  handles the event and SHORT-CIRCUITS; later abilities stay armed. Multiple lost
  blocks in one sweep resolve in block-spawn order.
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
| `FallSpeedMultiplier` | folded into the spawn-speed multiplier | the multiplier (0.5 = half) |
| `ScorePerBlockBonus` | `GameManager.AddScore` adds it per grant | extra score (+1 = double progression) |
| `Custom` | nothing built-in; abilities query `IsActive(def)` | yours |

Stack policies: `RefreshDuration` (timer restarts), `ExtendDuration` (durations add),
`StackMagnitude` (magnitudes add, timer refreshes). Timers tick on scaled time —
pauses freeze every state for free. A new shareable state = one new asset; new code
only when a new KIND needs a new consult point in a core system.

Note: `ScorePerBlockBonus` amplifies score, and score is the progression currency (win
targets, picker milestones, wave counts all accelerate — that's the designed effect).
The picker milestone check is crossing-based so jumps can't skip offers, and the
difficulty ramp deliberately uses the UNAMPLIFIED amount.

## 6. Combo triggers — patterns separate from effects

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
4. `requiresVariantsInLevel`: every listed `BlockData` must exist in the mode's spawn
   tables (ambient chances or fallback variants) → **automatic** content conditions
   ("no Dizzy bricks in this level → don't offer the anti-Dizzy ability").

Exotic conditions: override `IsAvailable`, call `base` for the standard rules. An
offer whose candidates all filter out is quietly skipped (by design).

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
own `CanActivate`: not paused, not game over, **not during win verification** (a
freeze during the hold-steady countdown would cheat the sturdiness test). Slots dim
when unusable, same affordance as the nudge pills.

## 9. How to add things (recipes)

**New instant/consumable/passive/combo ability**
1. Reuse an existing class with new field values if possible — these cover most ideas
   with **zero code**:

   | Class | Kind | Fields | Covers |
   |---|---|---|---|
   | `StatusConsumableAbility` | Consumable | status | "activate: enter state X" |
   | `StatusPassiveAbility` | Passive | triggerEvent, status (+ charges) | "on life lost / on spawn: enter state X" |
   | `StatusComboAbility` | Combo | trigger, status (+ charges) | "pattern lands: enter state X" |
   | `BlockVariantChancePowerUp` | Passive (stackable) | variant, chancePerBlock | "% chance blocks spawn as variant V" |
   | `BlockDefinitionChancePowerUp` | Passive (stackable) | definition, firstStackChance, additionalStackChance | "shape X appears a little more often" |
   | `NextBlockVariantPowerUp` | Instant | variant | "next block becomes variant V" |
   | `ExtraLifePowerUp` | Instant | lives | flat life grant |
   | `SlowMotionPowerUp` | Instant | duration | timed timescale effect |

   Otherwise subclass the kind in `Definitions/` — one file.
2. Create the asset (Create > Stacking > Abilities > …) under `Assets/Data/PowerUps/`.
   Set rarity, unique/maxStacks, charges, conditions.
3. Add it to a mode's Power Up Choice Pool.
4. **Presentation is part of done** (the Bullet is the reference): card icon via
   `Tools/generate_ability_icons.py` per ART.md §12, and the §13 juice standard
   (slot punch is free; the ability owes its own transform/impact moment — pick
   Cartoon FX `CFXR…` prefabs into serialized effect fields and play them via
   `Vfx.Spawn`, layered with hit-stop / camera kick / a custom sfx).

**Transform-style consumable** (changes the active falling piece — the Bullet
pattern): the projectile/replacement is a full block variant — `BlockData`
subclass + behaviour in `Blocks/Variants/`, a 1-cell prefab cribbed from
`Block_Bullet.prefab`, a `BlockDefinition` + data asset, a `piece_<Name>.png`
skin sprite in `Skins/Classic/` (or ApplyBlockSkin warns per swap) — wired into
the ability asset and swapped in via `Spawner.ReplaceActivePiece` (validates
before destroying; does NOT re-raise `BlockSpawned` — same logical turn).
`CanActivate` must pre-check every way `Activate` could fail (the slot is
consumed first): config wired, piece in the air and not already transformed,
piece not below `LossZone.CullY`. Never replace the piece outside the Spawner —
the lock→spawn chain has no retry.

**New shared effect helper** (used by more than one kind): static method in
`Effects/AbilityEffects.cs`. Effects touching the world follow PHYSICS.md: velocity or
lifecycle only on landed blocks; spawned static geometry matches the world contract
(friction 0.95, footprint 0.94, corner radius 0.06, never materialize intersecting
anything).

**New status effect**: a `StatusEffectDefinition` asset. New kind = enum member + its
consult point in the relevant core system.

**New combo trigger**: a `ComboTriggerDefinition` asset; new relation = enum member +
one case in `ComboDetector.Matches`.

**New trigger-able game event**: add to `GameEvents` (with its `Reset()` entry), raise
at the source, add a virtual handler on `PassiveAbility`, fan out in `AbilityRuntime`.

## 10. Rules for ability EFFECTS (hard constraints)

- **Never** write position/rotation on landed blocks (PHYSICS.md I1). Velocity
  (`ApplyJolt`) and lifecycle (`FreezeInPlace`, `Destroy`) are the legal verbs.
- Loss interception receives **landed blocks only** — the active piece always takes
  the normal loss path (saving it would strand the spawner's control gate). An
  interceptor that returns true must leave the block non-lost (frozen or destroyed),
  or the 10 Hz cull sweep re-fires and drains every armed charge.
- Abilities may grant score (it's the progression currency), but the per-block
  difficulty ramp must stay tied to real placements — `AddScore` already handles this.
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
- **Consumable gates** (blanket, before per-ability `CanActivate`): not paused, not game
  over, not during win verification.
- All ability components live on the GameManager's object (added in `GameManager.Awake`,
  order matters: StatusEffects → AbilityRuntime → ComboDetector → AbilityHud →
  AbilityChoiceController).

## 12. The ability test bench (Testing Grounds → "Ability Range" level)

Picker every 3 blocks, PlaceBlocks 30 target, rarity profile = `RarityProfile_TestEqual`.
The mode's pool (`GameMode_AbilityTest`) is intentionally tiny and hand-edited to the
abilities currently under test (today: Bullet + Spike Supply + Cube Supply), so offers
stay focused while still exercising multiple-card presentation and pick/re-pick paths.
Building a new ability = temporarily add it here. The 12 inert dummies (`Data/PowerUps/Dummies/`) and the migrated
proof abilities still exist as assets for chrome/regression testing; never add
dummies to real level pools.

## 13. Shipped abilities

### Spike Supply (Common, passive)
`BlockDefinitionChancePowerUp` asset targeting `Block_I`. The first pickup registers
a stronger run-local definition chance with `Spawner.AddDefinitionChance`; later
stacks add smaller deltas. This injects extra straight pieces before the normal
authored shape bag is drawn. It never mutates `BlockDefinition.bagCopies`, so authored
mode data stays immutable. `maxStacks = 5`, so the picker filters it out after the
fifth pickup.

### Cube Supply (Common, passive)
Same `BlockDefinitionChancePowerUp` pattern as Spike Supply, targeting `Block_O`.
The first pickup adds the larger run-local definition chance and later stacks add
smaller deltas, making square pieces appear more often without mutating authored
shape-bag assets. `maxStacks = 5`.

### Bullet (Common, consumable)
`BulletAbility` — the active falling piece transforms into a 1×1 shell
(`Block_Bullet` prefab + `BulletBlockData`) via `Spawner.ReplaceActivePiece`,
which keeps the lock→spawn chain intact: the projectile IS a normal piece
(steering, fast drop, flick, rotation all free). On lock, `BulletBlockData
.OnLocked` runs `BulletImpact.Run` (static — no lifecycle): a downward BoxCast
finds the block it landed on; only a **dynamic, landed `BlockController`**
counts — the floor,
support islands, and frozen (Static-body) blocks are bulletproof, the shot is
wasted. Victim + bullet are destroyed (the authored `impactEffect` plays on the
victim — a Cartoon FX CFXR prefab — plus `impact_shatter_01`, a micro hit-stop,
and a camera kick); a wasted shot gets the `wastedEffect` dud + soft thud. The old piece is replaced
without locking (no score, no extra spawn). The bullet is flagged
`countsAsPlacedBlock:false` + `costsLifeWhenLost:false` (it isn't a real block), so
it never adds to the block total and never costs a life if pushed off; its victim
drops the live total by one (see [BLOCKS.md](BLOCKS.md)). Guards: no detonation
after game over or below the loss line (off-screen locks fizzle silently), and
`CanActivate` refuses without a piece in the air, on a piece that is already a
bullet, on a doomed below-screen piece, or with the projectile unwired.

Known quirks (accepted, by design or frozen-code constraint): bulleting the very
top block can briefly record a phantom max height (the bullet locks atop the
victim before both vanish — monotonic `_maxHeight` lives in frozen BlockController
code, and UpdateMaxHeight isn't suppressed the way scoring is);
a milestone variant applied directly to the falling piece dies with it if the
player then bullets that piece (their choice to sacrifice it).

**Juice standard (applies to every future ability):** activating must FEEL
like something happened. The Bullet sets the bar:
- **Authored VFX prefabs**, played through `Vfx.Spawn(prefab, position, scale)`.
  The effects come from the **Cartoon FX Remaster** packs (`Assets/JMO Assets/`,
  prefab prefix `CFXR…`); each ability references its effects as **serialized
  prefab fields** on its asset (Bullet: `transformEffect` on the ability,
  `impactEffect`/`wastedEffect` on `BulletData`) so swapping a look is a
  drag-and-drop in the Inspector, never a code change. Prefer **Unlit + HDR**
  CFXR variants (consistent in 2D, glow through the global bloom). CFXR prefabs
  self-destroy (`CFXR_Effect`), so `Vfx.Spawn` only instantiates.
  - **Break from the whole body, not one point.** A block shatters across all its
    cells: `AbilityEffects.BurstFromEveryCell` spawns the effect at each cell
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
  punch (`AbilityHud` via `FxKit.Elastic`), and `AbilityEffects.ImpactPunch` —
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
| `AbilityEffects.BurstFromEveryCell(block, prefab, scale)` | shatter an effect across every cell of a block |
| `AbilityEffects.ImpactPunch(stop?, shakeAmp?, shakeDur?)` | hit-stop + camera kick |
| `AbilityEffects.DestroyBlockWithShatter(block, tint)` | destroy a block with the standard shatter |
| `BlockQuery.SupportBlockBelow(block)` | nearest dynamic, landed block beneath (statics/frozen excluded) |
| `FxKit.Elastic(t, amp, damp, freq)` | the game's one elastic settle curve |

`BulletImpact` is the reference composition: `SupportBlockBelow` → `BurstFromEveryCell`
+ `ImpactPunch` + sfx → destroy. ~45 lines, all of it Bullet's own decisions
(guards, which sounds); every reusable mechanic is one of the calls above.
