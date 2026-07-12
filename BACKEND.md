# MadTowers Backend: Auth, Tables & Cloud Sync

How accounts, online data storage, sync, and leaderboards will work. This is the
**detailed design** for the cloud layer; `DATA.md` owns the **local** save contract and its
five rules (one gateway, stable IDs, monotonic values, schema version, timestamps). Everything
here slots in *behind* `ProgressStore` — gameplay code never changes.

> **Status: PLANNING — not yet implemented.** Deferred to **Phase E** of the roadmap. The game
> stays fully playable **local-first / offline** without any of this. We write it down now so
> Phase B (the data spine) is built to a target, and so future sessions don't re-derive it.
> Decision: **Supabase, local-first, hybrid schema** (the developer knows Postgres; we want
> evolving per-user data + cross-platform progression + custom leaderboards).

---

## 1. The mental model (three separate concerns)

People conflate these; keep them apart:

1. **Identity** — *who* the player is: a stable `user_id`. Provided by an auth system.
2. **Storage** — *where* the player's data lives and *what shape* it has.
3. **Live game state** — what the running game reads/writes. This is **always local**
   (`ProgressStore`), never a network call. (See §5.)

### Why not the native game services alone (Game Center / Google Play Games)?

They bundle identity + a *narrow* kind of storage:

- **Leaderboards** — submit a number, they rank it. You can't model fields on it.
- **Saved Games / Cloud Save** — an **opaque blob**: "store these N bytes for this user, give
  them back on their other devices" (a few MB). It is **not queryable** — no SQL, no
  cross-user questions ("how many players discovered the Maw?"). It's a backup of *your* JSON.

So native cloud save *can* hold "which blocks discovered" (it's a field in your save), but can't
*do* anything with it server-side, and is siloed per ecosystem (an iOS save isn't an Android
save). We need queryable + cross-platform + evolving data → a real database.

**Native services are optional later** (see §4) for the polished native leaderboard UI /
achievement popups. They are not required and not the foundation.

---

## 2. The decision: Supabase + local-first + hybrid

- **Supabase** (Postgres + Auth + RLS) is the cloud backend. It gives real tables, `jsonb` for
  arbitrary/evolving data, row-level security, and social auth — home territory.
- **Local-first**: the device save is the source of truth during play; Supabase is a background
  **mirror**. The game is fully playable offline.
- **Hybrid storage**: the bulk personal save is one **`jsonb` document** (trivial to extend with
  any new tracked thing, no migration); the few things we **query across users** (leaderboard
  scores) get **real columns/tables**. Dump new data into the document; promote a field to a
  column only when you need to query it.

---

## 3. Auth

### 3.1 Guest-first (no login wall)

A mobile game must never gate play behind a login screen. So:

- **First launch → Supabase anonymous sign-in.** The player gets a real `user_id` immediately,
  bound to the device, with zero friction. They play; the local save works as today.
- The client ships **only the anon/public key**. Row-Level Security (RLS) does all the guarding.
  **Never ship the service-role key** (it bypasses RLS).

### 3.2 Linking a real account (opt-in, enables cross-device + social)

When the player wants their progress on another device, or to appear on friend leaderboards,
they sign in with a provider, which **links** to (upgrades) the anonymous user — same `user_id`,
progress preserved:

- **iOS → Sign in with Apple.** **Android → Google sign-in.** Both are first-class Supabase Auth
  providers and are the "game account" players expect (native, frictionless).
- **Apple policy note:** if you ever offer *other* third-party logins (Google, Facebook…) on
  iOS, you **must** also offer Sign in with Apple. Apple-only on iOS / Google-only on Android is
  fine. Plan for both providers.

### 3.3 Native services (Game Center / Google Play Games) — optional, later

Layer them on *after* the Supabase path works, only if we want the native leaderboard overlay,
achievement popups, or auto sign-in. They become an additional identity that maps to the same
Supabase `user_id`. Not a v1 requirement.

### 3.4 Account deletion (store requirement)

Both stores require an in-app **"delete account"** path once you have accounts. Implement a
server function (or RLS-guarded cascade) that wipes the user's rows (`profiles`, `progress`,
`scores`) and the `auth.users` entry. Put the button in Settings (Phase C reserves the slot).

