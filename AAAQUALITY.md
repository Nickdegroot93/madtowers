# AAAQUALITY.md — Gap analysis vs. the AAA mobile bar (July 2026)

Snapshot of the full-game audit done 2026-07-26: what MadTowers already has, what's missing
for AAA mobile quality, and in which order it matters. This is a **planning doc, not a binding
contract** — binding specs live in the per-topic docs (PHYSICS.md, JUICE.md, SHOP.md, BACKEND.md, …).

**Nick's shipping stance (2026-07-26):** finish the game as-is first. Tier 1 below is the
ship list. Retention features (daily mode, events, push) and online PvP are **explicitly
deferred to post-launch** — wanted, but later. Finishing touches (full audio pass etc.)
also later. Current focus: fixing puzzle mode (Laser Limit waves).

---

## Verdict

The core game already sits at or above the premium tier: physics feel with a binding juice
contract, server-authoritative leaderboards with an anti-cheat handshake, CLEAN/BOOSTED board
split, a collection meta (Vault), unlock reveals, safe-area-correct responsive UI, and a
no-ads / coins-never-sold economy — the same template the genre leaders use (Royal Match runs
zero ads; Grindstone is the closest structural model). Nobody in the physics-stacker niche
ships the full premium package (Tricky Towers: modes but no live cadence or daily challenge;
Stack/Six!: feel but no meta), so the intersection MadTowers aims at is open.

The genre's most-cited one-star failure mode is **broken cloud save / lost progress** —
engineer against that above all.

---

## Tier 1 — Ship-blockers (invisible infrastructure; none exists yet)

| Gap | State today | Notes |
|---|---|---|
| Crash reporting | Absent (Cloud Diagnostics off, no Sentry/Crashlytics) | Cheapest, highest-leverage gap. Add before any store build. |
| Analytics | Absent (Unity Analytics disabled, no SDK) | Without it: no crash-free KPI, no FTUE funnel, no drop-off data. |
| Production backend cutover | Online layer runs vs local Supabase dev stack only | BACKEND.md §10.5. Phase E, unfinished. |
| Apple/Google sign-in linking | Designed, native plugins not integrated | Until then anonymous accounts don't survive uninstall → the genre's #1 complaint. |
| IAP integration | "MadTowers Unlimited" $3.99 is a disabled shelf card; no Unity Purchasing/StoreKit, no restore flow | Designed in SHOP.md, not built. |
| iOS haptics | Deliberate no-op (Android done); no player toggle | Haptics are an Apple featuring criterion (Alto's Odyssey's ADA was largely for world-event haptics). Pending Nice Vibrations import. |
| Legal/compliance | Nothing wired: no consent flow, no ATT prompt, no privacy-policy URL; About/Legal tab is placeholder | Non-negotiable for store submission. Consent ordering matters once rewarded ads ship (UMP before ATT init, ad SDK init after both). |
| Review prompt | Absent | Trivial win. Prompt after a win, never session 1, never after a loss. 90% of Apple-featured apps hold 4.0+. |
| App icon / store assets | Icon still Unity template default; no screenshots/listing assets | Product-page quality is itself a featuring criterion. |
| Audio holes | Level-win / game-over / life-lost / gentle-landing sounds unwired; procedural placeholders live; music: all 15 chapters covered (stale claim fixed 2026-08-30) | Phase F scope. Sound design is an Apple featuring criterion. |
| Puzzle mode correctness | Block counting + wave math wrong (see PUZZLE section / Nick's notes) | **Current focus.** |

## Tier 2 — Retention layer (biggest structural gap vs. genre) — **POST-LAUNCH by decision**

After finishing available chapters there is no reason to open the app tomorrow. No daily
anything, no events, no push (Settings tab is "COMING SOON"). Genre-fit versions, scaled to
a small team, in rough value order:

1. **Daily challenge with its own leaderboard** — Grindstone's "Daily Grind" is the model;
   Endless win-condition + mode system already provide the raw material; Tricky Towers' lack
   of one is a visible gap to beat.
2. **Streak / daily coin reward** — coins fit the earn-only economy; lowest-cost D7 lever.
3. **Segmented push notifications** — behavioral triggers only (attempts-full, streak-at-risk,
   event-live), never blasts.
4. Later still: one small-cohort recurring tournament (≈50-player, not global), a free
   season-shaped reward track, rotating side modes surfaced to players (the 57 GameMode
   assets exist; Custom Game is editor-only today), **online/async PvP stacking** (Tricky
   Towers proves the core carries it; nobody does it well on mobile).

## Tier 3 — Featuring levers (small team punches above weight)

- **Accessibility — mostly absent; the most under-exploited Apple-featuring lever** (2025 ADA
  winner was a solo dev who won on accessibility). For a physics game: colorblind-safe block
  palettes, reduced-motion (screen-shake + effects toggles already exist — halfway there),
  global text scale (today every font size is a hardcoded literal), audio/haptic cues for
  tower-stability state.
- **Localization — zero i18n**, all strings hardcoded English at call sites. Featuring bar is
  ~8–10 languages (EFIGS + CJK + pt-BR). Retrofit cost grows weekly → at minimum start routing
  strings through a table early.
- **Game Center / Play Games achievements + leaderboard mirroring** — supplemental layer only
  (BACKEND.md correctly rejects them as the account system).
- **iPad layout/performance verification** — recurring review-bomb theme in the genre.

## Tier 4 — Worth knowing, lower urgency

- **Remote config** — prices/tuning are code-owned constants by deliberate contract (SHOP.md);
  fine for launch, revisit if the economy ever needs tuning without app-store releases.
- **Block-skin cosmetics** — the genre-native coin sink (Tricky Towers sells brick skins);
  Vault + earn-only coins are a natural fit; gives late-game players a spend.
- **Async versus / multiplayer** — far future; the genre's open crown.

## Reference: research basis

- AAA standards brief: FTUE (fun < 1 min, tutorial woven in), retention benchmarks (top-quartile
  D1 ≈ 26–28%, D7 ≈ 12%+), hybrid monetization norms, Firebase-class infra as table stakes,
  Apple featuring criteria (UX, accessibility, localization, sound, product page; nominations
  ≥ 2 weeks lead), review-prompt discipline, EFIGS+CJK localization norm.
- Genre brief: Monument Valley/Alto's/Grindstone define the premium tier (event-driven haptics,
  audio identity, restraint, daily challenge + collection meta); Royal Match/Candy Crush define
  the live-ops tier (event archetypes: streak, competitive, collaborative, collection,
  mini-game); Stack is the cautionary ad-saturated tale; Balatro proves premium mobile viability.
