# SHOP.md — the run-supplies shop & split leaderboards

**Status: DESIGN FINAL — reopened by Nick 2026-07-20, open knobs resolved same day
by research** (Nick delegated the monetization/retention calls: "I'm designing the
game itself... go for it"). The 2026-07-11 "cosmetics only" decision is superseded.
Cosmetics (titles/avatars/banners) remain planned for the online phase on top of
this — they are not displaced, just no longer the *only* thing.

Research inputs (July 2026): match-3 pre-level booster conventions (free-taste
onboarding is universal in the subgenre), loss-only lives systems (the standard for
fail-heavy skill games — wins feel rewarding because they're free), rewarded-ads
best practice (opt-in refills convert >90%, "watch for +2 attempts"-style explicit
copy beats vague rewards), and FTUE findings (players whose first session has zero
monetization pressure convert ~40% better later; offer help at the moment of
difficulty, not after the tutorial).

---

## 1. Why the 2026-07-11 rejection no longer holds

The run-supplies idea (buy lives + boosts, equip before Play) was rejected for three
reasons. Each now has an answer:

1. **"Ranks contamination"** → **the split leaderboard.** Every run is classified at
   start: touched by *any* purchased supply → **Boosted board**; untouched → **Clean
   board**. Two boards per level, never mixed. A boosted score can't poison ranked
   play because it never appears next to a clean score. (§5)
2. **"Complexity — inventory persistence, loadout plumbing, poverty-trap balancing"**
   → **there is no inventory.** Supplies are bought *at the level modal, for this
   run*, charged on Play, consumed by the run. Nothing is stockpiled, carried,
   or managed. The plumbing is: a purchase record for one run + config overrides
   at run start — machinery we already have (Custom Game applies config overrides
   today). (§3)
3. **"Taste — buying gameplay power doesn't fit"** → reframed: supplies are a
   **stuck-player valve and a coin sink**, not a power fantasy. The clean board stays
   the game's real yardstick; supplies exist so a player walled by Chapter 9 can
   *choose* an assisted clear (visibly labeled as such) instead of churning. Hunt
   Showdown's honesty rule applies: assisted runs are publicly marked, never hidden.

---

## 2. Two different "lives" — name them apart, always

The word "lives" means two unrelated systems. In code, UI, and docs:

- **Run lives** — in-run buffer (`RunState.Lives`): a lost block costs a life
  instead of ending the run (BLOCKS.md costs-life flag). Bought pre-run. Max **3**.
- **Attempts** — the meta/energy system (not yet implemented): how many runs you may
  *start*. Regenerates on a timer; ads and purchases refill it; the premium unlock
  removes it entirely. (§7)

Never render either as a generic heart without its own icon language: run lives =
shield-heart pips in the run HUD; attempts = a counter on the Play screen top bar.

---

## 3. Run supplies — what you can buy for a run

Bought in the **level modal** between selecting a level and pressing Play. No shop
inventory: the modal shows the catalog with coin prices; picks are charged when the
run actually starts (back out = charged nothing). Supplies apply to that run only.

### 3.1 Run lives (0–3)

- Three pip slots in the modal. Price escalates per pip:
  **1st = 40, 2nd = 60, 3rd = 90 coins** (all-in ≈ 190 ≈ two decent runs' earnings).
- **Hard cap 3 in-run, from all sources combined.** `RunState` gets the clamp
  (`AddLife` currently increments unbounded): purchased lives + authored level lives
  + `ExtraLifePowerUp` pickups can never exceed 3. At 3, the extra-life ability is
  simply not offered in the draft (ABILITIES.md availability gate), same rule as
  other unavailable abilities.
- Levels that *author* starting lives (e.g. Maw Sort's 2) keep them; purchases top
  up to the cap (so there you could buy at most 1). Authored lives do NOT mark the
  run boosted — only purchases do.

### 3.2 Boosts (max 2 per run)

Passive run modifiers. Design rules:

- **No ability duplicates.** Boosts only do what in-run abilities *can't*: rate,
  speed, and economy modifiers that exist before the first brick falls. The ability
  draft stays the in-run decision layer. The single bridge: "stock a consumable"
  (below) front-loads an *existing* consumable rather than cloning its effect.
- **Config-level implementation.** Every boost is an override on numbers the mode
  config / directors already own — no new gameplay systems.
- **Visible when relevant.** A boost that can't apply to this level (hazard filter on
  a hazard-free level) isn't shown greyed — it isn't shown at all.
- **No free samples.** A "first taste free" mechanic (match-3 convention) was built
  and REJECTED by Nick 2026-07-20 — everything costs its listed price, always. Don't
  re-add without him.

The launch catalog (5, deliberately small; all in `SupplyCatalog` - a code-owned
static class, not an asset, so price tuning can't go stale in serialized defaults):

| Boost | Effect | Dial it turns | Price |
|---|---|---|---|
| **Slow Descent** | fall speed ×0.9 (start AND cap) | `DifficultyController.ScaleSpeeds` | 80 |
| **Scarce Hazards** | hostile-brick spawn chances ×0.5 | `Spawner.ReduceHazardChances` (run-local) | 60 |
| **Quick Study** | first ability choice arrives at 3 blocks instead of the level's cadence | `AbilityChoiceController` first-fire | 30 |
| **Stocked: Slo-Mo** | start the run holding one Slo-Mo charge | consumable pre-grant | 30 |
| **Stocked: Zap** | start the run holding one Zap charge | consumable pre-grant | 40 |

(A "Steady Hands" wind-reduction boost was designed and cut at build time: the game
has no weather/wind system yet, and a Rare ability already owns that name. Revisit
when weather ships.)

Notes:
- Slow Descent at ×0.9 ≈ two-and-a-half chapter rows of relief (rows grow ~4%,
  PROGRESSION.md §2.1) — the strongest boost, priced accordingly. Never offer a
  deeper slow; below ×0.9 runs stop resembling the level.
- Scarce Hazards halves only entries whose brick is hostile (Feather/Ice/Locked/
  Sandstone/Vortex/Boulder/Magma/Bomb/Maw/Tremor); helpers (Vine/Anchor) and the
  Pyramid monument are untouched.
- **Gold Rush was considered and cut**: a coins-for-coins boost is a treadmill, and
  an economy modifier that doesn't affect difficulty muddies the "any supply =
  boosted" rule's fairness story. Revisit only if the sink proves too strong.

### 3.3 What supplies never are

- Never purchasable with real money, directly or indirectly (coins are never sold —
  §8). The boosted board stays "earned coins spent," not "wallet opened."
- Never required. Every level must remain clearable clean — supplies are relief,
  not a difficulty budget the curve secretly assumes (PROGRESSION.md owns the curve;
  it is authored for zero supplies).
- Never mid-run. All purchasing ends when the run starts. (If an ad-revive is ever
  added, it marks the run boosted by the same rule.)

---

## 4. Economy — prices vs. income

Income (JUICE.md §3): ~70–95 coins per 100 bricks + 25 win bonus → a typical
successful mid-game run banks **~100–120 coins**; a failed run maybe 40–70.

Target feel: a **full loadout (3 lives + 2 strong boosts ≈ 330)** costs ~3 runs of
earnings — an occasional "tonight I beat this wall" decision, not a default-on tax.
A **single life (40)** costs less than half a run — the light-touch common case.

These are opening brackets to playtest, tuned in one place (a `SupplyCatalog`
ScriptableObject). Watch two failure modes: everyone always buys (too cheap /
curve too hard) and no one ever buys (too dear / game too easy). Balances persist,
so today's hoards (no sink yet) will splurge early — expected, fine.

---

## 5. The split leaderboard

- Every run gets a flag at start: `boosted = (purchasedLives > 0 || boosts.Count > 0)`.
  Authored level lives don't set it; only purchases do.
- **Two boards per level: CLEAN and BOOSTED.** Local bests split the same way
  (`bestScoreClean` / `bestScoreBoosted`, both monotonic per-metric max — DATA.md).
- BACKEND.md `scores` table: primary key becomes `(user_id, level_id, board)` where
  `board ∈ {clean, boosted}`, plus a `loadout jsonb` column on boosted rows (which
  supplies — shown as small icons next to the score, the Hunt-style honesty badge).
- UI: one leaderboard screen, a two-tab toggle, **CLEAN is the default tab**. Your
  boosted best is never shown on the clean tab, and vice versa.
- Boosted is a real board with real bragging rights (fastest assisted clears are
  still a ladder) — it's "open class" racing, not a shame bin. The label does the
  separating; no further handicapping/normalizing of boosted scores (rejected: score
  multipliers per supply — unexplainable and gameable).

---

## 6. Progression interplay

- **Boosted completions count for campaign progression.** Unlocking the next level
  is about moving forward; leaderboards are about comparing. A player who buys their
  way past a wall still earned the coins in-game. (This is the point of the system.)
- This **answers PROGRESSION.md §5's open lives-model question**: the campaign is
  authored around `startingLives 0` everywhere; run lives come from the shop.
  Authored lives remain a rare level-design spice (Maw Sort), not a curve tool.
- The Vault/discovery layer is untouched — supplies discover nothing.

---

## 7. Attempts (energy) & monetization

Designed here so the shop, ads and IAP land as one coherent system. The model is
**loss-only lives** (the fail-heavy-skill-game standard: wins never charge the
meter, so victories feel free and failures create the decision point).

- **Free players hold max 5 attempts.** Starting a run spends 1.
  **Winning the run refunds it** — losses are the only thing the meter charges.
  (Sharpens the supplies pitch: protect your attempt.) A Try-Again retry is a new
  run start: it spends the next attempt, so each loss nets exactly one.
- **Regeneration: +1 attempt per 10 minutes**, rolling (full 0→5 in 50 min — near
  Nick's "~30 minutes" intent; rolling beats a cliff reset because the meter always
  visibly heals). Genre reference points: Candy Crush 1/30min (aggressive), casual
  merge games 1/2min (toothless); 1/10min sits deliberately player-friendly of the
  classics while still giving the premium unlock a reason to exist. Wall-clock
  based; clock-cheating accepted for v1 (same trust level as local scores).
- **Watch an ad → +2 attempts** (cap 5). The only ad placement in the game, opt-in
  rewarded video with explicit copy ("+2 attempts"). No forced ads, no
  interstitials — ads exist purely as the free player's refill lever.
- **No Hour Pass.** A coin-priced "unlimited attempts for an hour" was designed,
  built, and CUT by Nick 2026-07-20 ("we don't buy the hour pass — it's not part of
  it anymore"). The only refills are waiting and, once ads ship, the rewarded ad.
  Don't re-add without him.
- **Premium unlock — one-time IAP at $3.99/€3.99 ("MadTowers Unlimited"): attempts
  system removed forever.** Pitched on the Profile page as "the full game, forever —
  no ads, unlimited lives, one purchase". This is "buying the game." It never touches run
  supplies, boosts, coins, or leaderboards — premium players and free players
  compete identically. Receipt validation per BACKEND.md §9 when it ships.
- Attempts gate **campaign runs only**. Custom Game (editor-only today) and any
  future practice mode don't spend attempts.

### 7.1 Soft landing (FTUE rule, binding)

**Chapter 1 is monetization-silent.** While the player hasn't completed Chapter 1:
no supplies row in the modal, no attempts meter, no shop tab contents beyond
COMING SOON. Both systems switch on at first Chapter 1 completion (one save flag,
`metaSystemsUnlockedAtUnixUtc`). Research basis: zero monetization pressure in the
first session ≈ 40% better later conversion — and it matches PROGRESSION.md's
"beatable cold" opener philosophy.

### 7.2 Offer help at the wall (the only nudge, binding limits)

After **3 consecutive losses on the same level**, the level modal opens its
boost tray automatically once (per level, per streak). No popup, no discount timer,
no currency upsell; the tray is simply already open. Research basis: the moment of
difficulty is when help lands as help, not as a sales pitch.

---

## 8. Ethics guardrails (binding)

1. **Coins are never sold for real money.** The only IAP is the premium unlock
   (time, not power). This keeps supplies "earned," keeps the boosted board honest,
   and keeps server-side receipt scope tiny.
2. **Power is never sold for real money** — follows from 1, stated separately so
   it survives future "just this once" ideas.
3. **Assisted runs are always visibly marked** (boosted tab + loadout icons).
4. **The clean game is the whole game.** Supplies removed, nothing else changes —
   no level, mode, or content is supply-gated.
5. Kids/store compliance: ads are opt-in rewarded only; premium price shown in
   store currency; account deletion path per BACKEND.md §3.4.

---

## 9. UI

Taste contract applies everywhere (near-black bodies, accent only in the neon edge,
no ornament, ≥64px touch targets, Archivo Black display font).

### 9.1 Level modal (the heart of it)

Between the level info and the Play button, the **SUPPLIES** section — reworked
2026-07-20 after Nick's UX review killed a pip-grid v1 ("tiny buttons, chaotic"):
mobile-first, two full-width card rows with ≥80px buttons, prices ON the buttons.

- **RUN LIVES row**: heart icon + "Survive a topple. Max 3." + three heart pips
  showing the current pick, and a big **[+ LIFE 40 $]** stepper button (plus [−]
  once any are picked). The next pip's price always rides the + button.
- **BOOSTS row**: shows the picked boost names (or "None picked.") + a big
  **CHOOSE** button opening the boost tray — a bottom sheet listing the
  level-relevant boosts as tall cards (name, one-line effect, price; tap toggles).
- **Status line**: running TOTAL + WALLET. The attempts meter does NOT appear in
  the modal (its home is the top-bar chip) — except when it actually blocks play,
  when the status line becomes "OUT OF ATTEMPTS — NEXT IN mm:ss" (no buy-out —
  the Hour Pass was cut, §7; waiting and the future rewarded ad are the refills).
- **The Play button tells the truth**: empty loadout → **PLAY — CLEAN**; any
  supply → **PLAY — BOOSTED** with a gold edge. Nothing is charged until the run
  starts.
- Insufficient coins: buttons stay visible with their price, clearly dimmed —
  "can't afford" must never read as "broken". No "get more coins" upsell (coins
  are earn-only).

### 9.2 Profile tab (the Shop tab was cut, 2026-07-20; v2 same day)

With purchasing fully point-of-use in the modal, a storefront page was redundant —
Nick cut it, then cut the v1 profile's stats/attempts clutter too ("remove all the
bullshit"). The nav slot is **PROFILE** (Person icon), three cards only:

1. **Identity** — avatar placeholder + name, with the explicit promise that Game
   Center / Google Play Games sign-in (and their avatars) arrives with online play.
2. **MADTOWERS UNLIMITED** — the one pitch: the full game forever, no ads, unlimited
   lives, one purchase; $3.99 chip disabled until the IAP ships.
3. **ONLINE PLAY — COMING SOON** — a big locked block (lock icon) listing
   leaderboards, achievements, profiles & avatars, titles & banners. Players should
   SEE that online is on the way.

No wallet display, no meter, no lifetime stats — the top bar owns the meter, and
this page is identity and promises, not a dashboard.

**Currency display rule (binding):** prices are always the gold **coin icon +
number** (`Resources/Menu/coin`, the top bar's coin) — never "$" or any currency
symbol. The coins have a face; use it.

### 9.3 Run HUD

Run lives render as up to 3 shield-heart pips by the score (only when >0; most runs
show nothing). Losing one plays the existing costs-life feedback; no celebration on
spending supplies (JUICE.md principle 1).

---

## 10. Data model (DATA.md-compliant)

- Wallet stays `currencyEarned`/`currencySpent` counters (balance derived) — every
  supply purchase increments `currencySpent`. Fold `PlayerProfileStore` coins into
  `ProgressStore` **before** building the shop (BACKEND.md §8 Phase B obligation).
- New save fields (all monotonic or timestamped):
  - `suppliesSpentTotal` (counter, analytics/tuning),
  - `attemptsState { count, lastRegenUnixUtc, unlimitedUntilUnixUtc }` (timestamped),
  - `premiumUnlocked` (monotonic bool, set only via validated receipt),
  - bests split per board (per-metric max, as today).
- No per-run purchase persistence needed: a loadout lives in memory from modal →
  run start; app death between the two charges nothing (charge happens at run
  start, atomically with the config override).

---

## 11. Implementation status (offline core BUILT 2026-07-20)

1. ✅ **Cap rule** — `RunState.MaxLives = 3` clamps `AddLife` (all sources);
   `ExtraLifePowerUp.IsAvailable` returns false at cap.
2. ✅ **SupplyCatalog (static) + run-start apply** — `RunSuppliesState` carries the
   modal's loadout across the launch reload; `RunSuppliesApplier` (installed on the
   GameManager host) charges + applies atomically in Awake and nulls the carrier so
   Try Again is always clean. Speed boost via `DifficultyController.ScaleSpeeds`;
   Scarce Hazards is PULLED by the Spawner after it registers ambient chances
   (push would race Start order); Stocked consumables grant in Start.
3. ✅ **Modal supplies row + boost tray + attempts line** —
   `MainMenuRuntime.Supplies.cs`; Play button reads PLAY-CLEAN / PLAY-BOOSTED.
4. ✅ **Split bests locally** + BOOSTED RUN tag on the results card + boosted-best
   caption in the modal; wallet folded into ProgressStore (earned/spent, schema v4,
   PlayerPrefs migration); `PlayerProfileStore` stays as the facade.
5. ✅ **Attempts system offline part** (`AttemptsService`: meter, rolling regen,
   win-refund; soft-landing gate; top-bar chip; Profile tab per §9.2 — the Hour
   Pass and the Shop tab were both cut). Ad refill is coded behind
   `AdsEnabled = false`; premium IAP is a shelf-slot card only.
6. ⬜ Leaderboard server split lands with BACKEND.md Phase E (schema designed, §5).

---

## 12. Resolved decisions (2026-07-20, research-backed; Nick delegated)

- **Prices**: lives 40/60/90, boosts per §3.2 table — brackets to playtest, all
  in `SupplyCatalog`. (The Hour Pass and its 150 price were cut with it, §7.)
- **Boost catalog**: the five in §3.2. Free first-use samples were built, then rejected by Nick (no freebies).
- **Win refunds attempt**: YES — loss-only lives model.
- **Regen**: rolling +1/10 min (no cliff reset).
- **Premium**: $3.99 one-time, "MadTowers Unlimited"; ships when an ad SDK ships
  (the meter without its escape valves would be pure friction).
- **Board naming**: **CLEAN / BOOSTED** — "boosted" is the word casual players
  already know from boosters; "assisted" reads apologetic, "open" reads cryptic.
- **Soft landing**: everything meta stays hidden until Chapter 1 is completed (§7.1).
