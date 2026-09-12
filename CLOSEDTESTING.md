# Closed testing — Android 12-person checklist

**Current decision: Nick, 2026-09-12. Focus on the 12-person Android closed test.**
Unity iOS export, Xcode builds and iPhone/TestFlight acceptance are deferred to
[IOS_TESTING.md](IOS_TESTING.md); they do not block this Android test. This replaces
the earlier 25-person/both-platform scope. Android purchase work remains outstanding;
this decision does not mark it complete or waive its existing acceptance checks.

Small internal tests are allowed and necessary to prove readiness first. This document
controls the group invitation gate; [GOLIVE.md](GOLIVE.md) tracks the wider launch,
[STOREACCOUNTS.md](STOREACCOUNTS.md) owns console setup, and [BACKEND.md](BACKEND.md)
and [SHOP.md](SHOP.md) describe implementation. A checked item needs evidence from
the actual implementation or console; Editor simulations alone do not prove readiness.

## Current status

**Android login test passed (reported by Nick, 2026-09-12).** Nick reports that testing
works after submitting 2.1.2 / code 3 to closed testing. Individual scenario outcomes
and device details were not supplied. iOS and real purchases remain pending.

| Area | Available | Remaining blocker |
| --- | --- | --- |
| Accounts | Anonymous auth, sign-in UI, cloud progress sync; native Google/Apple integration and provider setup added 2026-09-12 | Android login works per Nick; detailed edge-case evidence not recorded; iOS deferred |
| Unlimited | Buy/Restore UI, cached ownership, offline access, thank-you UI | Device store provider and backend receipt validation absent |
| Startup and saves | Connection gate and offline-progress merge implemented | Physical-device network, account recovery and store tests outstanding |
| Distribution | Android 2.1.2 / code 3 submitted and tested by Nick | Measured download size; higher unused code for the next upload |

Supabase's public Auth settings were checked on 2026-09-10: Apple and Google were
disabled. Nick confirmed both providers enabled on 2026-09-12, plus manual linking and
Google identity scopes. Nick subsequently confirmed the Android device test works.

## Google login device checklist

1. Build a fresh signed Android AAB, using a version code higher than the latest Play upload.
2. Upload to Play **Internal testing** and install using its tester opt-in link.
   Use the testing track, not Internal app sharing (which uses a separate signing certificate).
3. With a disposable QA account, cancel Google login once: guest progress must remain.
4. Earn progress, finish the run, then link Google. Restart: account and progress must persist.
5. Confirm the matching user in Supabase Authentication has a Google identity; confirm cloud sync.
6. Recover that account on another device or after clearing only the QA app's data.
   Sign in with the same Google account: the same user ID and synced progress must return.
7. Record build/device/results below; fix failures before promoting to Closed testing.

| Build / device | Link + restart | Cancel | Recovery / same user ID | Date |
| --- | --- | --- | --- | --- |
| 2.1.2 / 3 submitted; device unspecified | Nick reports working overall | Not individually recorded | Not individually recorded | 2026-09-12 |

