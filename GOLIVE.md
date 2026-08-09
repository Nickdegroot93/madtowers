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

**Update 2026-08-08 — the ads adapter is no longer one of those.** The real AdMob SDK,
adapter and GDPR consent flow are built and device-playable on test ad units (Phase 4);
what is left there is four ID strings, ATT and SSV. Also done since: the game is renamed
**Hazard Heights**, `hazardheights.com` is bought, and the legal/support site is built
and pushed (`github.com/Nickdegroot93/hazardheights-web`) — Phase 0's domain and
privacy/terms items are closed. Sign-in and IAP adapters remain simulated-only.

**The rename is COMPLETE as of 2026-08-08.** `productName` is "Hazard Heights", bundle
IDs are locked (Phase 1), the URL consts point at the real domain, and the player-facing
strings are fixed. Two things that turned out NOT to need work: the splash art carries no
wordmark (pure illustration), and "HAZARD HEIGHTS UNLIMITED" measures 565px/549px in its
720px/640px boxes, so it fits. What internal names remain — the repo folder, the git
remote, the `.md` filenames, the `MadTowers/HeatHaze` shader, the PlayerPrefs key — is
deliberate: no player sees them, and renaming the prefs key would orphan saves.

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

**Amended 2026-08-08 — separate the ACCOUNT from the CODE.** The ordering above is
right about accounts and wrong if read as "write the ad code last". Google publishes
public test ad units tied to no account, so Phase 4's SDK, adapter and consent flow were
all built and verified with no AdMob account in existence (see Phase 4). The same holds
in reverse for Phases 2–3: sign-in and IAP **code** is blocked on console access, not on
the game being finished — an App ID for the Sign in with Apple capability, and store
products before `validate_receipt` can be sandbox-tested.

So the real rule is: **create ad-network accounts late, create developer accounts early,
and never let either gate the code.** Nick's instinct to finish the game before dealing
with publishing holds for everything except the Play Console account, which carries a
hard 14-day tester clock (Phase 1) that has nothing to do with polish.

---

## Phase 0 — now (no store accounts required)

- [x] **Push the XP migration to production** — DONE 2026-08-04: `20260801000003_xp.sql`
      pushed via `db push`, smoke suite 22/22 against production (XP checks e3–e5/g3
      included). Until then hosted `finish_run` paid no XP — the reason XP sat at 0.
- [x] **Own the domain** — DONE 2026-08-08: `hazardheights.com` bought. **The game is
      renamed MadTowers → Hazard Heights** (store title `Hazard Heights`, subtitle carries
      the "tower stacker" keywords). The repo, folder and docs keep the MadTowers name
      internally — only Product Name, bundle ID and the URL consts are player-facing.
      `MainMenuRuntime.Settings.cs:529` now points at the real domain.
- [x] **Write + host the privacy policy & terms** — DRAFTED 2026-08-08 in the sibling repo
      `../hazard-heights-web` (static Next.js, deploys to Vercel). Covers privacy, terms,
      support, and the Play-required public **account-deletion page**. Names the SDKs
      (Supabase, Unity LevelPlay/AdMob, Unity IAP), what's collected, and the in-app
      deletion path (verified real: `Settings.cs:481` → `delete_account` RPC).
      `PrivacyPolicyUrl` / `TermsUrl` / `SupportEmail` replaced.
      **Still open:** point DNS at the deploy, make `support@` + `privacy@` deliver, and
      have the legal copy reviewed (`legalIsDraft: false` drops the draft banner). Final
      SDK list is only certain after Phase 4 — re-check then.
- [ ] **Crash/analytics decision** — nothing is integrated today. Decide (Unity Cloud
      Diagnostics / Crashlytics / none), integrate or explicitly skip, and declare it in
      the Phase 5 forms. Deciding late means redoing the data-safety forms.
- [x] **Display-name moderation** — this box was stale (verified 2026-08-08):
      `claim_display_name` already does format validation (`^[A-Za-z0-9 _-]{3,16}$`), a
      profanity list, case-insensitive uniqueness and a `unique_violation` fallback, and
      the smoke suite covers all three refusal paths. **Still open, but it's a decision
      not code:** the guest-claim policy (claim-now-link-later, as built, vs. link-gated).
- [ ] **Per-level score sanity bounds** (BACKEND.md §6.2): derive the max-plausible
      score/height/duration table from Nick's current playtesting data; tighten
      `finish_run`. (Same pass can set XP farming bounds, XP.md §6.)

