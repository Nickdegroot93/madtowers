# SHOP.md — shop & coin-sink direction (decision record)

**Status: PARKED.** The shop stays "coming soon" until online play exists.
Decided 2026-07-11. Don't build shop features before then unless Nick reopens this.

## The decision

The shop will sell **cosmetic identity only**, and it gets built **together with
online play** (see BACKEND.md), not before:

- **Titles** — text flair shown with the player name.
- **Avatars** — profile picture.
- **Banners** — the backdrop card behind name/avatar (leaderboards, profiles).

Cosmetics only make sense when other players can see them, which is why the shop
waits for online. Until then the Shop tab keeps the generic "COMING SOON" dummy
screen (`Menu/MainMenuRuntime.Nav.cs` → `BuildDummyScreen`).

**No gameplay purchases, ever.** Nothing bought with coins may affect a run.

## Rejected: run-supplies shop (Hunt Showdown model)

Considered and rejected 2026-07-11. The idea: buy lives and consumable abilities
in the shop, equip up to 3 lives + 2 consumables in the level-detail modal before
pressing Play, supplies consumed on run start. Rejected because:

1. **Ranks contamination** — the moment leaderboards exist, a 3-life run and a
   naked run aren't comparable scores. Purchased power poisons ranked play.
2. **Complexity** — inventory persistence, pre-run loadout plumbing, equip UI,
   consumption rules, poverty-trap balancing — a lot of machinery for a mechanic
   that fights the game's direction.
3. **Taste** — buying gameplay power doesn't fit this game.

Don't re-propose this unless Nick brings it up.

## What coins mean in the meantime

Coins keep working exactly as JUICE.md §3 defines: physical reward feedback
(golden bricks, perfect stacks, win bonus), banked to the persistent balance.
They have no sink yet — that's accepted. Balances persist, so everything earned
now is spendable in the eventual cosmetic shop. JUICE.md §5's open item ("the
store that gives coins meaning") is deferred to the online-play phase, not
dropped.

## Constraints already on the books for when the shop is built

- **DATA.md rule 3** — spending is non-monotonic; model the wallet as
  `earned`/`spent` counters (balance derived), owned cosmetics as a monotonic
  owned-set. All persistence through `ProgressStore`.
- **BACKEND.md §8 Phase B** — fold `PlayerProfileStore` coins (PlayerPrefs) into
  `ProgressStore` as earned/spent counters; do this fold no later than when the
  shop ships.
- **BACKEND.md §9** — if coins are ever sold for real money, purchases must be
  server-validated (receipt check); never trust the client for paid balance.
- Shop UI follows the menu taste contract: near-black cards, accent only in the
  neon edge, no ornament, ≥64px touch targets.

## Sequencing

Finish the game first (levels/content per the roadmap), then online play
(BACKEND.md), then the cosmetic shop as part of the online identity work.
