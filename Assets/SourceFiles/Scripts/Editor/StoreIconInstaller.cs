using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Wires the store/launcher icon set into Player Settings from Assets/Store/Icons (GOLIVE.md
/// Phase 1 store assets). Run after replacing any of the PNGs there. Sources are cut by the
/// store-art pipeline in ~/Documents/MadTowers/Store/final (golem key art, 2026-09-05):
///   - play-store-icon-512.png    the tight golem crop - default icon + Android legacy/round
///   - adaptive-foreground-432.png a WIDER cut of the same render so the tight composition
///                                 lands inside the launcher mask's inner 66% (72/108 dp)
///   - adaptive-background-432.png the same cut blurred and darkened (parallax base)
/// </summary>
public static class StoreIconInstaller
{
    private const string Folder = "Assets/Store/Icons/";

    [MenuItem("Tools/MadTowers/Apply Store Icons")]
    public static void Apply()
    {
        Texture2D main = Load("play-store-icon-512.png");
        Texture2D fg = Load("adaptive-foreground-432.png");
        Texture2D bg = Load("adaptive-background-432.png");
        if (main == null || fg == null || bg == null) return;

        // Default icon (every platform without its own set, and the editor's preview).
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { main }, IconKind.Any);

        int slots = 0;
        // Per-platform sets. Adaptive Android icons are the two-layer slots (background,
        // foreground) - detected by layer count, so this needs no Android-module reference.
        foreach (BuildTargetGroup group in new[] { BuildTargetGroup.Android, BuildTargetGroup.iOS })
        {
            foreach (PlatformIconKind kind in PlayerSettings.GetSupportedIconKindsForPlatform(group))
            {
                PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(group, kind);
                for (int i = 0; i < icons.Length; i++)
                {
                    if (icons[i].maxLayerCount >= 2) icons[i].SetTextures(bg, fg);
                    else icons[i].SetTexture(main);
                    slots++;
                }
                PlayerSettings.SetPlatformIcons(group, kind, icons);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"StoreIconInstaller: applied icons to {slots} platform slots + default icon.");
    }

    private static Texture2D Load(string file)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + file);
        if (tex == null) Debug.LogError($"StoreIconInstaller: missing {Folder}{file}");
        return tex;
    }
}
