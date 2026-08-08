# MadTowers Backend: Auth, Tables, Server-Checked Lives & Cloud Sync

How accounts, online data storage, sync, leaderboards, and the server-authoritative attempts
meter work. This is the **detailed design** for the cloud layer; `DATA.md` owns the **local**
save contract and its five rules (one gateway, stable IDs, monotonic values, schema version,
timestamps). The sync layer slots in *behind* `ProgressStore` — gameplay code never changes.

> **Status: DESIGN FINAL (Nick approved 2026-07-22). Phase E core BUILT 2026-07-23 against
> the local dev stack (§10.5) — production cutover (real project, providers, plugins) pending.**
> Supersedes the earlier "local-first mirror, online optional" stance: **online is the
> STANDARD.** Campaign runs require a connection (attempts are server-granted, scores
> server-recorded); every player is logged in from first launch via an invisible anonymous
> account. Decision stack: **Supabase** (auth + Postgres + RLS), **anonymous-first auth**
> upgraded by **Sign in with Apple (iOS) / Google (Android)**, **hybrid schema** (jsonb
> personal payload + real columns for cross-user data), **server-authoritative attempts &
> scores**, **no email/password login**, **no Game Center / Play Games as the account system**.

---

## 1. The mental model (three separate concerns)

People conflate these; keep them apart:

1. **Identity** — *who* the player is: a stable `user_id`. Provided by Supabase Auth.
2. **Storage** — *where* the player's data lives and *what shape* it has.
3. **Live game state** — what the running game reads/writes moment-to-moment. Still local
   (`ProgressStore`, `RunState`); the network is consulted only at defined checkpoints
   (run start, run end, sync pushes) — never per-frame, never mid-physics.

### Why not the native game services (Game Center / Google Play Games)?

They are **per-ecosystem** — a Game Center identity doesn't exist on Android and vice versa,
so they can never give one cross-platform leaderboard (a core requirement). And they aren't a
database: their leaderboards accept a number and rank it (no fields, no clean/boosted split,
no loadout badges), and their cloud save is an opaque blob (no SQL, no cross-user queries).
They remain **optional later** (§3.6) purely for native achievement popups / the platform
overlay, mapped onto the same Supabase user. Not the foundation.

### Why not email + password?

Highest-friction option on mobile (typing, verification mail, forgotten passwords), lowest
player trust, and it makes us run password infrastructure. Apple/Google one-tap sign-in is
what casual-game players expect. Supabase supports email if a genuine need ever appears;
don't ship it in v1.

---

## 2. The decision: Supabase + anonymous-first + server-authoritative

- **Supabase** (Postgres + Auth + RLS + Edge Functions) is the entire cloud backend — the
  login system and the database in one, home territory (Postgres).
- **Everyone is logged in from second one**: first launch performs a silent **anonymous
  sign-in** — a real account, a real `user_id`, no screen shown. Server-checked attempts and
  leaderboard participation work for 100% of players from the first session.
- **Server-authoritative where fairness or money is involved**: attempts (lives meter),
  score submission, ad-refill grants, premium receipt — all go through server functions the
  client can invoke but not bypass.
- **Local-first survives only for the personal progress payload** (discoveries, completions,
  bests, settings, wallet): the device save remains the play-time source of truth and mirrors
  to the cloud via the DATA.md merge rules. Attempts and leaderboard scores are **not**
  local-first — the server owns them.
- **Hybrid storage**: bulk personal save = one **`jsonb` document** (extend freely, no
  migrations); anything queried **across users** (scores) or owned by the **server**
  (attempts, runs) = real columns/tables.

---

## 3. Auth

### 3.1 How the login actually works (no passwords anywhere)

"Sign in with Apple" / "Sign in with Google" are the same mechanism:

1. Player taps the button → the **OS** shows its own sheet (Face ID / account picker), one tap.
2. Apple/Google returns a signed **identity token** ("this is genuinely account X").
3. The app forwards the token to Supabase Auth (`signInWithIdToken`), which verifies the
   signature and creates-or-finds the user, returning a session (`user_id` + JWT used on
   every request).

We never see or store a password, never run password-reset flows. Apple may hide the
player's real email (private relay) — fine, we don't need emails.

### 3.2 Anonymous-first (binding)

- **First launch → Supabase anonymous sign-in**, silent. Full account immediately: progress,
  coins, unlocks, server-checked attempts, leaderboard rows — nothing is "pending login."
