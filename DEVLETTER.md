# DEVLETTER.md — the solo-dev letter, premium microcopy & review ask

**Status: BUILT 2026-08-22** (letter: `MainMenuRuntime.DevLetter.cs`; review facade:
`Shop/StoreReview.cs` over `com.google.play.review` + iOS `Device.RequestStoreReview`;
microcopy on the Profile card + refill offer; save flags `devLetterShownAtUnixUtc` /
`reviewAskedAtUnixUtc`, monotonic timestamps that cloud-merge as max with no SQL change).
The letter waits out an unlock reveal and shows the same visit; the link prompt defers
to the letter (one-shots never stack). Design below agreed 2026-08-05, unchanged.
Extends SHOP.md's monetization
surfaces with three one-shot beats; SHOP.md §7/§8 remain binding and unchanged. The
original idea (one popup at chapter 2 offering "buy the game OR leave a review")
was split apart on research — combining the asks weakens both and flirts with store
policy. Each ask now lands alone, at its natural moment.

Research inputs (Aug 2026): review prompts must go through the official APIs —
Google In-App Review (no pre-qualifying conversation around the flow) and Apple
SKStoreReviewController (hard cap 3 prompts/user/365 days, may silently not show,
UI not customizable — never build UI that *promises* a review dialog on iOS).
Reviews may never be traded against anything (Google incentivized-ratings policy);
"review instead of paying" framing invites obligation reviews and 1-star "stop
begging" reactions. Early purchase pressure measurably backfires into negative
reviews (AppFollow paywall-complaint analysis); milestone moments are the
documented best time to ask for a rating. No published A/B data exists for the
"solo dev letter" itself — treat it as a credibility/tone play, not a proven
conversion multiplier. Its real job: make the attempts meter read as one person's
honest design, not a company's funnel; its measurable effect lands mostly on
review count/warmth.

---

## 1. The three beats

| Beat | Moment | Surface | Ask |
|---|---|---|---|
| **Letter** | first Chapter 1 completion — the same moment `metaSystemsUnlockedAtUnixUtc` flips and the attempts meter + shop appear (SHOP.md §7.1) | one-shot full modal, once ever, skippable | none (one soft pointer to Profile) |
| **Premium microcopy** | the existing attempts-wall / Profile premium card | one line added to what's already built | the existing $ unlock |
| **Review ask** | first Chapter 2 completion (or first personal best, whichever ships) | official in-app review API, no custom preamble | a rating |

Sequencing logic: the letter explains the systems the instant they appear
(inoculation, not sale — "unlimited lives" is worth money only to someone who has
run out, and nobody has at chapter 2); premium stays where friction already is
(SHOP.md §7.2's wall is the conversion moment); the review ask fires alone at an
emotional peak, per platform best practice.

## 2. The letter (beat 1)

- **Trigger**: first Chapter 1 completion, after the win flow settles, before the
  player is back in the level list. Shown once ever (save flag, e.g.
  `devLetterShownAtUnixUtc` next to `metaSystemsUnlockedAtUnixUtc`, DATA.md rules).
- **Form**: modal in the standard taste contract (near-black body, neon edge, no
  ornament). Signed "— Nick". Single button: **KEEP PLAYING**. No price, no BUY
  button, no review button. Android back / tap-outside dismisses too — never trap.
- **Copy REWRITTEN by Nick 2026-08-22** (supersedes the draft below; as-built in
  `MainMenuRuntime.DevLetter.cs`): personal intro ("I'm Nick, the developer of…"),
  free + no pay-to-win, the premium pitch WITH the live store price
  (`PremiumStore.PriceText`, never a hardcoded number - localized tiers), and a soft
  standalone review mention. Two of the original rules were consciously relaxed: the
  letter may now name the price and mention a review. Two still bind: the review line
  is never conditional on not buying ("review instead of paying" framing invites
  obligation reviews), and there is still exactly one button and no reward. The
  original draft, kept for the record:

  > Hey — I'm Nick. MadTowers is made by one person.
  >
  > It's free, there are no forced ads, and nothing is pay-to-win. The meter
  > you'll see from here just paces losses — wins never cost anything.
  >
  > If you ever want to own the whole game outright, that lives on my Profile
  > page. Thanks for playing.
  >
  > — Nick

- **Rule**: every claim must stay verifiably true (no forced ads / no P2W / wins
  free — SHOP.md §8). The letter's credibility IS the feature; the moment it
  sells, it's a sales script. Don't add prices, timers, or a second button.

## 3. Premium microcopy (beat 2)

One line on the existing premium card (Profile + out-of-attempts pitch):
**"one purchase, supports one developer"** — flavor on the surface that already
converts. NOT a new popup: SHOP.md §7.2's "the boost tray is the only nudge"
restraint stands. No other change to the premium flow.

## 4. Review ask (beat 3)

- **Trigger**: first Chapter 2 completion (fallback: first new personal best after
  chapter 2 exists). Positive milestone, player not mid-task.
- **Mechanism**: Google In-App Review API on Android; SKStoreReviewController on
  iOS. Fire the API and accept that the OS may show nothing — no custom dialog
  before it (Google explicitly), no sentiment gating ("enjoying the game?"
  filters are gray-area on both stores), no reward, no "review instead of buying"
  anywhere, ever.
- **Frequency**: one lifetime trigger from us. The OS quotas are the outer bound,
  not the target.

## 5. Implementation notes (when built)

- Letter flag piggybacks the SHOP.md §7.1 unlock moment — same save-write, one
  modal in `MainMenuRuntime` language (~40 lines in the existing modal kit).
- Review beat needs the Play Core / StoreKit calls plumbed behind a tiny facade
  like `RewardedAds`/`PremiumStore` (editor = log-only simulated provider).
- SHOP.md §7/§9 get a cross-reference line when this ships (this file owns the
  detail; SHOP.md stays the monetization contract).
- GOLIVE placement: beats 1–2 are pre-launch content; beat 3 can ship in the same
  release (the API call is inert until players reach chapter 2 anyway).

Sources (Aug 2026): developer.android.com/guide/playcore/in-app-review ·
developer.apple.com/documentation/storekit/skstorereviewcontroller ·
Google Play incentivized-ratings policy (android-developers.googleblog.com
2017/06) · support.google.com/googleplay/android-developer/answer/9898684 ·
appfollow.io paywall-complaints analysis · appreply.co App Store Reviews 101.
