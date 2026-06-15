using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Campaign progression rules, computed from ProgressStore + the chapter assets:
/// - chapters play in sortOrder; a chapter unlocks when the previous chapter is fully completed
///   (or it's the first, or it's flagged AlwaysUnlocked - testing/sandbox chapters)
/// - levels within a chapter are sequential: each unlocks when the previous one is completed
/// Pure read-side logic: nothing here writes progress.
/// </summary>
public static class Campaign
{
    // DEV ONLY: short-circuits every lock so all chapters/levels are playable while building
    // content. Progress and personal bests still record normally (the save stays honest).
    // Compile-gated so a release build can never ship with it true; development builds and
    // the editor keep it on.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static readonly bool UnlockAllForTesting = true;
#else
    public static readonly bool UnlockAllForTesting = false;
#endif

    // Chapter assets never change at runtime; load once instead of re-hitting Resources on
    // every lookup (scene loads, backdrop resolves, completion panels all call in here).
    private static ChapterDefinition[] _cachedChapters;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        _cachedChapters = null;
    }

    private static ChapterDefinition[] LoadChapters()
    {
        if (_cachedChapters == null)
        {
            _cachedChapters = Resources.LoadAll<ChapterDefinition>("Chapters");
            Array.Sort(_cachedChapters, (a, b) => a.SortOrder.CompareTo(b.SortOrder));
        }
        return _cachedChapters;
    }

    /// <summary>All chapters, sorted by play order.</summary>
    public static ChapterDefinition[] LoadChaptersInOrder()
    {
        return LoadChapters();
    }

    /// <summary>The chapter whose level list contains the given level, or null.</summary>
    public static ChapterDefinition FindChapterOf(LevelDefinition level)
    {
        if (level == null) return null;

        ChapterDefinition[] chapters = LoadChapters();
        for (int c = 0; c < chapters.Length; c++)
        {
            IReadOnlyList<LevelDefinition> levels = chapters[c].Levels;
            if (levels == null) continue;

            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i] == level) return chapters[c];
            }
        }
        return null;
    }

    public static bool IsChapterCompleted(ChapterDefinition chapter)
    {
        IReadOnlyList<LevelDefinition> levels = chapter != null ? chapter.Levels : null;
        if (levels == null || levels.Count == 0) return false;

        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] != null && !ProgressStore.IsLevelCompleted(levels[i])) return false;
        }
        return true;
    }

    /// <summary>chaptersInOrder must come from LoadChaptersInOrder (or be sorted the same way).</summary>
    public static bool IsChapterUnlocked(ChapterDefinition[] chaptersInOrder, int chapterIndex)
    {
        if (UnlockAllForTesting) return true;

        ChapterDefinition chapter = chaptersInOrder[chapterIndex];
        if (chapter.AlwaysUnlocked) return true;

        // Unlocked when every preceding campaign chapter is completed (AlwaysUnlocked chapters
        // are sandboxes and don't gate the campaign).
        for (int i = 0; i < chapterIndex; i++)
        {
            if (chaptersInOrder[i].AlwaysUnlocked) continue;
            if (!IsChapterCompleted(chaptersInOrder[i])) return false;
        }
        return true;
    }

    /// <summary>Sequential within the chapter: first level always, others need the previous one.</summary>
    public static bool IsLevelUnlocked(ChapterDefinition chapter, int levelIndex)
    {
        if (UnlockAllForTesting) return true;
        if (chapter.AlwaysUnlocked) return true;
        if (levelIndex <= 0) return true;

        LevelDefinition previous = chapter.Levels[levelIndex - 1];
        return previous == null || ProgressStore.IsLevelCompleted(previous);
    }
}