- **Auto display name** on the on-theme pattern **`Builder-XXXX`** written to `profiles`.
  Anonymous players appear on leaderboards immediately under it (a player who sees
  themselves at #23 is hooked; a board they're not on is a shrug).
- The one real weakness: an anonymous account's only proof of ownership is the device
  session — **uninstall = account gone**. That is exactly what the link prompts sell.
- No first-launch login wall. Architecturally a wall would be a one-line policy change on
  this same design; it's rejected because pre-gameplay screens measurably cost installs and
  buy nothing the anonymous account doesn't already do. (Also keeps us clear of Apple's
  guideline against demanding accounts before core functionality.)

### 3.3 Linking a real account (Apple/Google) — the upgrade

Linking **upgrades** the anonymous user in place — same `user_id`, all progress/scores/coins
preserved. It adds: **recovery** (progress survives uninstall / new phone), **cross-device**
(sign in elsewhere, same account), and **a claimed display name**.

- **iOS → Sign in with Apple. Android → Google sign-in.** Both first-class Supabase providers.
- **Apple policy:** offering Google login inside the iOS app **requires** also offering Sign
  in with Apple. Apple-only on iOS / Google-only on Android is allowed. For cross-*platform*
  moves (iOS ↔ Android), let a player link **both** providers to one account.

### 3.4 When we prompt (binding — pull, not push)

1. **After Chapter 1 completion** (the same moment SHOP.md §7.1's soft-landing gate flips
   meta systems on): one dismissible card, shown once — "Sign in so your tower survives a
   new phone."
2. **Tapping their auto-name anywhere** (leaderboard row, Profile tab): "Sign in to claim
   your name." Expected main conversion point. Custom display names require a linked account.
3. **The Profile tab identity card** always shows the sign-in button (SHOP.md §9.2 reserves
   the slot).

No other prompts. No wall, no login interstitials, no nagging on a timer.

### 3.5 Display names

Auto `Builder-XXXX` until linked; claiming a custom name = link + pick name. Names need a
uniqueness rule (or visible discriminator) and at least a basic profanity filter before
leaderboards go public — server-side check in the rename function (§11 open item).

### 3.6 Native services (Game Center / Play Games) — optional, later

Layer on after the Supabase path works, only for the native overlay / achievement popups /
platform auto-signin. They become an *additional* identity mapped to the same Supabase
`user_id`. Not a v1 requirement.

### 3.7 Account deletion (store requirement)

Both stores require an in-app **"delete account"** path once accounts exist. A server
function wipes the user's rows (`profiles`, `progress`, `scores`, `attempts`, `runs`) and
the `auth.users` entry. Button lives in Settings.

---

## 4. Storage — the hybrid schema

### 4.1 Tables

```sql
-- Identity / display
profiles (
  user_id      uuid primary key references auth.users(id) on delete cascade,
  display_name text not null,                    -- auto "Builder-XXXX" until claimed
  is_linked    boolean not null default false,   -- has Apple/Google identity attached
  created_at   timestamptz default now(),
  updated_at   timestamptz default now()
)

-- The synced save document (the whole ProgressStore payload as one blob)
progress (
  user_id        uuid primary key references auth.users(id) on delete cascade,
  payload        jsonb not null default '{}',    -- see §4.2
  schema_version int  not null default 1,
  updated_at     timestamptz default now()
)

-- Competitive data we query ACROSS users → real columns, not the blob
scores (
  user_id      uuid references auth.users(id) on delete cascade,
  level_id     text not null,                    -- stable asset name (DATA.md rule 2)
  board        text not null check (board in ('clean','boosted')),  -- SHOP.md §5 split
  best_score   int  not null default 0,
  best_height  real not null default 0,
  loadout      jsonb,                            -- boosted rows: which supplies (honesty badge)
  achieved_at  timestamptz default now(),
  primary key (user_id, level_id, board)         -- upsert on improvement, via finish_run only
)

-- SERVER-OWNED: the attempts meter. Client never writes this directly.
attempts (
  user_id       uuid primary key references auth.users(id) on delete cascade,
  count         int not null default 5,          -- current attempts (cap 5)
  last_regen_at timestamptz not null default now(),
  premium       boolean not null default false,  -- set only by receipt validation (§6.4)
  updated_at    timestamptz default now()
)

-- SERVER-OWNED: the run ledger (the start/finish handshake, §6.2) + free analytics
runs (
  run_id      uuid primary key default gen_random_uuid(),
  user_id     uuid not null references auth.users(id) on delete cascade,
  level_id    text not null,
  board       text not null,                     -- clean/boosted, fixed at start
  loadout     jsonb,                             -- purchased supplies, fixed at start
  started_at  timestamptz not null default now(),
  finished_at timestamptz,                       -- null = still open / abandoned
  won         boolean,
  score       int
)
```

