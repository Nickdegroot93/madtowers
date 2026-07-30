using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Local-first player progress: level completions and per-level personal bests, persisted
/// as one JSON document in Application.persistentDataPath. This class is the ONLY thing
/// that touches the file; all gameplay code goes through this narrow API, so a cloud
/// backend (Supabase) can later slot in behind it without touching gameplay.
///
/// The rules that keep this cloud-sync ready (full rationale in DATA.md):
/// - stable string IDs (asset names), never indices or object references
/// - monotonic values only: completion is a set union, bests are per-metric max,
///   so merging two divergent states (offline play on two devices) never conflicts
/// - schemaVersion for forward migrations
/// - timestamps on records for a free audit trail / leaderboard rows later
/// </summary>
public static class ProgressStore
{
    private const int CurrentSchemaVersion = 4;

    [Serializable]
    public class PlayerProgress
    {
        public int schemaVersion = CurrentSchemaVersion;
        public List<string> completedLevelIds = new List<string>();
        public List<LevelBest> bests = new List<LevelBest>();
        // One-shot: the first-run gesture tutorial has been finished (see TUTORIAL.md). Monotonic
        // false->true, so merging two devices is just an OR. v1 saves lack the field and default
        // to false - correct, since those players never saw the new tutorial.
        public bool tutorialCompleted;
        // The Vault's discovery sets (v3, BACKEND.md §7): which brick variants the player has SEEN
        // drop in play, which abilities have ever APPEARED in a 3-card offer (picked or not), and
        // which Vault entries have been opened at least once (clears the "NEW" badge). All three
        // are monotonic string sets keyed by asset name - merge = union, so they cloud-sync for
        // free. v2 saves lack the fields and default to empty - correct: nothing discovered yet.
        public List<string> discoveredBlocks = new List<string>();
        public List<string> abilitiesSeen = new List<string>();
        public List<string> vaultInspected = new List<string>();
        // The wallet (v4, SHOP.md §10 / DATA.md rule 3): spending is non-monotonic, so the
        // balance is DERIVED from two monotonic counters - merging two devices is max() on
        // each. Folded here from the old PlayerPrefs "profile.coins" (see MigrateLegacyCoins).
        public long currencyEarned;
        public long currencySpent;
        // Attempts meter (SHOP.md §7): inherently non-monotonic, so it follows the settings
        // pattern - last-writer-wins by timestamp (DATA.md). Count is the value AT the
        // timestamp; regen since then is derived on read.
        public int attemptsCount = -1; // -1 = never initialized (fresh meter starts full)
        public long attemptsUpdatedAtUnixUtc;
        // One-time premium unlock (monotonic false->true). Only ever set via a validated IAP
        // receipt when that ships (BACKEND.md §9); until then it stays false in production.
        public bool premiumUnlocked;
        // The post-chapter-1 "sign in" card has been shown once (BACKEND.md §3.4). Monotonic
        // timestamp: 0 = never shown; merge = max.
        public long linkPromptShownAtUnixUtc;
    }

    [Serializable]
    public class LevelBest
    {
        public string levelId;
        // Clean-board bests (SHOP.md §5). Pre-v4 saves only had these fields - correct, since
        // every pre-shop run was by definition clean.
        public int bestScore;
        public float bestHeightMeters;
        public long achievedAtUnixUtc;
        // Boosted-board bests: runs that started with any purchased supply. Never mixed with
        // the clean fields; both pairs are per-metric max (monotonic).
        public int bestScoreBoosted;
        public float bestHeightMetersBoosted;
    }

    private static PlayerProgress _data;

    private static string FilePath => Path.Combine(Application.persistentDataPath, "progress.json");

    /// <summary>Stable identity of a level across sessions, saves and (later) the cloud. Runtime
    /// levels (Custom Game) have an empty asset name and therefore NO identity - returning null
    /// makes every store operation a no-op for them, so one custom run can never mark all future
    /// custom games completed or leak a shared "best" between unrelated configurations.</summary>
    public static string LevelId(LevelDefinition level) =>
        level != null && !string.IsNullOrEmpty(level.name) ? level.name : null;

    public static bool IsLevelCompleted(LevelDefinition level)
    {
        string id = LevelId(level);
        return id != null && Data.completedLevelIds.Contains(id);
    }

