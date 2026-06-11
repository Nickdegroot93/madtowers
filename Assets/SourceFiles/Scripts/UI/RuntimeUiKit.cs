using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shared primitives for the code-built overlay screens (level select, power-up choice,
/// level complete). One place to restyle them all later.
/// </summary>
public static class RuntimeUiKit
{
    public static Font DefaultFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    public static readonly Color PanelColor = new Color(0.075f, 0.105f, 0.125f, 0.96f);
    public static readonly Color ButtonColor = new Color(0.13f, 0.19f, 0.22f, 1f);
    public static readonly Color TitleColor = new Color(0.92f, 0.97f, 1f, 1f);

    public static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<StandaloneInputModule>();
    }

    /// <summary>Full-screen overlay canvas scaled for the 1080x1920 reference resolution.</summary>
    public static GameObject CreateOverlayCanvas(string name, int sortingOrder)
    {
        GameObject root = new GameObject(name);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        return root;
    }

    public static Image CreateBackdrop(Transform canvasRoot, Color color)
    {
        GameObject backdropObject = new GameObject("Backdrop");
        backdropObject.transform.SetParent(canvasRoot, false);
        RectTransform rect = backdropObject.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image backdrop = backdropObject.AddComponent<Image>();
        backdrop.color = color;
        return backdrop;
    }

    /// <summary>Centered panel with a vertical layout, ready for labels/buttons.</summary>
    public static GameObject CreateCenteredPanel(Transform canvasRoot, Vector2 size, bool drawBackground = true)
    {
        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(canvasRoot, false);
        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;

        if (drawBackground)
        {
            Image image = panel.AddComponent<Image>();
            image.color = PanelColor;
        }

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(36, 36, 36, 36);
        layout.spacing = 22f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return panel;
    }

    /// <summary>
    /// A fixed-size panel whose children scroll vertically - for lists that can outgrow
    /// the screen (level select). Add rows to <paramref name="content"/>; it sizes itself
    /// to its children and the panel clips + scrolls.
    /// </summary>
    public static GameObject CreateScrollColumn(Transform canvasRoot, Vector2 size, out Transform content)
    {
        GameObject panel = new GameObject("ScrollPanel");
        panel.transform.SetParent(canvasRoot, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = size;

        Image background = panel.AddComponent<Image>();
        background.color = PanelColor;
        panel.AddComponent<RectMask2D>();

        GameObject contentObject = new GameObject("Content");
        contentObject.transform.SetParent(panel.transform, false);
        RectTransform contentRect = contentObject.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(36, 36, 36, 36);
        layout.spacing = 22f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = panel.AddComponent<ScrollRect>();
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        content = contentObject.transform;
        return panel;
    }

    public static Text CreateLabel(Transform parent, string text, int fontSize, float height,
        FontStyle style, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
    {
        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(parent, false);

        Text label = labelObject.AddComponent<Text>();
        label.font = DefaultFont;
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = color;

        LayoutElement layout = labelObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        return label;
    }

    public static Button CreateButton(Transform parent, string text, float height,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new GameObject(text);
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.AddComponent<Image>();
        image.color = ButtonColor;

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = ButtonColor;
        colors.highlightedColor = new Color(0.19f, 0.28f, 0.32f, 1f);
        colors.pressedColor = new Color(0.08f, 0.14f, 0.16f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        LayoutElement buttonLayout = buttonObject.AddComponent<LayoutElement>();
        buttonLayout.preferredHeight = height;

        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 0f);
        textRect.offsetMax = new Vector2(-24f, 0f);

        Text label = textObject.AddComponent<Text>();
        label.font = DefaultFont;
        label.text = text;
        label.fontSize = 32;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;

        return button;
    }
}
