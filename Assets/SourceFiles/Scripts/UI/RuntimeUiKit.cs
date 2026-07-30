using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shared primitives for the code-built overlay screens (level select, power-up choice,
/// level complete). One place to restyle them all later.
/// </summary>
public static partial class RuntimeUiKit
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
                _titleFont = Resources.Load<Font>("Fonts/ArchivoBlack-Regular");
                if (_titleFont == null) _titleFont = Resources.Load<Font>("Fonts/Inter-Variable");
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

    // object-fit: cover for stretch-sized containers: keeps `graphic` at `aspect` while always
    // fully covering its parent (AspectRatioFitter.EnvelopeParent), cropping the overflow -
    // never squashing the art. For containers whose size isn't fixed at build time (full-screen
    // backdrops that must survive rotation/resize); the fixed-size slot variant is
    // CreateCoverImage above. Clipping is the caller's concern: overflow past the screen edge
    // is harmless, overflow into a sibling's window (the menu's swipe track) needs a RectMask2D.
    public static void FitToCover(Graphic graphic, float aspect)
    {
        AspectRatioFitter fit = graphic.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio = aspect > 0f ? aspect : 1f;
    }

    public static float SpriteAspect(Sprite sprite, float fallback = 1f)
    {
        return sprite != null && sprite.rect.height > 0f
            ? sprite.rect.width / sprite.rect.height
            : fallback;
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

    // The heavy display face (Archivo Black) as a TMP asset - the ability cards' titles,
    // chips and buttons use it. TmpTitleFont (Inter) stays the menu's default title face;
    // this is the louder voice for card/hero moments.
    private static TMP_FontAsset _tmpDisplayFont;
    public static TMP_FontAsset TmpDisplayFont
    {
        get
        {
            if (_tmpDisplayFont == null)
            {
                Font archivo = Resources.Load<Font>("Fonts/ArchivoBlack-Regular");
                _tmpDisplayFont = archivo != null ? TMP_FontAsset.CreateFontAsset(archivo) : TmpTitleFont;
            }
            return _tmpDisplayFont;
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

    /// <summary>A themed 0..1 horizontal slider: full-width rounded track, accent fill, round
    /// handle, and a transparent hit area so a tap/drag anywhere on the band moves it. Fills its
    /// parent rect. <paramref name="onChanged"/> fires continuously while dragging - add a
    /// <c>PointerUpProxy</c> to the returned slider for a commit-on-release hook.</summary>
    public static Slider CreateSlider(Transform parent, string name, float value,
        Color fillColor, Color trackColor, UnityEngine.Events.UnityAction<float> onChanged,
        float trackThickness = 14f, float handleSize = 44f) // handle >= 44pt-equivalent minimum; callers on roomy screens pass bigger
    {
        RectTransform root = CreateRect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Slider slider = root.gameObject.AddComponent<Slider>();

        // Transparent full-area hit target so a tap/drag anywhere on the band drives the slider.
        Image hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;

        Image track = CreateImage(root, "Track", RuntimeSprites.RoundedPanel(), trackColor);
        track.type = Image.Type.Sliced;
        RectTransform trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(0f, 0.5f);
        trackRect.anchorMax = new Vector2(1f, 0.5f);
        trackRect.pivot = new Vector2(0.5f, 0.5f);
        trackRect.anchoredPosition = Vector2.zero;
        trackRect.sizeDelta = new Vector2(0f, trackThickness);

        // Fill container is inset by the handle radius each side so the fill aligns with the
        // handle's travel; the Slider drives this Fill's right anchor from the value.
        RectTransform fillArea = CreateRect(root, "Fill Area", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-handleSize, trackThickness));
        Image fillImage = CreateImage(fillArea, "Fill", RuntimeSprites.RoundedPanel(), fillColor);
        fillImage.type = Image.Type.Sliced;
        RectTransform fillRect = fillImage.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        RectTransform handleArea = CreateRect(root, "Handle Slide Area", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-handleSize, 0f));
        Image handleImage = CreateImage(handleArea, "Handle", MenuSprites.CircleBadge(Color.white, fillColor), Color.white);
        handleImage.preserveAspect = true;
        handleImage.raycastTarget = true;
        RectTransform handleRect = handleImage.rectTransform;
        handleRect.anchorMin = new Vector2(0f, 0.5f);
        handleRect.anchorMax = new Vector2(0f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.sizeDelta = new Vector2(handleSize, handleSize);

        slider.direction = Slider.Direction.LeftToRight;
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = Mathf.Clamp01(value);
        if (onChanged != null) slider.onValueChanged.AddListener(onChanged);
        return slider;
    }

    /// <summary>A themed sliding pill toggle built into <paramref name="pill"/> (a fixed-size rect
    /// the caller has positioned): rounded background that fills with <paramref name="accentColor"/>
    /// when on, plus a sliding round knob. <paramref name="onChanged"/> fires with the new state on
    /// tap. Sizes the knob/travel from the pill's own dimensions, so any pill size works.</summary>
    public static void CreatePillToggle(RectTransform pill, bool value, Color accentColor,
        UnityEngine.Events.UnityAction<bool> onChanged)
    {
        float width = pill.sizeDelta.x;
        float height = pill.sizeDelta.y;
        float knob = height - 8f;

        Image bg = pill.gameObject.AddComponent<Image>();
        bg.sprite = RuntimeSprites.RoundedPanel();
        bg.type = Image.Type.Sliced;
        Button button = pill.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.transition = Selectable.Transition.None;

        Image knobImage = CreateImage(pill, "Knob", MenuSprites.CircleBadge(Color.white, new Color(0f, 0f, 0f, 0.18f)), Color.white);
        knobImage.preserveAspect = true;
        RectTransform knobRect = knobImage.rectTransform;
        knobRect.anchorMin = new Vector2(0f, 0.5f);
        knobRect.anchorMax = new Vector2(0f, 0.5f);
        knobRect.pivot = new Vector2(0.5f, 0.5f);
        knobRect.sizeDelta = new Vector2(knob, knob);

        Color offColor = new Color(1f, 1f, 1f, 0.18f);
        float offX = 4f + knob * 0.5f;
        float onX = width - 4f - knob * 0.5f;
        bool on = value;
        void Refresh()
        {
            bg.color = on ? accentColor : offColor;
            knobRect.anchoredPosition = new Vector2(on ? onX : offX, 0f);
        }
        button.onClick.AddListener(() =>
        {
            on = !on;
            Refresh();
            onChanged?.Invoke(on);
        });
        Refresh();
    }

    /// <summary>A themed segmented (single-choice) control built into <paramref name="container"/>
    /// (a fixed-size rect the caller positions): a rounded track with one accent-filled segment per
    /// option. Tapping a segment selects it (recolours in place, no rebuild) and fires
    /// <paramref name="onSelect"/> with its index.</summary>
    public static void CreateSegmentedControl(RectTransform container, string[] options, int selectedIndex,
        Color accentColor, UnityEngine.Events.UnityAction<int> onSelect, int fontSize = 20)
    {
        Image track = container.gameObject.AddComponent<Image>();
        track.sprite = RuntimeSprites.RoundedPanel();
        track.type = Image.Type.Sliced;
        track.color = new Color(1f, 1f, 1f, 0.08f);

        int count = Mathf.Max(1, options.Length);
        Image[] fills = new Image[count];
        TextMeshProUGUI[] labels = new TextMeshProUGUI[count];
        Color onText = new Color(0.12f, 0.12f, 0.11f, 1f);  // dark - reads on the bright accent fill
        Color offText = new Color(1f, 1f, 1f, 0.62f);
        int current = Mathf.Clamp(selectedIndex, 0, count - 1);

        for (int i = 0; i < count; i++)
        {
            RectTransform seg = CreateRect(container, $"Seg{i}",
                new Vector2(i / (float)count, 0f), new Vector2((i + 1) / (float)count, 1f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            seg.offsetMin = Vector2.zero;
            seg.offsetMax = Vector2.zero;
            Image hit = seg.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            Button button = seg.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;

            Image fill = CreateImage(seg, "Fill", RuntimeSprites.RoundedPanel(), accentColor);
            fill.type = Image.Type.Sliced;
            RectTransform fr = fill.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = new Vector2(4f, 4f);
            fr.offsetMax = new Vector2(-4f, -4f);
            fills[i] = fill;

            labels[i] = CreateTmp(seg, "Label", options[i], fontSize, offText, TextAnchor.MiddleCenter,
                FontStyle.Bold, TitleFont);

            int index = i;
            button.onClick.AddListener(() => { current = index; Refresh(); onSelect?.Invoke(index); });
        }

        void Refresh()
        {
            for (int i = 0; i < count; i++)
            {
                fills[i].enabled = i == current;
                labels[i].color = i == current ? onText : offText;
            }
        }
        Refresh();
    }
}