    public static void MarkLevelCompleted(LevelDefinition level)
    {
        string id = LevelId(level);
        if (id == null || Data.completedLevelIds.Contains(id)) return;

        Data.completedLevelIds.Add(id);
        Save();
    }

    /// <summary>Has the player finished the one-time gesture tutorial? Gates the tutorial
    /// overlay only (never the level's own win/completion). See TUTORIAL.md.</summary>
    public static bool IsTutorialCompleted() => Data.tutorialCompleted;

    /// <summary>Mark the gesture tutorial done, forever. Idempotent.</summary>
    public static void MarkTutorialCompleted()
    {
        if (Data.tutorialCompleted) return;
        Data.tutorialCompleted = true;
        Save();
    }

    /// <summary>Clear only the tutorial flag so it plays again next run (Settings > Account).
    /// Leaves level completions and bests untouched. Idempotent.</summary>
    public static void ResetTutorial()
    {
        if (!Data.tutorialCompleted) return;
        Data.tutorialCompleted = false;
        Save();
    }

    // ---- discovery (the Vault's data; see BLOCKPREVIEWS.md + BACKEND.md §7) -----------------

    /// <summary>Stable identity of a brick variant. A null variant is the plain chapter brick,
    /// which shares one campaign-wide identity ("Normal" - also the Normal.asset's name, so the
    /// null-variant and explicit-asset spawn paths converge on the same id).</summary>
    public static string BlockId(BlockData variant) => variant != null ? variant.name : "Normal";

    /// <summary>Has this brick variant ever visibly dropped for this player?</summary>
    public static bool HasDiscoveredBlock(BlockData variant) =>
        Data.discoveredBlocks.Contains(BlockId(variant));

    /// <summary>Record that the player has now seen this brick variant in play. Idempotent;
    /// unlocks the Vault entry and permanently retires the variant's debut modal.</summary>
    public static void MarkBlockDiscovered(BlockData variant)
    {
        string id = BlockId(variant);
        if (Data.discoveredBlocks.Contains(id)) return;
        Data.discoveredBlocks.Add(id);
        Save();
    }

    /// <summary>Has this ability ever been shown to the player in an offer?</summary>
    public static bool HasSeenAbility(AbilityDefinition ability) =>
        ability != null && Data.abilitiesSeen.Contains(ability.name);

    /// <summary>Record every ability the player was just shown (an offer counts for all of its
    /// cards, picked or not). One save for the whole batch; idempotent.</summary>
    public static void MarkAbilitiesSeen(IReadOnlyList<AbilityDefinition> abilities)
    {
        if (abilities == null) return;

        bool changed = false;
        for (int i = 0; i < abilities.Count; i++)
        {
            if (abilities[i] == null) continue;
            string id = abilities[i].name;
            if (Data.abilitiesSeen.Contains(id)) continue;
            Data.abilitiesSeen.Add(id);
            changed = true;
        }
        if (changed) Save();
    }

    /// <summary>Has this Vault entry (block or ability id) been opened at least once? Drives the
    /// "NEW" badge on freshly discovered entries.</summary>
    public static bool HasInspectedInVault(string entryId) =>
        entryId != null && Data.vaultInspected.Contains(entryId);

    /// <summary>The player opened this entry's Vault detail view; retire its "NEW" badge.</summary>
    public static void MarkInspectedInVault(string entryId)
    {
        if (entryId == null || Data.vaultInspected.Contains(entryId)) return;
        Data.vaultInspected.Add(entryId);
        Save();
    }

    /// <summary>Clear the discovery sets so every debut modal and Vault unlock happens again
    /// (debug / testing). Leaves completions, bests and the tutorial flag untouched.</summary>
    public static void ResetDiscoveries()
    {
        if (Data.discoveredBlocks.Count == 0 && Data.abilitiesSeen.Count == 0 &&
            Data.vaultInspected.Count == 0) return;
        Data.discoveredBlocks.Clear();
        Data.abilitiesSeen.Clear();
        Data.vaultInspected.Clear();
        Save();
    }