### 4.2 The `payload` document (what lives in the jsonb)

Exactly the `ProgressStore` save object, serialized — "just a JSON object per user." Every
field obeys DATA.md rule 3 (monotonic, or timestamped):

```jsonc
{
  "schemaVersion": 4,
  "completedLevelIds": ["Level_JD1_TheUndergrowth", "..."],   // set (union merge)
  "bests": [ { "levelId": "...", "board": "clean", "bestScore": 84,
               "bestHeightMeters": 14.2, "achievedAtUnixUtc": 1784290000 } ], // per-metric max
  "discoveredBlocks": ["normal", "ice", "maw", "magma"],     // set (union) — drives the Vault
  "abilitiesSeen":   ["recovery", "zap", "fission"],         // set (union) — Vault "seen"
  "abilitiesUsed":   { "zap": 12, "fission": 3 },            // counters (monotonic)
  "currencyEarned": 1280, "currencySpent": 860,              // balance = earned - spent
  "suppliesSpentTotal": 240,                                 // counter (SHOP.md §10)
  "settings": { "music": 0.8, "sfx": 1.0, "haptics": true,
                "updatedAtUnixUtc": 1784290000 }             // last-writer-wins by timestamp
}
```

Note what is **not** in the payload anymore: the attempts meter (server table, §6) and
premium (server flag). The local `attemptsState` becomes a display cache (§6.3), not synced
truth. `premiumUnlocked` mirrors the server flag read-only.

**Rule of thumb — what goes where:**

| Data | Home | Why |
|---|---|---|
| Personal progress, collections, settings, wallet counters | `progress.payload` (jsonb) | Per-user, read by that user only; evolves often → no migrations |
| Leaderboard scores | `scores` (columns) | Queried/sorted across users; written only by `finish_run` |
| Attempts meter, premium flag | `attempts` (columns) | Server-owned; fairness + money attached |
| Run ledger | `runs` | The anti-cheat handshake; analytics |
| Identity / display name | `profiles` + `auth.users` | Shared/joined; auth-owned |
| Future social (friends, % discovered, events) | new relational tables | Cross-user queries/joins |

### 4.3 Security model (RLS + server functions)

- The client ships **only the anon/public key**. **Never ship the service-role key.**
- `profiles`: read all (names show on boards), write own row (rename via a validating function).
- `progress`: read/write **own row only** (`auth.uid() = user_id`), writes through the merge
  function (§5.2).
- `scores`, `attempts`, `runs`: **read** — scores public, attempts/runs own-rows-only;
  **write — no direct client writes at all.** Mutations happen only inside `security definer`
  functions (`start_run`, `finish_run`, `grant_ad_refill`, `validate_receipt`,
  `claim_display_name`, `delete_account`). RLS + this rule is the whole security story.

---

## 5. Sync & the online requirement

### 5.1 What "online-only" means in practice (binding)

Enforcement lives at exactly **two moments**: run **start** (server must grant an attempt)
and run **end** (server must accept the result). The minutes in between can happen in a
tunnel — the run was already paid for. No heartbeat, no per-frame checks.

