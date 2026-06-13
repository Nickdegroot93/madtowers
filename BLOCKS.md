# Block accounting — counting & life (binding contract)

How a block participates in the score, the live block total, and life loss. Two
**independent** per-block flags drive everything; any combination is valid. This is
the contract every new block variant and every ability that creates/destroys blocks
must respect. Lives on `BlockData` so it travels with the variant.

## The two flags (on `BlockData`, both default `true`)

| Flag | Meaning | Normal block | Bullet | A future "free" block |
|---|---|---|---|---|
| `countsAsPlacedBlock` | Placing it `+1` to the live total; it leaving (destroyed **or** fallen) `−1`. | true | **false** | true |
| `costsLifeWhenLost` | Falling off the bottom costs a life. | true | **false** | **false** |

The Bullet is `false / false` — it isn't a real block: it never counts and never
costs a life when pushed off. A "free" block (`true / false`) is a real block that
counts when placed but is safe to drop. The two are orthogonal — combine freely.

**Authoring gotcha:** the flags default `true` *in C#*, but an existing `.asset`
saved before the fields existed has no key for them, so they resolve to `true`
regardless of intent (the serialized-default-staleness trap). To make a block
`false`, the key must actually be written to the `.asset` (untick in the Inspector
and save, or hand-author `countsAsPlacedBlock: 0` / `costsLifeWhenLost: 0`).

## The two numbers (don't conflate them)

- **`score`** — CUMULATIVE progression. Real placements only; **never decrements**.
  Drives the difficulty ramp, the ability-picker milestones, and rarity escalation.
  Overdrive (`ScorePerBlockBonus`) amplifies it. A non-counting block (bullet) adds
  nothing; a lost/destroyed block subtracts nothing.
- **`placedBlocks`** (`GameManager.placedBlocks`, event `StandingBlocksChanged`) —
  the LIVE count of real placed blocks still standing. `+1` per *physical* placement
  (never amplified), `−1` when a counting block is destroyed or falls. Drives the
  **HUD total** and the **PlaceBlocks win target** (which now genuinely sets back
  when blocks are destroyed or dropped — the hold-steady verification aborts if the
  live count falls below target).

Why two: losing blocks must lower your visible total and your PlaceBlocks goal, but
must NOT rewind difficulty or revoke an earned picker (decided with Nick).

## The rules (where the bookkeeping lives — all outside the frozen BlockController)

The `+1` and the matching `−1` are tied to the **block itself**, not re-derived at each
site: when a placement counts, the block's `BlockIdentity` is marked counted; removal
decrements **exactly once** via `TryConsumeCounted()` (a double-remove is a no-op, not a
clamp-masked bug).

- **Placed** (`GameManager.AddScore`, called from the frozen lock): suppressed for a
  non-counting block via `_activeBlockData` (set by `Spawner.WireBlock` →
  `SetActivePiece`, which also covers mid-fall swaps — `ReplaceActivePiece` and the
  in-air `ApplyVariantToNextBlock` path both re-report the piece, so a swapped-in
  variant's flags are never read stale). A counting placement does `score += amount`,
  `placedBlocks += 1`, and `BlockIdentity.MarkCountedAsPlaced()`.
- **Destroyed** — *any* code that destroys a placed block MUST call
  `GameManager.RemovePlacedBlock(block)` first (it `−1`s only if the block's placement
  was counted; idempotent). Current callers: `BulletImpact`, `AbilityEffects
  .DestroyBlockWithShatter` (so every ability that shatters a block is covered),
  `BombBlockBehaviour`, `HeightLimitWavesModifier`. **New destruction site → add the
  call**, or the live count silently desyncs above reality.
- **Fell off** (`LossZone`, the single loss gateway, both the cull sweep and the
  trigger): runs the frozen `HandleLostBelowScreen` *inside*
  `GameManager.DuringBlockLoss(block, action)` — the one entry point that scopes the
  loss policy (try/finally, so a throw can't strand it). It decides the life charge
  (`costsLifeWhenLost`, read by `GameManager.GameOver`), `−1`s the live total once for
  a counted block, and suppresses the posthumous lock-score of the lost piece. An
  active piece pushed off was never counted, so it never `−1`s — only its life charge
  (if any) applies.

## Quick effect check
- Normal block placed → `score +1`, `placedBlocks +1`.
- Bullet placed → nothing.
- Bullet destroys a landed block → that block `placedBlocks −1`; net `−1`.
- Bullet / other `costsLifeWhenLost:false` block pushed off → no life, no count change.
- Normal landed block knocked off → `placedBlocks −1` and a life.
- Bomb detonation → `−1` per destroyed neighbour and the bomb itself.

See also: [ABILITIES.md](ABILITIES.md) (abilities that create/destroy blocks),
[PHYSICS.md](PHYSICS.md) (never write transforms on landed blocks).
