using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// Bottom navigation, placeholder tab screens, and Custom Game entry.
// (partial of MainMenuRuntime, split from the main file for readability - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private static void BuildDummyScreen(Transform parent, MenuTab tab)
    {
        RectTransform panel = CreateRect(parent, $"{tab}Panel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -70f), new Vector2(740f, 260f));
        Image image = panel.gameObject.AddComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
        image.color = CardDark;
        MenuRule(panel, AccentOutline(0.18f));

        CreateTmp(panel, "Title", tab.ToString().ToUpperInvariant(), 58, TextPrimary,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -44f), new Vector2(740f, 82f), new Vector2(0.5f, 1f));
        CreateTmp(panel, "Status", "COMING SOON", 24, TextMuted,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -132f), new Vector2(740f, 54f), new Vector2(0.5f, 1f));
    }

    // Home remains the primary destination: a raised chapter-coloured medallion.
    // The bar and its overhang both live inside the existing safe-area parent.
    private const float NavLabelY = -34f;
    private const float NavIconY = 22f;

    private static Color ChromeFill(ChapterDefinition chapter) => Color.Lerp(
        new Color(.055f, .06f, .07f, .98f),
        chapter != null ? WithAlpha(chapter.MenuAccentColor, .98f) : new Color(.25f, .3f, .3f, .98f), .12f);

    private static Color HomeHexTopColor(ChapterDefinition chapter) => Color.Lerp(
        chapter != null ? chapter.MenuAccentColor : MenuAccent, Color.white, .12f);

    private static Color HomeHexBottomColor(ChapterDefinition chapter) => Color.Lerp(
        chapter != null ? chapter.MenuAccentColor : MenuAccent, Color.black, .45f);

    private static void BuildBottomNav(Transform parent)
    {
        ChapterDefinition chapter = _chapters.Length > 0 ? _chapters[_chapterIndex] : null;
        Color accent = chapter != null ? ChapterLight(chapter) : MenuAccent;
        RectTransform nav = CreateRect(parent, "BottomNavigation",
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(.5f, 0), Vector2.zero, Vector2.zero);
        nav.offsetMin = new Vector2(44f, 56f);
        nav.offsetMax = new Vector2(-44f, 210f);
        _navFill = nav.gameObject.AddComponent<Image>();
        _navFill.sprite = RuntimeSprites.RoundedPanel();
        _navFill.type = Image.Type.Sliced;
        _navFill.color = ChromeFill(chapter);
        _navOutline = RuntimeUiKit.AddOutline(nav, WithAlpha(accent, .32f));
        _navDividers.Clear();
        _navHexImage = null;
        MenuTab[] tabs = { MenuTab.Profile, MenuTab.Chapters, MenuTab.Home, MenuTab.Vault, MenuTab.Settings };
        for (int i = 0; i < tabs.Length; i++)
        {
            if (tabs[i] == MenuTab.Home) BuildHomeNavButton(nav, i, tabs.Length, chapter);
            else BuildNavButton(nav, tabs[i], i, tabs.Length, accent);
        }
        foreach (float x in new[] { .2f, .8f })
        {
            var divider = CreateImage(nav, "Divider", RuntimeSprites.Square(), WithAlpha(accent, .20f));
            SetCenteredAt(divider.rectTransform, new Vector2(x, .5f), Vector2.zero, new Vector2(1.5f, 86f));
            _navDividers.Add(divider);
        }
    }

    private static void BuildHomeNavButton(Transform nav, int index, int count, ChapterDefinition chapter)
    {
        RectTransform slot = CreateNavSlot(nav, "HomeNav", index, count, out Button button);
        button.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            _activeTab = MenuTab.Home;
            BuildMenu();
        });
        Image face = CreateImage(slot, "HomeMedallion",
            MenuSprites.HexButton(HomeHexTopColor(chapter), HomeHexBottomColor(chapter)), Color.white);
        SetCenteredAt(face.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, 8f), new Vector2(208f, 224f));
        face.preserveAspect = true;
        face.raycastTarget = true; // the visible overhang is part of the Home button too
        button.targetGraphic = face;
        _navHexImage = face;
        ColorBlock colors = button.colors;
        colors.normalColor = _activeTab == MenuTab.Home ? Color.white : new Color(.8f, .8f, .8f, 1f);
        colors.selectedColor = colors.normalColor;
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(.66f, .66f, .66f, 1f);
        button.colors = colors;
        face.CrossFadeColor(colors.normalColor, 0f, true, true);
        var glyph = CreateImage(face.transform, "HomeIcon", MenuSprites.NavHouse(TextPrimary), Color.white);
        SetCenteredAt(glyph.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, 25f), new Vector2(66f, 66f));
        glyph.preserveAspect = true;
        var label = CreateTmp(face.transform, "Label", "HOME", 22, TextPrimary, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, -38f), new Vector2(140f, 34f), new Vector2(.5f, .5f));
        label.characterSpacing = 4f;
    }

    // One tab = a slot stretched to a fraction (1/count) of the bar, so widths track the screen.
    private static RectTransform CreateNavSlot(Transform nav, string name, int index, int count,
        out Button button)
    {
        RectTransform slot = CreateRect(nav, name,
            new Vector2(index / (float)count, 0f), new Vector2((index + 1) / (float)count, 1f),
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        slot.offsetMin = Vector2.zero;
        slot.offsetMax = Vector2.zero;

        Image target = slot.gameObject.AddComponent<Image>();
        target.color = Color.clear;
        button = slot.gameObject.AddComponent<Button>();
        button.targetGraphic = target;
        return slot;
    }

    private static void BuildNavButton(Transform nav, MenuTab tab, int index, int count, Color activeColor)
    {
        RectTransform slot = CreateNavSlot(nav, $"{tab}Nav", index, count, out Button button);
        button.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            _activeTab = tab;
            BuildMenu();
        });

        bool selected = _activeTab == tab;
        Color tint = selected ? TextPrimary : Color.Lerp(TextMuted, TextPrimary, .35f);
        if (selected)
        {
            var plate = CreateImage(slot, "Selected", RuntimeSprites.RoundedPanel(), WithAlpha(activeColor, .16f));
            plate.type = Image.Type.Sliced;
            Stretch(plate.rectTransform);
            plate.rectTransform.offsetMin = new Vector2(12f, 12f);
            plate.rectTransform.offsetMax = new Vector2(-12f, -12f);
            RuntimeUiKit.AddOutline(plate.transform, WithAlpha(activeColor, .32f));
        }
        Sprite glyph = tab switch
        {
            MenuTab.Home => MenuSprites.NavHouse(tint),
            MenuTab.Profile => MenuSprites.Person(tint),
            MenuTab.Chapters => MenuSprites.NavLayers(tint),
            MenuTab.Vault => MenuSprites.NavGrid(tint),
            MenuTab.Settings => MenuSprites.NavGear(tint),
            _ => null
        };
        if (glyph != null)
        {
            Image icon = CreateImage(slot, "Icon", glyph, Color.white);
            icon.preserveAspect = true;
            SetCenteredAt(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, NavIconY), new Vector2(58f, 58f));
        }
        CreateTmp(slot, "Label", tab.ToString().ToUpperInvariant(), 21, tint, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, NavLabelY), new Vector2(180f, 34f), new Vector2(0.5f, 0.5f));
    }

    private static void OpenCustomGame()
    {
        TearDownRoot();
        _activeTab = MenuTab.Home;
        CustomGameMenu.Show(BuildMenu);
    }

}
