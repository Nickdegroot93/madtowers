# GOLIVE.md — the ordered release plan

**Current testing scope — Nick, 2026-09-12:** 12-person Android closed testing.
Prove Google login/recovery on a Play-installed build first; follow
[CLOSEDTESTING.md](CLOSEDTESTING.md) for Android readiness. Unity iOS export, Xcode
builds and iPhone/TestFlight tests are deferred to [IOS_TESTING.md](IOS_TESTING.md)
and do not block this Android test. This supersedes the earlier 25-person/both-platform
scope; Android purchase acceptance remains outstanding.

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
- [x] **Crash/analytics decision** — DECIDED + WIRED 2026-08-22: **Unity 6's built-in
      Diagnostics** (crashes, C# exceptions, Android ANRs → Unity Cloud dashboard →
      project → Developer Data → Diagnostics). Chosen over Crashlytics/Sentry: zero new
      SDK, zero new vendor in the data-safety forms (Unity is already declared as the
      engine), and it was ALREADY ON — the `Diagnostic Data` project setting
      (`InsightsSettings.m_EngineDiagnosticsEnabled`) defaulted enabled with the Unity
      6.2+ upgrade; the deprecated legacy Cloud Diagnostics stays off. Added a dev-build
      "DEV: TEST CRASH" row (Settings → Alerts, two-tap) to prove the pipeline.
      **Still open, both on the next device build:** tap TEST CRASH, relaunch, confirm
      the report reaches the dashboard; and confirm IL2CPP symbols upload with the build
      (dashboard → Symbols) so native stacks aren't garbage. Phase 5 forms: declare
      "crash diagnostics / device info via Unity". No separate analytics product —
      the runs-ledger telemetry (level_stats) covers gameplay questions.
- [x] **Display-name moderation** — this box was stale (verified 2026-08-08):
      `claim_display_name` already does format validation (`^[A-Za-z0-9 _-]{3,16}$`), a
      profanity list, case-insensitive uniqueness and a `unique_violation` fallback, and
      the smoke suite covers all three refusal paths. **Still open, but it's a decision
      not code:** the guest-claim policy (claim-now-link-later, as built, vs. link-gated).
- [ ] **Per-level score sanity bounds** (BACKEND.md §6.2): derive the max-plausible
      score/height/duration table from Nick's current playtesting data; tighten
      `finish_run`. (Same pass can set XP farming bounds, XP.md §6.)

## Phase 1 — store accounts & listings (the unlock for everything below)

> **The account setup lives in `STOREACCOUNTS.md`** (split out 2026-08-23; DECIDED:
> publishing as a PERSON, both stores): the Play 14-day/12-tester wall and its exact
> closed-test steps, Apple enrollment + App ID + IAP product + Small Business Program,
> the EU DSA trader-privacy prep, keystore + Play App Signing, payout/tax, and the
> future eenmanszaak conversion path. That file is the checklist; this phase is done
> when it is.

- [ ] **Work through `STOREACCOUNTS.md`** — start §1 (Play) immediately: the 14-day
      tester clock decides the launch date more than any polish does.
- [x] **Bundle IDs LOCKED 2026-08-08: `com.nickdegroot.hazardheights`** on Android, iOS
      and Standalone. Android previously read `com.nickdegroot.madtowers`; **iOS and
      Standalone were still the Unity template default `com.unity.template.get-started`**,
      which would have been rejected on submission. Permanent from the moment a listing
      exists — do not change it after Phase 1. Then create both app listings.
