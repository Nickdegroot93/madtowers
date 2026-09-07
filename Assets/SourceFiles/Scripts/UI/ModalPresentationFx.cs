using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Presentation only: safe-area framing and a weighted, unscaled sheet arrival.
/// Buttons retain their existing listeners, pause ownership and availability.</summary>
public sealed class ModalPresentationFx : MonoBehaviour
{
    private RectTransform _panel;
    private CanvasGroup[] _rows;
    private float _age;

    public static RectTransform Frame(GameObject panel)
    {
        var safe = RuntimeUiKit.CreateRect(panel.transform.parent, "ModalSafeArea",
            Vector2.zero, Vector2.one, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        safe.gameObject.AddComponent<SafeAreaFitter>();
        panel.transform.SetParent(safe, false);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = new Vector2(.10f, .5f);
        rect.anchorMax = new Vector2(.90f, .5f);
        rect.sizeDelta = new Vector2(0f, rect.sizeDelta.y);
        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(56, 56, 54, 48);
        layout.spacing = 24f;
        panel.AddComponent<WidthFitter>();
        return rect;
    }

    public static void Play(GameObject panel)
    {
        var fx = panel.AddComponent<ModalPresentationFx>();
        fx._panel = Frame(panel);
        var fitter = panel.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        // The legacy pause builder still owns the copy. Replace only its glyph rendering.
        foreach (var label in panel.GetComponentsInChildren<Text>()) ReplaceType(label);
        foreach (var button in panel.GetComponentsInChildren<Button>()) StyleAction(button);
        fx._rows = new CanvasGroup[panel.transform.childCount];
        for (int i = 0; i < fx._rows.Length; i++)
        {
            var row = panel.transform.GetChild(i).gameObject;
            fx._rows[i] = row.GetComponent<CanvasGroup>();
            if (fx._rows[i] == null) fx._rows[i] = row.AddComponent<CanvasGroup>();
            fx._rows[i].alpha = 0f;
        }
        Pose(fx._panel, 0f);
    }

    public static void StyleAction(Button button)
    {
        foreach (var label in button.GetComponentsInChildren<Text>()) ReplaceType(label);
        var image = button.GetComponent<Image>();
        if (image != null) { image.sprite = RuntimeSprites.RoundedPanel(); image.type = Image.Type.Sliced; }
        // Brightness, not saturation, carries the primary action.
        var colors = button.colors;
        if (colors.normalColor.maxColorComponent > .4f)
        {
            colors.normalColor = Color.Lerp(GameMenuStyle.Accent, Color.white, .72f);
            colors.highlightedColor = Color.Lerp(colors.normalColor, Color.white, .12f);
            colors.pressedColor = Color.Lerp(colors.normalColor, Color.black, .16f);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
        }
        foreach (var label in button.GetComponentsInChildren<TextMeshProUGUI>())
            label.font = RuntimeUiKit.TmpTitleFont;
    }

    private static void ReplaceType(Text label)
    {
        if (!label.enabled) return;
        var tmp = RuntimeUiKit.CreateTmp(label.transform, "DisplayType", label.text,
            Mathf.Max(20, label.fontSize), label.color, label.alignment,
            FontStyle.Normal, RuntimeUiKit.TitleFont);
        if (label.fontStyle == FontStyle.Bold || label.fontStyle == FontStyle.BoldAndItalic)
            tmp.font = RuntimeUiKit.TmpTitleFont;
        tmp.raycastTarget = false;
        label.enabled = false;
        var mirror = label.gameObject.AddComponent<DisplayLabel>();
        mirror.Source = label;
        mirror.Display = tmp;
    }

    // Legacy builders may update their Text after an ad or async action. Keep that
    // contract intact while the visible glyphs use the display face.
    private sealed class DisplayLabel : MonoBehaviour
    {
        public Text Source;
        public TextMeshProUGUI Display;
        private void LateUpdate()
        {
            if (Source == null || Display == null) return;
            if (Display.text != Source.text) Display.text = Source.text;
            if (Display.color != Source.color) Display.color = Source.color;
        }
    }

    public static void Pose(RectTransform panel, float age)
    {
        float u = Mathf.Clamp01(age / .46f);
        float ease = 1f - Mathf.Pow(1f - u, 4f);
        panel.anchoredPosition = new Vector2(0f, 24f * (1f - ease));
        float settle = Mathf.Sin(u * Mathf.PI) * Mathf.Exp(-u * 5f);
        panel.localScale = new Vector3(1f + settle * .018f, 1f - settle * .028f, 1f);
    }

    private sealed class WidthFitter : MonoBehaviour
    {
        private float _lastWidth = -1f;
        private void LateUpdate()
        {
            var rect = (RectTransform)transform;
            var parent = rect.parent as RectTransform;
            if (parent == null || Mathf.Approximately(parent.rect.width, _lastWidth)) return;
            _lastWidth = parent.rect.width;
            rect.sizeDelta = new Vector2(-Mathf.Max(0f, _lastWidth * .8f - 860f), rect.sizeDelta.y);
        }
    }

    private void Update()
    {
        _age += Time.unscaledDeltaTime;
        Pose(_panel, _age);
        for (int i = 0; i < _rows.Length; i++)
        {
            if (_rows[i] == null) continue;
            float u = Mathf.Clamp01((_age - .08f - i * .045f) / .24f);
            _rows[i].alpha = 1f - Mathf.Pow(1f - u, 3f);
        }
        if (_age >= 1f) enabled = false;
    }
}
