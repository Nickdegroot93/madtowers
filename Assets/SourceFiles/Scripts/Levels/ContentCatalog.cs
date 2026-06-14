using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Editor-only discovery of every ability and block asset in the project, for the Custom Game
/// setup screen. It LOOPS over the whole project, so any newly-authored ability or block shows
/// up automatically - there is no list to maintain (see CUSTOMGAME.md). Editor-only by design
/// (AssetDatabase): the Custom Game screen is a dev/testing tool, so in a built player these
/// return empty and the screen says so.
/// </summary>
public static class ContentCatalog
{
    public static bool IsAvailable
    {
#if UNITY_EDITOR
        get => true;
#else
        get => false;
#endif
    }

    /// <summary>Every real ability (dummies/scaffolding excluded), sorted by rarity then name.</summary>
    public static List<AbilityDefinition> AllAbilities()
    {
        var list = new List<AbilityDefinition>();
#if UNITY_EDITOR
        foreach (string guid in AssetDatabase.FindAssets("t:AbilityDefinition"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(path);
            if (ability == null || ability is DummyPassiveAbility) continue;
            list.Add(ability);
        }
        list.Sort((a, b) =>
        {
            int r = a.Rarity.CompareTo(b.Rarity);
            return r != 0 ? r : string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal);
        });
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
#endif
        return null;
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
#endif
        return list;
    }
}
