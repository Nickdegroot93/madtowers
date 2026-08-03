using UnityEngine;
using UnityEngine.UI;

// Legacy uGUI Text kit: the older Text-based label/button, the Custom Game form-control rows
// (toggle / stepper / cycle) and the centered/scroll panel scaffolds. Split out from the modern
// TMP + sprite kit (RuntimeUiKit.cs) so the two UI vocabularies are visibly separate - same
// partial class, so every RuntimeUiKit.* call site is unchanged. Consumed by CustomGameMenu,
// PauseMenuController and the LevelRuntimeController overlays.
public static partial class RuntimeUiKit
{
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
        // A fresh RectTransform defaults to sizeDelta (100,100); with the stretch anchors
        // above that made the content 100 units WIDER than the panel, hanging 50 out each
        // side of the mask (rows near the edges got clipped). Height is the fitter's job.
        contentRect.sizeDelta = Vector2.zero;

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
        // The centered/scroll panels run childControlHeight = false, which sizes children by
        // their own rect (a fresh RectTransform's default 100), NOT the LayoutElement - so the
        // rect must carry the height too or every label renders 100 tall and overflows the panel.
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.sizeDelta = new Vector2(labelRect.sizeDelta.x, height);
        return label;
    }

    public static Button CreateButton(Transform parent, string text, float height,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new GameObject(text);
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.AddComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel(); // corners match the rounded panels these sit in
        image.type = Image.Type.Sliced;
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
        // Guard against null: callers that need the Button reference inside their handler (e.g.
        // CreateCycleRow, whose handler reads the button's own value label) pass null here and
        // AddListener their real handler afterward. A null listener throws on click (Unity calls
        // delegate.Target during invoke), which aborted the listener chain before the real one ran
        // - that was why every cycle row (Preset / Win by / Difficulty ramp) did nothing on tap.
        if (onClick != null) button.onClick.AddListener(onClick);

        LayoutElement buttonLayout = buttonObject.AddComponent<LayoutElement>();
        buttonLayout.preferredHeight = height;
        // Same childControlHeight=false rect rule as CreateLabel: the rect IS the layout height.
        RectTransform buttonRect = (RectTransform)buttonObject.transform;
        buttonRect.sizeDelta = new Vector2(buttonRect.sizeDelta.x, height);

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

    // ---- form controls (Custom Game setup, and any future settings screen) -----------------

    private static readonly Color CheckOnColor = new Color(0.27f, 0.62f, 0.55f, 1f);
    private static readonly Color CheckOffColor = new Color(0.16f, 0.2f, 0.24f, 1f);

    private static GameObject CreateControlRow(Transform parent, float height, out Text labelText, string label)
    {
        GameObject row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        row.AddComponent<LayoutElement>().preferredHeight = height;
        // Same childControlHeight=false rect rule as CreateLabel: the rect IS the layout height.
        RectTransform rowRect = (RectTransform)row.transform;
        rowRect.sizeDelta = new Vector2(rowRect.sizeDelta.x, height);
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        labelText = CreateLabel(row.transform, label, 28, height, FontStyle.Bold, BodyTextColor, TextAnchor.MiddleLeft);
        // The label flexes from ZERO so the fixed-width control on the right always fits and the
        // row never overgrows the panel (which clipped both ends). It absorbs the leftover width.
        LayoutElement labelLayout = labelText.GetComponent<LayoutElement>();
        labelLayout.minWidth = 0f;
        labelLayout.preferredWidth = 0f;
        labelLayout.flexibleWidth = 1f;
        labelText.horizontalOverflow = HorizontalWrapMode.Wrap;
        return row;
    }

    private static Button CreateMiniButton(Transform parent, string text, float width, float height,
        UnityEngine.Events.UnityAction onClick)
    {
        Button button = CreateButton(parent, text, height, onClick);
        LayoutElement layout = button.GetComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.flexibleWidth = 0f;
        Text label = button.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.fontSize = 30;
            RectTransform rect = label.rectTransform;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
        return button;
    }

    /// <summary>Label + a checkbox that flips on tap. Returns the row; calls onChanged(bool).</summary>
    public static GameObject CreateToggleRow(Transform parent, string label, bool initial,
        System.Action<bool> onChanged)
    {
        GameObject row = CreateControlRow(parent, 56f, out Text _, label);

        bool state = initial;
        Image box = null;
        Button button = CreateMiniButton(row.transform, "", 56f, 56f, () =>
        {
            state = !state;
            if (box != null) box.color = state ? CheckOnColor : CheckOffColor;
            onChanged?.Invoke(state);
        });
        box = button.GetComponent<Image>();
        box.color = state ? CheckOnColor : CheckOffColor;
        Text mark = button.GetComponentInChildren<Text>();
        if (mark != null) mark.text = ""; // colour alone reads as on/off
        return row;
    }

    /// <summary>Label + [-] value [+]. Steps within [min,max]; calls onChanged(value).</summary>
    public static GameObject CreateStepperRow(Transform parent, string label, float value, float min,
        float max, float step, string format, System.Action<float> onChanged)
    {
        GameObject row = CreateControlRow(parent, 56f, out Text _, label);

        float current = Mathf.Clamp(value, min, max);
        Text valueText = null;

        void Apply(float v)
        {
            current = Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
            if (valueText != null) valueText.text = current.ToString(format);
            onChanged?.Invoke(current);
        }

        CreateMiniButton(row.transform, "−", 56f, 56f, () => Apply(current - step));
        valueText = CreateLabel(row.transform, current.ToString(format), 28, 56f, FontStyle.Bold, TitleColor);
        LayoutElement valueLayout = valueText.GetComponent<LayoutElement>();
        valueLayout.preferredWidth = 130f;
        valueLayout.flexibleWidth = 0f;
        CreateMiniButton(row.transform, "+", 56f, 56f, () => Apply(current + step));
        return row;
    }

    /// <summary>Label + a button that cycles through options on tap; calls onChanged(index).</summary>
    public static GameObject CreateCycleRow(Transform parent, string label, string[] options, int index,
        System.Action<int> onChanged)
    {
        GameObject row = CreateControlRow(parent, 56f, out Text _, label);

        int current = options.Length > 0 ? Mathf.Clamp(index, 0, options.Length - 1) : 0;
        Text valueLabel = null;
        Button button = CreateMiniButton(row.transform, options.Length > 0 ? options[current] : "", 240f, 56f, null);
        valueLabel = button.GetComponentInChildren<Text>();
        button.onClick.AddListener(() =>
        {
            if (options.Length == 0) return;
            current = (current + 1) % options.Length;
            if (valueLabel != null) valueLabel.text = options[current];
            onChanged?.Invoke(current);
        });
        return row;
    }
}
