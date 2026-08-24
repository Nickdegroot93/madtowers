# STOREACCOUNTS.md — developer accounts, step by step

**Decision made (Nick, 2026-08-23): publishing as a PERSON on both stores. 100%.**
An eenmanszaak/KvK may come later for payouts and tax — see the final section for the
conversion path; nothing in the setup below blocks it. Facts verified against store
policy 2026-08-23. GOLIVE.md Phase 1 points here; this file owns the detail. Check items
off as they're done.

What "person" costs us, accepted with eyes open:
- **Google's 14-day tester wall** applies (it runs during playtesting anyway — see §1).
- **Your legal name** appears as the seller on both store listings.
- **EU trader info goes public** (address/phone/email on the EU store pages) — masked
  with a dedicated email, number and address in §3, so the home address never appears.

---

## 1. Google Play Console — start FIRST (it owns the launch date)

The wall, verbatim: a personal account created after 2023-11-13 must run a **closed test
with ≥12 testers opted in for 14 continuous days** before it can *apply* for production
access (support.google.com/googleplay/android-developer/answer/14151465; the count
dropped from 20 to 12 on 2024-12-11). The clock only ticks while 12+ stay opted in —
recruit 14+ for slack. The build does NOT need to be finished and can update freely
during the window.

- [x] 1. Create the developer account at play.google.com/console — **personal**, $25
      one-time. DONE 2026-08-23; identity verification COMPLETE 2026-08-24 — console
      now offers "create your first app". App creation + the closed test are unblocked.
- [ ] 2. Set up the **payments profile** (payouts + selling IAP) as an individual and
      complete **tax info** (US tax form W-8BEN inside the payments profile; NL income
      is then declared privately in box 1 — no KvK needed to receive payouts as a
      person).
- [ ] 3. Create the app: **Hazard Heights**, bundle id `com.nickdegroot.hazardheights`
      (LOCKED, GOLIVE Phase 1 — never change once the listing exists).
- [ ] 4. **Release keystore + Play App Signing** (before the first AAB): create a keystore
      locally, back it up somewhere that survives this machine, upload-key model via Play
      App Signing. Phase 2's Google sign-in needs BOTH SHA-1s (upload key AND Play App
      Signing key) later.
- [ ] 5. Build a **closed-test AAB**. ⚠️ The old "debug build for test ads" idea does NOT
      work: Play rejects debuggable artifacts, so tester builds are release builds — and
      a release build serves LIVE AdMob ads to every tester (test fill only exists for
      devices registered in `TestDeviceIds`, applied in debug builds only). Fix wired
      2026-08-24: **tester-build discipline is `Assets/csc.rsp` containing EXACTLY
      `-define:MADTOWERS_SIM_ADS`** (never the unlock-all define). That define compiles
      AdMobBootstrap to a no-op and installs `SimulatedRewardedAdProvider` on device —
      the whole attempts→watch→refill economy runs with zero AdMob traffic. IL2CPP,
      ARM64, release signing, versionCode +1 per upload. The PRODUCTION build later
      returns to an EMPTY csc.rsp (GOLIVE Phase 6 check catches this).
- [ ] 6. Create a **closed testing track**, upload the AAB, add testers by email list,
      send the opt-in link, and confirm ≥12 show as opted in.
- [ ] 7. **Note the date 12+ were opted in. Day 15 or later:** apply for production
      access in Play Console (it asks about the test — answer honestly; this also
      doubles as the difficulty playtest, `level_stats` collects while they play).
- [ ] 8. After production access: complete the store listing (assets per GOLIVE Phase 1),
      content rating questionnaire (IARC), data-safety form (GOLIVE Phase 5), and the
      **trader declaration** (§3).

## 2. Apple Developer Program — start the SAME day (unblocks code work)

Membership is **$99/year, billed at enrollment, year starts that day** — no free setup
tier. Worth paying now: it unblocks Sign in with Apple (Phase 2), the IAP product +
`validate_receipt` sandbox (Phase 3), TestFlight, and real-device iOS testing.

- [x] 1. PURCHASED 2026-08-23; ⏳ "may take up to 48 hours to process" — everything
      below waits for the welcome email. Enroll at **developer.apple.com/programs/enroll** — **individual** (Apple ID
      with 2FA; identity verification + the €99 payment happen DURING enrollment,
      usually approved in 24–48 h; no D-U-N-S needed as a person). ⚠️ Logging in ≠
      enrolling: a plain Apple ID login shows the free site WITHOUT "Certificates,
      Identifiers & Profiles" — that whole section only appears once the paid
      membership is active (hit this 2026-08-23). The Apple Developer iPhone app is
      often the fastest enrollment route (inline ID scan).
