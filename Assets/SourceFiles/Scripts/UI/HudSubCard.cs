using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE one layout for the small status cards that hang under the top bar (coin total, NEXT
/// WAVE countdown, timed-goal clock, banked medal). Each card is exactly as wide as the dark
/// inset card in the bar segment above it - same left and right edges, on every screen size -
/// because the edges are ANCHORED the way the bar segments are (half-screen anchor plus the
/// same fixed offsets), never a hardcoded width. Content is one centered row (icon / caption /
/// value), mirroring how the objective and lives cards center their own clusters, so a short
/// value never leaves a stray gap against one edge (Nick 2026-09-04: "align it with the dark
/// inner card"). Cards stack in rows below the bar when a corner has more than one tenant.
/// </summary>
public static class HudSubCard
{
    public const float Height = 52f;
    public const float GapBelowBar = 12f;
    public const float RowGap = 12f;
    public const float TopOffsetBelowSafeArea = UIManager.BarBottomBelowSafeArea + GapBelowBar;

    /// <summary>UIManager's BarInsetColor - the cards read as the bar's inset cards continued.</summary>
    public static readonly Color Fill = new Color(0f, 0f, 0f, 0.78f);
    /// <summary>UIManager's StatLabelColor - captions match the bar's "WAVE"/"BLOCKS" captions.</summary>
    public static readonly Color CaptionColor = new Color(0.80f, 0.80f, 0.80f, 0.55f);

    public const float IconSize = 34f;
    public const float ValueFontSize = 30f;
    public const float CaptionFontSize = 16f;
    public const float LabelFontSize = 17f;
    public const float RowSpacing = 14f;

    public enum Side { Left, Right }

    /// <summary>A rounded card under the bar on one side. Horizontal edges are anchored to the
    /// inset card above; call <see cref="Place"/> (every frame is fine) for the vertical slot.</summary>
    public static RectTransform Create(Transform canvasRoot, string name, Side side)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(canvasRoot, false);
        rect.pivot = new Vector2(0.5f, 0.5f); // settle-pops scale about the middle

        if (side == Side.Left)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(UIManager.InnerCardOuterMargin, -(TopOffsetBelowSafeArea + Height));
            rect.offsetMax = new Vector2(-UIManager.InnerCardCenterOffset, -TopOffsetBelowSafeArea);
        }
        else
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(UIManager.InnerCardCenterOffset, -(TopOffsetBelowSafeArea + Height));
            rect.offsetMax = new Vector2(-UIManager.InnerCardOuterMargin, -TopOffsetBelowSafeArea);
        }

        Image bg = go.GetComponent<Image>();
        bg.sprite = RuntimeSprites.RoundedPanel();
        bg.type = Image.Type.Sliced;
        bg.color = Fill;
        bg.raycastTarget = false;
        return rect;
    }

    /// <summary>Vertical slot: row 0 sits GapBelowBar under the bar, each further row one card
    /// plus RowGap lower. Safe-area aware; horizontal edges are untouched.</summary>
    public static void Place(RectTransform card, Canvas canvas, int row)
    {
        float top = RuntimeUiKit.SafeAreaTopInset(canvas) + TopOffsetBelowSafeArea + row * (Height + RowGap);
        Vector2 min = card.offsetMin;
        Vector2 max = card.offsetMax;
        min.y = -(top + Height);
        max.y = -top;
        card.offsetMin = min;
        card.offsetMax = max;
    }

    /// <summary>The centered content row: children lay out left-to-right at their preferred
    /// sizes and the whole cluster stays centered in the card whatever the card's width.
    /// <paramref name="scale"/> scales the spacing for enlarged twins (MedalHud's debut).</summary>
    public static RectTransform CreateRow(RectTransform card, float scale = 1f)
    {
        GameObject go = new GameObject("Row", typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(card, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;

        HorizontalLayoutGroup layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = RowSpacing * scale;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rect;
    }

    public static Image AddIcon(RectTransform row, string name, Sprite sprite, Color color, float scale = 1f)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(row, false);
        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.color = color;
        image.raycastTarget = false;

        LayoutElement element = go.AddComponent<LayoutElement>();
        element.preferredWidth = IconSize * scale;
        element.preferredHeight = IconSize * scale;
        return image;
    }

    /// <summary>A single-line TMP label sized by its content (the row centers it). NoWrap +
    /// Overflow so a word can never break inside the card (the "BRONZ/E" lesson).</summary>
    public static TextMeshProUGUI AddText(RectTransform row, string name, string value, float fontSize,
        Color color, float characterSpacing = 0f, float scale = 1f)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(row, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize * scale;
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = characterSpacing;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>A text-only content pass keeps the row honest after the string changes (TMP
    /// reports a new preferred width; the layout group re-centers on the next rebuild).</summary>
    public static void MarkDirty(RectTransform row)
    {
        if (row != null) LayoutRebuilder.MarkLayoutForRebuild(row);
    }
}