---

## 4. Storage — the hybrid schema

### 4.1 Tables

```sql
-- Identity / display
profiles (
  user_id      uuid primary key references auth.users(id) on delete cascade,
  display_name text,
  created_at   timestamptz default now(),
  updated_at   timestamptz default now()
)

-- The synced save document (the whole ProgressStore payload as one blob)
progress (
  user_id        uuid primary key references auth.users(id) on delete cascade,
  payload        jsonb not null default '{}',   -- see §4.2
  schema_version int  not null default 1,
  updated_at     timestamptz default now()
)

-- Competitive data we query ACROSS users → real columns, not the blob
scores (
  user_id      uuid references auth.users(id) on delete cascade,
  level_id     text not null,                   -- stable asset name (DATA.md rule 2)
  best_score   int  not null default 0,
  best_height  real not null default 0,
  achieved_at  timestamptz default now(),
  primary key (user_id, level_id)               -- one row per user per level; upsert on improvement
)
```

### 4.2 The `payload` document (what lives in the jsonb)

This is exactly the `ProgressStore` save object, serialized. It is **"just a JSON object per
user"** — and that's fine. Every field obeys DATA.md rule 3 (monotonic, or timestamped):

```jsonc
{
  "schemaVersion": 2,
  "completedLevelIds": ["Level_JD1_TheUndergrowth", "..."],   // set (union merge)
  "bests": [ { "levelId": "...", "bestScore": 84, "bestHeightMeters": 14.2,
               "achievedAtUnixUtc": 1781290000 } ],          // per-metric max merge
  "discoveredBlocks": ["normal", "ice", "maw", "magma"],     // set (union) — drives the Vault
  "abilitiesSeen":   ["recovery", "zap", "fission"],         // set (union) — Vault "seen"
  "abilitiesUsed":   { "zap": 12, "fission": 3 },            // counters (monotonic) — Vault "used"
  "currencyEarned": 1280, "currencySpent": 860,              // balance = earned - spent (both monotonic)
  "settings": { "music": 0.8, "sfx": 1.0, "haptics": true,
                "updatedAtUnixUtc": 1781290000 }             // small; last-writer-wins by timestamp
}
```

**Rule of thumb — what goes where:**

| Data | Home | Why |
|---|---|---|
| Personal progress, collections, settings, counters | `progress.payload` (jsonb) | Per-user, read by that user only; evolves often → no migrations |
| Leaderboard scores | `scores` table (columns) | Queried/sorted across users |
| Identity / display name | `profiles` + `auth.users` | Shared/joined; auth-owned |
| Future social (friends, % discovered, events) | new relational tables | Needs cross-user queries/joins |

`jsonb` accepts **anything** (nested objects, arrays, scalars) and Postgres can even query inside
it (`payload->'discoveredBlocks'`) — so adding a new tracked thing later is a one-line change to
the save object, never a schema migration.

### 4.3 Row-Level Security (RLS)

- `profiles`, `progress`: a user may **read/write only their own row** (`auth.uid() = user_id`).
- `scores`: **read all** (leaderboards are public), **write only your own row**, and later route
  writes through a validating function (§6) so scores can't be faked by editing the local save.

---

## 5. Sync architecture (local-first)

The rule that makes mobile saves work: **the game never waits on the network.**

