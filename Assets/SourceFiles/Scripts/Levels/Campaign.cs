using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Campaign progression rules, computed from ProgressStore + the theme assets:
/// - themes play in sortOrder; a theme unlocks when the previous theme is fully completed
///   (or it's the first, or it's flagged AlwaysUnlocked - testing/sandbox themes)
/// - levels within a theme are sequential: each unlocks when the previous one is completed
/// Pure read-side logic: nothing here writes progress.
/// </summary>
public static class Campaign
{
    // DEV ONLY: short-circuits every lock so all themes/levels are playable while building
    // content. Progress and personal bests still record normally (the save stays honest).
    // Flip to false for release - or wire to a settings toggle when one exists.
    public static readonly bool UnlockAllForTesting = true;

    /// <summary>All themes, sorted by play order. Loaded fresh per call site (menu build).</summary>
    public static ThemeDefinition[] LoadThemesInOrder()
    {
        ThemeDefinition[] themes = Resources.LoadAll<ThemeDefinition>("Themes");
        Array.Sort(themes, (a, b) => a.SortOrder.CompareTo(b.SortOrder));
        return themes;
    }

    /// <summary>The theme whose level list contains the given level, or null.</summary>
    public static ThemeDefinition FindThemeOf(LevelDefinition level)
    {
        if (level == null) return null;

        ThemeDefinition[] themes = Resources.LoadAll<ThemeDefinition>("Themes");
        for (int t = 0; t < themes.Length; t++)
        {
            IReadOnlyList<LevelDefinition> levels = themes[t].Levels;
            if (levels == null) continue;

            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i] == level) return themes[t];
            }
        }
        return null;
    }

    public static bool IsThemeCompleted(ThemeDefinition theme)
    {
        IReadOnlyList<LevelDefinition> levels = theme != null ? theme.Levels : null;
        if (levels == null || levels.Count == 0) return false;

        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] != null && !ProgressStore.IsLevelCompleted(levels[i])) return false;
        }
        return true;
    }

    /// <summary>themesInOrder must come from LoadThemesInOrder (or be sorted the same way).</summary>
    public static bool IsThemeUnlocked(ThemeDefinition[] themesInOrder, int themeIndex)
    {
        if (UnlockAllForTesting) return true;

        ThemeDefinition theme = themesInOrder[themeIndex];
        if (theme.AlwaysUnlocked) return true;

        // Unlocked when every preceding campaign theme is completed (AlwaysUnlocked themes
        // are sandboxes and don't gate the campaign).
        for (int i = 0; i < themeIndex; i++)
        {
            if (themesInOrder[i].AlwaysUnlocked) continue;
            if (!IsThemeCompleted(themesInOrder[i])) return false;
        }
        return true;
    }

    /// <summary>Sequential within the theme: first level always, others need the previous one.</summary>
    public static bool IsLevelUnlocked(ThemeDefinition theme, int levelIndex)
    {
        if (UnlockAllForTesting) return true;
        if (theme.AlwaysUnlocked) return true;
        if (levelIndex <= 0) return true;

        LevelDefinition previous = theme.Levels[levelIndex - 1];
        return previous == null || ProgressStore.IsLevelCompleted(previous);
    }
}