Internal testing does not count toward the 12-person closed-test requirement.
See [Play testing tracks](https://support.google.com/googleplay/android-developer/answer/9845334)
and [closed-test requirements](https://support.google.com/googleplay/android-developer/answer/14151465).

## 1. Accounts and store setup

- [ ] Confirm developer account access, agreements and store prerequisites for testing
  sign-in and selling in-app products on Android; iOS setup is deferred. Complete required payment/tax
  setup; keep private keys and service credentials out of the repository.
- [ ] Preserve app identifier `com.nickdegroot.hazardheights` on both platforms.
- [ ] Configure Google OAuth clients and Supabase's Google provider. Register the
  signing certificates used by the actual Play-installed build, including Play App Signing;
  also configure any separately signed builds used in internal QA.
- [ ] Confirm Google works on a Play-installed Android build. Apple build and device
  acceptance are tracked separately in [IOS_TESTING.md](IOS_TESTING.md).
- [ ] Explicitly define supported cross-platform account linking/recovery. Do not promise
  Android-to-iPhone recovery until a shared supported identity path is implemented and tested.

## 2. Implement and prove sign-in and recovery

- [ ] Replace `OnlineService.LinkWithGoogle` and `LinkWithApple` placeholders with
  native authentication and verified Supabase identity linking/sign-in.
- [ ] Link an existing guest without losing its progress or changing ownership incorrectly.
- [ ] Handle returning accounts and an identity already linked to another account:
  define and implement the save merge/conflict behavior before switching sessions.
- [ ] Handle cancellation, denial, expired credentials, network failure and duplicate
  callbacks. A failed sign-in must leave the original guest save usable.
- [ ] Persist/refresh sessions and prevent in-flight saves or purchases from being applied
  to the wrong account after an account change.
- [ ] Verify linked-account deletion and the subsequent signed-out/fresh-account state.

Run the following on real Android devices with disposable QA accounts. Repeat on iOS later:

- [ ] Earn guest progress, link the platform account, and verify completions, scores and
  currencies remain correct after restarting.
- [ ] Sign in on another device and recover the same profile.
- [ ] Uninstall/reinstall a QA build, sign in, and recover synced progress.
- [ ] As an Unlimited owner, finish four levels offline; reconnect and wait for confirmed
  sync; recover those completions on another device. Offline runs must not enter ranked
  leaderboards. Unsynced data deleted before reconnection cannot be recovered.
- [ ] Exercise an existing cloud save plus local guest progress, cancellation, interrupted
  login and session expiry. Record expected and observed outcomes.

## 3. Implement Unlimited purchases

Android acceptance applies now; Apple-specific work in this section is deferred to iOS.

- [ ] Create/activate the non-consumable product `madtowers_unlimited` in both store
  consoles: Hazard Heights Unlimited, intended US price $3.99 with store-localized prices.
- [ ] Install and implement the Unity IAP device adapter for `IPremiumStoreProvider`;
  initialize it at boot and fetch the product and localized price from the store.
- [ ] Implement server verification of Google purchase tokens and Apple transactions
  through `validate_receipt` or its documented replacement. Validate product, app,
  purchase state and ownership; keep verification credentials server-side.
- [ ] Grant server premium only after verification. Make retries/duplicate callbacks
  idempotent and handle acknowledgement/transaction completion correctly.
- [ ] Handle pending purchases, cancellation, rejection, network loss and app termination
  between store success and backend confirmation; resume verification without charging again.
- [ ] Implement store restoration and define entitlement ownership across guest linking,
  account changes and reinstall. Do not let a receipt silently grant unrelated accounts.
- [ ] Distinguish sandbox/test transactions from production purchases in backend records
  and entitlement policy; do not accidentally promise permanent production ownership
  from a free test purchase.
- [ ] Define refund/revocation handling and verify it, with a documented manual procedure
  acceptable for this testing phase if automated notifications are not yet installed.
- [ ] Confirm the purchase thank-you appears once, restore uses the restore confirmation,
  and verified ownership survives restart and enables offline play, unlimited lives and no ads.

## 4. Configure free store test purchases

Payment integration must work before launch; testers do not need to spend real money.

- [ ] **Android:** add each account that will test purchases to Play Console's **license
  testing** configuration as well as the closed-testing access list. Being a closed tester
  alone does not make purchases free. Verify the Play account on the device matches.
- [ ] Install the actual Play-distributed release and confirm a test payment method is
  available before asking anyone to buy. Test approval, decline and delayed completion.
- Deferred: iOS TestFlight/sandbox purchase testing — see [IOS_TESTING.md](IOS_TESTING.md).
- [ ] Verify buy, restart, reinstall/restore, second-device restore, cancellation, pending
  payment, duplicate callback, interrupted validation and refund behavior on Google Play
  now; repeat for Apple before iOS distribution.
- [ ] Verify invalid receipts are rejected and a verification retry does not lose a valid
  purchase or show Unlimited as confirmed prematurely.

Official testing references:

- [Google Play Billing testing](https://developer.android.com/google/play/billing/test)
- [Play Console license testing](https://support.google.com/googleplay/android-developer/answer/6062777)
- [Apple purchase testing](https://developer.apple.com/documentation/storekit/testing-at-all-stages-of-development-with-xcode-and-the-sandbox)
- [TestFlight purchase behavior](https://testflight.apple.com/)

## 5. Prepare the actual tester builds

- [ ] Build a fresh signed Android release AAB with a previously unused, increasing
  version code. **2.1.2 / 3** has been submitted; use a higher unused code for the next upload.
  Do not upload the old `HazardHeightsBuilds/HazarHeights-1.aab` as this update.
- Deferred: iOS signed export and TestFlight build — see [IOS_TESTING.md](IOS_TESTING.md).
- [ ] Use the real sign-in and purchase integrations, not Editor purchase simulation.
- [ ] Inspect `Assets/csc.rsp`: retain `MADTOWERS_SIM_ADS` for the current closed-test
  ad strategy and remove `MADTOWERS_UNLOCK_ALL` for the release candidate. It is currently
  present but guarded to the Editor; release verification must still use earned progression.
- [ ] Verify simulated-ad refills work with the backend configuration. Do not enable
  `ssv_enabled` while these builds depend on simulated refills. Changing to real ads is
  a separate coordinated client/server change described in GOLIVE.md.
- [ ] Measure the new bundle's compressed device download using bundletool/Play Console.
  Prefer below 200 MB (the optimization target remains ~160 MB). The current
  [Play size policy](https://support.google.com/googleplay/android-developer/answer/9859372)
  lists a 500 MB base-module compressed-download limit; above 200 MB triggers a
  non-blocking mobile-data warning. Raw AAB bytes are not the download measurement.
- [ ] If size needs reducing, inspect the build report for large textures, audio and
  unused Resources assets. The 30 new chapter sprites already use 512px ASTC 6×6 on
  Android (~3.39 MiB texture payload); measure before reducing visual quality.
- [ ] Confirm store forms, privacy/support links and account-deletion information match
  the integrated SDKs and data use, and satisfy the consoles' testing requirements.
- [ ] Check install/update from the previous tester build preserves existing saves.
- [ ] Run device smoke tests: fresh welcome and 25-block tutorial, chapters, Vault,
  gameplay/results, pause/resume, lives/refills, purchases, restore and profile.
- [ ] Test startup online, slow connection, offline free-account Retry and cached Unlimited
  offline entry; background/resume during startup, login and purchases. No stuck splash,
  accidental access grant, premature OFFLINE flicker or lost save.
- [ ] Review device logs for crashes/exceptions and rerun relevant repository regression
  checks after integration changes. Record remaining issues and resolve blockers.

## 6. Invitation gate and tester instructions

Complete this with Nick's own devices/a small internal pilot before starting the 12-person Android group.

- [ ] Record accepted Android build numbers, source commit, backend deployment,
  device/OS coverage and dated results for every account/purchase scenario above.
- [ ] Google sign-in and recovery passed on a Play-installed Android build.
- [ ] Android Unlimited purchase and restore passed with server verification.
- [ ] Confirm all 12 intended participants have the correct distribution access; those
  testing Android purchases also have license-testing access before attempting a purchase.
- [ ] Prepare one short tester brief: install/opt-in instructions, supported login method,
  how to identify a free test checkout, how to report bugs, and what to test each session.
  If a real payment method appears instead of a test checkout, stop and report it.
- [ ] Explain that simulated ads are intentional, test purchases are for testing, and
  players should link and confirm sync before any requested reinstall test.
- [ ] Provide a feedback channel and request build number, device/OS, reproduction steps
  and optional screenshots; never request passwords, tokens or full payment credentials.
- [ ] Confirm the Android group's participation schedule meets the requirements shown in
  this app's Play Console and the agreed testing arrangement; recruitment alone is not
  evidence that testers have opted in or completed testing.
- [ ] **Nick confirms readiness and starts the 12-person Android invitation.** Until the sign-in
  and payment gates pass, keep the group invitation on hold.

Do not mark this release ready solely because it compiles, Editor tests pass, or store
products exist. The acceptance evidence is an actual store-installed build completing
login, recovery, purchase and restore against the configured backend.
