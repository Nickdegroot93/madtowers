using UnityEngine;

/// <summary>
/// One-shot handoff between a first-time level completion and the menu's unlock-reveal
/// animation: the win path records WHICH level was just completed for the first time, and
/// the next menu build resolves what that completion unlocked (the following level, or the
/// next chapter) and plays the reveal, consuming the record. PlayerPrefs-backed so quitting
/// the app between the win and the next menu visit still plays the reveal exactly once.
///
/// Presentation state only - it never gates progression (ProgressStore/Campaign own that),
/// which is why it deliberately lives outside progress.json and its cloud-merge rules.
/// </summary>
public static class UnlockRevealPending
{
    private const string Key = "unlockReveal.pendingLevelId";

    /// <summary>Record that this level was just completed for the FIRST time. Levels without
    /// a stable id (Custom Game) are no-ops, matching ProgressStore.</summary>
    public static void RecordFirstCompletion(LevelDefinition level)
    {
        string id = ProgressStore.LevelId(level);
        if (id == null) return;
        PlayerPrefs.SetString(Key, id);
        PlayerPrefs.Save();
    }

    /// <summary>The recorded level id, or null when nothing is pending. Peek does not consume:
    /// the menu only consumes once it actually shows the chapter the reveal belongs to.</summary>
    public static string PeekLevelId()
    {
        string id = PlayerPrefs.GetString(Key, string.Empty);
        return string.IsNullOrEmpty(id) ? null : id;
    }

    public static void Clear()
    {
        PlayerPrefs.DeleteKey(Key);
    }
}
