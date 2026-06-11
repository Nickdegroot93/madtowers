# MadTowers Data & Persistence Architecture

How player data is stored, the decisions behind it, and the path to cloud sync,
leaderboards, and (far-future) online co-op. Read this before adding any persisted
data. Sister docs: LEVELS.md (what a level is), PHYSICS.md (the simulation contract).

## Today: local-first JSON document

There is no database on the device — by design. All player state is one JSON document
written by **`ProgressStore`** (`Scripts/Core/ProgressStore.cs`) to
`Application.persistentDataPath/progress.json` (survives app updates; per-app sandboxed).

```jsonc
{
  "schemaVersion": 1,
  "completedLevelIds": ["Level_TW1_Foundations", "Level_TW2_UnderPressure"],
  "bests": [
    { "levelId": "Level_Classic", "bestScore": 84, "bestHeightMeters": 14.2,
      "achievedAtUnixUtc": 1781290000 }
  ]
}
```

For a web developer: think *local-first app with a typed single-document store*. The C#
classes (`PlayerProgress`, `LevelBest`) are the schema; `schemaVersion` is the migration
key; the file is the row.

## The five rules (these are what keep the cloud path open)

1. **One gateway.** Only `ProgressStore` touches the file. Gameplay code calls the narrow
   API (`IsLevelCompleted`, `MarkLevelCompleted`, `ReportResult`, `GetBest`). A cloud
   backend later slots in *behind* this API — zero gameplay changes.
2. **Stable string IDs.** Levels are identified by asset name (`Level_TW1_Foundations`),
   never by array index or object reference. These are the future foreign keys. Renaming
   a level asset orphans its progress — treat asset names as immutable once shipped.
3. **Monotonic values only.** Completions form a *set* (merge = union); bests are
   per-metric *maxima* (merge = max). Two divergent states (offline play on two devices)
   merge without any conflict resolution. **Any new persisted field must either be
   monotonic or carry a timestamp for last-writer-wins.**
4. **Schema version + additive evolution.** New features add fields/lists; they never
   repurpose existing ones. Old saves load with defaults for missing fields
   (JsonUtility's behavior), migrations key on `schemaVersion`.
5. **Timestamps on records.** Costs nothing now; later they're audit trails, sync
   cursors, and leaderboard rows.

### Read-side separation

Lock/unlock state is **never stored** — it's *computed* from completions by `Campaign`
(`Scripts/Levels/Campaign.cs`: themes unlock when the previous theme completes; levels
are sequential; `AlwaysUnlocked` themes are sandboxes that don't gate anything).
Derived state on disk = sync bugs; we persist facts, not conclusions.

## Adding new data (e.g. achievements) — the pattern

1. Add a `[Serializable]` record class + a list on `PlayerProgress`
   (e.g. `List<AchievementRecord> achievements`, each `{ id, unlockedAtUnixUtc }`).
2. Expose intent-level API on `ProgressStore` (`UnlockAchievement(id)`,
   `IsAchievementUnlocked(id)`) — never hand out the raw document.
3. Keep it monotonic (an unlock set is). Done — it syncs for free later.

Same recipe for: cosmetics owned, currencies (use *earned-total* + *spent-total*, both
monotonic, balance = derived), statistics (counters are monotonic), settings (small,
last-writer-wins with timestamp).

## Tomorrow: cloud sync + leaderboards (Supabase)

The plan when online day comes — nothing below requires changing today's code, only
adding behind `ProgressStore`:

- **Auth:** Supabase anonymous sign-in at first launch (device-bound), optional account
  linking later. The client ships only the anon/public key; RLS does the guarding.
- **Tables (sketch):**
  - `profiles(user_id, display_name, created_at)`
  - `progress(user_id, payload jsonb, schema_version, updated_at)` — the synced document
  - `scores(user_id, level_id text, best_score int, best_height real, achieved_at)`
    — unique `(user_id, level_id)`, upsert on improvement
- **Sync algorithm:** push local document; server merges with the same union/max rules
  (a Postgres function); pull merged result. Works after any amount of offline play on
  any number of devices, *because* of rule 3.
- **Leaderboards:** personal best = the upsert above; Top-100 =
  `select ... from scores where level_id = $1 order by best_score desc limit 100`
  (indexed), or a per-level view/RPC. UI reads via PostgREST from C#
  (`UnityWebRequest` — plain HTTPS, no SDK required).
- **Anti-cheat reality check:** client-submitted scores are spoofable. Acceptable for a
  casual game v1; mitigations when it matters: server-side sanity bounds (max plausible
  score/height per level), shadow-flagging outliers, never shipping a service key.
- Managed alternatives (Unity Gaming Services Leaderboards, PlayFab) exist, but
  Supabase is the home-territory choice here (the developer knows Postgres).

## Far future: online co-op (alternating-control mode)

Not designed yet — these notes exist so nothing we do now blocks it:

- **What already helps:** gameplay events flow through `GameEvents`; the active piece is
  a single handle (`BlockController.ActiveControlled`) that touch/keyboard feed into —
  a remote player's inputs are just another feeder. Level/theme identity is stable
  string IDs. Physics has no wall-clock or `Random` dependence in the contract paths.
- **What it will need (new work, isolated):** a session/lobby service, an authority
  model — likely host-authoritative state sync (Box2D is not deterministic across
  devices, so lockstep is out; the host simulates, the guest sends inputs for their
  turns and renders replicated state), and reconnection rules.
- **Standing constraints to honor meanwhile:** keep input producers decoupled from
  `BlockController` internals (feed the same public methods); keep run-defining data
  (seeds, wave configs) in assets/IDs rather than ad-hoc state; never persist derived
  state (rule above) — replication has the same allergy.

---

*Update this file when persistence or sync decisions change. The five rules win over
convenience — every one of them is what keeps the next milestone cheap.*
