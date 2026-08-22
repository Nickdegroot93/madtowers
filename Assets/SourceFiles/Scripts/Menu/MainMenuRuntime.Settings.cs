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
        else if (tab == SettingsTab.Notifications) BuildNotificationSettings(panel, light);
        else if (tab == SettingsTab.Account) BuildAccountSettings(panel, light);
        else if (tab == SettingsTab.About) BuildAboutSettings(panel, light);
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

    // Where tab content starts inside the panel: the header hairline sits at y = -128 (see
    // BuildPanelHeader), plus a breathing gap so the first row doesn't crowd it.
    private const float SettingsRowsTop = 158f;

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
        RectTransform rows = NewRowsBlock(panel);
        float y = 0f;

        y = BuildSliderRow(rows, MenuSprites.Note, "MUSIC VOLUME", "Adjust the background music volume.",
            SettingsService.MusicVolume, accent, track, y,
            v => SettingsService.MusicVolume = v,
            SettingsService.Save);

        y = BuildSliderRow(rows, MenuSprites.Speaker, "SOUND EFFECTS", "Adjust the game sound effects volume.",
            SettingsService.SfxVolume, accent, track, y,
            v => SettingsService.SfxVolume = v,
            CommitSetting); // the click on release doubles as a preview of the new SFX level

        BuildToggleRow(rows, MenuSprites.SpeakerOff, "MUTE ALL", "Turn off all sounds.",
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

        RectTransform rows = NewRowsBlock(panel);
        float y = 0f;
        y = BuildSegmentedRow(rows, MenuSprites.Monitor, "FRAME RATE", "Higher is smoother; lower saves battery.",
            rateLabels, selected, accent, y,
            i => { SettingsService.TargetFrameRate = rates[i]; CommitSetting(); });

        y = BuildToggleRow(rows, MenuSprites.Sparkle, "VISUAL EFFECTS", "Bloom, glow and particle effects.",
            SettingsService.VisualEffects, accent, y,
            on => { SettingsService.VisualEffects = on; CommitSetting(); });

        BuildToggleRow(rows, MenuSprites.Shake, "SCREEN SHAKE", "Camera shake on impacts.",
            SettingsService.ScreenShake, accent, y,
            on => { SettingsService.ScreenShake = on; CommitSetting(); });
    }

    // ---- UI / Controls tab ------------------------------------------------------------------
    // The tab's only action is opening the layout editor: one prominent button, top-anchored
    // under the header like every other tab's content so switching tabs doesn't jump.
    private static void BuildControlsSettings(RectTransform panel, ChapterDefinition chapter, Color accent)
    {
        RectTransform button = CreateRect(panel, "CustomizeButton",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -SettingsRowsTop - 12f), new Vector2(520f, 196f));
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
        SetCenteredAt(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(70f, 70f));
        CreateTmp(button, "Label", "CUSTOMIZE LAYOUT", 30, TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(0f, -132f), new Vector2(490f, 42f), new Vector2(0.5f, 1f));

        TextMeshProUGUI desc = CreateTmp(panel, "CustomizeDesc",
            "Move & resize the consumable slots and set the nudge-guide visibility.", 20, SettingsDescColor,
            TextAnchor.UpperCenter, FontStyle.Normal, RuntimeUiKit.TitleFont,
            new Vector2(0f, -SettingsRowsTop - 230f), new Vector2(600f, 60f), new Vector2(0.5f, 1f));
        desc.textWrappingMode = TextWrappingModes.Normal;
    }

    // ---- Alerts / Notifications tab -----------------------------------------------------
    // Three honest states, one per OS permission verdict. NotRequested: a single TURN ON
    // row (a toggle would be a dead switch until the OS says yes, and dead switches read
    // as broken). Denied: say so and deep-link to system settings - only the OS can undo
    // its own no. Granted: ONE yes/no toggle for everything - per-type toggles were built
    // and cut same-day (Nick 2026-08-12: "do you want notifications, yes or no" is the
    // whole setting; nobody curates ping categories for a tower game).
    private static void BuildNotificationSettings(RectTransform panel, Color accent)
    {
        RectTransform rows = NewRowsBlock(panel);
        NotificationScheduler.PermissionState permission = NotificationScheduler.Permission;

        if (permission == NotificationScheduler.PermissionState.Denied)
        {
            RectTransform denied = NewSettingsRow(rows, "AlertsDenied", 0f, ToggleRowH);
            BuildRowLabel(denied, MenuSprites.Bell, "NOTIFICATIONS ARE OFF",
                "Blocked in your phone's settings.", accent);
            (Button open, _) = BuildRowActionButton(denied, "OpenButton", "OPEN SETTINGS", accent, TextPrimary);
            open.onClick.AddListener(() =>
            {
                SfxPlayer.Play("ui-button-click");
                NotificationScheduler.OpenSystemSettings();
            });
            return;
        }

        if (permission == NotificationScheduler.PermissionState.NotRequested)
        {
            RectTransform enable = NewSettingsRow(rows, "AlertsEnable", 0f, ToggleRowH);
            BuildRowLabel(enable, MenuSprites.Bell, "GET NOTIFIED",
                "Lives refilled, and the odd nudge.", accent);
            (Button turnOn, TextMeshProUGUI turnOnLabel) =
                BuildRowActionButton(enable, "TurnOnButton", "TURN ON", accent, TextPrimary);
            turnOn.onClick.AddListener(() =>
            {
                SfxPlayer.Play("ui-button-click");
                turnOn.interactable = false;
                turnOnLabel.text = "...";
                NotificationScheduler.RequestPermission(_ =>
                {
                    // Verdict changes which of the three states this tab is in: rebuild
                    // (guarded - the player may have left Settings during the OS dialog).
                    if (turnOnLabel != null && _activeTab == MenuTab.Settings) BuildMenu();
                });
            });
            return;
        }

        float y = 0f;
        y = BuildToggleRow(rows, MenuSprites.Bell, "NOTIFICATIONS",
            "Lives refilled, comeback nudges.",
            SettingsService.AlertsEnabled, accent, y,
            on => { SettingsService.AlertsEnabled = on; CommitSetting(); });

        y = BuildDevTestPingRow(rows, y, accent);
        BuildDevTestCrashRow(rows, y, accent);
    }

    /// <summary>DEVELOPMENT BUILDS ONLY: a 60-second test notification, so device passes
    /// verify the pipeline (permission, channel, delivery) without waiting out a regen
    /// cycle. Stay IN the app - backgrounding wipes and reschedules the real alerts.</summary>
    private static float BuildDevTestPingRow(RectTransform rows, float rowTop, Color accent)
    {
        if (!Debug.isDebugBuild) return rowTop;

        RectTransform row = NewSettingsRow(rows, "DevTestPing", rowTop, ToggleRowH);
        BuildRowLabel(row, MenuSprites.Info, "DEV: TEST PING",
            "Test build only. Notification in 60s - stay in the app.", accent);

        (Button click, TextMeshProUGUI label) =
            BuildRowActionButton(row, "TestPingButton", "SEND", accent, TextPrimary);
        click.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            NotificationScheduler.ScheduleTestPing();
            label.text = "SENT - WAIT 60S";
        });
        return rowTop - ToggleRowH;
    }

    /// <summary>DEVELOPMENT BUILDS ONLY: kill the app with a real native crash so a device
    /// build can prove the Unity Diagnostics pipeline end to end (capture, upload,
    /// symbolication - the report lands in Unity Cloud &gt; Developer Data &gt; Diagnostics a
    /// few minutes after the NEXT launch). Two taps on purpose: the first arms, the second
    /// crashes - a mis-tap must not nuke a play session. In the editor this would take the
    /// whole editor down, so it no-ops behind isDebugBuild's device gate AND an explicit
    /// editor check.</summary>
    private static void BuildDevTestCrashRow(RectTransform rows, float rowTop, Color accent)
    {
        if (!Debug.isDebugBuild) return;

        RectTransform row = NewSettingsRow(rows, "DevTestCrash", rowTop, ToggleRowH);
        BuildRowLabel(row, MenuSprites.Info, "DEV: TEST CRASH",
            "Test build only. Force a native crash to verify crash reporting.", accent);

        bool armed = false;
        (Button click, TextMeshProUGUI label) =
            BuildRowActionButton(row, "TestCrashButton", "CRASH", accent, TextPrimary);
        click.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            if (!armed)
            {
                armed = true;
                label.text = "TAP AGAIN";
                return;
            }
            if (Application.isEditor)
            {
                Debug.Log("[Diagnostics] Test crash is device-only (it would take the editor down).");
                label.text = "DEVICE ONLY";
                return;
            }
            UnityEngine.Diagnostics.Utils.ForceCrash(UnityEngine.Diagnostics.ForcedCrashCategory.AccessViolation);
        });
    }

    // ---- Account tab ------------------------------------------------------------------------
    // Who you are, where you'd sign in (v2, 2026-07-29 - the tab held only the tutorial reset
    // and read as empty; Nick: it should show what the Profile page shows). The same live
    // identity as the Profile card - avatar, server name, guest/signed-in status, CHANGE NAME +
    // SIGN IN - then RESTORE PURCHASES (store-connected builds only; Apple mandates the
    // affordance) and the tutorial reset as normal settings rows. Deliberately NOT the whole
    // Profile page: the Unlimited pitch and the online-play promo are storefront, not settings.
    // Still to come per SETTINGS.md §4: delete account (server RPC exists, client flow doesn't),
    // language.
    private static void BuildAccountSettings(RectTransform panel, Color accent)
    {
        const float identityH = 168f;
        const float buttonsH = 108f;
        bool restoreOn = PremiumStore.HasStore;
        bool deleteOn = OnlineService.Enabled;   // no server account = nothing to delete
        RectTransform rows = NewRowsBlock(panel);

        bool guest = !OnlineService.IsLinked;

        // -- identity: avatar + live name + honest status (mirrors BuildProfileIdentityCard) --
        RectTransform identity = NewSettingsRow(rows, "Identity", 0f, identityH);

        Image ring = CreateImage(identity, "AvatarRing", MenuSprites.CircleBadge(
            new Color(0.12f, 0.11f, 0.09f, 1f), WithAlpha(accent, 0.55f)), Color.white);
        SetRect(ring.rectTransform, new Vector2(10f, 0f), new Vector2(132f, 132f), new Vector2(0f, 0.5f));
        Image person = CreateImage(ring.transform, "Person", MenuSprites.Person(WithAlpha(accent, 0.85f)), Color.white);
        person.preserveAspect = true;
        SetCenteredAt(person.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 64f));

        // Identity texts stretch from the avatar to the row's right edge (fixed 520px
        // overflowed the panel on narrow aspects) - ellipsis over overlap, always.
        TextMeshProUGUI name = CreateTmp(identity, "Name", OnlineService.DisplayName, 40, TextPrimary,
            TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(172f, -22f), new Vector2(520f, 56f), new Vector2(0f, 1f));
        // Box must clear the 40pt line HEIGHT (~46px): Ellipsis truncates whole lines, and a
        // line that misses vertically renders as NOTHING, not as overflow. Autosize is the
        // second belt for long claimed names.
        StretchIdentityText(name, -22f, 56f);
        AutoSize(name, 24f, 40f);
        TextMeshProUGUI status = CreateTmp(identity, "Status",
            guest ? "GUEST ACCOUNT" : "SIGNED IN", 19,
            guest ? WithAlpha(TextMuted, 0.9f) : WithAlpha(GoldBase, 0.9f),
            TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(172f, -84f), new Vector2(520f, 26f), new Vector2(0f, 1f));
        StretchIdentityText(status, -84f, 26f);
        TextMeshProUGUI detail = CreateTmp(identity, "Detail",
            guest ? "UNINSTALLING LOSES YOUR PROGRESS" : "YOUR PROGRESS IS SAFE ON EVERY DEVICE", 16,
            WithAlpha(TextMuted, 0.65f), TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(172f, -116f), new Vector2(520f, 24f), new Vector2(0f, 1f));
        StretchIdentityText(detail, -116f, 24f);

        // Same live-refresh + eager-unhook pattern as the Profile card (menu rebuilds are
        // constant, auth changes are rare - never leave dead closures on the static event).
        TextMeshProUGUI nameButtonLabel = null;
        void RefreshIdentity()
        {
            if (name == null)
            {
                OnlineService.StateChanged -= RefreshIdentity;
                return;
            }
            bool g = !OnlineService.IsLinked;
            name.text = OnlineService.DisplayName;
            status.text = g ? "GUEST ACCOUNT" : "SIGNED IN";
            status.color = g ? WithAlpha(TextMuted, 0.9f) : WithAlpha(GoldBase, 0.9f);
            detail.text = g ? "UNINSTALLING LOSES YOUR PROGRESS" : "YOUR PROGRESS IS SAFE ON EVERY DEVICE";
            if (nameButtonLabel != null)
                nameButtonLabel.text = HasClaimedName ? "CHANGE NAME" : "CLAIM YOUR NAME";
        }
        OnlineService.StateChanged += RefreshIdentity;
        identity.gameObject.AddComponent<UnhookOnDestroy>().Unhook =
            () => OnlineService.StateChanged -= RefreshIdentity;

        // -- the identity actions: CHANGE NAME (dark) + SIGN IN (gold CTA) for guests ----------
        RectTransform buttons = NewSettingsRow(rows, "IdentityButtons", -identityH, buttonsH);

        RectTransform BuildIdentityButton(string goName, string label, bool gold, float minX, float maxX, Action onClick)
        {
            Image bg;
            if (gold)
            {
                bg = CreateImage(buttons, goName, MenuSprites.RoundedGradient(
                    new Color(1f, 0.86f, 0.45f, 1f), new Color(0.82f, 0.58f, 0.18f, 1f)), Color.white);
            }
            else
            {
                bg = CreateImage(buttons, goName, RuntimeSprites.RoundedPanel(), new Color(0.13f, 0.12f, 0.10f, 1f));
                RuntimeUiKit.AddOutline(bg.transform, GoldOutline(0.35f));
            }
            bg.type = Image.Type.Sliced;
            RectTransform rt = bg.rectTransform;
            rt.anchorMin = new Vector2(minX, 0.5f);
            rt.anchorMax = new Vector2(maxX, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(minX <= 0f ? 10f : 12f, -44f);
            rt.offsetMax = new Vector2(maxX >= 1f ? -10f : -12f, 44f);
            bg.raycastTarget = true;
            CreateTmp(bg.transform, "Label", label, 24,
                gold ? new Color(0.16f, 0.11f, 0.04f, 1f) : TextPrimary,
                TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
            Button button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(() => { SfxPlayer.Play("ui-button-click"); onClick?.Invoke(); });
            return rt;
        }

        string nameLabel = HasClaimedName ? "CHANGE NAME" : "CLAIM YOUR NAME";
        RectTransform nameButton;
        if (guest)
        {
            nameButton = BuildIdentityButton("ChangeName", nameLabel, false, 0f, 0.5f,
                () => OpenClaimNameModal(RefreshIdentity));
            BuildIdentityButton("SignIn", "SIGN IN", true, 0.5f, 1f, OpenSignInSheet);
        }
        else
        {
            nameButton = BuildIdentityButton("ChangeName", nameLabel, false, 0f, 1f,
                () => OpenClaimNameModal(RefreshIdentity));
        }
        nameButtonLabel = nameButton.GetComponentInChildren<TextMeshProUGUI>();
        AddRowDivider(buttons);

        // -- tutorial replay, as a normal settings row (label cluster + right-edge action) -----
        float rowTop = -(identityH + buttonsH + 34f);
        RectTransform reset = NewSettingsRow(rows, "ResetTutorial", rowTop, ToggleRowH);
        rowTop -= ToggleRowH;
        BuildRowLabel(reset, MenuSprites.Info, "RESET TUTORIAL",
            "Replay the first-time controls walkthrough.", accent);

        (Button resetClick, TextMeshProUGUI resetLabel) =
            BuildRowActionButton(reset, "ResetButton", "RESET", accent, TextPrimary);
        resetClick.onClick.AddListener(() =>
        {
            ProgressStore.ResetTutorial();
            SfxPlayer.Play("ui-button-click");
            resetLabel.text = "DONE";   // the button itself confirms - no floating status line
        });

        // -- restore purchases: new phone / reinstall recovers Unlimited from the store's
        // purchase history (Apple mandates a visible affordance). The button speaks its own
        // verdict, same pattern as RESET.
        if (restoreOn)
        {
            RectTransform restore = NewSettingsRow(rows, "RestorePurchases", rowTop, ToggleRowH);
            rowTop -= ToggleRowH;
            BuildRowLabel(restore, MenuSprites.Sparkle, "RESTORE PURCHASES",
                "Bought Unlimited before? Get it back on this device.", accent);

            (Button restoreClick, TextMeshProUGUI restoreLabel) =
                BuildRowActionButton(restore, "RestoreButton", "RESTORE", accent, TextPrimary);
            restoreClick.onClick.AddListener(() =>
            {
                SfxPlayer.Play("ui-button-click");
                restoreClick.interactable = false;
                restoreLabel.text = "...";
                PremiumStore.Restore(result =>
                {
                    if (restoreLabel == null) return;   // tab switched while in flight
                    restoreClick.interactable = true;
                    restoreLabel.text = result switch
                    {
                        PremiumStoreResult.Restored => "RESTORED",
                        PremiumStoreResult.NothingToRestore => "NOTHING FOUND",
                        _ => "FAILED - RETRY",
                    };
                });
            });
        }

        // -- delete account: store-required (BACKEND.md §3.7), styled as the danger it is.
        // The row only opens the confirm sheet - the real deletion lives behind an explicit
        // second step that spells out what dies.
        Color dangerColor = new Color(0.86f, 0.32f, 0.26f, 1f);
        if (deleteOn)
        {
            RectTransform delete = NewSettingsRow(rows, "DeleteAccount", rowTop, ToggleRowH);
            rowTop -= ToggleRowH;
            BuildRowLabel(delete, MenuSprites.Person, "DELETE ACCOUNT",
                "Erase your account and progress everywhere.", dangerColor);

            (Button deleteClick, _) = BuildRowActionButton(delete, "DeleteButton", "DELETE", dangerColor, dangerColor);
            deleteClick.onClick.AddListener(() =>
            {
                SfxPlayer.Play("ui-button-click");
                OpenDeleteAccountConfirm(dangerColor);
            });
        }

        // -- start over: the device-local factory reset (FactoryReset.EraseAllAndQuit) -
        // everything back to a fresh install, including a new anonymous account next boot.
        // Unlike DELETE ACCOUNT it needs no server and touches nothing server-side, so it
        // works offline; the app closes itself as the confirmation. Two-tap confirm in the
        // button itself (the row pattern), not a sheet - the quit is loud enough.
        RectTransform startOver = NewSettingsRow(rows, "StartOver", rowTop, ToggleRowH);
        BuildRowLabel(startOver, MenuSprites.Info, "START OVER",
            "Wipe this device and restart as a fresh install.", dangerColor);

        (Button eraseClick, TextMeshProUGUI eraseLabel) =
            BuildRowActionButton(startOver, "EraseButton", "ERASE", dangerColor, dangerColor);
        bool armed = false;
        eraseClick.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            if (!armed)
            {
                armed = true;
                eraseLabel.text = "TAP TO CONFIRM";
                return;
            }
            FactoryReset.EraseAllAndQuit();
        });
        rowTop -= ToggleRowH;

        BuildDevSkipChapterOneRow(rows, ref rowTop, accent);
    }

    /// <summary>
    /// DEVELOPMENT BUILDS ONLY: mark chapter 1 finished so the meta systems (attempts
    /// meter, shop, supplies) switch on without replaying it.
    ///
    /// The gate is SERVER-side - `attempts_meter_charged` counts `completedLevelIds` in
    /// the synced payload against `chapter1_level_count` - so nothing local, and not the
    /// unlock-all define either, can turn the meter on by itself. Testing the ad-refill
    /// loop otherwise means genuinely clearing three levels on the device first.
    ///
    /// Guarded by `Debug.isDebugBuild`, the same switch that keeps ads on test units, so
    /// it cannot reach a release build even if this line is forgotten.
    /// </summary>
    private static void BuildDevSkipChapterOneRow(RectTransform rows, ref float rowTop, Color accent)
    {
        if (!Debug.isDebugBuild) return;

        ChapterDefinition[] chapters = Campaign.LoadChaptersInOrder();
        if (chapters.Length == 0) return;
        ChapterDefinition first = chapters[0];

        RectTransform row = NewSettingsRow(rows, "DevCompleteChapter1", rowTop, ToggleRowH);
        rowTop -= ToggleRowH;
        BuildRowLabel(row, MenuSprites.Info, "DEV: COMPLETE CHAPTER 1",
            "Test build only. Turns on lives, shop and supplies.", accent);

        (Button click, TextMeshProUGUI label) =
            BuildRowActionButton(row, "DevCompleteButton", "COMPLETE", accent, accent);
        click.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            int marked = 0;
            foreach (LevelDefinition level in first.Levels)
            {
                if (level == null) continue;
                // A plausible best as well as the completion: the level cards read the
                // local best, and a blank one next to a completed tick looks broken.
                if (!ProgressStore.IsLevelCompleted(level))
                {
                    ProgressStore.MarkLevelCompleted(level);
                    marked++;
                }
                ProgressStore.ReportResult(level, 25, 6f);
            }
            // Push now rather than waiting for the debounced pusher: the meter cannot flip
            // until the SERVER sees the completions, and the point of this button is not
            // waiting. OnBackground is the existing "push what we have" entry point.
            ProgressSync.OnBackground();
            label.text = marked > 0 ? $"DONE ({marked})" : "ALREADY DONE";
            Debug.Log($"[Dev] chapter 1 marked complete ({marked} newly), progress pushed.");
        });
    }

    // ---- About / Legal tab --------------------------------------------------------------
    // Store-required (SETTINGS.md §4): version, privacy policy, terms, support, credits.
    // hazardheights.com is owned and the pages are built (../hazard-heights-web). Trailing
    // slashes are deliberate - the site is a static export, so /privacy/ is the real path.
    // Still to do before ship: point the DNS at the deploy and make support@ deliver.
    private const string PrivacyPolicyUrl = "https://hazardheights.com/privacy/";
    private const string TermsUrl = "https://hazardheights.com/terms/";
    private const string SupportEmail = "support@hazardheights.com";

    private static void BuildAboutSettings(RectTransform panel, Color accent)
    {
        RectTransform rows = NewRowsBlock(panel);
        float rowTop = 0f;

        // Version: informational row, value where the control would sit.
        RectTransform version = NewSettingsRow(rows, "Version", rowTop, ToggleRowH);
        rowTop -= ToggleRowH;
        BuildRowLabel(version, MenuSprites.Info, "VERSION", "Game build you are playing.", accent);
        CreateTmp(version, "Value", $"v{Application.version}", 24, WithAlpha(TextPrimary, 0.9f),
            TextAnchor.MiddleRight, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -36f), new Vector2(190f, 34f), new Vector2(1f, 1f));

        rowTop = BuildLinkRow(rows, rowTop, "PrivacyPolicy", "PRIVACY POLICY",
            "What we store and how to delete it.", accent, () => Application.OpenURL(PrivacyPolicyUrl));
        rowTop = BuildLinkRow(rows, rowTop, "Terms", "TERMS OF SERVICE",
            "The rules of playing Hazard Heights.", accent, () => Application.OpenURL(TermsUrl));
        rowTop = BuildLinkRow(rows, rowTop, "Support", "SUPPORT",
            "Stuck or found a bug? Write to us.", accent,
            () => Application.OpenURL($"mailto:{SupportEmail}"));

        CreateTmp(rows, "Credits", "MADE BY NICK DE GROOT  -  © 2026", 16,
            WithAlpha(TextMuted, 0.7f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, rowTop - 16f), new Vector2(600f, 24f), new Vector2(0.5f, 1f));
    }

    /// <summary>A row whose action opens an external link (browser / mail). Same anatomy as
    /// every settings row: label cluster + right-edge button.</summary>
    private static float BuildLinkRow(RectTransform rows, float top, string goName, string name,
        string description, Color accent, Action onOpen)
    {
        RectTransform row = NewSettingsRow(rows, goName, top, ToggleRowH);
        BuildRowLabel(row, MenuSprites.Info, name, description, accent);

        (Button button, _) = BuildRowActionButton(row, "OpenButton", "OPEN", accent, TextPrimary);
        button.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            onOpen?.Invoke();
        });
        return top - ToggleRowH;
    }

    /// <summary>The standard right-edge action for a settings row: 190x72 accent-tinted
    /// pill + outline, autosized label. Returns the button UNWIRED (callers add their own
    /// onClick) and the label so verdicts can be spoken on it (DONE / RESTORED / ...).</summary>
    private static (Button button, TextMeshProUGUI label) BuildRowActionButton(
        RectTransform row, string goName, string label, Color accent, Color textColor)
    {
        Image bg = CreateImage(row, goName, RuntimeSprites.RoundedPanel(), WithAlpha(accent, 0.14f));
        bg.type = Image.Type.Sliced;
        SetRect(bg.rectTransform, new Vector2(0f, -36f), new Vector2(190f, 72f), new Vector2(1f, 1f));
        RuntimeUiKit.AddOutline(bg.rectTransform, WithAlpha(accent, 0.55f));
        bg.raycastTarget = true;
        TextMeshProUGUI text = CreateTmp(bg.transform, "Label", label, 22, textColor,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        AutoSize(text, 14f, 22f);
        Button button = bg.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        return (button, text);
    }

    /// <summary>The all-or-nothing confirm sheet for account deletion. CANCEL is the big
    /// easy target; the destructive button is explicit about permanence. Success rebuilds
    /// the menu over the fresh anonymous account; failure speaks on the sheet and leaves
    /// everything untouched.</summary>
    private static void OpenDeleteAccountConfirm(Color danger)
    {
        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Delete Account Confirm", 5800);

        Image backdrop = CreateImage(overlay.transform, "Backdrop", null, new Color(0f, 0f, 0f, 0.72f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        Button backdropClose = backdrop.gameObject.AddComponent<Button>();
        backdropClose.transition = Selectable.Transition.None;
        backdropClose.onClick.AddListener(() => UnityEngine.Object.Destroy(overlay));

        Image panel = CreateImage(overlay.transform, "Panel", RuntimeSprites.RoundedPanel(),
            new Color(0.075f, 0.065f, 0.058f, 1f));
        panel.type = Image.Type.Sliced;
        SetRect(panel.rectTransform, Vector2.zero, new Vector2(760f, 560f), new Vector2(0.5f, 0.5f));
        panel.raycastTarget = true;
        RuntimeUiKit.AddOutline(panel.rectTransform, WithAlpha(danger, 0.5f));

        TextMeshProUGUI title = CreateTmp(panel.transform, "Title", "DELETE YOUR ACCOUNT?", 34, danger,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -44f), new Vector2(680f, 44f), new Vector2(0.5f, 1f));
        title.characterSpacing = 2f;
        TextMeshProUGUI body = CreateTmp(panel.transform, "Body",
            "This permanently erases your account, progress, scores and coins on every device. " +
            "It cannot be undone.\n\nPurchases can be restored from the app store afterwards.",
            21, WithAlpha(TextPrimary, 0.9f), TextAnchor.UpperLeft, FontStyle.Normal,
            RuntimeUiKit.TitleFont, new Vector2(0f, -112f), new Vector2(640f, 190f), new Vector2(0.5f, 1f));
        body.textWrappingMode = TextWrappingModes.Normal;

        // CANCEL: the big, bright, easy choice.
        Image cancelBg = CreateImage(panel.transform, "Cancel", MenuSprites.RoundedGradient(
            Color.Lerp(GoldBase, Color.white, 0.12f), Color.Lerp(GoldBase, Color.black, 0.22f)), Color.white);
        cancelBg.type = Image.Type.Sliced;
        SetRect(cancelBg.rectTransform, new Vector2(0f, 130f), new Vector2(640f, 96f), new Vector2(0.5f, 0f));
        cancelBg.raycastTarget = true;
        CreateTmp(cancelBg.transform, "Label", "KEEP MY ACCOUNT", 26, new Color(0.10f, 0.08f, 0.03f, 1f),
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button cancel = cancelBg.gameObject.AddComponent<Button>();
        cancel.targetGraphic = cancelBg;
        cancel.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            UnityEngine.Object.Destroy(overlay);
        });

        // The destructive choice: quiet, outlined, explicit.
        Image confirmBg = CreateImage(panel.transform, "Confirm", RuntimeSprites.RoundedPanel(),
            WithAlpha(danger, 0.12f));
        confirmBg.type = Image.Type.Sliced;
        SetRect(confirmBg.rectTransform, new Vector2(0f, 36f), new Vector2(640f, 78f), new Vector2(0.5f, 0f));
        RuntimeUiKit.AddOutline(confirmBg.rectTransform, WithAlpha(danger, 0.6f));
        confirmBg.raycastTarget = true;
        TextMeshProUGUI confirmLabel = CreateTmp(confirmBg.transform, "Label", "DELETE FOREVER", 23, danger,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button confirm = confirmBg.gameObject.AddComponent<Button>();
        confirm.targetGraphic = confirmBg;
        confirm.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            confirm.interactable = false;
            cancel.interactable = false;
            backdropClose.interactable = false;   // no half-cancelled deletions
            confirmLabel.text = "DELETING...";
            OnlineService.DeleteAccount((ok, reason) =>
            {
                if (ok)
                {
                    // Fresh anonymous account is booting; rebuild over the clean slate.
                    UnityEngine.Object.Destroy(overlay);
                    BuildMenu();
                    return;
                }
                if (confirmLabel == null) return;
                confirm.interactable = true;
                cancel.interactable = true;
                backdropClose.interactable = true;
                confirmLabel.text = reason == "offline" ? "OFFLINE - TRY LATER" : "FAILED - TRY AGAIN";
            });
        });
    }

    // Row heights, one place: sized for phone thumbs (Apple 44pt / Material 48dp are MINIMUMS -
    // AAA mobile settings run comfortably past them; the old 44px segments and 66x36 toggle
    // read as desktop-web, Nick 2026-07-29).
    private const float SliderRowH = 176f;
    private const float ToggleRowH = 140f;
    private const float SegmentedRowH = 208f;

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

    /// <summary>A container for a tab's rows, anchored snug under the panel header's divider.
    /// Rows build into it top-down from y = 0. History: top-aligned v1 → centred 2026-07-29
    /// ("sea of empty glass") → back to top-aligned 2026-08-01 (the centred block orphaned the
    /// header and read as floating, which was worse). If the glass below feels too empty again,
    /// fix THAT (hug the panel to its content / bottom watermark), not the row anchor.</summary>
    private static RectTransform NewRowsBlock(RectTransform panel)
    {
        RectTransform block = CreateRect(panel, "Rows",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        block.offsetMin = Vector2.zero;
        block.offsetMax = new Vector2(0f, -SettingsRowsTop);
        return block;
    }

    /// <summary>Right-edge space every row label must keep clear: the widest row control
    /// (190px action button / 104px toggle) plus a margin. Labels STRETCH to this, never a
    /// fixed pixel width - fixed 560px descriptions ran straight under the buttons on
    /// narrow aspects (Nick's screenshot, 2026-07-30; RESPONSIVE.md).</summary>
    private const float RowControlReserve = 216f;

    // Shared left cluster for every row: accent icon + name on one line, muted description below.
    private static void BuildRowLabel(RectTransform row, Func<Color, Sprite> icon, string name, string description, Color accent)
    {
        Image glyph = CreateImage(row, "RowIcon", icon(accent), Color.white);
        glyph.preserveAspect = true;
        SetCenteredAt(glyph.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -25f), new Vector2(36f, 36f));
        TextMeshProUGUI rowName = CreateTmp(row, "RowName", name, 28, TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(52f, -6f), new Vector2(460f, 38f), new Vector2(0f, 1f));
        StretchRowText(rowName, 52f, -6f, 38f);
        TextMeshProUGUI rowDesc = CreateTmp(row, "RowDesc", description, 21, SettingsDescColor, TextAnchor.MiddleLeft, FontStyle.Normal,
            RuntimeUiKit.TitleFont, new Vector2(0f, -52f), new Vector2(560f, 30f), new Vector2(0f, 1f));
        StretchRowText(rowDesc, 0f, -52f, 30f);
    }

    /// <summary>Identity-block variant: stretch from the avatar column (x = 172) to the
    /// row's right edge, ellipsis on overflow.</summary>
    private static void StretchIdentityText(TextMeshProUGUI text, float top, float height)
    {
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.offsetMin = new Vector2(172f, top - height);
        rect.offsetMax = new Vector2(-4f, top);
        // Truncate, not Ellipsis: Archivo Black has no '…' glyph, so Ellipsis just logs a
        // warning and falls back to Truncate anyway.
        text.overflowMode = TextOverflowModes.Truncate;
    }

    /// <summary>Re-anchor a row label to stretch from <paramref name="left"/> to the row's
    /// right edge minus the control reserve, truncating with an ellipsis rather than
    /// running under the control.</summary>
    private static void StretchRowText(TextMeshProUGUI text, float left, float top, float height)
    {
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.offsetMin = new Vector2(left, top - height);
        rect.offsetMax = new Vector2(-RowControlReserve, top);
        text.overflowMode = TextOverflowModes.Truncate;   // see StretchIdentityText
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
        float height = SliderRowH;
        RectTransform row = NewSettingsRow(panel, $"{name}Row", top, height);
        BuildRowLabel(row, icon, name, description, accent);

        TextMeshProUGUI pct = CreateTmp(row, "Pct", $"{Mathf.RoundToInt(value01 * 100f)}%", 27, TextPrimary,
            TextAnchor.MiddleRight, FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, -106f), new Vector2(130f, 40f),
            new Vector2(1f, 1f));

        RectTransform sliderArea = CreateRect(row, "SliderArea", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        sliderArea.offsetMin = new Vector2(0f, -156f);   // band bottom
        sliderArea.offsetMax = new Vector2(-146f, -100f); // band top; right inset leaves room for "%"

        Slider slider = CreateSlider(sliderArea, "Slider", value01, accent, track, v =>
        {
            pct.text = $"{Mathf.RoundToInt(v * 100f)}%";
            onChanged?.Invoke(v);
        }, trackThickness: 18f, handleSize: 54f);
        slider.gameObject.AddComponent<PointerUpProxy>().OnRelease = onCommit;

        AddRowDivider(row);
        return top - height;
    }

    // Label cluster + a pill toggle (RuntimeUiKit primitive) pinned to the row's right edge.
    // Returns next row top.
    private static float BuildToggleRow(RectTransform panel, Func<Color, Sprite> icon, string name, string description,
        bool value, Color accent, float top, UnityEngine.Events.UnityAction<bool> onChanged)
    {
        float height = ToggleRowH;
        RectTransform row = NewSettingsRow(panel, $"{name}Row", top, height);
        BuildRowLabel(row, icon, name, description, accent);
        // 104x56: a real console-style switch, not a web checkbox - the row's one control
        // should look grabbable from arm's length.
        RectTransform pill = CreateRect(row, "Toggle", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -36f), new Vector2(104f, 56f));
        CreatePillToggle(pill, value, accent, onChanged);
        return top - height;
    }

    // Label cluster + a segmented control band (single-choice, e.g. frame rate). Returns next top.
    private static float BuildSegmentedRow(RectTransform panel, Func<Color, Sprite> icon, string name, string description,
        string[] options, int selectedIndex, Color accent, float top, UnityEngine.Events.UnityAction<int> onSelect)
    {
        float height = SegmentedRowH;
        RectTransform row = NewSettingsRow(panel, $"{name}Row", top, height);
        BuildRowLabel(row, icon, name, description, accent);

        // An 84px band: each segment is a full-size button (the 44px v1 was the "way too
        // small" poster child - a frame-rate picker is tapped once ever, but it sets the
        // quality bar for the whole screen).
        RectTransform band = CreateRect(row, "Segments", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        band.offsetMin = new Vector2(0f, -184f);
        band.offsetMax = new Vector2(0f, -100f);
        CreateSegmentedControl(band, options, selectedIndex, accent, onSelect, fontSize: 26);

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

        // Icon and title share one line (matched vertical centres at y = -48).
        Image headerIcon = CreateImage(panel, "PanelIcon", icon(accent), Color.white);
        headerIcon.preserveAspect = true;
        SetCenteredAt(headerIcon.rectTransform, new Vector2(0f, 1f), new Vector2(pad + 22f, -49f), new Vector2(44f, 44f));
        CreateTmp(panel, "PanelTitle", title, 36, TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(pad + 58f, -23f), new Vector2(560f, 52f), new Vector2(0f, 1f));

        // Description on the line below, left-aligned under the icon.
        CreateTmp(panel, "PanelDesc", description, 21, SettingsDescColor, TextAnchor.MiddleLeft, FontStyle.Normal,
            RuntimeUiKit.TitleFont, new Vector2(pad, -90f), new Vector2(580f, 30f), new Vector2(0f, 1f));

        // Hairline under the whole header, stretched to (near) the full panel width.
        RectTransform divider = CreateRect(panel, "PanelDivider",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -128f), new Vector2(0f, 2f));
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
            new Vector2(0f, 20f), new Vector2(380f, 72f));
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
