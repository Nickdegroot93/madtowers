using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class LevelSelectRuntimeMenu
{
    private const string ResourcesPath = "Levels";
    private const string ThemesResourcesPath = "Themes";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSelectionForPlayMode()
    {
        LevelSelectionState.ClearSelection();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void PrepareSelection()
    {
        LevelSelectionState.BeginSelectionIfNeeded();
    }

    /// <summary>
    /// Quit the current run back to the level menu (pause menu's "Back to Menu"). The
    /// RuntimeInitializeOnLoadMethod hooks only fire at app start, so this re-shows the
    /// menu manually after the scene reload.
    /// </summary>
    public static void ReturnToMenu()
    {
        LevelSelectionState.ClearSelection();
        LevelSelectionState.BeginSelectionIfNeeded();
        Time.timeScale = 1f;
        SceneManager.sceneLoaded += ShowMenuOnceAfterLoad;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private static void ShowMenuOnceAfterLoad(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= ShowMenuOnceAfterLoad;
        ShowMenuIfNeeded();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ShowMenuIfNeeded()
    {
        if (!LevelSelectionState.IsSelectionPending) return;

        LevelDefinition[] levels = Resources.LoadAll<LevelDefinition>(ResourcesPath);
        if (levels == null || levels.Length == 0)
        {
            LevelSelectionState.SelectLevel(null);
            Time.timeScale = 1f;
            return;
        }

        Array.Sort(levels, (left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal));
        Time.timeScale = 0f;
        RuntimeUiKit.EnsureEventSystem();
        BuildMenu(levels);
    }

    private static void BuildMenu(LevelDefinition[] levels)
    {
        GameObject root = RuntimeUiKit.CreateOverlayCanvas("Level Select", 5000);
        RuntimeUiKit.CreateBackdrop(root.transform, new Color(0.04f, 0.06f, 0.08f, 0.96f));

        // Scrollable: the campaign list grows with every theme and must never run off-screen.
        RuntimeUiKit.CreateScrollColumn(root.transform, new Vector2(620f, 1300f), out Transform panelContent);
        Transform panel = panelContent;

        RuntimeUiKit.CreateLabel(panel, "Choose Level", 52, 82f, FontStyle.Bold, RuntimeUiKit.TitleColor);

        // Campaign themes in play order, with progression locks (rules in Campaign).
        // Levels not claimed by any theme follow alphabetically (one-off test levels).
        var claimed = new System.Collections.Generic.HashSet<LevelDefinition>();
        ThemeDefinition[] themes = Campaign.LoadThemesInOrder();
        ThemeDefinition previousCampaignTheme = null;
        for (int themeIndex = 0; themeIndex < themes.Length; themeIndex++)
        {
            ThemeDefinition theme = themes[themeIndex];
            if (theme.Levels == null || theme.Levels.Count == 0) continue;

            bool themeUnlocked = Campaign.IsThemeUnlocked(themes, themeIndex);
            string header = theme.DisplayName + (Campaign.IsThemeCompleted(theme) ? "  ✓" : "");
            RuntimeUiKit.CreateLabel(panel.transform, header, 34, 50f, FontStyle.Bold,
                themeUnlocked ? RuntimeUiKit.TitleColor : LockedColor);

            if (!themeUnlocked)
            {
                string gate = previousCampaignTheme != null
                    ? $"Complete \"{previousCampaignTheme.DisplayName}\" to unlock"
                    : "Locked";
                RuntimeUiKit.CreateLabel(panel.transform, gate, 24, 36f, FontStyle.Italic, LockedColor);
            }
            else
            {
                for (int i = 0; i < theme.Levels.Count; i++)
                {
                    LevelDefinition level = theme.Levels[i];
                    if (level == null || !claimed.Add(level)) continue;

                    CreateLevelButton(panel.transform, level, Campaign.IsLevelUnlocked(theme, i));
                }
            }

            if (!themeUnlocked)
            {
                // Hidden levels must still not reappear under "Other".
                for (int i = 0; i < theme.Levels.Count; i++)
                {
                    if (theme.Levels[i] != null) claimed.Add(theme.Levels[i]);
                }
            }

            // AlwaysUnlocked sandboxes don't gate the campaign, so they're not "previous".
            if (!theme.AlwaysUnlocked) previousCampaignTheme = theme;
        }

        bool hasUnthemed = false;
        for (int i = 0; i < levels.Length; i++)
        {
            if (claimed.Contains(levels[i])) continue;

            if (!hasUnthemed && themes.Length > 0)
            {
                RuntimeUiKit.CreateLabel(panel.transform, "Other", 34, 50f, FontStyle.Bold, RuntimeUiKit.TitleColor);
                hasUnthemed = true;
            }

            CreateLevelButton(panel.transform, levels[i], unlocked: true);
        }
    }

    private static readonly Color LockedColor = new Color(0.5f, 0.56f, 0.62f, 1f);

    private static void CreateLevelButton(Transform parent, LevelDefinition level, bool unlocked)
    {
        string text = level.DisplayName;
        if (ProgressStore.IsLevelCompleted(level)) text += "  ✓";

        ProgressStore.LevelBest best = ProgressStore.GetBest(level);
        if (best != null)
        {
            text += $"   ·   Best {best.bestScore} / {best.bestHeightMeters:F1}m";
        }

        LevelDefinition selectedLevel = level;
        Button button = RuntimeUiKit.CreateButton(parent, unlocked ? text : $"{level.DisplayName}   ·   Locked",
            96f, () => SelectLevel(selectedLevel));

        if (!unlocked)
        {
            button.interactable = false;
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = LockedColor;
        }
    }

    private static void SelectLevel(LevelDefinition level)
    {
        LevelSelectionState.SelectLevel(level);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