- **Source of truth during play:** `ProgressStore` (local JSON, instant, offline). Unchanged.
- **Supabase is a background mirror.** Sync points:
  - **On launch / after login:** pull the cloud `payload`, **merge** into local, continue.
  - **On app-background / after key events** (level complete, ability used, block discovered):
    **push** local → cloud, debounced (don't spam writes).
  - **Offline:** queue the push; retry on reconnect. Gameplay is never blocked.
- **Merge = the five rules, server-side.** A Postgres function merges incoming payload with the
  stored one: completions/sets → **union**, bests/counters → **max**, settings → **last-writer-
  wins by timestamp**. Because every field is monotonic or timestamped, **two devices that played
  offline merge with no conflict UI** — this is *why* DATA.md rule 3 is non-negotiable.
- **Schema migration:** `schema_version` on both the client save and the `progress` row; migrate
  on load (client) and in the merge function (server) so old payloads upgrade cleanly.

The whole sync layer hides **behind `ProgressStore`'s existing API** — gameplay calls
`MarkBlockDiscovered`, `ReportResult`, etc.; sync happens underneath. (DATA.md rule 1.)

---

## 6. Leaderboards

- **Personal best** = upsert into `scores` on improvement (mirrors the local `bests` write).
- **Top-N** = `select … from scores where level_id = $1 order by best_score desc limit 100`
  (indexed), or a per-level view / RPC. Add friend filters later via a `friends` table.
- **Reading from C#** needs no SDK: PostgREST over plain HTTPS (`UnityWebRequest`) with the anon
  key + the user's JWT. (A thin Supabase C# client is optional sugar.)
- **Anti-cheat reality:** client-submitted scores are spoofable. Acceptable for a casual v1.
  When it matters: validate writes in a server function (sanity bounds — max plausible
  score/height per level), shadow-flag outliers, and **never ship the service key**. RLS already
  stops a user writing *another* user's row.

---

## 7. Worked example: the Collections (Vault) tab

End-to-end, the thing you asked about:

1. **In play:** a brick variant first appears → gameplay calls
   `ProgressStore.MarkBlockDiscovered("maw")` (a new monotonic set field — DATA.md "adding new
   data" recipe). Same for `MarkAbilitySeen` / `RecordAbilityUsed`.
2. **Local:** it lands in `payload.discoveredBlocks` and saves to disk instantly. Works offline.
3. **Sync:** on next push, it merges into the cloud `progress.payload` (union — so discoveries
   from any device accumulate).
4. **Vault UI:** reads the **local** set (always available, even offline) and renders the grid:
   discovered = full art, undiscovered = silhouette. No per-item table needed — it's just a set
   in the save document. (If we later want "what % of all players found the Maw," *that* would
   promote to a relational table + an aggregate query.)

---

## 8. Build order (maps to the roadmap)

- **Phase B (data spine) — do this part now-ish, no backend needed:**
  - Extend `ProgressStore` so the save object is exactly the §4.2 shape and **serializes cleanly
    to one JSON document** (it already nearly does).
  - Add the Vault fields (`discoveredBlocks`, `abilitiesSeen`, `abilitiesUsed`) as monotonic
    sets/counters; add currency as `earned`/`spent` (balance derived). Fold `PlayerProfileStore`
    (coins) into this model.
  - Keep settings in the document with an `updatedAtUnixUtc`.
- **Phase E (online) — the actual backend:**
  1. Create the Supabase project + the §4 tables + RLS policies.
  2. Anonymous auth on first launch; the sync layer behind `ProgressStore` (pull/merge/push).
  3. The merge Postgres function (§5).
  4. `scores` upserts + a leaderboard screen.
  5. Account linking (Apple/Google) + the account-deletion path.
  6. (Optional, later) native Game Center / Play Games, server-side score validation.

---

## 9. Open decisions to revisit at Phase E

- **Currency authority.** Soft currency can stay client-side (earned/spent, monotonic). If
  currency is ever **bought via IAP**, that purchase must be server-validated (receipt check) —
  don't trust the client for paid balance. Decide when monetization is decided.
- **Cross-platform progression** — confirmed wanted? (It's the reason we chose Supabase over
  native-only. If it turns out single-platform, native cloud save would be simpler.)
- **When to add native services** (Game Center / Play Games) — nice-to-have vs. launch.
- **Anti-cheat threshold** — when leaderboards get competitive enough to warrant server
  validation.
- **Free-tier limits / cost** — fine for launch; watch row counts and egress as the base grows.

---

*Keep this consistent with DATA.md's five rules — they are what make this cloud path cheap.
Update when any auth/table/sync decision changes.*
