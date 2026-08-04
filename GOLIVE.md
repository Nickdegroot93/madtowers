# GOLIVE.md — the ordered release plan

**Status: binding checklist — restructured 2026-08-04 into a strictly ordered plan**
(Nick: "we're nearing done; levels/difficulty tuning continues in parallel and is NOT
tracked here"). This is the single place that tracks launch work. Detail lives in the
binding docs each section points to — this doc is the map, not the territory.

**Where we actually stand (verified against code + production 2026-08-04):**
all three player-facing systems — sign-in, premium IAP, rewarded ads — are BUILT and
playtestable on the client (simulated providers in the editor). The backend is not
"pending cutover" anymore: **production Supabase has been live since 2026-07-23**
(`cyinvljdxpdtynlkiqhm`, eu-north-1; anonymous sign-ins on; smoke suite green; the
editor talks to production). What remains is: accounts & consoles, real SDK adapters
behind the existing seams, server money-paths, compliance, and release builds.

**The order and why.** Everything funnels through the store consoles: app listings
unlock the Sign in with Apple capability, the IAP products, and AdMob app approval.
So: consoles first. Then the three systems in this order —
**1) sign-in, 2) payments, 3) ads** — because sign-in has no money risk and its
Apple/Google console work overlaps the listing setup; payments next because the
`validate_receipt` server work can be built and sandbox-tested the moment products
exist; ads last because ad-network accounts want a registered listing and **rot when
idle** (AdMob deactivates after 6 idle months — create those accounts as late as
possible). Ads and premium ship in the **same release** (SHOP.md §12: the attempts
meter without both escape valves is pure friction), so "ads last" costs nothing.

---

## Phase 0 — now (no store accounts required)

- [x] **Push the XP migration to production** — DONE 2026-08-04: `20260801000003_xp.sql`
      pushed via `db push`, smoke suite 22/22 against production (XP checks e3–e5/g3
      included). Until then hosted `finish_run` paid no XP — the reason XP sat at 0.
- [ ] **Own the domain.** `madtowers.app` is hard-coded as the privacy/terms/support
      placeholder (`MainMenuRuntime.Settings.cs:529`) — buy it (or pick the real domain
      and update the consts). Needed before any store form asks for a privacy URL.
- [ ] **Write + host the privacy policy & terms** (GitHub Pages is fine). Name the SDKs:
      Supabase, Unity LevelPlay/AdMob, Unity IAP; what's collected; the in-app deletion
      path. Then replace `PrivacyPolicyUrl` / `TermsUrl` / `SupportEmail` —
      **shipping the placeholders would 404.** (Final SDK list is only certain after
      Phase 4 — draft now, finalize then.)
- [ ] **Crash/analytics decision** — nothing is integrated today. Decide (Unity Cloud
      Diagnostics / Crashlytics / none), integrate or explicitly skip, and declare it in
      the Phase 5 forms. Deciding late means redoing the data-safety forms.
- [ ] **Display-name moderation** (BACKEND.md §11): uniqueness rule or discriminator +
      profanity filter in the rename RPC; also decide guest-claim policy (claim-now-link-
      later, as built, vs. link-gated). Must land before boards are public.
- [ ] **Per-level score sanity bounds** (BACKEND.md §6.2): derive the max-plausible
      score/height/duration table from Nick's current playtesting data; tighten
      `finish_run`. (Same pass can set XP farming bounds, XP.md §6.)

## Phase 1 — store accounts & listings (the unlock for everything below)

- [ ] **Apple Developer Program** — $99/year; needs D-U-N-S if publishing as a company
      (that lookup can take weeks — start immediately if company).
- [ ] **Google Play Console** — $25 one-time.
- [ ] **Lock bundle IDs FIRST** (permanent), then create both app listings.
- [ ] **Release keystore (Android)**: create, back up safely, enroll Play App Signing.
      Done here because Phase 2's Google sign-in needs its SHA-1 fingerprints.
- [ ] Store assets: icon set, screenshots per device class, feature graphic (Play),
      description copy.
