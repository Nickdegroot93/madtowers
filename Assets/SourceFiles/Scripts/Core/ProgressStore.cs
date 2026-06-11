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
    private const int CurrentSchemaVersion = 1;

    [Serializable]
    public class PlayerProgress
    {
        public int schemaVersion = CurrentSchemaVersion;
        public List<string> completedLevelIds = new List<string>();
        public List<LevelBest> bests = new List<LevelBest>();
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

    /// <summary>Stable identity of a level across sessions, saves and (later) the cloud.</summary>
    public static string LevelId(LevelDefinition level) => level != null ? level.name : null;

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
