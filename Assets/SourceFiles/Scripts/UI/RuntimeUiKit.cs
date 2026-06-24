using TMPro;
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

    // Display font for titles/buttons (Inter, OFL - Resources/Fonts): a humanist sans that
    // reads warmer and less mechanical than a geometric/condensed display face. Falls back to
    // the built-in font if the asset is ever missing, so UI never hard-fails.
    private static Font _titleFont;
    public static Font TitleFont
    {
        get
        {
            if (_titleFont == null)
            {
                _titleFont = Resources.Load<Font>("Fonts/Inter-Variable");
                if (_titleFont == null) _titleFont = DefaultFont;
            }
            return _titleFont;
        }
    }

    public static readonly Color PanelColor = new Color(0.075f, 0.105f, 0.125f, 0.96f);
    public static readonly Color ButtonColor = new Color(0.13f, 0.19f, 0.22f, 1f);
    public static readonly Color TitleColor = new Color(0.92f, 0.97f, 1f, 1f);
    public static readonly Color BodyTextColor = new Color(0.82f, 0.88f, 0.92f, 1f);
    public static readonly Color ModalBackdropColor = new Color(0.02f, 0.04f, 0.05f, 0.82f);

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

    // ---- Safe area (notch / camera cutout / status bar / home indicator) --------------------
    // Unity's device-safe region is Screen.safeArea (pixels). These helpers turn it into per-edge
    // insets so any top/bottom/side-pinned element can keep clear of cutouts on every phone. See
    // RESPONSIVE.md for the contract; SafeAreaFitter is the container-based way to consume this.

    // Each raw inset is clamped to this fraction of the screen on its axis. Screen.safeArea can
    // momentarily report a degenerate rect (first frame, simulator, mid-rotation); without the
    // clamp that can shove a whole bar a full screen inward and make it vanish. Real notches /
    // indicators are well under 10%, so the clamp only ever bites a bad read, never a real one.
    public const float SafeAreaMaxInsetFraction = 0.1f;

    /// <summary>
    /// Device safe-area insets in SCREEN PIXELS as (left, right, top, bottom), each clamped to
    /// <see cref="SafeAreaMaxInsetFraction"/> of the screen. Canvas-independent.
    /// </summary>
    public static Vector4 SafeAreaInsetsPixels()
    {
        int w = Screen.width;
        int h = Screen.height;
        if (w <= 0 || h <= 0) return Vector4.zero;

        Rect safe = Screen.safeArea;
        float maxX = w * SafeAreaMaxInsetFraction;
        float maxY = h * SafeAreaMaxInsetFraction;
        return new Vector4(
            Mathf.Clamp(safe.xMin, 0f, maxX),          // left
            Mathf.Clamp(w - safe.xMax, 0f, maxX),      // right
            Mathf.Clamp(h - safe.yMax, 0f, maxY),      // top
            Mathf.Clamp(safe.yMin, 0f, maxY));         // bottom
    }

    /// <summary>
    /// Safe-area insets as (left, right, top, bottom) converted to the UI units of
    /// <paramref name="canvas"/> (i.e. divided by its scaleFactor), ready to add to a
    /// RectTransform offset. Pass the canvas the element lives under.
    /// </summary>
    public static Vector4 SafeAreaInsets(Canvas canvas)
    {
        Vector4 px = SafeAreaInsetsPixels();
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        return px / scale;
    }

    public static float SafeAreaTopInset(Canvas canvas) => SafeAreaInsets(canvas).z;
    public static float SafeAreaBottomInset(Canvas canvas) => SafeAreaInsets(canvas).w;
    public static float SafeAreaLeftInset(Canvas canvas) => SafeAreaInsets(canvas).x;
    public static float SafeAreaRightInset(Canvas canvas) => SafeAreaInsets(canvas).y;

    public static RectTransform CreateRect(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        return rect;
    }

    public static Image CreateImage(Transform parent, string name, Sprite sprite, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);

        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    // Fills `size` with `sprite` cropped to cover (like CSS object-fit: cover): the sprite is
    // scaled up to whichever axis needs it so it fully covers the slot, and the overflow is
    // clipped by a RectMask2D. Lets one photographic sprite (e.g. a level thumbnail) sit
    // undistorted in slots of any aspect ratio. Returns the clipping frame so callers can
    // outline/anchor it. preserveAspect is unnecessary: the child is pre-sized to the sprite's
    // own aspect, so it never stretches.
    public static RectTransform CreateCoverImage(Transform parent, string name, Sprite sprite,
        Color tint, Vector2 anchoredPosition, Vector2 size, Vector2 anchor)
    {
        RectTransform frame = CreateRect(parent, name, anchor, anchor, anchor, anchoredPosition, size);
        // Rounded clip so the cropped image takes the card's corner radius (no square corners).
        MakeRoundedMask(frame);

        Image image = CreateImage(frame, "Image", sprite, tint);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;

        float spriteWidth = sprite != null ? sprite.rect.width : size.x;
        float spriteHeight = sprite != null ? sprite.rect.height : size.y;
        float scale = Mathf.Max(size.x / Mathf.Max(spriteWidth, 1f), size.y / Mathf.Max(spriteHeight, 1f));
        rect.sizeDelta = new Vector2(spriteWidth * scale, spriteHeight * scale);
        return frame;
    }

    // Turns `target` into a rounded clip region: a RoundedPanel stencil (invisible) under a Mask,
    // so its children are clipped to the panel's corner radius. Shared by cover-images and the
    // menu's frosted-glass panels so both pick up the exact same rounding.
    public static void MakeRoundedMask(RectTransform target)
    {
        Image stencil = target.GetComponent<Image>();
        if (stencil == null) stencil = target.gameObject.AddComponent<Image>();
        stencil.sprite = RuntimeSprites.RoundedPanel();
        stencil.type = Image.Type.Sliced;
        stencil.color = Color.white;
        stencil.raycastTarget = false;

        Mask mask = target.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
    }

    // A white rounded tile that backs a TRANSPARENT ability glyph, so cards and the
    // consumable slot share one look. The tile fills its parent; `pad` insets the glyph
    // from the tile edge. `tileAlpha` lets the slot show a 90% tile while cards use a
    // solid one. Returns the glyph Image (sprite set by the caller); `tile` is handed back
    // so callers can resize it (cards center a fixed square) or toggle the pair as one.
    public static Image CreateIconTile(Transform parent, float tileAlpha, float pad, out Image tile, Color? borderColor = null)
    {
        tile = CreateImage(parent, "IconTile", RuntimeSprites.RoundedPanel(), new Color(1f, 1f, 1f, tileAlpha));
        tile.type = Image.Type.Sliced;
        RectTransform tileRect = tile.rectTransform;
        tileRect.anchorMin = Vector2.zero;
        tileRect.anchorMax = Vector2.one;
        tileRect.offsetMin = Vector2.zero;
        tileRect.offsetMax = Vector2.zero;

        Image glyph = CreateImage(tile.transform, "Icon", null, Color.white);
        glyph.preserveAspect = true;
        RectTransform glyphRect = glyph.rectTransform;
        glyphRect.anchorMin = Vector2.zero;
        glyphRect.anchorMax = Vector2.one;
        glyphRect.offsetMin = new Vector2(pad, pad);
        glyphRect.offsetMax = new Vector2(-pad, -pad);

        // Optional rarity border (off-white / blue / purple), owned here so every call site
        // gets the same look without re-deriving it.
        if (borderColor.HasValue) AddOutline(tile.transform, borderColor.Value);
        return glyph;
    }

    public static RawImage CreateRawImage(Transform parent, string name, Texture texture, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);

        RawImage image = go.GetComponent<RawImage>();
        image.texture = texture;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    // ---- TextMeshPro -------------------------------------------------------------------------
    // SDF text for the menu: crisp at any size, real spacing, inline rich text. Font assets are
    // built once at runtime from the same TTFs the legacy path used (no pre-baked .asset needed),
    // so the menu and the rest of the UI can share fonts during the gradual migration off Text.

    private static TMP_FontAsset _tmpBodyFont;
    public static TMP_FontAsset TmpBodyFont
    {
        get
        {
            if (_tmpBodyFont == null) _tmpBodyFont = TMP_FontAsset.CreateFontAsset(DefaultFont);
            return _tmpBodyFont;
        }
    }

    private static TMP_FontAsset _tmpTitleFont;
    public static TMP_FontAsset TmpTitleFont
    {
        get
        {
            if (_tmpTitleFont == null)
            {
                Font inter = Resources.Load<Font>("Fonts/Inter-Variable");
                _tmpTitleFont = inter != null ? TMP_FontAsset.CreateFontAsset(inter) : TmpBodyFont;
            }
            return _tmpTitleFont;
        }
    }

    private static TMP_FontAsset TmpFontFor(Font font) => font == TitleFont ? TmpTitleFont : TmpBodyFont;

    private static TextAlignmentOptions TmpAlign(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
            case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
            case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
            case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
            case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
            case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
            case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
            case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
            default: return TextAlignmentOptions.BottomRight;
        }
    }

    /// <summary>TMP twin of CreateText (stretched). Same signature so call sites barely change.</summary>
    public static TextMeshProUGUI CreateTmp(Transform parent, string name, string value, int size, Color color,
        TextAnchor alignment, FontStyle style, Font font)
    {
        TextMeshProUGUI tmp = CreateTmp(parent, name, value, size, color, alignment, style, font,
            Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Stretch(tmp.rectTransform);
        return tmp;
    }

    /// <summary>TMP twin of CreateText (positioned).</summary>
    public static TextMeshProUGUI CreateTmp(Transform parent, string name, string value, int size, Color color,
        TextAnchor alignment, FontStyle style, Font font, Vector2 anchoredPosition, Vector2 rectSize, Vector2 anchor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = rectSize;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = TmpFontFor(font);
        tmp.text = value;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TmpAlign(alignment);
        tmp.fontStyle = style == FontStyle.Bold ? FontStyles.Bold
            : style == FontStyle.Italic ? FontStyles.Italic
            : style == FontStyle.BoldAndItalic ? (FontStyles.Bold | FontStyles.Italic)
            : FontStyles.Normal;
        tmp.richText = true;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>Best-fit autosizing for a TMP label (TMP twin of resizeTextForBestFit + min/max).</summary>
    public static void AutoSize(TMP_Text tmp, float min, float max)
    {
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = min;
        tmp.fontSizeMax = max;
        tmp.fontSize = max;
    }

    public static void SetRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size, Vector2 anchor)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    /// <summary>Stretched outline child over a panel fill (RoundedOutline matches
    /// RoundedPanel's geometry, so the pair reads as one bordered shape).</summary>
    public static Image AddOutline(Transform parent, Color color)
    {
        GameObject outlineObject = new GameObject("Outline", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)outlineObject.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image outline = outlineObject.GetComponent<Image>();
        outline.sprite = RuntimeSprites.RoundedOutline();
        outline.type = Image.Type.Sliced;
        outline.color = color;
        outline.raycastTarget = false;

        LayoutElement layout = outlineObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        return outline;
    }

    /// <summary>Full-screen modal scaffold: overlay canvas + the standard dim backdrop.</summary>
    public static GameObject CreateModal(string name, int sortingOrder)
    {
        GameObject root = CreateOverlayCanvas(name, sortingOrder);
        CreateBackdrop(root.transform, ModalBackdropColor);
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
        // Guard against null: callers that need the Button reference inside their handler (e.g.
        // CreateCycleRow, whose handler reads the button's own value label) pass null here and
        // AddListener their real handler afterward. A null listener throws on click (Unity calls
        // delegate.Target during invoke), which aborted the listener chain before the real one ran
        // - that was why every cycle row (Preset / Win by / Difficulty ramp) did nothing on tap.
        if (onClick != null) button.onClick.AddListener(onClick);

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

    // ---- form controls (Custom Game setup, and any future settings screen) -----------------

    private static readonly Color CheckOnColor = new Color(0.27f, 0.62f, 0.55f, 1f);
    private static readonly Color CheckOffColor = new Color(0.16f, 0.2f, 0.24f, 1f);

    private static GameObject CreateControlRow(Transform parent, float height, out Text labelText, string label)
    {
        GameObject row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        row.AddComponent<LayoutElement>().preferredHeight = height;
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