- [ ] Store assets (2026-09-05: brand = the lava GOLEM mascot from the splash, key art
      lives in `~/Documents/MadTowers/Store/`, finals in `Store/final/`):
      - [x] Icon set: `Assets/Store/Icons/` (512 store icon + 432 adaptive fg/bg, tight
            golem crop; adaptive layers cut wider so the face + T-piece sit inside the
            launcher mask's inner 66%). Wire via **Tools > MadTowers > Apply Store Icons**
            (`StoreIconInstaller`) after any PNG swap - Player Settings slots were all EMPTY
            before (builds shipped Unity's default icon).
      - [x] Feature graphic 1024×500: `Store/final/feature-graphic-1024x500.png` (golem
            placing a T-piece on a tetromino tower, jungle dusk, ground fog, stone wordmark).
      - [ ] Phone screenshots: 2-8 real captures, 9:16 (phone shoots 1080×2340 = 9:19.5 -
            crop/frame to 1080×1920). Shot list: Jungle mid-tower w/ fog, a hazard moment,
            medal results card, chapter select w/ medal strips, Vault, Neon Nightfall tower.
      - [ ] Description copy (title 30, short 80, full 4000 chars), category, content
            rating questionnaire, privacy-policy URL, data-safety form.

## Phase 2 — sign-in: Apple & Google account linking (BACKEND.md §3.3)

Anonymous auth, link prompts, sign-in sheet, delete-account flow: BUILT. Public Auth
settings checked 2026-09-10: anonymous/email enabled, Apple and Google disabled then.
**2026-09-12 console setup (confirmed by Nick):** Google provider enabled in Supabase,
with the Web client first and three Android clients (current classical, PQC, previous
classical signing certificates). The Web client's secret was saved in Supabase only.
Public Google Web client ID (`Hazard Heights Supabase`):
`337047199421-jfad61tsukq8nubjbel0fs4deee4f6e8.apps.googleusercontent.com`.
Manual identity linking enabled (confirmed by Nick with a dashboard screenshot on
2026-09-12); new-user signup and anonymous sign-ins remain enabled. Separately signed local QA builds
still need matching Android OAuth clients. Apple App ID
`com.nickdegroot.hazardheights` registered with Sign in with Apple enabled (confirmed
by Nick on 2026-09-12). Supabase Apple provider enabled with that bundle ID and no
OAuth secret (confirmed by Nick on 2026-09-12). Google `openid`, `userinfo.email`,
`userinfo.profile` scopes confirmed by Nick's screenshot. Android device testing was subsequently
confirmed working by Nick; broader audience settings are not independently audited.
The planned iOS flow uses native Apple ID tokens; Services ID and OAuth client secret
are only needed if a browser-based Apple flow is added.
**Client implementation added 2026-09-12:** native Android Credential Manager and iOS
AuthenticationServices bridges, nonce-checked Supabase identity linking, existing-account
recovery with progress merge, account-owned caches/finish queues and guarded callbacks.
Editor/Android C# and Android Java compilation passed. Review fixes cover partial
links, session-write failure and account-specific refill state; 166 isolated assertions
pass, including checks with the real save/cache classes. Nick confirmed Android
device testing works on 2026-09-12 after submitting 2.1.2 / code 3 to closed testing;
individual scenarios and device details were not recorded. iOS build/device acceptance
remains deferred (Xcode and Unity iOS Build Support were missing during implementation).
See [Tools/IdentityChecks/README.md](Tools/IdentityChecks/README.md) for the recovery
policy, verification limits and device checklist. Remaining:

- [ ] **Sign in with Apple**: compile and validate the native iOS bridge, linking,
      recovery and deletion on device. Apple grant revocation is not implemented by
      the existing delete-account RPC; complete the revocation flow/credentials before
      iOS acceptance. Services ID is only needed for an added browser sign-in flow.
- [x] **Google Sign-In**: Android device test confirmed working by Nick, 2026-09-12.
      Separately signed sideloaded builds still require a matching OAuth client.
- [ ] **Test matrix**: link guest → Apple/Google · sign in on second device pulls
      progress + premium · unlink/re-link edges · delete a linked account (the
      `delete_account` client flow is built — verify once against production).

## Phase 3 — payments: "MadTowers Unlimited" IAP (SHOP.md §7; seam: `PremiumStore`)

Client flow DONE (Profile BUY, localized price, RESTORE in Settings → Account, offline
entitlement cache, premium offline-unranked play, explicit purchase thank-you and restore
confirmation). The startup splash checks cached ownership and exposes Restore when the
provider is ready. The production store provider and receipt validator are still absent;
Editor purchase tests are simulations, not proof that device purchases work. Remaining:

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
- [ ] **Device startup QA**: slow/failed auth and cloud loads; airplane-mode free vs cached
      Unlimited; background/foreground during Retry and Restore; late store callbacks.
      Editor review passed 500 assertions plus the isolated fresh-install splash-to-welcome
      scene transition. These do not replace store-sandbox or physical-device validation.
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
      (`supabase/tests/ad_budget.sh`, 6 checks; smoke still 22/22). Pushed to production
      (verified applied via `migration list --linked`, 2026-08).

Still open, and each one is genuinely account-gated:

- [x] **AdMob account + apps** — DONE 2026-08-09. Two apps (Android + iOS), each added as
      "not listed on a store yet", each with one Rewarded unit `attempts_refill`.
      ⚠️ Still to do at launch: **link both to the store listings** once they exist, or ad
      serving stays limited. Publisher `ca-app-pub-4384624714813425`.
- [x] **Real IDs wired** — DONE 2026-08-09. App IDs in `GoogleMobileAdsSettings.asset`,
      rewarded units in `AdMobRewardedProvider.cs`. Safety model (revised on device
      2026-08-09): the REAL ad units are used always — test fill comes from the
      registered `TestDeviceIds` list (Nick's phone), applied only when
      `Debug.isDebugBuild`; sample units can't be used because SSV is configured per
      OUR ad unit. **A non-development build serves LIVE ads — do not tap them.**
      For the closed-test track (Play rejects debuggable AABs, so tester builds are
      release builds): build with `-define:MADTOWERS_SIM_ADS` in `Assets/csc.rsp`,
      which no-ops AdMobBootstrap and installs the simulated provider on device —
      zero AdMob traffic from testers (wired 2026-08-24, STOREACCOUNTS.md §1.5).
- [ ] **iOS ATT** — Google routes the prompt through a UMP message configured in the
      AdMob console, so it cannot be built before the account. Also unverifiable here:
      no iOS build has ever been run on this machine.
- [x] **Server: AdMob SSV** — BUILT + DEPLOYED 2026-08-09 (SHOP.md §7.3 item 5 has the
      detail). Endpoint is live and verified rejecting forged signatures. **Two manual
      steps left before it is actually protecting anything:** register
      `https://cyinvljdxpdtynlkiqhm.supabase.co/functions/v1/admob-ssv` on each rewarded
      ad unit in the AdMob console, then flip `backend_config.ssv_enabled` to `true`.
      Until that flip the client-claimed path still pays — deliberate, so SSV can be
      proven on a device first. ⚠️ Do NOT flip while the closed test runs on
      `MADTOWERS_SIM_ADS` builds: simulated watches pay via the client-claimed path,
      so the flip would silently stop testers' refills. **This exact incident happened
      2026-08-25** (flag was found `true` during the closed test; ad claims silently
      paid nothing) — flipped back to `false` same day. Current hosted state: `false`.
      The launch flip is a Phase 6 checklist item.

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

- [ ] **Flip `backend_config.ssv_enabled` to `true`** — MUST happen together with the
      first real-ads release (no `MADTOWERS_SIM_ADS`), never before (kills tester
      refills — bitten 2026-08-25, see Phase 4 SSV item). Prereq: SSV callback URL
      registered on both rewarded units in the AdMob console (Phase 4). One-liner:
      `Tools/bin/supabase db query --linked "update public.backend_config set value = 'true'::jsonb where key = 'ssv_enabled'"`
- [ ] **`Assets/csc.rsp` contains NO dev defines — verify per release.** (2026-09-09:
      removed `MADTOWERS_UNLOCK_ALL` so closed testers follow genuine progression;
      the override is now editor-only even if reintroduced. `MADTOWERS_SIM_ADS`
      remains for closed testing; remove it together with the real-ads/SSV switch above.)
- [ ] **Size pass — aim for a download under ~160 MB** (first closed-test AAB measured
      197 MB max download, 2026-08). As checked 2026-09-11, [Play Console's size policy](https://support.google.com/googleplay/android-developer/answer/9859372)
      lists a **500 MB compressed-download limit for the base module**; over 200 MB
      triggers a non-blocking mobile-data warning, not an automatic upload rejection.
      Measure the new release with bundletool or Play Console: the AAB file size is
      not the device download size. The thirty new ChapterArt sprites already use
      512px ASTC 6×6 on Android (~3.39 MiB texture payload before packaging).
      Usual suspects in payoff order: texture compression on
      chapter/backdrop art (ASTC, sane max sizes), audio import settings (streamed
      Vorbis ~0.4, no decompress-on-load music), dead weight in `Resources/` from
      purchased packs (everything there ships whether referenced or not). The build's
      Editor.log has the per-asset size breakdown. Use Play Asset Delivery if content
      eventually needs to exceed the base-module limit (post-launch watchlist).
- [ ] Bump version/build numbers; signing: keystore (Phase 1) on Android,
      provisioning/certs on iOS.
      Android **2.1.2 / version code 3** submitted to closed testing; Nick confirmed
      Android testing works on 2026-09-12. Code 3 is used: the next new upload needs
      a higher unused version code. Final download-size measurement remains open.
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
- **Play Asset Delivery (install-time packs) when chapter count grows** — the 500 MB cap
  applies to the BASE module only; tagging chapter art + music as an install-time asset
  pack (same .aab, same one-tap install for the player, fully offline) raises the ceiling
  to ~4 GB. Decided 2026-08: NOT before launch — it means moving that content off
  `Resources/` onto AssetBundles/Addressables, a real content-pipeline refactor. Trigger:
  when the compression headroom (Phase 6 size pass) is being eaten by new chapters —
  ~30 more chapters ≈ +300–450 MB, so it is a when, not an if.

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