- **Campaign requires a connection to START a run** — for FREE players. No `start_run`
  grant → no run, with an honest message ("You're offline — MadTowers needs a connection to
  play ranked levels"). **Premium exception (Nick, 2026-07-30): Unlimited owners play
  offline, UNRANKED** — `RunGate.BeginRun` falls back to a local, non-server-backed run (no
  `run_id` → the finish report no-ops, the score can never reach a leaderboard; local bests
  still record). Offline play is one of the three things the purchase buys (SHOP.md §7).
- **`finish_run` failures are queued and retried** with the same `run_id` — a dropped
  connection at the results screen never loses the attempt refund or the score.
- **Offline grace runs were considered and REJECTED** (2026-07-22): reconciling
  offline-started runs reopens every cheat this design closes. Don't re-add without Nick.
- **Custom Game / practice modes stay attempts-free and offline-fine** (SHOP.md §7) — the
  app is never a brick on a plane.
- This formally retires the old "fully playable offline" principle **for campaign runs**.
  DATA.md's local-first contract still governs the progress payload (below) — its five rules
  are unchanged; only their *scope* shrank.

### 5.2 Progress payload sync (local-first, unchanged mechanics)

- **Source of truth during play:** `ProgressStore` (local JSON, instant). Unchanged.
- **Pull + merge** on launch/login; **push** debounced on app-background / key events
  (level complete, discovery, purchase). Offline pushes queue and retry.
- **Merge = the five rules, server-side** (a Postgres function): sets → **union**,
  bests/counters → **max**, settings → **last-writer-wins by timestamp**. Two devices that
  diverged merge with no conflict UI — this is *why* DATA.md rule 3 is non-negotiable.
- **Schema migration:** `schema_version` on both sides; migrate on load (client) and in the
  merge function (server).

Everything hides behind `ProgressStore`'s existing API (DATA.md rule 1) — gameplay calls
`MarkBlockDiscovered`, `ReportResult`, etc.; sync happens underneath.

---

## 6. Server-authoritative attempts (the lives meter goes online)

Replaces the offline `AttemptsService` wall-clock model (SHOP.md §7 keeps the design values:
cap 5, +1/10 min rolling, loss-only charging, win refunds). The server runs **no timers and
no cron** — regen is computed lazily whenever the row is touched.

### 6.1 `start_run(level_id, board, loadout)`

1. Lock the user's `attempts` row.
2. **Lazy regen:** `count = min(5, count + floor((now() - last_regen_at) / 10 min))`, advance
   `last_regen_at` by the whole intervals consumed (rolling, no cliff).
3. `premium = true` → skip charging entirely.
4. `count == 0` → refuse, returning exact **seconds until next attempt** (feeds the modal's
   "OUT OF ATTEMPTS — NEXT IN mm:ss" line, SHOP.md §9.1).
5. Else decrement, insert a `runs` row, return `{run_id, attempts_state}`.

Clock-cheating is dead: the phone's clock is never consulted.

### 6.2 `finish_run(run_id, won, score, height)`

1. Verify the run: exists, belongs to caller, not yet finished, duration plausible.
2. `won = true` → **refund the attempt** (loss-only model, enforced server-side).
3. **Submit the score** in the same call: sanity-bound it (max plausible score/height per
   level), then upsert `scores` on improvement for the run's `board` with its `loadout`.
4. Stamp the `runs` row finished, recording `paid_progress` (what XP has been paid for).

**`improve_run_score(run_id, score, height, progress)` — added 2026-08-08.** A won run
stays open to ONE more report: the post-victory "Keep Playing" session, whose score would
otherwise never reach a board (see XP.md "Win timing"). Deliberately narrow — won runs
only, raises only (`greatest`), never touches `attempts` (the refund already happened),
and pays only the XP delta above `paid_progress` so client retries are worth 0. The board
is taken from the run row, never from the client, so CLEAN/BOOSTED cannot be switched
after the fact. The client arms this window per run AND per level: a run id that outlives
its run (menu return, Custom Game, an unranked premium-offline launch) would otherwise
post the wrong level's score against it.

The `run_id` handshake is the core anti-cheat structure for free: a score can only be
submitted against a run the server saw start, once, on the board fixed at start, within a
plausible duration. That kills the laziest 90% of fake submissions. Abandoned runs (app
killed mid-run) simply stay unfinished — the attempt was spent, matching the loss-only rule.

**Accepted v1 trust boundary (reviewed 2026-07-23, deliberate):** the server cannot verify
gameplay, so two client claims are taken on trust inside the handshake — the `won` flag
(a cheater always claiming wins never drains their meter; economic ceiling = what the $3.99
premium unlock sells anyway) and the score value within global sanity bounds (per-level
bounds are the §11 open item pending playtest data; until then a determined cheater can
top a board). Both are the same trust tier BACKEND accepted for casual v1 all along; the
upgrade path when it matters is a per-level max-plausible table + shadow-flagging outliers.

### 6.3 The client meter becomes a display cache

`AttemptsService` keeps its UI role (top-bar chip, countdown) but stores only *the server's
last answer* + local cosmetic countdown between calls. Refresh on app-foreground and after
every `start_run`/`finish_run`. It is never authoritative.

### 6.4 Ad refills & premium (money paths — always server-verified)

- **Watch ad → +2 attempts (cap 5):** never let the client claim "I watched an ad." Proper
  path: the ad network's **server-side verification callback** (e.g. AdMob SSV) hits an Edge
  Function which grants the +2. Acceptable v1 fallback until SSV is wired: a
  `grant_ad_refill` function with a hard server-side rate limit (e.g. max 3/day).
  **As built (2026-07-30):** the client is wired to the v1 fallback —
  `AttemptsService.RequestAdRefill` calls `grant_ad_refill` after the ad reports
  watched-to-end (`RewardedAds` provider facade; simulated ad in the editor, no provider in
  device builds yet). Full remaining-work list: SHOP.md §7.3.
- **Premium unlock ($3.99 "MadTowers Unlimited"):** an Edge Function verifies the purchase
  receipt with Apple/Google, then sets `attempts.premium = true`. `start_run` reads the
  flag; the client never declares itself premium — **for the meter/leaderboard side.** The
  LOCAL save flag (`ProgressStore.IsPremium`) is deliberately client-held: it is the
  **offline entitlement cache** that makes airplane-mode play work, refreshed from the
  server verdict whenever one arrives, and worst-case it only self-grants what money can't
  buy twice (offline unranked play + no meter locally). **As built (2026-07-30):** client
  purchase/restore flow is fully wired through `PremiumStore` (simulated store in the
  editor, Unity IAP adapter at go-live); `validate_receipt` is NOT built. GOLIVE.md §3.

---

## 7. Leaderboards

- **Written only by `finish_run`** (§6.2) — there is no client score-write path at all.
- **Two boards per level: CLEAN and BOOSTED** (`scores.board`), per SHOP.md §5. Boosted rows
  carry `loadout` for the honesty badge icons. CLEAN is the default tab.
- **Cross-platform for free:** identity is Supabase's, not Apple's or Google's, so iPhone and
  Android players are just rows in one table. Top-100 =
  `select … where level_id = $1 and board = $2 order by best_score desc limit 100`
  (indexed), joined to `profiles.display_name`. Friend filters later via a `friends` table.
- **Reading from C# needs no SDK:** PostgREST over HTTPS (`UnityWebRequest`) with the anon
  key + the user's JWT. (A thin Supabase C# client is optional sugar.)
- Anonymous players appear under their auto-name (§3.2); tapping your own name when unlinked
  is a link prompt (§3.4).

---

## 8. Worked example: the Collections (Vault) tab

1. **In play:** a brick variant first appears → gameplay calls
   `ProgressStore.MarkBlockDiscovered("maw")`.
2. **Local:** lands in `payload.discoveredBlocks`, saves to disk instantly.
3. **Sync:** next push union-merges it into the cloud payload — discoveries from any device
   accumulate.
4. **Vault UI:** reads the **local** set and renders discovered vs. silhouette. No per-item
   table. (If we later want "what % of players found the Maw," *that* promotes to a
   relational table + aggregate.)

---

## 9. Build order (Phase E, each step ships independently)

1. **Supabase project + §4 tables + RLS + server functions' skeletons** — all server-side,
   game untouched.
2. **Anonymous auth on launch + progress sync** behind `ProgressStore` (pull/merge/push,
   §5.2) + auto display names. Game feels identical; data now mirrors to the cloud.
3. **Server-authoritative attempts** — `start_run`/`finish_run`, `AttemptsService` becomes a
   cache (§6.3), online-required messaging for campaign (§5.1). First player-visible change.
4. **Scores + the leaderboard screen** — CLEAN/BOOSTED tabs, riding `finish_run`.
5. **Apple/Google linking + claimed display names + the §3.4 prompts** + account deletion.
6. **Later, independently:** ad-refill SSV, premium receipt validation, optional native
   Game Center / Play Games layer.

Steps 1–2 are low-risk plumbing. Prerequisite from Phase B (done): the save object serializes
to one clean JSON document; wallet folded into `ProgressStore` (SHOP.md §10 ✅).

---

## 10. First-mobile-game practical checklist

- **Apple Developer Program** ($99/yr) — required to ship on iOS; also where the Sign in
  with Apple capability is enabled. **Google Play Console** ($25 one-time).
- **Supabase project** — free tier suffices through launch; watch row counts and egress as
  the base grows, not features.
- **Unity plugins:** a Sign in with Apple plugin (Lupidan's is the community standard),
  Google's sign-in plugin for Android. Supabase needs no SDK (plain HTTPS).
- **Ads SDK decision** (AdMob is the default candidate) — ships with premium per SHOP.md;
  nothing above blocks on it.
- **Store compliance that comes with accounts:** in-app account deletion (§3.7) and a
  privacy policy URL on both store listings.

---

## 10.5 As built (2026-07-23) — dev stack & file map

The Phase E core is IMPLEMENTED against a local Supabase stack. What exists:

- **Local stack:** Supabase CLI at `Tools/bin/supabase` (repo-local; brew was sandbox-blocked).
  Project config in `supabase/config.toml` — **ports shifted to 55321/55322/… because another
  project's stack (TradeParley) owns the 54321 defaults on this Mac.** Anonymous sign-ins
  enabled in config. Start: `Tools/bin/supabase start`; apply schema: `Tools/bin/supabase db
  reset`; smoke-test: `bash supabase/tests/smoke.sh` (must be all-PASS).
- **Schema:** migrations `supabase/migrations/20260722000001_core.sql` (tables, RLS,
  trigger, all RPCs, soft-landing config table) + `..._get_profile.sql` +
  `20260801000003_xp.sql` (account XP on `profiles.xp`, paid inside `finish_run`; XP.md
  owns that design). The client DTO key names are load-bearing contracts — comments in
  the SQL mark them.
- **Unity layer:** `Assets/SourceFiles/Scripts/Online/` — SupabaseConfig (URL/anon key,
  code-owned statics; local dev values, replace for production), SupabaseHttp, SupabaseSession
  (atomic file store), OnlineService (host + facade; boot/refresh single-flight; mid-session
  offline detection), ProgressSync (merge push/pull, mutation-counter guard, backoff),
  AttemptsSync (display cache + projection), RunGate (start/finish handshake, busy guard,
  disk-backed finish queue), Leaderboards. Menu: `MainMenuRuntime.Leaderboard.cs` (Ranks
  overlay), `MainMenuRuntime.Identity.cs` (claim-name modal + post-Ch1 link prompt).
- **Client kill-switch:** `SupabaseConfig.Enabled = false` turns the whole online layer inert
  (legacy local behavior) — useful for offline editor work.
- **Production cutover checklist:** real Supabase project URL + anon key in SupabaseConfig
  (enable **anonymous sign-ins** on the hosted project — off by default); Apple/Google
  provider config + native plugins (§3.3); in-app delete-account button in Settings (§3.7 —
  the `delete_account` RPC exists and is smoke-tested, the client UI does not); AdMob SSV
  for grant_ad_refill; receipt validation Edge Function; per-level score bounds;
  display-name moderation pass.

---

## 11. Open decisions (revisit as they bite)

- **Display-name moderation** — uniqueness rule (or discriminator) + profanity filter in the
  rename function; decide before boards go public.
- **Guest name claims** — as built, an anonymous (unlinked) user CAN claim a custom name
  (claim-now-link-later; linking keeps the same account and name). §3.4's stricter reading
  ("sign in to claim") would gate the rename RPC on a linked identity. Decide before boards
  go public; the loose model maximizes leaderboard identity, the strict one makes the name
  the linking carrot.
- **Mid-session token revocation policy** — as built, a definitively rejected session mid-run
  turns the client Offline but KEEPS the stored session and retries; it never auto-creates a
  fresh anonymous account (that would silently orphan the player's progress). A permanently
  revoked anonymous session therefore means permanently offline until reinstall/link — rare
  by construction (nothing revokes anonymous refresh tokens in normal operation), revisit if
  support tickets appear.
- **`runs` retention** — the ledger grows one row per run; decide an archival/pruning window
  once volume is real.
- **Score sanity bounds per level** — need a max-plausible table (score/height/duration);
  derive from playtest data at step 4.
- **When to add native services** (Game Center / Play Games) — nice-to-have vs. launch.
- **DATA.md scope note** — add one line there recording that attempts/scores are excluded
  from the local-first contract (its five rules otherwise unchanged).
- **Free-tier limits / cost** — fine for launch; re-check at real user counts.

---

*Keep this consistent with DATA.md's five rules (progress payload) and SHOP.md §5/§7 (boards,
attempts values, ethics guardrails). Update when any auth/table/sync decision changes.*
