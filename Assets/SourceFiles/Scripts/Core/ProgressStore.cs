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
    private const int CurrentSchemaVersion = 3;

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
    }

    [Serializable]
    public class LevelBest
    {
        public string levelId;
        public int bestScore;
        public float bestHeightMeters;
        public long achievedAtUnixUtc;
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
    /// is safe to call from any end-of-run path (completion, game over, both).
    /// </summary>
    public static void ReportResult(LevelDefinition level, int score, float heightMeters)
    {
        string id = LevelId(level);
        if (id == null) return;

        LevelBest best = FindBest(id);
        if (best == null)
        {
            best = new LevelBest { levelId = id };
            Data.bests.Add(best);
        }
        else if (score <= best.bestScore && heightMeters <= best.bestHeightMeters)
        {
            return; // no improvement, no write
        }

        best.bestScore = Mathf.Max(best.bestScore, score);
        best.bestHeightMeters = Mathf.Max(best.bestHeightMeters, heightMeters);
        best.achievedAtUnixUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Save();
    }

    public static LevelBest GetBest(LevelDefinition level)
    {
        string id = LevelId(level);
        return id != null ? FindBest(id) : null;
    }

    /// <summary>Wipe local progress (debug / "reset progress" settings button).</summary>
    public static void ResetAll()
    {
        _data = new PlayerProgress();
        Save();
    }

    // ---- plumbing --------------------------------------------------------------------------

    private static PlayerProgress Data => _data ??= Load();

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
        }
    }
}
