using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Discovery of every ability and block asset for the Custom Game testing screen. In the EDITOR it
/// loops over the whole project live via AssetDatabase, so any newly-authored ability or block shows
/// up automatically - there is no list to maintain (see CUSTOMGAME.md). A player build has no
/// AssetDatabase, so it reads a <see cref="ContentManifest"/> baked under Resources at build time
/// (ContentManifestBuilder). The screen is a dev/testing tool, so in a player it is gated to
/// DEVELOPMENT builds (Debug.isDebugBuild) - it never appears in a release build.
/// </summary>
public static class ContentCatalog
{
    public static bool IsAvailable
    {
#if UNITY_EDITOR
        get => true;
#else
        // TEMPORARY: exposed in ALL player builds (dev + release) so Custom Game is reachable on
        // device for testing. Restore the `Debug.isDebugBuild &&` gate to hide it from release.
        get => Manifest != null;
#endif
    }

#if !UNITY_EDITOR
    private static ContentManifest _manifest;
    private static bool _manifestLoaded;
    private static ContentManifest Manifest
    {
        get
        {
            if (!_manifestLoaded)
            {
                _manifestLoaded = true;
                _manifest = Resources.Load<ContentManifest>("ContentManifest");
            }
            return _manifest;
        }
    }
#endif

    /// <summary>Every ability, sorted by rarity then name.</summary>
    public static List<AbilityDefinition> AllAbilities()
    {
        var list = new List<AbilityDefinition>();
#if UNITY_EDITOR
        foreach (string guid in AssetDatabase.FindAssets("t:AbilityDefinition"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(path);
            if (ability == null) continue;
            list.Add(ability);
        }
        list.Sort((a, b) =>
        {
            int r = a.Rarity.CompareTo(b.Rarity);
            return r != 0 ? r : string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal);
        });
#else
        if (Manifest != null)
        {
            foreach (AbilityDefinition ability in Manifest.Abilities)
                if (ability != null) list.Add(ability);
        }
#endif
        return list;
    }

    /// <summary>The equal-odds rarity profile (so every enabled ability is equally likely to be
    /// offered - what you want on a test bench). Null = the game's progress-scaled defaults.</summary>
    public static AbilityRarityProfile EqualRarityProfile()
    {
#if UNITY_EDITOR
        foreach (string guid in AssetDatabase.FindAssets("t:AbilityRarityProfile"))
        {
            var profile = AssetDatabase.LoadAssetAtPath<AbilityRarityProfile>(AssetDatabase.GUIDToAssetPath(guid));
            if (profile != null) return profile; // only one exists (TestEqual); first match is fine
        }
        return null;
#else
        return Manifest != null ? Manifest.EqualRarityProfile : null;
#endif
    }

    /// <summary>Every block shape definition, sorted by name.</summary>
    public static List<BlockDefinition> AllBlocks()
    {
        var list = new List<BlockDefinition>();
#if UNITY_EDITOR
        foreach (string guid in AssetDatabase.FindAssets("t:BlockDefinition"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var block = AssetDatabase.LoadAssetAtPath<BlockDefinition>(path);
            if (block != null) list.Add(block);
        }
        list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
#else
        if (Manifest != null)
        {
            foreach (BlockDefinition block in Manifest.Blocks)
                if (block != null) list.Add(block);
        }
#endif
        return list;
    }
}
