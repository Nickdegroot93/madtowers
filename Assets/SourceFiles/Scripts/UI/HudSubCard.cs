using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared open secondary HUD rows: coins, wave countdown, clock and banked medal.
/// Horizontal anchors follow the main objective/lives groups; no panel or border.</summary>
public static class HudSubCard
{
    public const float Height = 52f;
    public const float GapBelowBar = 20f;
    public const float RowGap = 8f;
    public const float TopOffsetBelowSafeArea = UIManager.BarBottomBelowSafeArea + GapBelowBar;

    /// <summary>Transparent backing retained for existing row choreography.</summary>
    public static Color Fill => Color.clear;
    /// <summary>Chapter ink shared with the main objective caption.</summary>
    public static Color CaptionColor => HudVisualStyle.Current.Secondary;

    public const float IconSize = 34f;
    public const float ValueFontSize = 30f;
    public const float CaptionFontSize = 20f;
    public const float LabelFontSize = 20f;
    public const float RowSpacing = 10f;

    public enum Side { Left, Right }

    /// <summary>An open row under one side of the HUD. Horizontal edges follow the
    /// group above; call <see cref="Place"/> (every frame is fine) for the vertical slot.</summary>
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
    /// plus RowGap lower. Re-applies top and side safe-area insets.</summary>
    public static void Place(RectTransform card, Canvas canvas, int row)
    {
        float top = RuntimeUiKit.SafeAreaTopInset(canvas) + TopOffsetBelowSafeArea + row * (Height + RowGap);
        Vector2 min = card.offsetMin;
        Vector2 inset = new Vector2(RuntimeUiKit.SafeAreaLeftInset(canvas), RuntimeUiKit.SafeAreaRightInset(canvas));
        if(card.anchorMin.x == 0f) min.x = UIManager.InnerCardOuterMargin + inset.x;
        Vector2 max = card.offsetMax;
        if(card.anchorMax.x == 1f) max.x = -UIManager.InnerCardOuterMargin - inset.y;
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
        HudVisualStyle.Text(text, true);
        text.characterSpacing = Mathf.Min(characterSpacing, 3f);
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