- [ ] 2. Once membership is active — in Certificates, Identifiers & Profiles →
      Identifiers → "+" → App IDs → App: create the **App ID** for
      `com.nickdegroot.hazardheights` and enable the **Sign in with Apple** capability;
      create the **Services ID + key** for it (Phase 2 needs these for Supabase's Apple
      provider).
- [ ] 3. In App Store Connect: create the app listing shell (name **Hazard Heights** —
      claim it early, names are first-come).
- [ ] 4. Create the IAP product **`madtowers_unlimited`** — non-consumable, $3.99 tier
      (SHOP.md §7; same product id as Google, GOLIVE Phase 3).
- [ ] 5. Sign the **Paid Applications agreement**, add banking + tax forms (W-8BEN as an
      individual) — IAP sandbox testing is blocked until this is signed.
- [ ] 6. **Enroll in the App Store Small Business Program** (15% instead of 30% under
      $1M/yr) — a separate manual enrollment, do it BEFORE the first sale; Google's
      equivalent (15% on first $1M) is automatic.
- [ ] 7. TestFlight: upload the first iOS build when one exists (GOLIVE Phase 6 — iOS has
      never been run; simulator first, no account needed for that).

## 3. EU trader requirements (DSA) — the personal-account privacy prep

Selling IAP makes you a **trader** under the EU Digital Services Act. Both stores require
a trader declaration for EU distribution and **publicly display the trader's address,
phone number, and email on the EU store pages** (enforced since 2025-02-17; Apple removes
non-compliant apps in the EU, Google restricts updates). Phone and email must pass
verification.

**Bare-minimum posture (Nick, 2026-08-23: launch cheap, upgrade if it earns):**
- [ ] Support email — FREE via forwarding, no paid mailbox: ImprovMX or forwardemail.net
      → add their MX + TXT records in Vercel's DNS panel → `support@hazardheights.com`
      forwards to Gmail. Needed for store verification, the privacy policy contact and
      the trader declaration. (Site is LIVE as of 2026-08-23; email still to do.)
- [ ] Phone — use the real number (it becomes public on EU store pages; a ~€10 prepaid
      SIM is the later fix if it bothers you).
- [ ] Address — use the home address (legal, satisfies the requirement, just public in
      the EU). A P.O. box / virtual address (~€10–25/mo) is a LATER upgrade — trader
      info is editable any time, so nothing is locked in.

## 4. What gates what (why the order above)

- The **14-day wall** gates the Play launch date → start §1 immediately, in parallel
  with playtesting.
- The **Apple account** gates Phase 2 (Sign in with Apple code), Phase 3 (IAP + receipt
  validation), and TestFlight → enroll now so code work isn't idle.
- **AdMob app-store linking** (GOLIVE Phase 4 leftover) needs both listings to exist —
  do it when the listings go live, or ad serving stays limited.
- **Ad-network accounts rot when idle** (AdMob deactivates after ~6 idle months) — that
  account already exists (2026-08-09), so don't let it sit unused past winter.

## 5. Costs recap

| Item | Cost | When |
|---|---|---|
| Google Play Console | $25 one-time | today |
| Apple Developer Program | $99/year, starts at payment | today |
| Email forwarding (ImprovMX / forwardemail.net) | free | before trader declaration |
| Virtual address / P.O. box | ~€10–25/mo — later upgrade, only if the app earns | optional |

---

## Later: converting to an eenmanszaak (when revenue justifies it)

Nothing above burns a bridge. When the KvK registration happens (tax number, business
payouts, business trader address), the path per store:

- **KvK first**: register the eenmanszaak (~€80 one-time), get the KvK number; request a
  **D-U-N-S number** for it (free via Dun & Bradstreet, days–weeks) — Apple's org
  identity is D-U-N-S-based.
- **Apple: true in-place conversion exists.** Contact Apple Developer Support to convert
  the individual membership to an organization membership using the D-U-N-S number —
  same account, same apps, no transfer, no user impact. Seller name flips to the
  business name.
- **Google: no conversion — transfer instead.** Create a NEW organization Play Console
  account ($25 again, needs the D-U-N-S/business verification) and use Play's **app
  transfer** to move Hazard Heights to it. Users, reviews, stats and Play App Signing
  keys survive a transfer. The new org account is exempt from the 14-day wall (and the
  old account's production access carries no meaning after transfer).
- **Update both trader declarations** and the payout/tax profiles to the business
  identity; update the privacy policy's controller/contact if the legal entity changes.
- Timing hint: do it in a quiet week — both processes are days, not hours, and store
  listings should not be mid-review during a transfer.
