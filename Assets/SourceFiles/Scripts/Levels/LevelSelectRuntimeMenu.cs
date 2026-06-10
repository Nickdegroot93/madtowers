using System;
using UnityEngine;
using UnityEngine.EventSystems;
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
        EnsureEventSystem();
        BuildMenu(levels);
    }

    private static void BuildMenu(LevelDefinition[] levels)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        GameObject root = new GameObject("Level Select");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        GameObject backdropObject = new GameObject("Backdrop");
        backdropObject.transform.SetParent(root.transform, false);
        RectTransform backdropRect = backdropObject.AddComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;
        Image backdrop = backdropObject.AddComponent<Image>();
        backdrop.color = new Color(0.04f, 0.06f, 0.08f, 0.96f);

        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(620f, 620f);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.075f, 0.105f, 0.125f, 0.96f);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(36, 36, 36, 36);
        layout.spacing = 22f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter sizeFitter = panel.AddComponent<ContentSizeFitter>();
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateLabel(panel.transform, font, "Choose Level", 52, 82f, FontStyle.Bold);

        // Themed levels first: themes by sortOrder, their levels in authored order. Levels not
        // claimed by any theme follow alphabetically (handy for one-off test levels).
        var claimed = new System.Collections.Generic.HashSet<LevelDefinition>();
        ThemeDefinition[] themes = Resources.LoadAll<ThemeDefinition>(ThemesResourcesPath);
        Array.Sort(themes, (left, right) => left.SortOrder.CompareTo(right.SortOrder));
        for (int themeIndex = 0; themeIndex < themes.Length; themeIndex++)
        {
            ThemeDefinition theme = themes[themeIndex];
            if (theme.Levels == null || theme.Levels.Count == 0) continue;

            CreateLabel(panel.transform, font, theme.DisplayName, 34, 50f, FontStyle.Bold);
            for (int i = 0; i < theme.Levels.Count; i++)
            {
                LevelDefinition level = theme.Levels[i];
                if (level == null || !claimed.Add(level)) continue;

                CreateButton(panel.transform, font, level);
            }
        }

        bool hasUnthemed = false;
        for (int i = 0; i < levels.Length; i++)
        {
            if (claimed.Contains(levels[i])) continue;

            if (!hasUnthemed && themes.Length > 0)
            {
                CreateLabel(panel.transform, font, "Other", 34, 50f, FontStyle.Bold);
                hasUnthemed = true;
            }

            CreateButton(panel.transform, font, levels[i]);
        }
    }

    private static void CreateLabel(Transform parent, Font font, string text, int fontSize, float height, FontStyle style)
    {
        GameObject labelObject = new GameObject(text);
        labelObject.transform.SetParent(parent, false);

        Text label = labelObject.AddComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.92f, 0.97f, 1f, 1f);

        LayoutElement layout = labelObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
    }

    private static void CreateButton(Transform parent, Font font, LevelDefinition level)
    {
        GameObject buttonObject = new GameObject(level.DisplayName);
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.13f, 0.19f, 0.22f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.13f, 0.19f, 0.22f, 1f);
        colors.highlightedColor = new Color(0.19f, 0.28f, 0.32f, 1f);
        colors.pressedColor = new Color(0.08f, 0.14f, 0.16f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.colorMultiplier = 1f;
        button.colors = colors;

        LayoutElement buttonLayout = buttonObject.AddComponent<LayoutElement>();
        buttonLayout.preferredHeight = 96f;

        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 0f);
        textRect.offsetMax = new Vector2(-24f, 0f);

        Text label = textObject.AddComponent<Text>();
        label.font = font;
        label.text = level.DisplayName;
        label.fontSize = 34;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;

        button.targetGraphic = image;
        LevelDefinition selectedLevel = level;
        button.onClick.AddListener(() => SelectLevel(selectedLevel));
    }

    private static void SelectLevel(LevelDefinition level)
    {
        LevelSelectionState.SelectLevel(level);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<StandaloneInputModule>();
    }
}
