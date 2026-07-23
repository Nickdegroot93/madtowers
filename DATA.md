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
  "schemaVersion": 3,
  "completedLevelIds": ["Level_JD1_TheUndergrowth", "Level_JD2_CanopyTrial"],
  "bests": [
    { "levelId": "Level_Classic", "bestScore": 84, "bestHeightMeters": 14.2,
      "achievedAtUnixUtc": 1781290000 }
  ],
  "tutorialCompleted": true,                       // v2: one-shot, monotonic false->true
  "discoveredBlocks": ["Maw", "Vine"],             // v3: variant asset names seen in play (Vault + debut gating)
  "abilitiesSeen": ["Zap", "DragChute"],           // v3: every ability ever SHOWN in an offer
  "vaultInspected": ["Maw"]                        // v3: Vault entries opened (clears the NEW badge)
}
```

For a web developer: think *local-first app with a typed single-document store*. The C#
classes (`PlayerProgress`, `LevelBest`) are the schema; `schemaVersion` is the migration
key; the file is the row.

## The five rules (these are what keep the cloud path open)

1. **One gateway.** Only `ProgressStore` touches the file. Gameplay code calls the narrow
   API (`IsLevelCompleted`, `MarkLevelCompleted`, `ReportResult`, `GetBest`). A cloud
   backend later slots in *behind* this API — zero gameplay changes.
2. **Stable string IDs.** Levels are identified by asset name (`Level_JD1_TheUndergrowth`);
   note IDs can outlive their assets (retired levels like `Level_JD4_TempleSprint` may
   linger in saves — harmless, keep merges tolerant),
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
(`Scripts/Levels/Campaign.cs`: chapters unlock when the previous chapter completes; levels
are sequential; `AlwaysUnlocked` chapters are sandboxes that don't gate anything).
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

## Tomorrow: online play (Supabase) — and this doc's scope

> **Full design: `BACKEND.md` (DESIGN FINAL 2026-07-22 — online is the STANDARD).** Auth,
> the hybrid table schema, RLS, server-authoritative attempts & scores, sync, leaderboards,
> account deletion. BACKEND.md is the binding plan; build at roadmap Phase E.

**Scope carve-out (binding):** the five rules and the local-first contract govern the
personal **progress payload only** (completions, bests, discoveries, settings, wallet
counters). Two things are explicitly **outside** local-first — the **server owns them**:

- **The attempts meter** — granted/regenerated/refunded only by server functions
  (`start_run`/`finish_run`, regen computed lazily on server time; BACKEND.md §6). The
  local `AttemptsService` becomes a display cache of the server's last answer, never
  authoritative. Campaign runs require a connection to *start*.
- **Leaderboard scores** — written only by `finish_run` against a server-issued `run_id`
  (the anti-cheat handshake: a score is only submittable for a run the server saw start,
  once, with sanity bounds). No client write path exists.

What stays true, unchanged by the online decision:

- **Auth:** silent Supabase anonymous sign-in at first launch — everyone has a real
  `user_id` from second one — upgraded in place by Sign in with Apple / Google linking.
  The client ships only the anon/public key; RLS does the guarding.
- **Progress sync:** push the local document; a Postgres function merges with the same
  union/max/last-writer-wins rules; pull the merged result. Works after any amount of
  offline play on any number of devices, *because* of rule 3.
- **Tables:** `profiles`, `progress(payload jsonb)`, `scores` keyed
  `(user_id, level_id, board)` for the CLEAN/BOOSTED split, plus server-owned `attempts`
  and `runs` — full DDL in BACKEND.md §4.
- **Reads from C#:** PostgREST over plain HTTPS (`UnityWebRequest` — no SDK required).

## Far future: online co-op (alternating-control mode)

Not designed yet — these notes exist so nothing we do now blocks it:

- **What already helps:** gameplay events flow through `GameEvents`; the active piece is
  a single handle (`BlockController.ActiveControlled`) that touch/keyboard feed into —
  a remote player's inputs are just another feeder. Level/chapter identity is stable
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
