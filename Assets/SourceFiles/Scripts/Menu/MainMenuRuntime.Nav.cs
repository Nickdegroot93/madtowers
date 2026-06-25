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
            new Vector2(0f, -70f), new Vector2(740f, tab == MenuTab.Settings ? 380f : 260f));
        Image image = panel.gameObject.AddComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
        image.color = CardDark;
        RuntimeUiKit.AddOutline(panel, GoldOutline(0.18f));

        CreateTmp(panel, "Title", tab.ToString().ToUpperInvariant(), 58, TextPrimary,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -44f), new Vector2(740f, 82f), new Vector2(0.5f, 1f));
        CreateTmp(panel, "Status", "COMING SOON", 24, TextMuted,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -132f), new Vector2(740f, 54f), new Vector2(0.5f, 1f));

        if (tab == MenuTab.Settings && ContentCatalog.IsAvailable)
        {
            Button custom = CreateMenuButton(panel, "CustomGameButton", "CUSTOM GAME",
                new Vector2(0f, -230f), new Vector2(430f, 76f), OpenCustomGame);
            Text label = custom.GetComponentInChildren<Text>();
            if (label != null) label.color = TextPrimary;
        }
    }

    // A gold-rimmed bar that STRETCHES to the screen width (constant side insets, so it fits any
    // phone), five evenly-spaced tabs split by thin dividers, and a raised gold Home hexagon at
    // the centre. Lives in the menu's safe-area layer, so it already clears the home indicator.
    private const float NavSideInset = 60f;     // gap from each screen edge to the bar
    private const float NavBottomMargin = 28f;  // gap from the safe-area bottom to the bar
    private const float NavBarHeight = 150f;
    private const float NavLabelY = -47f;       // label baseline, shared by every tab
    private const float NavIconY = 20f;         // side-tab icon centre, above the label

    private static void BuildBottomNav(Transform parent)
    {
        // Stretch horizontally between fixed side insets; fixed height pinned to the bottom.
        RectTransform nav = CreateRect(parent, "BottomNavigation",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
        nav.offsetMin = new Vector2(NavSideInset, NavBottomMargin);
        nav.offsetMax = new Vector2(-NavSideInset, NavBottomMargin + NavBarHeight);

        Image navImage = nav.gameObject.AddComponent<Image>();
        navImage.sprite = RuntimeSprites.RoundedPanel();
        navImage.type = Image.Type.Sliced;
        navImage.color = new Color(0.06f, 0.05f, 0.04f, 0.96f);

        ChapterDefinition chapter = _chapters.Length > 0 ? _chapters[_chapterIndex] : null;
        Color gold = chapter != null ? ChapterLight(chapter) : GoldBase;
        RuntimeUiKit.AddOutline(nav, WithAlpha(gold, 0.55f));

        MenuTab[] tabs = { MenuTab.Shop, MenuTab.Chapters, MenuTab.Home, MenuTab.Vault, MenuTab.Settings };

        // Thin vertical dividers on the four internal slot boundaries (not the rounded ends).
        for (int i = 1; i < tabs.Length; i++)
        {
            Image divider = CreateImage(nav, $"NavDivider{i}", RuntimeSprites.Square(), WithAlpha(gold, 0.22f));
            RectTransform d = divider.rectTransform;
            float fx = i / (float)tabs.Length;
            d.anchorMin = new Vector2(fx, 0.5f);
            d.anchorMax = new Vector2(fx, 0.5f);
            d.pivot = new Vector2(0.5f, 0.5f);
            d.anchoredPosition = Vector2.zero;
            d.sizeDelta = new Vector2(1.5f, NavBarHeight * 0.5f);
        }

        for (int i = 0; i < tabs.Length; i++)
        {
            if (tabs[i] == MenuTab.Home) BuildHomeNavButton(nav, i, tabs.Length, chapter, gold);
            else BuildNavButton(nav, tabs[i], i, tabs.Length, gold);
        }
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

        Color tint = _activeTab == tab ? activeColor : TextMuted;
        Sprite glyph = tab switch
        {
            MenuTab.Shop => MenuSprites.NavBag(tint),
            MenuTab.Chapters => MenuSprites.NavLayers(tint),
            MenuTab.Vault => MenuSprites.NavGrid(tint),
            MenuTab.Settings => MenuSprites.NavGear(tint),
            _ => null
        };
        if (glyph != null)
        {
            Image icon = CreateImage(slot, "Icon", glyph, Color.white);
            icon.preserveAspect = true;
            SetCenteredAt(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, NavIconY), new Vector2(48f, 48f));
        }
        CreateTmp(slot, "Label", tab.ToString().ToUpperInvariant(), 17, tint, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, NavLabelY), new Vector2(160f, 30f), new Vector2(0.5f, 0.5f));
    }

    private static void BuildHomeNavButton(Transform nav, int index, int count, ChapterDefinition chapter, Color gold)
    {
        RectTransform slot = CreateNavSlot(nav, "HomeNav", index, count, out Button button);
        button.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            _activeTab = MenuTab.Home;
            BuildMenu();
        });

        bool active = _activeTab == MenuTab.Home;

        // A point-top hexagon, centred on the bar and TALLER than it, so its top and bottom points
        // overhang both bar edges - the reference's prominent centre button. Darker amber base
        // with a gradient up to a lighter top (HexButton lerps bottom->top); the house glyph and
        // the HOME label both sit INSIDE the hexagon.
        Color topColor, bottomColor;
        if (chapter != null)
        {
            topColor = chapter.PlayButtonTopColor;
            bottomColor = Color.Lerp(chapter.PlayButtonBottomColor, Color.black, 0.36f);
        }
        else
        {
            topColor = new Color(0.96f, 0.66f, 0.26f, 1f);
            bottomColor = new Color(0.55f, 0.24f, 0.05f, 1f);
        }

        RectTransform hex = CreateRect(slot, "HomeHex",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(208f, 208f));
        Image image = hex.gameObject.AddComponent<Image>();
        image.sprite = MenuSprites.HexButton(topColor, bottomColor);
        image.preserveAspect = true;
        image.color = active ? Color.white : new Color(0.66f, 0.64f, 0.62f, 1f);
        button.targetGraphic = image;

        // Light cream-gold glyph + label inside the hexagon, so it reads against the darker amber
        // (the label uses the chapter light colour, lifted toward white for contrast on the fill).
        Color glyphColor = active ? Color.Lerp(gold, Color.white, 0.3f) : Color.Lerp(TextMuted, gold, 0.35f);
        Image house = CreateImage(hex, "HomeIcon", MenuSprites.NavHouse(glyphColor), Color.white);
        house.preserveAspect = true;
        SetCenteredAt(house.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 26f), new Vector2(52f, 52f));

        CreateTmp(hex, "Label", "HOME", 16, glyphColor, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, -32f), new Vector2(124f, 26f), new Vector2(0.5f, 0.5f));
    }

    private static Button CreateMenuButton(Transform parent, string name, string label,
        Vector2 anchoredPosition, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = CreateRect(parent, name,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            anchoredPosition, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
        image.color = new Color(0.12f, 0.1f, 0.08f, 0.82f);
        RuntimeUiKit.AddOutline(rect, GoldOutline(0.22f));

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => { SfxPlayer.Play("ui-button-click"); onClick?.Invoke(); });
        CreateTmp(rect, "Label", label, 26, TextPrimary, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont);
        return button;
    }

    private static void OpenCustomGame()
    {
        TearDownRoot();
        _activeTab = MenuTab.Home;
        CustomGameMenu.Show(BuildMenu);
    }

}
