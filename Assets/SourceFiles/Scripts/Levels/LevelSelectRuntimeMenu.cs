using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The level select menu: a theme carousel (one theme per screen - name, progress, its
/// levels; arrows cycle through campaign themes) with sandbox/test levels tucked behind a
/// small "Test Levels" button. Lock rules come from Campaign; completion marks and
/// personal bests from ProgressStore.
/// </summary>
public static class LevelSelectRuntimeMenu
{
    private const string LevelsResourcesPath = "Levels";
    private static readonly Color LockedColor = new Color(0.5f, 0.56f, 0.62f, 1f);
    private static readonly Color SubtleColor = new Color(0.62f, 0.7f, 0.78f, 1f);

    private static GameObject _root;
    private static int _themeIndex;      // carousel position, remembered for the session
    private static bool _showTestPage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        LevelSelectionState.ClearSelection();
        _root = null;
        _showTestPage = false;
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

        LevelDefinition[] levels = Resources.LoadAll<LevelDefinition>(LevelsResourcesPath);
        if (levels == null || levels.Length == 0)
        {
            LevelSelectionState.SelectLevel(null);
            Time.timeScale = 1f;
            return;
        }

        Time.timeScale = 0f;
        RuntimeUiKit.EnsureEventSystem();
        BuildMenu();
    }

    // ---- pages ---------------------------------------------------------------------------

    private static void BuildMenu()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);

        ThemeDefinition[] allThemes = Campaign.LoadThemesInOrder();
        var campaignThemes = new List<ThemeDefinition>();
        var sandboxThemes = new List<ThemeDefinition>();
        var claimed = new HashSet<LevelDefinition>();
        foreach (ThemeDefinition theme in allThemes)
        {
            if (theme.Levels == null || theme.Levels.Count == 0) continue;
            (theme.AlwaysUnlocked ? sandboxThemes : campaignThemes).Add(theme);
            foreach (LevelDefinition level in theme.Levels)
            {
                if (level != null) claimed.Add(level);
            }
        }

        // One-off levels not in any theme also live on the test page.
        var unthemed = new List<LevelDefinition>(
            Array.FindAll(Resources.LoadAll<LevelDefinition>(LevelsResourcesPath),
                level => !claimed.Contains(level)));
        unthemed.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));

        bool hasTestContent = sandboxThemes.Count > 0 || unthemed.Count > 0;
        if (campaignThemes.Count == 0) _showTestPage = true;

        _root = RuntimeUiKit.CreateOverlayCanvas("Level Select", 5000);
        RuntimeUiKit.CreateBackdrop(_root.transform, new Color(0.04f, 0.06f, 0.08f, 0.96f));

        if (_showTestPage) BuildTestPage(sandboxThemes, unthemed, campaignThemes.Count > 0);
        else BuildThemePage(campaignThemes, hasTestContent);
    }

    private static void BuildThemePage(List<ThemeDefinition> themes, bool hasTestContent)
    {
        _themeIndex = Mathf.Clamp(_themeIndex, 0, themes.Count - 1);
        ThemeDefinition theme = themes[_themeIndex];
        ThemeDefinition[] orderedThemes = themes.ToArray();

        RuntimeUiKit.CreateScrollColumn(_root.transform, new Vector2(620f, 1100f), out Transform panel);

        CreateCarouselHeader(panel, themes);
        RuntimeUiKit.CreateLabel(panel, $"Theme {_themeIndex + 1} / {themes.Count}", 24, 34f,
            FontStyle.Normal, SubtleColor);

        if (!Campaign.IsThemeUnlocked(orderedThemes, _themeIndex))
        {
            string previous = _themeIndex > 0 ? themes[_themeIndex - 1].DisplayName : "the previous theme";
            RuntimeUiKit.CreateLabel(panel, $"Complete \"{previous}\" to unlock", 26, 60f,
                FontStyle.Italic, LockedColor);
        }
        else
        {
            for (int i = 0; i < theme.Levels.Count; i++)
            {
                LevelDefinition level = theme.Levels[i];
                if (level == null) continue;

                CreateLevelButton(panel, level, Campaign.IsLevelUnlocked(theme, i));
            }
        }

        if (hasTestContent)
        {
            RuntimeUiKit.CreateLabel(panel, "", 10, 18f, FontStyle.Normal, Color.clear); // spacer
            Button testButton = RuntimeUiKit.CreateButton(panel, "Test Levels", 64f,
                () => { _showTestPage = true; BuildMenu(); });
            Text testLabel = testButton.GetComponentInChildren<Text>();
            if (testLabel != null)
            {
                testLabel.fontSize = 24;
                testLabel.color = SubtleColor;
            }
        }
    }

    // [<]  Theme Name  [>] - arrows wrap around the campaign themes.
    private static void CreateCarouselHeader(Transform panel, List<ThemeDefinition> themes)
    {
        GameObject row = new GameObject("ThemeNav", typeof(RectTransform));
        row.transform.SetParent(panel, false);
        row.AddComponent<LayoutElement>().preferredHeight = 84f;

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        ThemeDefinition theme = themes[_themeIndex];
        int count = themes.Count;

        if (count > 1)
        {
            CreateArrowButton(row.transform, "<", () =>
            {
                _themeIndex = (_themeIndex - 1 + count) % count;
                BuildMenu();
            });
        }

        string title = theme.DisplayName + (Campaign.IsThemeCompleted(theme) ? "  ✓" : "");
        Text titleLabel = RuntimeUiKit.CreateLabel(row.transform, title, 42, 84f,
            FontStyle.Bold, RuntimeUiKit.TitleColor);
        titleLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;

        if (count > 1)
        {
            CreateArrowButton(row.transform, ">", () =>
            {
                _themeIndex = (_themeIndex + 1) % count;
                BuildMenu();
            });
        }
    }

    private static void CreateArrowButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
    {
        Button button = RuntimeUiKit.CreateButton(parent, text, 84f, onClick);
        LayoutElement layout = button.GetComponent<LayoutElement>();
        layout.preferredWidth = 90f;
        layout.flexibleWidth = 0f;
    }

    private static void BuildTestPage(List<ThemeDefinition> sandboxThemes, List<LevelDefinition> unthemed,
        bool hasCampaign)
    {
        RuntimeUiKit.CreateScrollColumn(_root.transform, new Vector2(620f, 1100f), out Transform panel);

        RuntimeUiKit.CreateLabel(panel, "Test Levels", 42, 70f, FontStyle.Bold, RuntimeUiKit.TitleColor);

        foreach (ThemeDefinition theme in sandboxThemes)
        {
            RuntimeUiKit.CreateLabel(panel, theme.DisplayName, 30, 44f, FontStyle.Bold, SubtleColor);
            for (int i = 0; i < theme.Levels.Count; i++)
            {
                if (theme.Levels[i] == null) continue;
                CreateLevelButton(panel, theme.Levels[i], unlocked: true);
            }
        }

        if (unthemed.Count > 0)
        {
            RuntimeUiKit.CreateLabel(panel, "Other", 30, 44f, FontStyle.Bold, SubtleColor);
            foreach (LevelDefinition level in unthemed)
            {
                CreateLevelButton(panel, level, unlocked: true);
            }
        }

        if (hasCampaign)
        {
            RuntimeUiKit.CreateLabel(panel, "", 10, 18f, FontStyle.Normal, Color.clear); // spacer
            RuntimeUiKit.CreateButton(panel, "< Back", 64f, () => { _showTestPage = false; BuildMenu(); });
        }
    }

    // ---- shared --------------------------------------------------------------------------

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