    /// <summary>
    /// Record a finished run's results. Monotonic: only improvements are stored, so this
    /// is safe to call from any end-of-run path (completion, game over, both). Boosted runs
    /// (any purchased supply, SHOP.md §5) improve only the boosted pair; clean runs only the
    /// clean pair - the two boards never mix.
    /// </summary>
    public static void ReportResult(LevelDefinition level, int score, float heightMeters, bool boosted = false)
    {
        string id = LevelId(level);
        if (id == null) return;

        LevelBest best = FindBest(id);
        if (best == null)
        {
            best = new LevelBest { levelId = id };
            Data.bests.Add(best);
        }
        else if (boosted
            ? score <= best.bestScoreBoosted && heightMeters <= best.bestHeightMetersBoosted
            : score <= best.bestScore && heightMeters <= best.bestHeightMeters)
        {
            return; // no improvement on the board this run played for, no write
        }

        if (boosted)
        {
            best.bestScoreBoosted = Mathf.Max(best.bestScoreBoosted, score);
            best.bestHeightMetersBoosted = Mathf.Max(best.bestHeightMetersBoosted, heightMeters);
        }
        else
        {
            best.bestScore = Mathf.Max(best.bestScore, score);
            best.bestHeightMeters = Mathf.Max(best.bestHeightMeters, heightMeters);
        }
        best.achievedAtUnixUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Save();
    }

    public static LevelBest GetBest(LevelDefinition level)
    {
        string id = LevelId(level);
        return id != null ? FindBest(id) : null;
    }

    // ---- wallet (SHOP.md §10; spend/earn are the ONLY writers) ------------------------------

    /// <summary>Spendable balance, derived from the two monotonic counters.</summary>
    public static int CoinBalance => (int)Math.Max(0L, Data.currencyEarned - Data.currencySpent);

    public static void EarnCoins(int amount)
    {
        if (amount <= 0) return;
        Data.currencyEarned += amount;
        Save();
    }

    /// <summary>Spend up to <paramref name="amount"/>; clamps at the balance (matches the old
    /// PlayerPrefs wallet's clamp-at-zero behaviour) and reports what was actually spent.</summary>
    public static int SpendCoins(int amount)
    {
        int spend = Mathf.Clamp(amount, 0, CoinBalance);
        if (spend == 0) return 0;
        Data.currencySpent += spend;
        Save();
        return spend;
    }

    // ---- attempts meter (SHOP.md §7) ---------------------------------------------------------

    /// <summary>Attempts meter state as last persisted (count is the value AT the timestamp;
    /// callers derive regen since). count -1 = never initialized.</summary>
    public static void GetAttemptsState(out int count, out long updatedAtUnixUtc)
    {
        count = Data.attemptsCount;
        updatedAtUnixUtc = Data.attemptsUpdatedAtUnixUtc;
    }

    public static void SetAttemptsState(int count, long updatedAtUnixUtc)
    {
        Data.attemptsCount = count;
        Data.attemptsUpdatedAtUnixUtc = updatedAtUnixUtc;
        Save();
    }

    /// <summary>The one-time premium unlock ("MadTowers Unlimited"). This save flag is the
    /// OFFLINE ENTITLEMENT CACHE - what makes airplane-mode play work. Only PremiumStore
    /// writes it (purchase / restore / server sync-down); the server's attempts.premium
    /// stays the online authority (BACKEND.md §6.4).</summary>
    public static bool IsPremium => Data.premiumUnlocked;

    public static void SetPremium(bool premium)
    {
        if (Data.premiumUnlocked == premium) return;
        Data.premiumUnlocked = premium;
        Save();
    }

    /// <summary>Has the one-time post-chapter-1 "sign in" card been shown (BACKEND.md §3.4)?</summary>
    public static bool WasLinkPromptShown() => Data.linkPromptShownAtUnixUtc > 0;

    public static void MarkLinkPromptShown()
    {
        if (Data.linkPromptShownAtUnixUtc > 0) return;
        Data.linkPromptShownAtUnixUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Save();
    }

    /// <summary>Account deletion (BACKEND.md §3.7): a TOTAL wipe - progress, wallet, premium,
    /// everything. Unlike ResetAll below this spares nothing: the player asked for their data
    /// to be erased, and the local save IS their data. Premium comes back via the store's
    /// RESTORE PURCHASES, never from a leftover flag.</summary>
    public static void WipeForAccountDeletion()
    {
        _data = new PlayerProgress();
        Save();
    }

