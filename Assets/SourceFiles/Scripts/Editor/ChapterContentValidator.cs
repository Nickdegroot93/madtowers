using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only content lint for campaign chapters. Runtime remains data-driven, but this
/// catches missing references and folder drift before a new chapter becomes a runtime bug.
/// </summary>
public static class ChapterContentValidator
{
    private const string ChaptersFolder = "Assets/Resources/Chapters";
    private const string LevelsFolder = "Assets/Resources/Levels";
    private const string ResourcesRoot = "Assets/Resources";

    [MenuItem("Tools/MadTowers/Validate Chapter Content")]
    public static void ValidateFromMenu()
    {
        Validate(logSuccess: true);
    }

    public static bool Validate(bool logSuccess)
    {
        int errors = 0;
        int warnings = 0;

        List<ChapterDefinition> chapters = LoadAssets<ChapterDefinition>(ChaptersFolder);
        chapters.Sort(CompareChapters);

        if (chapters.Count == 0)
        {
            Error("No ChapterDefinition assets found in Assets/Resources/Chapters.", null, ref errors);
        }

        Dictionary<int, ChapterDefinition> sortOrders = new Dictionary<int, ChapterDefinition>();
        HashSet<LevelDefinition> referencedLevels = new HashSet<LevelDefinition>();
        Dictionary<string, LevelDefinition> levelIds = new Dictionary<string, LevelDefinition>(StringComparer.Ordinal);

        for (int i = 0; i < chapters.Count; i++)
        {
            ChapterDefinition chapter = chapters[i];
            if (chapter == null) continue;

            if (sortOrders.TryGetValue(chapter.SortOrder, out ChapterDefinition existingChapter))
            {
                Error($"Duplicate chapter sortOrder {chapter.SortOrder}: {existingChapter.name} and {chapter.name}.", chapter, ref errors);
            }
            else
            {
                sortOrders.Add(chapter.SortOrder, chapter);
            }

            ValidateChapterPresentation(chapter, ref errors, ref warnings);
            ValidateChapterLevels(chapter, referencedLevels, levelIds, ref errors);
        }

        foreach (LevelDefinition level in LoadAssets<LevelDefinition>(LevelsFolder))
        {
            if (level == null) continue;

            if (!levelIds.TryGetValue(level.name, out LevelDefinition existing))
            {
                levelIds.Add(level.name, level);
            }
            else if (existing != level)
            {
                Error($"Duplicate level id '{level.name}'. ProgressStore uses asset names as stable IDs.", level, ref errors);
            }

            if (!referencedLevels.Contains(level))
            {
                Warning($"Level asset is in Resources/Levels but is not referenced by any chapter: {level.name}. It will still ship.", level, ref warnings);
            }

            if (level.GameModeConfig == null)
            {
                Error($"Level '{level.name}' has no GameModeConfig.", level, ref errors);
            }
        }

        string summary = $"[ChapterContent] Validation finished: {errors} error(s), {warnings} warning(s).";
        if (errors > 0) Debug.LogError(summary);
        else if (logSuccess) Debug.Log(summary);
        return errors == 0;
    }

    private static void ValidateChapterPresentation(ChapterDefinition chapter, ref int errors, ref int warnings)
    {
        if (chapter.MenuBackgroundImage == null && chapter.MenuBackgroundVideo == null)
        {
            Warning($"Chapter '{chapter.name}' has no menu background image or video.", chapter, ref warnings);
        }

        if (chapter.Backdrop == null)
        {
            Warning($"Chapter '{chapter.name}' has no BackdropPreset; gameplay will use the classic fallback.", chapter, ref warnings);
        }

        IReadOnlyList<AudioClip> playlist = chapter.MusicPlaylist;
        if (playlist == null || playlist.Count == 0)
        {
            Warning($"Chapter '{chapter.name}' has no music playlist.", chapter, ref warnings);
        }
        else
        {
            for (int i = 0; i < playlist.Count; i++)
            {
                AudioClip clip = playlist[i];
                if (clip == null)
                {
                    Error($"Chapter '{chapter.name}' has a null music clip at playlist index {i}.", chapter, ref errors);
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(clip);
                string extension = Path.GetExtension(path);
                if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
                {
                    Warning($"Chapter '{chapter.name}' music '{Path.GetFileName(path)}' is WAV; OGG is preferred for shipped chapter music.", clip, ref warnings);
                }
            }
        }

        string skinFolder = chapter.SkinFolder;
        string skinPath = $"{ResourcesRoot}/{skinFolder}";
        if (!AssetDatabase.IsValidFolder(skinPath))
        {
            Error($"Chapter '{chapter.name}' skinFolder points at missing Resources folder: {skinFolder}.", chapter, ref errors);
        }
    }

    private static void ValidateChapterLevels(
        ChapterDefinition chapter,
        HashSet<LevelDefinition> referencedLevels,
        Dictionary<string, LevelDefinition> levelIds,
        ref int errors)
    {
        IReadOnlyList<LevelDefinition> levels = chapter.Levels;
        if (levels == null || levels.Count == 0)
        {
            Error($"Chapter '{chapter.name}' has no levels.", chapter, ref errors);
            return;
        }

        for (int i = 0; i < levels.Count; i++)
        {
            LevelDefinition level = levels[i];
            if (level == null)
            {
                Error($"Chapter '{chapter.name}' has a null level slot at index {i}.", chapter, ref errors);
                continue;
            }

            referencedLevels.Add(level);
            if (!levelIds.TryGetValue(level.name, out LevelDefinition existing))
            {
                levelIds.Add(level.name, level);
            }
            else if (existing != level)
            {
                Error($"Duplicate level id '{level.name}'. ProgressStore uses asset names as stable IDs.", level, ref errors);
            }

            if (level.GameModeConfig == null)
            {
                Error($"Chapter '{chapter.name}' level '{level.name}' has no GameModeConfig.", level, ref errors);
            }
        }
    }

    private static List<T> LoadAssets<T>(string folder) where T : UnityEngine.Object
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder });
        List<T> assets = new List<T>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) assets.Add(asset);
        }
        return assets;
    }

    private static int CompareChapters(ChapterDefinition a, ChapterDefinition b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return 1;
        if (b == null) return -1;

        int order = a.SortOrder.CompareTo(b.SortOrder);
        if (order != 0) return order;

        int number = a.ChapterNumber.CompareTo(b.ChapterNumber);
        if (number != 0) return number;

        return string.Compare(a.name, b.name, StringComparison.Ordinal);
    }

    private static void Error(string message, UnityEngine.Object context, ref int errors)
    {
        errors++;
        Debug.LogError($"[ChapterContent] {message}", context);
    }

    private static void Warning(string message, UnityEngine.Object context, ref int warnings)
    {
        warnings++;
        Debug.LogWarning($"[ChapterContent] {message}", context);
    }
}
