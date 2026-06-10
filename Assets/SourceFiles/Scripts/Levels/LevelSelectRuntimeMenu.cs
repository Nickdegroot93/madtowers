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

        GameObject panel = RuntimeUiKit.CreateCenteredPanel(root.transform, new Vector2(620f, 620f));
        ContentSizeFitter sizeFitter = panel.AddComponent<ContentSizeFitter>();
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RuntimeUiKit.CreateLabel(panel.transform, "Choose Level", 52, 82f, FontStyle.Bold, RuntimeUiKit.TitleColor);

        // Themed levels first: themes by sortOrder, their levels in authored order. Levels not
        // claimed by any theme follow alphabetically (handy for one-off test levels).
        var claimed = new System.Collections.Generic.HashSet<LevelDefinition>();
        ThemeDefinition[] themes = Resources.LoadAll<ThemeDefinition>(ThemesResourcesPath);
        Array.Sort(themes, (left, right) => left.SortOrder.CompareTo(right.SortOrder));
        for (int themeIndex = 0; themeIndex < themes.Length; themeIndex++)
        {
            ThemeDefinition theme = themes[themeIndex];
            if (theme.Levels == null || theme.Levels.Count == 0) continue;

            RuntimeUiKit.CreateLabel(panel.transform, theme.DisplayName, 34, 50f, FontStyle.Bold, RuntimeUiKit.TitleColor);
            for (int i = 0; i < theme.Levels.Count; i++)
            {
                LevelDefinition level = theme.Levels[i];
                if (level == null || !claimed.Add(level)) continue;

                CreateLevelButton(panel.transform, level);
            }
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

            CreateLevelButton(panel.transform, levels[i]);
        }
    }

    private static void CreateLevelButton(Transform parent, LevelDefinition level)
    {
        LevelDefinition selectedLevel = level;
        RuntimeUiKit.CreateButton(parent, level.DisplayName, 96f, () => SelectLevel(selectedLevel));
    }

    private static void SelectLevel(LevelDefinition level)
    {
        LevelSelectionState.SelectLevel(level);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
