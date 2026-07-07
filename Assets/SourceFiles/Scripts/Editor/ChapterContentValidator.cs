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

    // Presets can be shared between chapters; validate each one once per run.
    private static readonly HashSet<BackdropPreset> _validatedBackdrops = new HashSet<BackdropPreset>();

    public static bool Validate(bool logSuccess)
    {
        int errors = 0;
        int warnings = 0;
        _validatedBackdrops.Clear();

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
        else if (_validatedBackdrops.Add(chapter.Backdrop))
        {
            ValidateBackdrop(chapter.Backdrop, ref errors, ref warnings);
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

    // Layer lint for imported backdrop packs: presets are authored as raw data (often by
    // editing YAML), so catch the classic layer mistakes at validation time instead of
    // eyeball time. Rules mirror the renderer's assumptions (see BACKDROPS.md / AMBIENCE.md).
    private static void ValidateBackdrop(BackdropPreset preset, ref int errors, ref int warnings)
    {
        IReadOnlyList<BackdropPreset.SpriteBackdropLayer> layers = preset.SpriteBackdropLayers;
        int fillLayers = 0;
        for (int i = 0; layers != null && i < layers.Count; i++)
        {
            BackdropPreset.SpriteBackdropLayer layer = layers[i];
            if (layer == null || layer.Sprite == null)
            {
                Error($"Backdrop '{preset.name}' layer {i} has no sprite.", preset, ref errors);
                continue;
            }

            if (layer.FillView)
            {
                // A translucent LAST layer with fillView is a full-screen tint overlay —
                // the deliberate color-grade pattern (e.g. Kvartal 4's glow_wash), which
                // hides nothing. A translucent fillView layer anywhere else in the stack
                // is still almost certainly an authoring mistake, so it keeps the warning.
                bool isTintOverlay = layer.Alpha < 0.5f && i == layers.Count - 1;
                if (!isTintOverlay) fillLayers++;
                if (i != 0 && !isTintOverlay)
                {
                    Warning($"Backdrop '{preset.name}' layer {i} ('{layer.Sprite.name}') is fillView but not the first layer; it will cover every layer behind it.", preset, ref warnings);
                }
                if (layer.DriftSpeedX != 0f)
                {
                    Warning($"Backdrop '{preset.name}' layer {i} ('{layer.Sprite.name}') sets driftSpeedX on a fillView layer; drift is ignored for fill layers.", preset, ref warnings);
                }
                if (layer.HoverAmount > 0f)
                {
                    Warning($"Backdrop '{preset.name}' layer {i} ('{layer.Sprite.name}') sets hoverAmount on a fillView layer; hover is ignored for fill layers.", preset, ref warnings);
                }
            }
            else if (layer.DriftSpeedX != 0f && layer.HorizontalTileRadius < 1)
            {
                Warning($"Backdrop '{preset.name}' layer {i} ('{layer.Sprite.name}') drifts with horizontalTileRadius 0; a single tile visibly pops when it wraps. Use radius >= 1.", preset, ref warnings);
            }

            if (layer.HoverAmount > 0f && layer.GroundFillColor.a > 0f)
            {
                Warning($"Backdrop '{preset.name}' layer {i} ('{layer.Sprite.name}') hovers AND has a ground apron; the apron bobs with the layer. Hovering layers are flying objects - drop the apron.", preset, ref warnings);
            }

            if (layer.Alpha <= 0f)
            {
                Warning($"Backdrop '{preset.name}' layer {i} ('{layer.Sprite.name}') has alpha 0; the layer is invisible.", preset, ref warnings);
            }
        }

        if (fillLayers > 1)
        {
            Warning($"Backdrop '{preset.name}' has {fillLayers} fillView layers; only the front-most is ever visible.", preset, ref warnings);
        }

        if (preset.ParticleCount > 0 && preset.ParticleColor.a <= 0f)
        {
            Warning($"Backdrop '{preset.name}' has {preset.ParticleCount} ambient particles with a fully transparent color.", preset, ref warnings);
        }

        if (preset.FlybyFlockSize > 0 && preset.FlybyIntervalSeconds.x > preset.FlybyIntervalSeconds.y)
        {
            Warning($"Backdrop '{preset.name}' flyby interval min ({preset.FlybyIntervalSeconds.x}) exceeds max ({preset.FlybyIntervalSeconds.y}).", preset, ref warnings);
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