## Phase 1 — store accounts & listings (the unlock for everything below)

- [ ] **Apple Developer Program** — $99/year; needs D-U-N-S if publishing as a company
      (that lookup can take weeks — start immediately if company).
- [ ] **Google Play Console** — $25 one-time. ⏱️ **Start this EARLY — it carries a
      14-day wall.** A **personal** account created after 13 Nov 2023 must run a closed
      test with **12 testers opted in for 14 continuous days** before it may even apply
      for production access (support.google.com/googleplay/android-developer/answer/
      14151465). That clock needs an installable AAB, not a finished game, and the build
      can keep updating while it runs — so starting it late simply adds two weeks to the
      launch. **Organization accounts are exempt**; registering as a business (KvK →
      D-U-N-S, which Apple wants anyway) removes the requirement entirely. Worth deciding
      before registering as a person — the account type cannot be swapped afterwards.
- [x] **Bundle IDs LOCKED 2026-08-08: `com.nickdegroot.hazardheights`** on Android, iOS
      and Standalone. Android previously read `com.nickdegroot.madtowers`; **iOS and
      Standalone were still the Unity template default `com.unity.template.get-started`**,
      which would have been rejected on submission. Permanent from the moment a listing
      exists — do not change it after Phase 1. Then create both app listings.
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

**Provider changed 2026-08-08: Google AdMob direct, not LevelPlay** (SHOP.md §7.3 holds
the reasoning — SSV is a direct-integration mechanism, and mediation's eCPM edge only
pays at volume this game will not have on day one).

**Most of this phase moved OUT of the "needs a store listing" trap.** Google publishes
public test ad units tied to no account, so the SDK, the adapter and the consent flow
were all built and verified before any account existed. What genuinely still needs the
account is small: four ID strings, ATT messaging, and SSV.

Built 2026-08-08, all of it running on test ad units — **playable on an Android device
today**, no account, no revenue, no invalid-traffic risk:

- [x] **SDK** — `com.google.ads.mobile@11.3.0` + `com.google.external-dependency-manager
      @1.2.188` via the OpenUPM scoped registry. EDM4U resolved the Android deps itself
      (`play-services-ads:25.4.0`, `user-messaging-platform:4.0.0`). Note: Unity's own
      gradle-template copy failed and left `Assets/Plugins/Android` empty — the templates
      were copied from the engine folder by hand. Expect this again on a clean checkout.
- [x] **Adapter** — `AdMobRewardedProvider` + `AdMobBootstrap`. Installed on device only;
      the simulated editor provider is untouched, so editor playtesting is unaffected.
- [x] **Consent (UMP)** — runs before `MobileAds.Initialize` and fails **closed**.
- [x] **Daily-budget mirror** — migration `20260808000004_ad_budget.sql`, tested locally
      (`supabase/tests/ad_budget.sh`, 6 checks; smoke still 22/22).
      ⚠️ **NOT pushed to production yet** — `Tools/bin/supabase db push` when ready.

Still open, and each one is genuinely account-gated:

- [x] **AdMob account + apps** — DONE 2026-08-09. Two apps (Android + iOS), each added as
      "not listed on a store yet", each with one Rewarded unit `attempts_refill`.
      ⚠️ Still to do at launch: **link both to the store listings** once they exist, or ad
      serving stays limited. Publisher `ca-app-pub-4384624714813425`.
- [x] **Real IDs wired** — DONE 2026-08-09. App IDs in `GoogleMobileAdsSettings.asset`,
      rewarded units in `AdMobRewardedProvider.cs`.
      **Live units are used ONLY in a release build** (`UseLiveAds => !Debug.isDebugBuild`).
      Requesting real ads from a machine you develop on is how accounts get flagged for
      invalid traffic, and the failure modes are not symmetric: shipping test units costs
      one release's revenue, testing on live units risks the account. So the gate fails
      toward test ads and logs which mode it booted in. **A non-development build serves
      LIVE ads — do not tap them.**
- [ ] **iOS ATT** — Google routes the prompt through a UMP message configured in the
      AdMob console, so it cannot be built before the account. Also unverifiable here:
      no iOS build has ever been run on this machine.
- [ ] **Server: AdMob SSV** replaces the client-claimed `grant_ad_refill` path
      (BACKEND.md §6.4); until wired, the 10/day rate limit is the only defense.

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