    /// <summary>Wipe local progress (debug / "reset progress" settings button). The wallet
    /// survives on purpose: coins lived outside this store (PlayerPrefs) before v4, so the
    /// reset button never touched them - folding them in must not change what the button does.</summary>
    public static void ResetAll()
    {
        long earned = Data.currencyEarned;
        long spent = Data.currencySpent;
        _data = new PlayerProgress { currencyEarned = earned, currencySpent = spent };
        Save();
    }

    // ---- cloud mirror seam (BACKEND.md §5.2; only ProgressSync calls these) ------------------

    /// <summary>Fired after every successful disk write (except merge applications - the
    /// guard below - so the sync layer never echoes its own pull back as a push).</summary>
    public static event Action Saved;

    public static int SchemaVersion => CurrentSchemaVersion;

    /// <summary>Increments on every gameplay save (not on merge applications). The sync
    /// layer compares this across a merge round trip: if it moved, the merged reply is
    /// missing a newer local write and must not replace the document.</summary>
    public static long MutationCounter { get; private set; }

    /// <summary>The whole save document as one JSON object - exactly what merge_progress
    /// takes as p_payload.</summary>
    public static string ExportPayloadJson() => JsonUtility.ToJson(Data);

    /// <summary>Replace the local document with the server-merged one. The merge is a
    /// superset of local state (union/max server-side), so replacing wholesale is safe.
    /// Saves without firing Saved - a pull must not schedule a push of itself.</summary>
    public static void ApplyMergedPayload(string json)
    {
        PlayerProgress merged = null;
        try { merged = JsonUtility.FromJson<PlayerProgress>(json); }
        catch (Exception e) { Debug.LogWarning($"[Progress] Unreadable merged payload: {e.Message}"); }
        if (merged == null) return;

        merged.schemaVersion = CurrentSchemaVersion;
        _data = merged;
        _suppressSavedEvent = true;
        try { Save(); }
        finally { _suppressSavedEvent = false; }
    }

    private static bool _suppressSavedEvent;

    // ---- plumbing --------------------------------------------------------------------------

    private static PlayerProgress Data
    {
        get
        {
            if (_data == null)
            {
                _data = Load();
                MigrateLegacyCoins();
            }
            return _data;
        }
    }

    // v4 fold (BACKEND.md §8 Phase B): the old PlayerPrefs wallet moves into the save document
    // as earned (its balance was all-earned by definition - there was no sink before the shop).
    // Runs after _data is assigned so the Save() inside doesn't recurse into Load().
    private const string LegacyCoinsKey = "profile.coins";

    private static void MigrateLegacyCoins()
    {
        if (!PlayerPrefs.HasKey(LegacyCoinsKey)) return;

        int legacy = Mathf.Max(0, PlayerPrefs.GetInt(LegacyCoinsKey, 0));
        if (legacy > 0) _data.currencyEarned += legacy;
        PlayerPrefs.DeleteKey(LegacyCoinsKey);
        PlayerPrefs.Save();
        Save();
    }

    private static LevelBest FindBest(string id)
    {
        List<LevelBest> bests = Data.bests;
        for (int i = 0; i < bests.Count; i++)
        {
            if (bests[i].levelId == id) return bests[i];
        }
        return null;
    }

    private static PlayerProgress Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                PlayerProgress loaded = JsonUtility.FromJson<PlayerProgress>(File.ReadAllText(FilePath));
                if (loaded != null)
                {
                    // Future schema migrations go here, keyed on loaded.schemaVersion.
                    loaded.schemaVersion = CurrentSchemaVersion;
                    return loaded;
                }
            }
        }
        catch (Exception e)
        {
            // A corrupt save must never brick the game; keep the broken file for forensics.
            Debug.LogWarning($"[Progress] Could not read save, starting fresh: {e.Message}");
            try { File.Copy(FilePath, FilePath + ".corrupt", true); } catch { /* best effort */ }
        }
        return new PlayerProgress();
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(Data, prettyPrint: true));
        }
        catch (Exception e)
        {
            Debug.LogError($"[Progress] Save failed: {e.Message}");
            return;
        }
        if (!_suppressSavedEvent)
        {
            MutationCounter++;
            Saved?.Invoke();
        }
    }
}
