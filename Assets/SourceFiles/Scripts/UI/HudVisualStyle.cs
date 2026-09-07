using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Open gameplay HUD: chapter ink, readable Manrope, and quiet brackets.
/// UI-only. Modal panels keep GameMenuStyle's opaque treatment and display typography.</summary>
public static class HudVisualStyle
{
    public readonly struct Palette
    {
        public readonly Color Ink, Secondary, Bracket, Heart, EmptyHeart, Danger;
        public Palette(string ink, string secondary, string heart)
        {
            Ink = Hex(ink); Secondary = Hex(secondary); Heart = Hex(heart);
            Bracket = new Color(Secondary.r, Secondary.g, Secondary.b, .65f);
            EmptyHeart = new Color(Secondary.r, Secondary.g, Secondary.b, .72f);
            Danger = Heart;
        }
    }
    private static TMP_FontAsset _medium, _semibold;
    public static TMP_FontAsset Font => _medium != null ? _medium : (_medium = LoadFont("Manrope-Medium"));
    public static TMP_FontAsset StrongFont => _semibold != null ? _semibold : (_semibold = LoadFont("Manrope-SemiBold"));
    private static TMP_FontAsset LoadFont(string name)
    {
        var source = Resources.Load<Font>("Fonts/" + name);
        if (source == null) return RuntimeUiKit.TmpTitleFont;
        var font = TMP_FontAsset.CreateFontAsset(source);
        font.name = name + " HUD SDF";
        return font;
    }
    private static LevelDefinition _paletteLevel;
    private static Palette _palette;
    private static bool _hasPalette;
    public static Palette Current
    {
        get
        {
            var level = LevelSelectionState.SelectedLevel;
            if (!_hasPalette || _paletteLevel != level)
            {
                _paletteLevel = level; _palette = ForChapter(GameMenuStyle.ActiveChapter); _hasPalette = true;
            }
            return _palette;
        }
    }
    // Authored against gameplay skies, not menuTopIsLight (that describes different artwork).
    public static Palette ForChapter(ChapterDefinition chapter) => (chapter != null ? chapter.ChapterNumber : 0) switch
    {
        1 => new Palette("F2EED7", "DFE7D1", "DB8C80"),
        2 => new Palette("49353F", "67515B", "9A4057"),
        3 => new Palette("E7DDEC", "C0ADC8", "D77591"),
        4 => new Palette("1D2D42", "2B3A50", "83374B"),
        5 => new Palette("EEE3CD", "D4C2A5", "E79586"),
        6 => new Palette("503B30", "6D5541", "994B3D"),
        7 => new Palette("18362F", "203B34", "783039"),
        8 => new Palette("EFE3D8", "CDB4A2", "DF897F"),
        9 => new Palette("263F43", "3D5654", "914A50"),
        10 => new Palette("F0DECC", "CEAF93", "ED9381"),
        11 => new Palette("3C3028", "4C3C33", "813A33"),
        12 => new Palette("3B2928", "472F32", "713035"),
        13 => new Palette("152F27", "1B372D", "70303A"),
        14 => new Palette("E4DCE8", "BFB0CA", "D7889D"),
        15 => new Palette("EFDFDF", "CFADB3", "E69191"),
        _ => new Palette("E8E5DD", "C1C5C0", "D88085")
    };
    private static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var c); return c; }
    private static Sprite _skyFade;
    public static RectTransform AddSkyFade(RectTransform parent)
    {
        // Lost City's moving pale planet crosses a dark sky behind the readouts.
        // A continuous atmospheric fade gives both backgrounds the same readable value;
        // it has no card edge and never intercepts gameplay input.
        if (GameMenuStyle.ActiveChapter?.ChapterNumber != 9) return null;
        if (_skyFade == null)
        {
            const int height = 128;
            var texture = new Texture2D(1, height, TextureFormat.RGBA32, false)
                { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < height; y++)
            {
                float alpha = Mathf.SmoothStep(0, .88f, Mathf.Clamp01(y / 75f));
                texture.SetPixel(0, y, new Color(1, 1, 1, alpha));
            }
            texture.Apply(false, true);
            _skyFade = Sprite.Create(texture, new Rect(0, 0, 1, height), new Vector2(.5f, 1));
            _skyFade.hideFlags = HideFlags.HideAndDontSave;
        }
        var image = Line(parent, "HudSkyFade", new Vector2(0, 1), Vector2.one,
            new Vector2(.5f, 1), Vector2.zero, new Vector2(0, 480), Hex("D7E0CC"));
        image.sprite = _skyFade;
        image.transform.SetAsFirstSibling();
        return image.rectTransform;
    }
    public static void Text(TextMeshProUGUI text, bool strong = false)
    {
        text.font = strong ? StrongFont : Font;
        text.fontStyle = FontStyles.Normal;
        text.color = Current.Ink;
        text.enableVertexGradient = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
    }
    public static void Brackets(RectTransform parent)
    {
        var color = Current.Bracket;
        for (int side = 0; side < 2; side++)
        {
            float x = side;
            Line(parent, "BracketStem", new Vector2(x, 0), new Vector2(x, 1),
                new Vector2(x, .5f), Vector2.zero, new Vector2(2.5f, -12f), color);
            for (int end = 0; end < 2; end++)
                Line(parent, "BracketReturn", new Vector2(x, end), new Vector2(x, end),
                    new Vector2(x, .5f), new Vector2(0, end == 0 ? 6 : -6), new Vector2(24, 2.5f), color);
        }
    }
    public static void PlaceHold(RectTransform rect)
    {
        if (rect == null) return;
        var canvas = rect.GetComponentInParent<Canvas>();
        float below = Mathf.Max(UIManager.NextCardBottomBelowSafeArea + 120f,
            UIManager.BarBottomBelowSafeArea + 224f);
        // Wordmark reaches 88 units above the cube. Leave 24 units below the rule
        // label as well, especially when Foresight extends that column downward.
        if (GameTypeBadgeHud.ActiveSource != null)
            below = Mathf.Max(below, GameTypeBadgeHud.BottomBelowSafeArea + 88f + 24f);
        rect.anchoredPosition = new Vector2(
            (RuntimeUiKit.SafeAreaLeftInset(canvas) - RuntimeUiKit.SafeAreaRightInset(canvas)) * .5f,
            -RuntimeUiKit.SafeAreaTopInset(canvas) - below);
    }
    public static TextMeshProUGUI Label(RectTransform parent, string name, string value, float size,
        Vector2 position, Vector2 dimensions, bool strong)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform; rect.SetParent(parent,false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f,.5f);
        rect.anchoredPosition = position; rect.sizeDelta = dimensions;
        var text = go.AddComponent<TextMeshProUGUI>(); Text(text,strong);
        text.text = value; text.fontSize = size; text.alignment = TextAlignmentOptions.Center;
        return text;
    }
    public static Image Line(RectTransform parent, string name, Vector2 min, Vector2 max,
        Vector2 pivot, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false); rect.anchorMin = min; rect.anchorMax = max;
        rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size;
        var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
        return image;
    }
}
