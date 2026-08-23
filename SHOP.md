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
- **Attempts** — the meta/energy system (**server-authoritative as of 2026-07-23**,
  BACKEND.md §6): how many runs you may *start*. Regenerates on a timer; the
  rewarded ad refills it; the premium unlock removes it entirely. (§7)

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
  run boosted — only purchases do. **Implemented in the modal 2026-08-22**: the RUN
  LIVES row is free-lives-aware (`FreeLives()` = max(authored, type-granted), the
  exact GameManager seeding rule) — free pips render pre-filled, the stepper sells
  only the remainder, and prices are per absolute pip slot (a 2-free level sells
  only the 90-coin third pip), so the row can never sell a pip the cap swallows.
- **Type-granted lives** (2026-08-22): a game type can grant free lives via
  `LevelModifier.GrantedRunLives` — The Flood grants all 3 (the anti-dump tax,
  LEVELS.md). Same non-boosting rule as authored lives. A mode granting the cap
  closes the lives sale: the modal's RUN LIVES row renders as the **INCLUDED
  acknowledgment** (full pips, "YOU START WITH 3", no stepper) so the player
  learns the free hearts before the run, not mid-panic.

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
- BACKEND.md §4 `scores` table is keyed `(user_id, level_id, board)` where
  `board ∈ {clean, boosted}`, plus a `loadout jsonb` column on boosted rows (which
  supplies — shown as small icons next to the score, the Hunt-style honesty badge).
  Scores are written only by the server's `finish_run` function (BACKEND.md §6.2).
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
  classics while still giving the premium unlock a reason to exist. **As built
  (2026-07-23) the meter is server-authoritative** — regen computed lazily on server time
  via `start_run`/`finish_run`, `AttemptsService` is a display cache (BACKEND.md §6); the
  wall-clock model survives only as the `SupabaseConfig.Enabled=false` offline fallback.
- **Watch an ad → +2 attempts** (cap 5). The only ad SURFACE in the game (the same
  opt-in rewarded refill, reachable from two spots), explicit copy where there is
  room. No forced ads, no interstitials — ads exist purely as the free player's
  refill lever. **As built (2026-07-30, second entry 2026-08-01):** the
  out-of-attempts status row grows a gold **WATCH AD +2** button, and the top bar's
  meter chip carries a **"+"** that runs the same flow (visible only below 5/5 with
  a showable, non-rate-limited ad — Nick 2026-08-01); both talk to `RewardedAds`
  (provider facade, `Scripts/Shop/RewardedAds.cs`) — no provider installed (all
  device builds today)
  = no button, ever; in the editor a simulated 5-second TEST AD overlay exercises
  both the skip-forfeits and watched-to-end paths. The grant is
  `AttemptsService.RequestAdRefill`: online it calls the server's `grant_ad_refill`
  (rate-limited 10/day = up to 20 lives, Nick 2026-08-09; a denial hides the button); offline it
  feeds the local wall-clock meter. **Ad SDK decision (2026-07-30): Unity LevelPlay
  mediation with AdMob as a bidder** — integrated near release, NOT now.
  Everything still needed to make ads real: **§7.3**.
