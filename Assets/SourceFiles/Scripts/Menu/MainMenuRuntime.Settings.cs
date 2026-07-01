using System;
using Coffee.UIEffects;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// The Settings screen: a chapter-themed left tab rail and a content panel. The rail and theming
// are the contract (see SETTINGS.md); per-tab controls are not built yet - every tab currently
// shows an empty placeholder panel.
// (partial of MainMenuRuntime, split from the main file for readability - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private enum SettingsTab
    {
        Controls,
        Graphics,
        Sound,
        Notifications,
        Account,
        About
    }

    // Defaults to Sound & Haptics so the screen opens on the tab we build first. Persists across
    // menu rebuilds while in Settings; reset with the rest of the menu on play-mode entry.
    private static SettingsTab _activeSettingsTab = SettingsTab.Sound;

    // Vertical envelope inside the content layer: below the title block, above the bottom nav.
    // Tuned so the body sits tight under the subtitle with a clear gap above the nav (it reads as
    // centred rather than hanging low); the bottom inset still clears the nav on every phone.
    private const float SettingsBodyTopInset = 344f;
    private const float SettingsBodyBottomInset = 236f;
    private const float SettingsBodySideInset = 60f;
    private const float SettingsRailWidth = 220f;
    private const float SettingsRailGap = 18f;

    private static void BuildSettingsScreen(Transform parent, ChapterDefinition chapter)
    {
        Color light = ChapterLight(chapter);

        BuildSettingsHeader(parent);

        // Body region: left rail + right panel, stretched to the screen width between side insets.
        RectTransform body = CreateRect(parent, "SettingsBody",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        body.offsetMin = new Vector2(SettingsBodySideInset, SettingsBodyBottomInset);
        body.offsetMax = new Vector2(-SettingsBodySideInset, -SettingsBodyTopInset);

        BuildSettingsRail(body, chapter, light);
        RectTransform panel = BuildSettingsPanel(body, chapter);
        BuildSettingsPanelContent(panel, _activeSettingsTab, chapter, light);

        // Editor-only Custom Game entry kept reachable while we have no real settings to host it.
        // (Lives in the panel footer for now; relocate once the tabs carry actual content.)
        if (ContentCatalog.IsAvailable) BuildCustomGameButton(panel, light);
    }

    private static void BuildSettingsHeader(Transform parent)
    {
        // Back chevron (the right-pointing glyph mirrored to point left) in a tappable slot.
        RectTransform backSlot = CreateRect(parent, "SettingsBack",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(56f, -188f), new Vector2(72f, 72f));
        Image backHit = backSlot.gameObject.AddComponent<Image>();
        backHit.color = Color.clear;
        backHit.raycastTarget = true;
        Button backButton = backSlot.gameObject.AddComponent<Button>();
        backButton.targetGraphic = backHit;
        backButton.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            _activeTab = MenuTab.Home;
            BuildMenu();
        });
        Image chevron = CreateImage(backSlot, "Chevron", MenuSprites.Chevron(TextPrimary), Color.white);
        chevron.preserveAspect = true;
        SetCenteredAt(chevron.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(32f, 32f));
        chevron.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

        TextMeshProUGUI title = CreateTmp(parent, "SettingsTitle", "SETTINGS", 60, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(132f, -180f), new Vector2(640f, 84f), new Vector2(0f, 1f));
        title.characterSpacing = 4f;
        AddTextShadow(title, 0.28f, new Vector2(0f, -2f), 1f);

        TextMeshProUGUI subtitle = CreateTmp(parent, "SettingsSubtitle", "Adjust your game preferences.", 22,
            TextPrimary, TextAnchor.MiddleLeft, FontStyle.Normal, RuntimeUiKit.TitleFont,
            new Vector2(134f, -268f), new Vector2(640f, 32f), new Vector2(0f, 1f));
        AddTextShadow(subtitle, 0.4f, new Vector2(0f, -1f), 0.4f);
    }

    private static void BuildSettingsRail(Transform body, ChapterDefinition chapter, Color light)
    {
        RectTransform rail = CreateRect(body, "SettingsRail",
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
            Vector2.zero, new Vector2(SettingsRailWidth, 0f));
        Image railImage = rail.gameObject.AddComponent<Image>();
        railImage.sprite = RuntimeSprites.RoundedPanel();
        railImage.type = Image.Type.Sliced;
        railImage.color = MenuGlassFill(chapter, 0.55f);
        AddFrostedGlass(rail, chapter != null ? chapter.MenuBackgroundImage : null, 0.7f);
        RuntimeUiKit.AddOutline(rail, GlassBorder);

        SettingsTab[] tabs =
        {
            SettingsTab.Controls, SettingsTab.Graphics, SettingsTab.Sound,
            SettingsTab.Notifications, SettingsTab.Account, SettingsTab.About
        };
        for (int i = 0; i < tabs.Length; i++) BuildSettingsTab(rail, tabs[i], i, tabs.Length, light);
    }

    // One tab = a slot stretched to a fraction (1/count) of the rail height, top-down. Selected
    // tab gets a chapter-light outline + soft glow + an inner-edge diamond notch.
    private static void BuildSettingsTab(Transform rail, SettingsTab tab, int index, int count, Color light)
    {
        float top = 1f - index / (float)count;
        float bottom = 1f - (index + 1) / (float)count;
        RectTransform slot = CreateRect(rail, $"{tab}Tab",
            new Vector2(0f, bottom), new Vector2(1f, top), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        slot.offsetMin = Vector2.zero;
        slot.offsetMax = Vector2.zero;
        Image hit = slot.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Button button = slot.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            _activeSettingsTab = tab;
            BuildMenu();
        });

        bool selected = tab == _activeSettingsTab;
        if (selected)
        {
            // Lit cell spanning the FULL rail width (only a small top/bottom inset, so adjacent
            // tabs stay visually separated): chapter-light fill, bright outline, soft halo.
            Image highlight = CreateImage(slot, "Highlight", RuntimeSprites.RoundedPanel(), WithAlpha(light, 0.12f));
            highlight.type = Image.Type.Sliced;
            RectTransform hr = highlight.rectTransform;
            hr.anchorMin = Vector2.zero;
            hr.anchorMax = Vector2.one;
            hr.offsetMin = new Vector2(0f, 9f);
            hr.offsetMax = new Vector2(0f, -9f);

            // Soft halo behind the selected tile (uGUI has no box-shadow; a zero-distance blurred
            // Replace-tinted shadow of the filled rect is the glow, same trick the level card uses).
            UIEffect glow = highlight.gameObject.AddComponent<UIEffect>();
            glow.shadowMode = ShadowMode.Shadow;
            glow.shadowDistance = Vector2.zero;
            glow.shadowIteration = 5;
            glow.shadowBlurIntensity = 1f;
            glow.shadowColorFilter = ColorFilter.Replace;
            glow.shadowColor = WithAlpha(light, 0.8f);
            RuntimeUiKit.AddOutline(highlight.transform, WithAlpha(light, 0.95f));
        }

        Color tint = selected ? Color.Lerp(light, Color.white, 0.25f) : TextMuted;
        (string railLabel, _, _, Func<Color, Sprite> icon) = SettingsTabInfo(tab);

        // Big icon and label, sitting as a tight centred cluster (small gap between the two).
        Image glyph = CreateImage(slot, "Icon", icon(tint), Color.white);
        glyph.preserveAspect = true;
        SetCenteredAt(glyph.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(64f, 64f));

        TextMeshProUGUI label = CreateTmp(slot, "Label", railLabel, 20, tint, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, -38f), new Vector2(SettingsRailWidth - 16f, 28f),
            new Vector2(0.5f, 0.5f));
        label.characterSpacing = 1f;
        AutoSize(label, 14, 20);
    }

    private static RectTransform BuildSettingsPanel(Transform body, ChapterDefinition chapter)
    {
        RectTransform panel = CreateRect(body, "SettingsPanel",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        panel.offsetMin = new Vector2(SettingsRailWidth + SettingsRailGap, 0f);
        panel.offsetMax = Vector2.zero;
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = RuntimeSprites.RoundedPanel();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = MenuGlassFill(chapter, 0.55f);
        AddFrostedGlass(panel, chapter != null ? chapter.MenuBackgroundImage : null, 0.82f);
        RuntimeUiKit.AddOutline(panel, GlassBorder);
        return panel;
    }

    private static void BuildSettingsPanelContent(RectTransform panel, SettingsTab tab, ChapterDefinition chapter, Color light)
    {
        (_, string header, string description, Func<Color, Sprite> icon) = SettingsTabInfo(tab);
        BuildPanelHeader(panel, icon, header, description, chapter, light);

        if (tab == SettingsTab.Sound) BuildSoundSettings(panel, light);
        else if (tab == SettingsTab.Graphics) BuildGraphicsSettings(panel, light);
        else if (tab == SettingsTab.Controls) BuildControlsSettings(panel, chapter, light);
        else BuildEmptyState(panel, icon, light);
    }

    // Faint chapter-tinted glyph + a "coming soon" note, for tabs with no controls yet.
    private static void BuildEmptyState(RectTransform panel, Func<Color, Sprite> icon, Color light)
    {
        Image ghost = CreateImage(panel, "Ghost", icon(WithAlpha(light, 0.16f)), Color.white);
        ghost.preserveAspect = true;
        SetCenteredAt(ghost.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 36f), new Vector2(120f, 120f));
        CreateTmp(panel, "Empty", "COMING SOON", 26, WithAlpha(TextMuted, 0.85f), TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, -66f), new Vector2(440f, 38f), new Vector2(0.5f, 0.5f));
    }

    // ---- Sound & Haptics tab ----------------------------------------------------------------
    private const float SettingsRowPad = 34f;

    // Description tone for headers/rows: lifted off plain TextMuted so the small print stays
    // readable on the frosted panel (older eyes on a phone) without competing with the titles.
    // A property, not a static field: TextMuted/TextPrimary live in another partial file and a
    // field initializer here could run before them (undefined cross-file static init order),
    // lerping default(Color) = transparent. Evaluating on access dodges that entirely.
    private static Color SettingsDescColor => Color.Lerp(TextMuted, TextPrimary, 0.35f);

    // The shared tail for every settings control: persist, then a click for tactile feedback.
    private static void CommitSetting()
    {
        SettingsService.Save();
        SfxPlayer.Play("ui-button-click");
    }

    private static void BuildSoundSettings(RectTransform panel, Color accent)
    {
        Color track = WithAlpha(TextPrimary, 0.16f);
        float y = -150f; // first row top, just below the header hairline

        y = BuildSliderRow(panel, MenuSprites.Note, "MUSIC VOLUME", "Adjust the background music volume.",
            SettingsService.MusicVolume, accent, track, y,
            v => SettingsService.MusicVolume = v,
            SettingsService.Save);

        y = BuildSliderRow(panel, MenuSprites.Speaker, "SOUND EFFECTS", "Adjust the game sound effects volume.",
            SettingsService.SfxVolume, accent, track, y,
            v => SettingsService.SfxVolume = v,
            CommitSetting); // the click on release doubles as a preview of the new SFX level

        BuildToggleRow(panel, MenuSprites.SpeakerOff, "MUTE ALL", "Turn off all sounds.",
            SettingsService.MuteAll, accent, y,
            on => { SettingsService.MuteAll = on; CommitSetting(); });
    }

    // ---- Graphics tab -----------------------------------------------------------------------
    private static void BuildGraphicsSettings(RectTransform panel, Color accent)
    {
        int[] rates = { 30, 60, 120 };
        string[] rateLabels = Array.ConvertAll(rates, r => r.ToString()); // one source of truth
        int selected = Array.IndexOf(rates, SettingsService.TargetFrameRate);
        if (selected < 0) selected = Array.IndexOf(rates, 60); // fall back to 60

        float y = -150f;
        y = BuildSegmentedRow(panel, MenuSprites.Monitor, "FRAME RATE", "Higher is smoother; lower saves battery.",
            rateLabels, selected, accent, y,
            i => { SettingsService.TargetFrameRate = rates[i]; CommitSetting(); });

        y = BuildToggleRow(panel, MenuSprites.Sparkle, "VISUAL EFFECTS", "Bloom, glow and particle effects.",
            SettingsService.VisualEffects, accent, y,
            on => { SettingsService.VisualEffects = on; CommitSetting(); });

        BuildToggleRow(panel, MenuSprites.Shake, "SCREEN SHAKE", "Camera shake on impacts.",
            SettingsService.ScreenShake, accent, y,
            on => { SettingsService.ScreenShake = on; CommitSetting(); });
    }

    // ---- UI / Controls tab ------------------------------------------------------------------
    // The tab's only action is opening the layout editor, so it's one prominent centred button.
    private static void BuildControlsSettings(RectTransform panel, ChapterDefinition chapter, Color accent)
    {
        RectTransform button = CreateRect(panel, "CustomizeButton",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 26f), new Vector2(440f, 176f));
        Image image = button.gameObject.AddComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
        image.color = WithAlpha(accent, 0.14f);
        RuntimeUiKit.AddOutline(button, WithAlpha(accent, 0.6f));
        Button click = button.gameObject.AddComponent<Button>();
        click.targetGraphic = image;
        click.transition = Selectable.Transition.None;
        click.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            HudLayoutEditor.Open(chapter, accent, null);
        });

        Image icon = CreateImage(button, "Icon", MenuSprites.NavGrid(accent), Color.white);
        icon.preserveAspect = true;
        SetCenteredAt(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(62f, 62f));
        CreateTmp(button, "Label", "CUSTOMIZE LAYOUT", 28, TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(0f, -118f), new Vector2(420f, 40f), new Vector2(0.5f, 1f));

        TextMeshProUGUI desc = CreateTmp(panel, "CustomizeDesc",
            "Move & resize the consumable slots and set the nudge-guide visibility.", 18, SettingsDescColor,
            TextAnchor.UpperCenter, FontStyle.Normal, RuntimeUiKit.TitleFont, new Vector2(0f, -104f),
            new Vector2(560f, 60f), new Vector2(0.5f, 0.5f));
        desc.textWrappingMode = TextWrappingModes.Normal;
    }

    // A full-width row anchored under the header, inset by the row padding. Children anchor to its
    // top-left. Returns the row so callers can populate it.
    private static RectTransform NewSettingsRow(RectTransform panel, string name, float top, float height)
    {
        RectTransform row = CreateRect(panel, name, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, top), new Vector2(0f, height));
        row.offsetMin = new Vector2(SettingsRowPad, row.offsetMin.y);
        row.offsetMax = new Vector2(-SettingsRowPad, row.offsetMax.y);
        return row;
    }

    // Shared left cluster for every row: accent icon + name on one line, muted description below.
    private static void BuildRowLabel(RectTransform row, Func<Color, Sprite> icon, string name, string description, Color accent)
    {
        Image glyph = CreateImage(row, "RowIcon", icon(accent), Color.white);
        glyph.preserveAspect = true;
        SetCenteredAt(glyph.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -23f), new Vector2(32f, 32f));
        CreateTmp(row, "RowName", name, 26, TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(46f, -6f), new Vector2(440f, 36f), new Vector2(0f, 1f));
        CreateTmp(row, "RowDesc", description, 20, SettingsDescColor, TextAnchor.MiddleLeft, FontStyle.Normal,
            RuntimeUiKit.TitleFont, new Vector2(0f, -48f), new Vector2(540f, 30f), new Vector2(0f, 1f));
    }

    private static void AddRowDivider(RectTransform row)
    {
        RectTransform divider = CreateRect(row, "RowDivider", new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 1.5f));
        Image image = divider.gameObject.AddComponent<Image>();
        image.sprite = RuntimeSprites.Square();
        image.color = WithAlpha(TextPrimary, 0.1f);
        image.raycastTarget = false;
    }

    // Label cluster + a full-width slider with a live "NN%" readout. onChanged fires while
    // dragging (live apply); onCommit fires on release (persist / preview). Returns next row top.
    private static float BuildSliderRow(RectTransform panel, Func<Color, Sprite> icon, string name, string description,
        float value01, Color accent, Color track, float top, UnityEngine.Events.UnityAction<float> onChanged, Action onCommit)
    {
        const float height = 150f;
        RectTransform row = NewSettingsRow(panel, $"{name}Row", top, height);
        BuildRowLabel(row, icon, name, description, accent);

        TextMeshProUGUI pct = CreateTmp(row, "Pct", $"{Mathf.RoundToInt(value01 * 100f)}%", 24, TextPrimary,
            TextAnchor.MiddleRight, FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, -98f), new Vector2(130f, 36f),
            new Vector2(1f, 1f));

        RectTransform sliderArea = CreateRect(row, "SliderArea", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        sliderArea.offsetMin = new Vector2(0f, -136f);   // band bottom
        sliderArea.offsetMax = new Vector2(-140f, -96f); // band top; right inset leaves room for "%"

        Slider slider = CreateSlider(sliderArea, "Slider", value01, accent, track, v =>
        {
            pct.text = $"{Mathf.RoundToInt(v * 100f)}%";
            onChanged?.Invoke(v);
        });
        slider.gameObject.AddComponent<PointerUpProxy>().OnRelease = onCommit;

        AddRowDivider(row);
        return top - height;
    }

    // Label cluster + a pill toggle (RuntimeUiKit primitive) pinned to the row's right edge.
    // Returns next row top.
    private static float BuildToggleRow(RectTransform panel, Func<Color, Sprite> icon, string name, string description,
        bool value, Color accent, float top, UnityEngine.Events.UnityAction<bool> onChanged)
    {
        const float height = 116f;
        RectTransform row = NewSettingsRow(panel, $"{name}Row", top, height);
        BuildRowLabel(row, icon, name, description, accent);
        RectTransform pill = CreateRect(row, "Toggle", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -40f), new Vector2(66f, 36f));
        CreatePillToggle(pill, value, accent, onChanged);
        return top - height;
    }

    // Label cluster + a segmented control band (single-choice, e.g. frame rate). Returns next top.
    private static float BuildSegmentedRow(RectTransform panel, Func<Color, Sprite> icon, string name, string description,
        string[] options, int selectedIndex, Color accent, float top, UnityEngine.Events.UnityAction<int> onSelect)
    {
        const float height = 150f;
        RectTransform row = NewSettingsRow(panel, $"{name}Row", top, height);
        BuildRowLabel(row, icon, name, description, accent);

        RectTransform band = CreateRect(row, "Segments", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        band.offsetMin = new Vector2(0f, -140f);
        band.offsetMax = new Vector2(0f, -96f);
        CreateSegmentedControl(band, options, selectedIndex, accent, onSelect);

        AddRowDivider(row);
        return top - height;
    }

    // Reusable settings-panel header: accent icon + title on ONE line, description on the line
    // below, and a thin near-full-width hairline under both. Every tab's panel opens with this,
    // so the styling lives in one place.
    private static void BuildPanelHeader(RectTransform panel, Func<Color, Sprite> icon, string title,
        string description, ChapterDefinition chapter, Color accent)
    {
        const float pad = 34f;

        // Icon and title share one line (matched vertical centres at y = -46).
        Image headerIcon = CreateImage(panel, "PanelIcon", icon(accent), Color.white);
        headerIcon.preserveAspect = true;
        SetCenteredAt(headerIcon.rectTransform, new Vector2(0f, 1f), new Vector2(pad + 20f, -47f), new Vector2(40f, 40f));
        CreateTmp(panel, "PanelTitle", title, 33, TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(pad + 52f, -23f), new Vector2(560f, 48f), new Vector2(0f, 1f));

        // Description on the line below, left-aligned under the icon.
        CreateTmp(panel, "PanelDesc", description, 20, SettingsDescColor, TextAnchor.MiddleLeft, FontStyle.Normal,
            RuntimeUiKit.TitleFont, new Vector2(pad, -86f), new Vector2(580f, 30f), new Vector2(0f, 1f));

        // Hairline under the whole header, stretched to (near) the full panel width.
        RectTransform divider = CreateRect(panel, "PanelDivider",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -124f), new Vector2(0f, 2f));
        divider.offsetMin = new Vector2(pad - 6f, divider.offsetMin.y);
        divider.offsetMax = new Vector2(-(pad - 6f), divider.offsetMax.y);
        Image dividerImage = divider.gameObject.AddComponent<Image>();
        dividerImage.sprite = RuntimeSprites.Square();
        dividerImage.color = WithAlpha(ChapterDark(chapter), 0.75f);
        dividerImage.raycastTarget = false;
    }

    private static void BuildCustomGameButton(RectTransform panel, Color light)
    {
        RectTransform rect = CreateRect(panel, "CustomGameButton",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 20f), new Vector2(360f, 58f));
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
        image.color = new Color(0.12f, 0.1f, 0.08f, 0.82f);
        RuntimeUiKit.AddOutline(rect, WithAlpha(light, 0.4f));
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            OpenCustomGame();
        });
        CreateTmp(rect, "Label", "CUSTOM GAME", 24, TextPrimary, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont);
    }

    // Rail label, panel header, one-line description and glyph for each tab. Placeholder copy and
    // art - the per-tab design is owned elsewhere (see SETTINGS.md §4/§7).
    private static (string rail, string header, string description, Func<Color, Sprite> icon) SettingsTabInfo(SettingsTab tab)
    {
        switch (tab)
        {
            case SettingsTab.Controls:
                return ("CONTROLS", "UI / CONTROLS", "Button layout & on-screen controls.", MenuSprites.Sliders);
            case SettingsTab.Graphics:
                return ("GRAPHICS", "GRAPHICS", "Quality, performance & effects.", MenuSprites.Monitor);
            case SettingsTab.Sound:
                return ("SOUND", "SOUND & HAPTICS", "Volume and vibration feedback.", MenuSprites.Equalizer);
            case SettingsTab.Notifications:
                return ("ALERTS", "NOTIFICATIONS", "What you get notified about.", MenuSprites.Bell);
            case SettingsTab.Account:
                return ("ACCOUNT", "ACCOUNT", "Profile, cloud save & language.", MenuSprites.Person);
            default:
                return ("ABOUT", "ABOUT / LEGAL", "Version, privacy, terms & credits.", MenuSprites.Info);
        }
    }
}
