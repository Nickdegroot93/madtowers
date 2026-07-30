# GOLIVE.md — everything between "the game is done" and "the game is live"

**Status: binding checklist.** This is the single place that tracks the launch work.
The game-side code for all of it is BUILT and playtestable (simulated providers in the
editor); what's listed here is the account setup, SDK integration, server work and
compliance that can only happen near release. Detail lives in the binding docs each
section points to — this doc is the map, not the territory.

Ordering matters: **§1 store accounts unlock §3 sign-in, §4 ads and §5 payments** (all
three need the app registered in the consoles). Do §1 when a store listing can actually
be created, not before — idle accounts rot (AdMob deactivates after 6 idle months).

---

## 1. Store accounts & listings

- [ ] **Apple Developer Program** — $99/year, needs D-U-N-S if publishing as a company.
- [ ] **Google Play Console** — $25 one-time.
- [ ] App listings created (bundle IDs locked: pick them FIRST, they're permanent).
- [ ] Store assets: icon set, screenshots per device class, feature graphic (Play),
      description copy.
- [ ] **Privacy policy URL + terms URL** — both stores require them. The About/Legal tab
      that links them is BUILT (Settings → About); what's missing is the real pages:
      write the policy (name the SDKs: Supabase + LevelPlay/AdMob/Unity IAP once
      integrated), host it (GitHub Pages is fine), then replace the placeholder consts
      `PrivacyPolicyUrl` / `TermsUrl` / `SupportEmail` in `MainMenuRuntime.Settings.cs` —
      **shipping with the placeholders would 404.**
- [ ] Content rating questionnaires (IARC on Play, age rating on App Store).
- [ ] Data-safety (Play) / privacy-nutrition-label (Apple) forms — declare ads SDK,
      analytics, account data per what's actually integrated by then.
- [ ] **Apple Small Business Program: ENROLL manually** (15% instead of 30% under $1M/yr;
      Google's equivalent is automatic). Enroll BEFORE the first sale.
- [ ] Payout details + tax forms in both consoles (money arrives monthly, ~15–45 days lag).

## 2. Backend cutover (BACKEND.md §10 — the authoritative list)

- [ ] Real (hosted) Supabase project; URL + anon key into `SupabaseConfig`.
- [ ] Enable **anonymous sign-ins** on the hosted project (off by default!).
- [ ] Apply all migrations + run `supabase/smoke.sh` against production.
- [x] **Delete account** client flow — BUILT 2026-07-30 (Settings → Account: danger row →
      confirm sheet → `delete_account` RPC → session clear + total local wipe + fresh
      anonymous boot). Verify once against the hosted project before submission.
- [ ] Per-level score bounds; display-name moderation pass.
- [ ] App-store review account / demo notes (reviewers must be able to play — campaign
      needs the server up).

## 3. Payments — "MadTowers Unlimited" IAP (SHOP.md §7; client seam: `PremiumStore`)

Client flow is DONE (buy on the Profile card, RESTORE PURCHASES in Settings → Account,
owned state, offline entitlement cache, premium offline unranked play). Remaining:

- [ ] Product `madtowers_unlimited` (non-consumable, $3.99 tier) created in **both**
      consoles — same ID both stores.
- [ ] **Unity IAP package (v5+)**: implement `IPremiumStoreProvider` over it (initialize
      on boot, localized `PriceText` from the store, purchase + restore mapped to
      `PremiumStoreResult`), `PremiumStore.Install(...)` at boot on device.
- [ ] **`validate_receipt` Edge Function** (BACKEND.md §6.4): client sends the store
      receipt after purchase/restore → server verifies with Apple/Google → sets
      `attempts.premium = true`. Client hook is the TODO in `PremiumStore.GrantEntitlement`.
- [ ] Refund/revocation: poll store voided-purchases (Play) / App Store server
      notifications, clear `attempts.premium` server-side. v1 can be a manual runbook.
- [ ] Test matrix: sandbox buy (both stores) · cancel mid-sheet · restore on second
      device · reinstall-then-restore · **airplane-mode play while premium** · refund.
- [ ] Apple review: RESTORE PURCHASES must be findable (it's in Settings → Account) and
      the purchase must work on the reviewer's sandbox account.

## 4. Ads — rewarded refill (SHOP.md §7.3 — the authoritative list)

Client flow is DONE (`RewardedAds` facade, WATCH AD +2 button, server-side
`grant_ad_refill` wired). Remaining, in SHOP.md §7.3's order: LevelPlay + AdMob accounts,
SDK + placement, `IRewardedAdProvider` adapter, **ATT prompt (iOS) + UMP consent (GDPR)**,
AdMob SSV replacing the claim path, client-side daily-budget mirror. Ads and premium ship
in the SAME release (the meter without escape valves is pure friction, SHOP.md §12).

## 5. Sign-in — Apple & Google account linking (BACKEND.md §3.3)

Anonymous auth + link prompt + sign-in sheet are BUILT against dev Supabase. Remaining:

- [ ] **Sign in with Apple**: capability on the App ID, Services ID + key in Apple
      Developer, configure the Apple provider in Supabase Auth. Native plugin for the
      credential UI. (MANDATORY on iOS if any third-party login exists.)
- [ ] **Google Sign-In**: OAuth client IDs (Android + web) in Google Cloud console,
      SHA-1 fingerprints for the release keystore + Play App Signing key, configure the
      Google provider in Supabase Auth. Native plugin.
- [ ] Test: link guest → Apple/Google · sign in on a second device pulls progress +
      premium · unlink/re-link edge cases · account deletion of a linked account.

## 6. Compliance & policy

- [ ] **ATT prompt** (iOS, required before any ad tracking) + **Google UMP** consent flow
      (GDPR/EEA) — ships with the ads SDK, §4.
- [ ] Privacy policy content: name the SDKs (LevelPlay, AdMob, Unity IAP, Supabase),
      what's collected, deletion path.
- [x] **About/Legal settings tab** — BUILT 2026-07-30 (version, privacy/terms/support
      link rows, credits). Placeholder URLs remain — see §1's privacy-policy item.
- [ ] Kids/families policy check: opt-in rewarded ads only (SHOP.md §8), content rating
      answers consistent with it.

## 7. Release engineering

- [ ] `Assets/csc.rsp` contains NO dev defines (unlock-all etc.) — verify per release.
- [ ] Bump version/build numbers; release keystore (Android) safely backed up +
      Play App Signing enrolled; provisioning/signing (iOS).
- [ ] IL2CPP release builds both platforms; on-device pass of the §3/§4/§5 test matrices.
- [ ] Crash/analytics decision (none integrated today — decide, then declare in §1 forms).

---

*Everything in this file is deliberately NOT started until Nick says the game is near a
store listing. The in-game seams (`PremiumStore`, `RewardedAds`, sign-in sheet) mean none
of it blocks feature work in the meantime.*
