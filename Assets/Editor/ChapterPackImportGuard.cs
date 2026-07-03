using System;
using UnityEditor;
using UnityEngine;

// Unity packages import to the paths embedded by the package author. Several chapter packs
// reuse top-level folders, so warn immediately when a new import recreates the old layout.
public sealed class ChapterPackImportGuard : AssetPostprocessor
{
    private const string CanonicalRoot = "Assets/Art/ChapterPacks";

    private static readonly string[] LegacyRoots =
    {
        "Assets/Jungle Landscape",
        "Assets/Desert Vibe",
        "Assets/Japan landscape",
        "Assets/Japan Landscape",
    };

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        string path = FirstLegacyPath(importedAssets);
        if (path == null) path = FirstLegacyPath(movedAssets);
        if (path == null) return;

        Debug.LogWarning(
            $"[ChapterPacks] Imported chapter-pack asset landed at '{path}'. " +
            $"Move the package into '{CanonicalRoot}/<Pack Name>' with its .meta files. " +
            "See ASSET_IMPORTS.md.");
    }

    private static string FirstLegacyPath(string[] paths)
    {
        if (paths == null) return null;

        for (int i = 0; i < paths.Length; i++)
        {
            string path = paths[i];
            if (string.IsNullOrEmpty(path)) continue;

            for (int j = 0; j < LegacyRoots.Length; j++)
            {
                if (IsUnderRoot(path, LegacyRoots[j])) return path;
            }
        }

        return null;
    }

    private static bool IsUnderRoot(string path, string root)
    {
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return true;
        return path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }
}
