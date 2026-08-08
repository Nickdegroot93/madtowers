# Block accounting — counting & life (binding contract)

How a block participates in the score, the live block total, and life loss. Two
**independent** per-block flags drive everything; any combination is valid. This is
the contract every new block variant and every ability that creates/destroys blocks
must respect. Lives on `BlockData` so it travels with the variant.

## The two flags (on `BlockData`, both default `true`)

| Flag | Meaning | Normal block | A non-counting piece | A future "free" block |
|---|---|---|---|---|
| `countsAsPlacedBlock` | Placing it `+1` to the live total; it leaving (destroyed **or** fallen) `−1`. | true | **false** | true |
| `costsLifeWhenLost` | Falling off the bottom costs a life. | true | **false** | **false** |

These two govern *accounting only*. `BlockData` also carries an orthogonal classification flag
`isHazard` (hostile bricks — Maw, Vortex, Bomb, …) that drives the "all hazards" abilities (Ward,
Purifier); it has nothing to do with counting/life. See BLOCKVARIANTS.md.

A non-counting piece is `false / false` — not a real block: it never counts and never
costs a life when pushed off. A "free" block (`true / false`) is a real block that
counts when placed but is safe to drop. The two are orthogonal — combine freely.

## The per-instance override (added 2026-08-09)

`BlockIdentity.SuppressPlacementCount()` excludes ONE SPAWNED INSTANCE from the placement
count, checked in `BlockLedger.HandleBlockLanded` right after the `countsAsPlacedBlock`
gate. It exists for **debris of a placement** — fragments that are physically real but were
not themselves placed.

Only user today: a **Magma** block melts into one stone Pip per cell, and all but the first
are suppressed. One magma placement is one block. Before this, a 2×2 magma paid four blocks
of score, four toward a `PlaceBlocks` goal and four toward a puzzle wave quota — a 7-block
wave cleared in two placements (Nick 2026-08-09).

Three properties that make it safe, and that a future user must preserve:

- **It cannot be a variant flag.** The Pip is a normal playable block elsewhere (the Pip
  ability drops one; Fission shatters a piece into shards the player places by hand) and
  must count there. The suppression is per instance, decided by whoever spawned it.
- **The FIRST fragment is left counting**, rather than crediting a phantom `+1`. That keeps
  the ordinary `−1` path honest: if that block later leaves the tower, `TryConsumeCounted`
  fires exactly once, as for any other block.
- **Suppressed fragments never counted, so they never decrement.** The return happens before
  `MarkCountedAsPlaced`, so `TryConsumeCounted` stays false and the live total cannot drift
  negative as they are destroyed.

Height is untouched: `TryUpdateMaxHeight` runs *before* the gate, so suppressed fragments
still raise the tower for `ReachHeight` goals. Note `LastPlacedBlock` (ScrapAbility's
target) becomes the first fragment rather than the last — deliberate, since that is the
instance holding the count.

**Authoring gotcha:** the flags default `true` *in C#*, but an existing `.asset`
saved before the fields existed has no key for them, so they resolve to `true`
regardless of intent (the serialized-default-staleness trap). To make a block
`false`, the key must actually be written to the `.asset` (untick in the Inspector
and save, or hand-author `countsAsPlacedBlock: 0` / `costsLifeWhenLost: 0`).

## The two numbers (don't conflate them)

- **`score`** — CUMULATIVE progression. Real placements only; **never decrements**.
  Drives the difficulty ramp, the ability-picker milestones, and rarity escalation.
  Overdrive (`ScorePerBlockBonus`) amplifies it. A non-counting block adds
  nothing; a lost/destroyed block subtracts nothing.
- **`placedBlocks`** (`GameManager.placedBlocks`, event `StandingBlocksChanged`) —
  the LIVE count of real placed blocks still standing. `+1` per *physical* placement
  (never amplified), `−1` when a counting block is destroyed or falls. Drives the
  **HUD total** and the **PlaceBlocks win target** (which now genuinely sets back
  when blocks are destroyed or dropped — the hold-steady verification aborts if the
  live count falls below target).

Why two: losing blocks must lower your visible total and your PlaceBlocks goal, but
must NOT rewind difficulty or revoke an earned picker (decided with Nick).

## The rules (where the bookkeeping lives)

The `+1` and the matching `−1` are tied to the **block itself**, not re-derived at each
site: when a placement counts, the block's `BlockIdentity` is marked counted; removal
decrements **exactly once** via `TryConsumeCounted()` (a double-remove is a no-op, not a
clamp-masked bug).

- **Placed** (`GameEvents.BlockLanded`, raised from the frozen lock): `BlockLedger`
  reads the block's `BlockIdentity.Variant`, so mid-fall swaps are naturally current.
  A counting placement does `score += amount`, `placedBlocks += 1`, records
  `LastPlacedBlock`, marks `BlockIdentity.MarkCountedAsPlaced()`, and advances the
  `DifficultyController` by the unamplified physical placement amount.
- **Destroyed** — *any* code that destroys a placed block MUST raise
  `GameEvents.BlockDestroyed(block)` first. `BlockLedger` is the single subscriber; it
  `−1`s only if the block's placement was counted (idempotent). Current funnels:
  `ImpactFx.DestroyBlockWithShatter` (so every ability that shatters a block is
  covered — Zap, Scrap, Sacrifice…), `BombBlockBehaviour`, `HeightLimitWavesModifier`,
  Extract, Rebound, and Sacrifice. **New destruction site → raise the event**, or the
  live count silently desyncs above reality.
- **Fell off** (`LossZone`, the single loss gateway, both the cull sweep and the
  trigger): runs the frozen `HandleLostBelowScreen` *inside*
  `GameManager.DuringBlockLoss(block, action)` — the one entry point that scopes the
  loss policy (try/finally, so a throw can't strand it). It decides the life charge
  (`costsLifeWhenLost`, read by `GameManager.GameOver`), asks `BlockLedger` to `−1`
  the live total once for a counted block, and suppresses the posthumous lock-score of
  the lost piece. An active piece pushed off was never counted, so it never `−1`s —
  only its life charge (if any) applies.
- **Devoured by a hazard** (the Maw): the prey is removed through `ImpactFx
  .DestroyBlockWithShatter` (so it `−1`s like any other destruction), and the maw
  additionally calls `GameManager.LoseLifeToHazard`. That life charge is INDEPENDENT of
  the prey's own `costsLifeWhenLost` — a hazard kill always costs a life (gated only by
  `LifeLossImmunity`), unlike a fall-off, which is gated by the lost block's flag.

## Quick effect check
- Normal block placed → `score +1`, `placedBlocks +1`.
- Zap destroys a landed block → that block `placedBlocks −1`.
- A `costsLifeWhenLost:false` block pushed off → no life, no count change.
- Normal landed block knocked off → `placedBlocks −1` and a life.
- Bomb detonation → `−1` per destroyed neighbour and the bomb itself.
- Maw devours a landed block → that block `placedBlocks −1` **and** a life — always, regardless of the eaten block's own flags.

See also: [BLOCKVARIANTS.md](BLOCKVARIANTS.md) (the variant catalog, looks & "add a brick" recipe),
[ABILITIES.md](ABILITIES.md) (abilities that create/destroy blocks),
[PHYSICS.md](PHYSICS.md) (never write transforms on landed blocks).