- [ ] **Apple Small Business Program: ENROLL manually** (15% instead of 30% under
      $1M/yr; Google's equivalent is automatic). Enroll BEFORE the first sale.
- [ ] Payout details + tax forms in both consoles (money arrives ~15–45 days lagged).

## Phase 2 — sign-in: Apple & Google account linking (BACKEND.md §3.3)

Anonymous auth, link prompts, sign-in sheet, delete-account flow: BUILT. Remaining:

- [ ] **Sign in with Apple**: capability on the App ID, Services ID + key in Apple
      Developer, configure the Apple provider in Supabase Auth, native plugin for the
      credential UI (Lupidan's is the community standard). MANDATORY on iOS since
      Google login exists too.
- [ ] **Google Sign-In**: OAuth client IDs (Android + web) in Google Cloud console,
      SHA-1s for the release keystore AND the Play App Signing key, configure the
      Google provider in Supabase Auth, native plugin.
- [ ] **Test matrix**: link guest → Apple/Google · sign in on second device pulls
      progress + premium · unlink/re-link edges · delete a linked account (the
      `delete_account` client flow is built — verify once against production).

## Phase 3 — payments: "MadTowers Unlimited" IAP (SHOP.md §7; seam: `PremiumStore`)

Client flow DONE (Profile BUY, localized price, RESTORE in Settings → Account, offline
entitlement cache, premium offline-unranked play). Remaining:

- [ ] Product **`madtowers_unlimited`** (non-consumable, $3.99 tier) in BOTH consoles —
      same ID both stores.
- [ ] **Unity IAP (v5+)**: implement `IPremiumStoreProvider` over it (init on boot,
      `PriceText` from the store, purchase + restore → `PremiumStoreResult`),
      `PremiumStore.Install(...)` at boot on device.
- [ ] **`validate_receipt` Edge Function** (BACKEND.md §6.4): receipt → verify with
      Apple/Google → set `attempts.premium = true`. Client hook is the TODO in
      `PremiumStore.GrantEntitlement`.
- [ ] Refund/revocation: poll Play voided-purchases / App Store server notifications →
      clear `attempts.premium`. v1 may be a manual runbook — write it down.
- [ ] **Test matrix**: sandbox buy (both stores) · cancel mid-sheet · restore on second
      device · reinstall-then-restore · airplane-mode play while premium · refund.
- [ ] Apple review notes: RESTORE PURCHASES must be findable (Settings → Account) and
      purchasable on the reviewer's sandbox account.

## Phase 4 — ads: rewarded refill (SHOP.md §7.3 is the authoritative list)

Client flow DONE (`RewardedAds` facade, WATCH AD +2 surfaces, server `grant_ad_refill`).
Create the accounts only NOW (idle-rot rule above). In §7.3's order:

- [ ] Accounts: Unity LevelPlay dashboard + Google AdMob (AdMob approval needs the
      registered listing from Phase 1).
- [ ] SDK: LevelPlay package, AdMob as bidding network, one rewarded placement
      (`attempts_refill`).
- [ ] Adapter: `IRewardedAdProvider` over LevelPlay (preload on boot, `IsReady`,
      watched-to-end → `onFinished(true)`), installed at boot on device; the simulated
      editor provider stays.
- [ ] **Consent/privacy: iOS ATT prompt + Google UMP flow (GDPR)** — required, ships
      with the SDK.
- [ ] Server: **AdMob SSV** replaces the client-claimed `grant_ad_refill` path
      (BACKEND.md §6.4); until wired, the 3/day server rate limit is the only defense.
- [ ] Daily-budget mirror client-side, so the button hides BEFORE a wasted watch.

## Phase 5 — compliance & store forms (needs the final SDK set, hence after 2–4)

- [ ] Content rating questionnaires (IARC on Play, age rating on App Store).
- [ ] Data-safety (Play) / privacy-nutrition-label (Apple) forms — declare ads SDK,
      analytics, account data per what's ACTUALLY integrated.
- [ ] Kids/families policy check: ads are opt-in rewarded only (SHOP.md §8); rating
      answers consistent with that.
- [ ] Finalize the privacy policy text (Phase 0 draft) against the shipped SDK list.
- [x] About/Legal settings tab — BUILT 2026-07-30 (version, link rows, credits);
      placeholder URLs are Phase 0's item.
- [ ] **App-store review account / demo notes** — reviewers must be able to play
      (campaign needs the server up) and to test the purchase.

## Phase 6 — release engineering & submission

- [ ] **`Assets/csc.rsp` contains NO dev defines — verify per release.** (2026-08-04:
      the working tree has `-define:MADTOWERS_UNLOCK_ALL` for playtesting; committed
      state is clean. This is exactly why the check exists.)
- [ ] Bump version/build numbers; signing: keystore (Phase 1) on Android,
      provisioning/certs on iOS.
- [ ] IL2CPP release builds, both platforms.
- [ ] On-device pass of the Phase 2/3/4 test matrices. **iOS has never been run on a
      device** (all testing is Android) — budget a real iPhone + TestFlight pass;
      first iOS run WILL surface surprises (safe-area, ATT, sign-in sheets, IAP sandbox).
- [ ] Submit; expect at least one rejection round; keep the review notes current.

## Post-launch watchlist (not blockers)

- `runs` ledger retention/pruning once volume is real (BACKEND.md §11).
- Supabase free-tier limits — watch rows + egress as the base grows.
- Refund-revocation automation if the manual runbook gets tedious.
- Native Game Center / Play Games layer, cosmetics, boost-weekend banner (XP.md §4) —
  all deliberately post-launch.

---

## Backend cutover — done (kept for the record)

- [x] Hosted Supabase project live 2026-07-23 (`cyinvljdxpdtynlkiqhm`); URL + anon key
      in `SupabaseConfig`; anonymous sign-ins enabled; migrations pushed (except XP —
      Phase 0); smoke suite green against production.
- [x] Delete-account client flow — BUILT 2026-07-30 (Settings → Account → confirm →
      `delete_account` RPC → session clear + local wipe + fresh anonymous boot).
      One production verification ride remains in Phase 2's test matrix.

*Levels, difficulty, floors and speed tuning are Nick's parallel track and never block
this list. The in-game seams (`PremiumStore`, `RewardedAds`, sign-in sheet) mean the
game stays fully playtestable while every phase above is in flight.*