- **No Hour Pass.** A coin-priced "unlimited attempts for an hour" was designed,
  built, and CUT by Nick 2026-07-20 ("we don't buy the hour pass — it's not part of
  it anymore"). The only refills are waiting and, once ads ship, the rewarded ad.
  Don't re-add without him.
- **Premium unlock — one-time IAP at $3.99/€3.99 ("MadTowers Unlimited"): attempts
  system removed forever.** Pitched on the Profile page as "the full game, forever —
  no ads, unlimited lives, **offline play**, one purchase". This is "buying the game." It
  never touches run supplies, boosts, coins, or leaderboards — premium players and free
  players compete identically. Receipt validation per BACKEND.md §6.4 when it ships.
  **Offline play rule (Nick, 2026-07-30, binding):** free players need a connection to
  start campaign runs (BACKEND.md §5.1); premium players play offline, but those runs are
  **UNRANKED** — no server run, no leaderboard submission, local bests only (the modal
  says so: "OFFLINE — RUNS WON'T RANK ON THE LEADERBOARDS"). **As built (2026-07-30):**
  the whole client flow is live — `PremiumStore` (provider facade, `Scripts/Shop/`,
  mirrors `RewardedAds`: no provider on device = COMING SOON CTA; simulated store sheet
  in the editor, with an EditorPrefs-backed "purchase history" so restore is honestly
  testable), BUY on the Profile card (localized price via `PriceText`), owned state,
  RESTORE PURCHASES row in Settings → Account, local save flag as the **offline
  entitlement cache** (synced down from the server verdict), premium offline unranked
  runs via `RunGate`. The top-bar attempts chip flips to **heart + ∞, no "+"** for
  premium (Nick 2026-07-30) — it outranks the OFFLINE chip (unlimited is true either
  way; the modal carries the unranked warning). Remaining to make it real money:
  **GOLIVE.md §3** (Unity IAP adapter, store products, `validate_receipt`).
- Attempts gate **campaign runs only**. Custom Game (editor-only today) and any
  future practice mode don't spend attempts.

### 7.1 Soft landing (FTUE rule, binding)

**Chapters 1–2 are monetization-silent** (moved from "chapter 1" on 2026-08-23 when the
early chapters were compressed to two ~5-minute levels each — the gate follows the "first
session silent" INTENT, not a chapter index). While the player hasn't completed Chapter 2:
no supplies row in the modal, no attempts meter, no shop tab contents beyond COMING SOON.
Everything switches on at first Chapter 2 completion (`AttemptsService.MetaEnabled`,
derived — the old `metaSystemsUnlockedAtUnixUtc` flag idea was never stored). Research
basis: zero monetization pressure in the first session ≈ 40% better later conversion —
and it matches PROGRESSION.md's "beatable cold" opener philosophy.

> **Cross-ref (built 2026-08-22):** the solo-dev letter, the "one purchase, supports
> one developer" microcopy and the one-lifetime review ask live in **DEVLETTER.md**
> (the binding design for those three beats). They add no new monetization surface:
> the letter asks for nothing, the microcopy rides the existing premium card, and the
> review ask is the silent official API. §7/§8 stand unchanged.

### 7.2 Offer help at the wall (the only nudge, binding limits)

After **3 consecutive losses on the same level**, the level modal opens its
boost tray automatically once (per level, per streak). No popup, no discount timer,
no currency upsell; the tray is simply already open. Research basis: the moment of
difficulty is when help lands as help, not as a sales pitch.

### 7.3 Shipping real ads — remaining work (checklist, near release)

The client flow is fully built and playtestable (§7 as-built: `RewardedAds` facade,
WATCH AD button, `RequestAdRefill` online/offline grant, simulated editor ad). What
turns it into real revenue, in order — none of it before the game is near a store
listing (AdMob deactivates accounts with 6 idle months, so **don't create accounts
early**):

**Provider changed 2026-08-08: Google AdMob direct, not LevelPlay.** The reason is
BACKEND.md §6.4 — the +2 grant comes from an **AdMob SSV callback**, which is direct-
integration territory; mediation would put a middleman between the watch and the server
grant. It is also one account instead of two, and mediation's eCPM edge only pays at
volume this game will not have on day one. Reversible for the price of one class:
`IRewardedAdProvider` is the entire contract the game knows about.

1. ✅ **Account + apps** — DONE 2026-08-09. Publisher `ca-app-pub-4384624714813425`, two
   apps (Android + iOS) added as "not listed on a store yet", one Rewarded unit each
   (`attempts_refill`). Real IDs are wired, but **live units serve only in a release
   build** — see item 3. Remaining: link both apps to the store listings at launch, or
   serving stays limited.
2. ✅ **SDK** — DONE 2026-08-08: `com.google.ads.mobile@11.3.0` +
   `com.google.external-dependency-manager@1.2.188` via the OpenUPM scoped registry
   (`com.google` added to the existing scopes). `GoogleMobileAdsSettings.asset` holds
   Google's **sample app IDs**. `Assets/Plugins/Android/mainTemplate.gradle` +
   `gradleTemplate.properties` copied from the engine so EDM4U can inject the Android
   dependencies (Unity's own copy step failed and left the folder empty).
3. ✅ **Adapter** — DONE 2026-08-08: `AdMobRewardedProvider` + `AdMobBootstrap`
   (`Assets/SourceFiles/Scripts/Shop/AdMobRewardedProvider.cs`). Preloads on boot,
   `IsReady` from `CanShowAd()`, watch-to-completion → `onFinished(true)`, single-use
   ad destroyed and reloaded on close. Installed via `RewardedAds.Install` on device
   only; the simulated editor provider stays untouched.
   **Runs on Google's public test ad units** — real video, real callbacks, no account.
   Swapping to real units is two constants plus the two app IDs in the settings asset.
4. 🟡 **Consent/privacy** — UMP DONE 2026-08-08, the rest open. `AdMobBootstrap` runs
   `ConsentInformation.Update` → `LoadAndShowConsentFormIfRequired` **before**
   `MobileAds.Initialize`; an ad requested without consent is the violation, not the
   ad that gets shown. Fails **closed** — unknown consent state means no ads that
   session, because failing open costs a GDPR complaint and failing closed costs a few
   rewarded views. Still open: **iOS ATT**, which Google now routes through a UMP
   message configured in the AdMob console, so it is account-gated (and Nick has never
   run an iOS build); store-listing data-safety / ads-declaration forms; kids-policy
   check (ads are opt-in rewarded only, §8).
5. 🟡 **Server: AdMob SSV** — BUILT + DEPLOYED 2026-08-09, **not yet switched on**.
   Migration `20260809000007_ssv.sql` + Edge Function `supabase/functions/admob-ssv`
   (deployed with `--no-verify-jwt`: the caller is Google, which carries no Supabase
   JWT). Google signs the callback with a rotating ECDSA P-256 key; the function
   verifies everything before `&signature=` against
   `gstatic.com/admob/reward/verifier-keys.json`, then calls
   `grant_ad_refill_verified(user_id, transaction_id)` as **service_role** — a
   function deliberately not callable by players, or it would be the very hole SSV
   closes. `custom_data` carries the Supabase user id, set per ad at load time in
   `AdMobRewardedProvider`; without it a callback cannot be attributed and pays
   nobody. `transaction_id` is uniquely indexed, so Google's retries grant once.
   **Two steps remain, both Nick's:**
   (a) register the callback URL on **each rewarded ad unit** in the AdMob console —
       `https://cyinvljdxpdtynlkiqhm.supabase.co/functions/v1/admob-ssv`
   (b) flip `backend_config.ssv_enabled` to `true` (service_role has the grant).
   Until (b) the client-claimed path still pays, so SSV can be proven on a real
   device before the old path is closed; flipping back is the same one row.
   The client needs no release for either: it discovers the switch from the
   `ssv_required` reply and starts polling the meter instead of claiming.

   **Hardened 2026-08-09 after review — the properties that make it safe:**
   - Every decision field is parsed from the **signed prefix**, never the raw URL.
     Reading `custom_data` from `url.searchParams` let anyone replay a genuine signed
     callback with `&custom_data=<their uuid>` appended and redirect the reward.
   - **`ad_unit` is allowlisted.** Google signs callbacks for every publisher with one
     global key set, so a valid signature proves "some AdMob account", not ours —
     without the check, anyone could point their own unit's SSV URL here.
   - `verify_jwt = false` is declared in `config.toml`, not just passed at deploy. One
     deploy without the flag would 401 every callback before the handler runs, killing
     all payouts silently with no log.
   - Replay is claimed **insert-first** (`20260809000008`): the old check-then-lock let
     two retried deliveries both pay while the ledger recorded one grant.
   - Unknown `key_id` refetches once, then answers **5xx so Google retries** — a 4xx is
     final, and key rotation would otherwise drop real rewards for hours.
   - `custom_data` is validated as a uuid; a malformed one would fail the cast inside
     the RPC and become a 500 Google retries forever.

   **Hardened again 2026-08-09 (second review round, migration `20260809000009`):**
   - **The daily cap on the verified path was dead code** — migration 8 compared `< 0`
     against a function clamped at 0, so Google-verified grants were unlimited
     server-side and only the client's hidden button "enforced" 10/day. Claim rows are
     now `granted=false` until they pay; the budget counts only granted rows and the
     `<= 0` check binds (regression-tested by seeding to the cap).
   - **The two grant paths are mutually exclusive on `ssv_enabled`** — the window
     between registering the SSV URL and flipping the flag used to pay +4 per watch.
   - **Refusals are budget-neutral** (`attempts_full` heals again) and a callback for a
     deleted account answers `no_user` instead of an FK exception → 500 → Google retry
     storm.
   - Client: the SSV poll baseline is captured pre-watch (the callback often lands
     before our own reply), and the out-of-attempts supplies row rebuilds when the ad
     finishes loading (live fill takes seconds; the row used to stay button-less).

   **Known and accepted for now:**
   - `custom_data` is still a raw user id chosen by the client, and user ids are
     publicly readable (`scores_select_all`). A modified client can attribute its own
     watched ad to another player — a grief (it burns the victim's daily budget), not
     self-gain, since the attacker gives away their own reward. The fix is a
     server-minted per-ad nonce resolved in the Edge Function; worth doing before the
     boards are public.
   - Under SSV a server-side refusal (`rate_limited`, `attempts_full`) is invisible to
     the player: the callback answers Google, not the app. Today they see the meter
     simply not move.
6. ✅ **Daily-budget mirror** — DONE 2026-08-08, migration `20260808000004_ad_budget.sql`
   (**applied to production 2026-08-09**, verified 8/8 there). `get_profile` and every
   `grant_ad_refill` reply now carry `grants_remaining`, so the button hides BEFORE a
   wasted watch instead of after the first denial. The daily-cap constant moved out of an
   inline `>= 3` into `ad_refill_daily_cap()` — a client showing a different number
   than the server enforces is the exact bug this fixes. `ad_grants_remaining(uuid)`
   is definer + revoked from clients (ad_grants is server-internal; it must not become
   a way to read another player's ledger). `attempts_full` deliberately does NOT
   consume budget: that branch heals, so reporting it as a cap would be a lie.
   Client: `AttemptsService.AdGrantsRemaining` with a `GrantsUnknown` (-1) sentinel —
   an older server that omits the field must not read as "budget exhausted".
   **`get_attempts` carries the budget too, and that is the only way the mirror can
   heal**: once the button is hidden no `grant_ad_refill` is ever sent, so without the
   focus-regain refresh a client would hold a stale zero long after the oldest grant
   aged out of the rolling window (review 2026-08-08). `no_meter` and `premium` report
   the real figure for the same reason — only `rate_limited` is genuinely 0.
   Tests: `supabase/tests/ad_budget.sh` (8 checks) + smoke unchanged at 22/22.
7. ⬜ **Premium ships in the same release** (§12: the meter without its escape
   valves is pure friction — ads and the $3.99 unlock launch together, receipt
   validation per BACKEND.md §6.4).

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
   store currency; account deletion path per BACKEND.md §3.7.

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
  once any are picked). The next pip's price always rides the + button. (A
  tappable-hearts direct-manipulation version was built and reverted the same day,
  2026-07-29 — Nick prefers the stepper; don't reopen without asking.)
- **BOOSTS row**: picked boosts render as **gold pill chips** (they should read as
  equipment, echoing the picker cards), or "NONE PICKED"; plus a big
  **CHOOSE/CHANGE** button opening the boost picker — a **centered modal** (the
  bottom sheet was cut the same day: small text, flickery destroy-reopen refresh)
  of full-width neon-edged toggle cards in the ability-picker chrome
  (CardGradient + CardNeonRing): equipped = gold ring + halo + check badge +
  EQUIPPED tag, available = quiet neutral ring with price, locked-out = dimmed
  with SLOTS FULL. Card name + blurb centre vertically in the card. A slot
  counter (n / 2) sits in the header; cards rebuild in place on toggle (the panel
  never blinks); one big gold **DONE** closes — it confirms nothing, the cards
  already did the work. The picker panel is EXACTLY the level modal's frame
  (`ModalHeightWithSupplies`, shared constant) — a smaller panel lets the modal
  underneath peek out around the edges; the card block centres in the leftover
  space.
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

1. **Identity** — avatar placeholder + name, with the explicit promise that **Sign in
   with Apple / Google** arrives with online play (BACKEND.md §3 — the account system is
   Supabase, not Game Center / Play Games; those are an optional cosmetic layer later).
   The built card's copy now reads "SIGN IN WITH APPLE / GOOGLE — COMES WITH THE MOBILE
   BUILD" (`MainMenuRuntime.Profile.cs`), matching the Supabase decision.
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
  `ProgressStore` **before** building the shop (BACKEND.md §9 Phase B prerequisite — done).
- New save fields (all monotonic or timestamped):
  - `suppliesSpentTotal` (counter, analytics/tuning),
  - `attemptsState { count, lastRegenUnixUtc, unlimitedUntilUnixUtc }` (timestamped) —
    offline-era field; once online ships this is a local display cache of the server-owned
    `attempts` row (BACKEND.md §6.3), no longer synced truth,
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
   Pass and the Shop tab were both cut). Ad-refill client flow is fully wired
   (§7: `RewardedAds` facade + WATCH AD button + `RequestAdRefill` online/offline
   grant; simulated ad in the editor, real SDK = LevelPlay near release). Premium
   client flow is fully wired too (§7: `PremiumStore` facade, Profile BUY, Settings
   restore, offline entitlement cache, premium offline unranked runs; real SDK =
   Unity IAP near release, GOLIVE.md §3).
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
