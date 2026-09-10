using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// The Vault: the player's collection of discovered bricks and abilities. Discovered bricks
// get a baked studio showcase poster and a detail modal with the SAME looping demo the
// in-game debut shows (BLOCKPREVIEWS.md's codex surface); UNDISCOVERED bricks are not
// rendered at all - the collection's size is a secret (Nick 2026-08-30, the Chapters-page
// ambiguity rule): no "7/15", just the finds plus one "undiscovered" teaser at the
// bottom. Abilities keep the bounded collection game: locked entries are silhouettes +
// "???" (the name is part of the reward); discovered ones get a spacious icon-led gallery tile.
// (partial of MainMenuRuntime, split from the main file for readability - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private enum VaultTab { Bricks, Abilities }
    private static VaultTab _activeVaultTab = VaultTab.Bricks;
    private static readonly float[] _vaultScrollPositions = { 1f, 1f };
    private static GalleryScrollRestore _vaultScroll;
    private static VaultTab _vaultScrollTab;

    // Capture before destroying the old tree: Destroy is deferred, and the selected tab
    // may already have changed. A not-yet-restored tree must not overwrite saved state.
    private static void CaptureVaultScrollPosition()
    {
        if (_vaultScroll != null && _vaultScroll.IsRestored)
            _vaultScrollPositions[(int)_vaultScrollTab] =
                Mathf.Clamp01(_vaultScroll.Scroll.verticalNormalizedPosition);
        _vaultScroll = null;
    }

    private const float VaultSideInset = 60f;
    private const float VaultSwitcherY = -342f;
    private const float VaultSwitcherHeight = 80f;
    private const float VaultGridTopInset = 450f;
    private const float VaultGridBottomInset = 220f;
    private const float BrickRowHeight = 360f;
    private const float AbilityRowHeight = 400f;
    private const float SectionRowHeight = 96f;
    private const float CellGap = 12f;

    // ---- screen ------------------------------------------------------------------------------

    private static void BuildVaultScreen(Transform parent, ChapterDefinition chapter)
    {
        Image wash = CreateImage(parent, "GalleryWash", null, new Color(.025f, .04f, .055f, .97f));
        Stretch(wash.rectTransform);
        wash.raycastTarget = false;
        BuildVaultHeader(parent, chapter);
        BuildVaultSwitcher(parent, chapter);
        BuildVaultGrid(parent, chapter);
    }

    private static void BuildVaultHeader(Transform parent, ChapterDefinition chapter)
    {
        var title = CreateTmp(parent, "VaultTitle", "VAULT", 60, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(76f, -196f), new Vector2(420f, 76f), new Vector2(0f, 1f));
        title.characterSpacing = 4f;
        VaultText(parent, "VaultSubtitle", "A collection built along the way.", 25, TextMuted,
            0f, 1f, 78f, -60f, -268f, 42f);
        (int found, int total) = _activeVaultTab == VaultTab.Bricks
            ? BrickCollectionCounts() : AbilityCollectionCounts();
        VaultText(parent, "VaultProgress", _activeVaultTab == VaultTab.Bricks
            ? $"{found} DISCOVERED" : $"{found} / {total} DISCOVERED", 22, MenuAccent,
            .5f, 1f, 0f, -VaultSideInset, -210f, 34f, TextAnchor.MiddleRight);
    }

    // Width-dependent text always stretches; long names wrap inside their own reserved area.
    private static TextMeshProUGUI VaultText(Transform parent, string name, string value, int size,
        Color color, float left, float right, float insetLeft, float insetRight, float top,
        float height, TextAnchor alignment = TextAnchor.UpperLeft)
    {
        var area = CreateRect(parent, name + "Area", new Vector2(left, 1f), new Vector2(right, 1f),
            new Vector2(.5f, 1f), Vector2.zero, Vector2.zero);
        area.offsetMin = new Vector2(insetLeft, top - height);
        area.offsetMax = new Vector2(insetRight, top);
        var text = CreateTmp(area, name, value, size, color, alignment, FontStyle.Normal,
            RuntimeUiKit.DefaultFont);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private static void VaultLine(Transform parent, string name, Color color, float left, float right,
        float y, float thickness = 1f)
    {
        var line = CreateImage(parent, name, null, color);
        var rect = line.rectTransform;
        rect.anchorMin = new Vector2(left, 0f); rect.anchorMax = new Vector2(right, 0f);
        rect.offsetMin = new Vector2(0f, y); rect.offsetMax = new Vector2(0f, y + thickness);
        line.raycastTarget = false;
    }

    private static void BuildVaultSwitcher(Transform parent, ChapterDefinition chapter)
    {
        RectTransform bar = CreateRect(parent, "VaultTabs",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, Vector2.zero);
        bar.offsetMin = new Vector2(VaultSideInset, VaultSwitcherY - VaultSwitcherHeight);
        bar.offsetMax = new Vector2(-VaultSideInset, VaultSwitcherY);
        VaultLine(bar, "TabRule", WithAlpha(TextPrimary, .12f), 0f, 1f, 0f);
        BuildVaultTabHalf(bar, 0, "Bricks", VaultTab.Bricks);
        BuildVaultTabHalf(bar, 1, "Abilities", VaultTab.Abilities);
    }

    private static void BuildVaultTabHalf(RectTransform bar, int index, string label, VaultTab tab)
    {
        RectTransform half = CreateRect(bar, $"Tab{tab}",
            new Vector2(index * 0.5f, 0f), new Vector2((index + 1) * 0.5f, 1f),
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        half.offsetMin = Vector2.zero;
        half.offsetMax = Vector2.zero;

        bool selected = _activeVaultTab == tab;
        Image fill = half.gameObject.AddComponent<Image>();
        fill.color = Color.clear;
        if (selected) VaultLine(half, "Selected", MenuAccent, .12f, .88f, 0f, 3f);

        Button button = half.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() =>
        {
            if (_activeVaultTab == tab) return;
            SfxPlayer.Play("ui-button-click");
            _activeVaultTab = tab;
            BuildMenu();
        });

        CreateTmp(half, "Label", label, 30, selected ? TextPrimary : TextMuted,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
    }

    // ---- the grid ------------------------------------------------------------------------------

    private static void BuildVaultGrid(Transform parent, ChapterDefinition chapter)
    {
        CaptureVaultScrollPosition();
        ScrollRect scroll = BuildGalleryScroll(parent, "Vault", chapter,
            VaultGridTopInset, VaultGridBottomInset);
        RectTransform content = scroll.content;
        if (_activeVaultTab == VaultTab.Bricks) BuildBrickRows(content, chapter);
        else BuildAbilityRows(content, chapter);

        _vaultScrollTab = _activeVaultTab;
        _vaultScroll = scroll.gameObject.AddComponent<GalleryScrollRestore>();
        _vaultScroll.Scroll = scroll;
        _vaultScroll.Position = _vaultScrollPositions[(int)_activeVaultTab];
    }

    /// <summary>A cell anchored to its column fraction inside a stretched row - cell widths track
    /// the real screen width (RESPONSIVE.md; GridLayoutGroup's fixed cellSize would not).</summary>
    private static RectTransform NewGridCell(RectTransform row, int column, int columns)
    {
        RectTransform cell = CreateRect(row, $"Cell{column}",
            new Vector2(column / (float)columns, 0f), new Vector2((column + 1) / (float)columns, 1f),
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        float left = column == 0 ? VaultSideInset : CellGap;
        float right = column == columns - 1 ? VaultSideInset : CellGap;
        cell.offsetMin = new Vector2(left, CellGap);
        cell.offsetMax = new Vector2(-right, -CellGap);
        return cell;
    }

    private static void AddNewBadge(RectTransform cell)
    {
        var text = VaultText(cell, "NewBadge", "• NEW", 19, MenuAccent,
            .5f, 1f, 0f, -18f, -12f, 28f, TextAnchor.MiddleRight);
        text.characterSpacing = 2f;
    }

    // ---- bricks --------------------------------------------------------------------------------

    private static List<BlockData> BrickEntries()
    {
        var entries = new List<BlockData> { ContentCatalog.NormalVariant() }; // may be null; id still "Normal"
        entries.AddRange(ContentCatalog.AllVariants());
        return entries;
    }

    private static bool IsBrickDiscovered(BlockData variant) =>
        ProgressStore.BlockId(variant) == "Normal" || ProgressStore.HasDiscoveredBlock(variant);

    private static (int discovered, int total) BrickCollectionCounts()
    {
        List<BlockData> entries = BrickEntries();
        int found = 0;
        foreach (BlockData entry in entries) if (IsBrickDiscovered(entry)) found++;
        return (found, entries.Count);
    }

    private static void BuildBrickRows(Transform content, ChapterDefinition chapter)
    {
        List<BlockData> entries = BrickEntries();

        (int found, _) = BrickCollectionCounts();
        if (found <= 1) BuildVaultEmptyBanner(content, "Your collection starts here",
            "Special bricks join your collection the first time they drop in play.");

        // One long horizontal card per DISCOVERED brick: thumbnail on the left, name +
        // description on the right (Nick's layout - a single readable column instead of a
        // 2-up grid). Undiscovered bricks aren't rendered (the ambiguity rule, file header);
        // the teaser card below is their only trace.
        foreach (BlockData entry in entries)
        {
            if (!IsBrickDiscovered(entry)) continue;
            RectTransform row = NewGridRow(content, BrickRowHeight);
            RectTransform card = CreateRect(row, "Card",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            card.offsetMin = new Vector2(VaultSideInset, CellGap);
            card.offsetMax = new Vector2(-VaultSideInset, -CellGap);
            BuildBrickCell(card, entry, chapter);
        }

        BuildVaultEmptyBanner(content, "More to discover",
            "Push deeper into the chapters to find new kinds of bricks.");
    }

    // Only ever called for DISCOVERED bricks - undiscovered ones aren't rendered (the
    // ambiguity rule); the "undiscovered" teaser is their only trace.
    private static void BuildBrickCell(RectTransform cell, BlockData variant, ChapterDefinition chapter)
    {
        string id = ProgressStore.BlockId(variant);
        Image hit = cell.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        VaultLine(cell, "Divider", WithAlpha(TextPrimary, .09f), 0f, 1f, 0f);

        var posterArea = CreateRect(cell, "PosterArea", new Vector2(0f, .5f),
            new Vector2(.40f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(-20f, 300f));
        var posterRect = CreateRect(posterArea, "PosterFrame", Vector2.zero, Vector2.one,
            new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        var square = posterRect.gameObject.AddComponent<AspectRatioFitter>();
        square.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        square.aspectRatio = 1f;
        // Keep the real studio render; a single soft frame replaces the nested card chrome.
        MakeRoundedMask(posterRect);
        var poster = CreateRawImage(posterRect, "Poster", null, Color.white);
        Stretch(poster.rectTransform); poster.raycastTarget = false;
        VaultPosterService.Assign(variant, chapter, poster);

        bool hazard = variant != null && variant.IsHazard;
        VaultText(cell, "Category", hazard ? "HAZARD" : "BRICK", 18,
            hazard ? new Color(.93f, .62f, .54f) : MenuAccent,
            .44f, 1f, 0f, -18f, -42f, 26f);
        string display = variant != null ? variant.DisplayName : "Normal";
        var title = VaultText(cell, "Name", display, 36, TextPrimary,
            .44f, 1f, 0f, -18f, -80f, 86f);
        title.font = RuntimeUiKit.TmpTitleFont;
        AutoSize(title, 28f, 36f);
        string summary = variant != null && !string.IsNullOrWhiteSpace(variant.BehaviourSummary)
            ? variant.BehaviourSummary
            : (id == "Normal" ? "The dependable standard brick." : BlockDemoCatalog.Caption(variant));
        var description = VaultText(cell, "Summary", summary, 23, TextMuted,
            .44f, 1f, 0f, -18f, -171f, 94f);
        description.overflowMode = TextOverflowModes.Ellipsis;
        VaultText(cell, "Action", "Take a closer look  ›", 21, MenuAccent,
            .44f, 1f, 0f, -18f, -284f, 32f);

        var button = cell.gameObject.AddComponent<Button>();
        button.targetGraphic = poster;
        button.onClick.AddListener(() => OpenBrickDetail(variant, chapter));
        if (!ProgressStore.HasInspectedInVault(id)) AddNewBadge(cell);
    }

    // ---- abilities -----------------------------------------------------------------------------

    private static (int discovered, int total) AbilityCollectionCounts()
    {
        List<AbilityDefinition> all = ContentCatalog.AllAbilities();
        int found = 0;
        foreach (AbilityDefinition ability in all) if (ProgressStore.HasSeenAbility(ability)) found++;
        return (found, all.Count);
    }

    private static void BuildAbilityRows(Transform content, ChapterDefinition chapter)
    {
        List<AbilityDefinition> all = ContentCatalog.AllAbilities();

        (int found, _) = AbilityCollectionCounts();
        if (found == 0) BuildVaultEmptyBanner(content, "A little power goes a long way",
            "Every ability shown in an offer joins your collection - picked or not.");

        // Rarity sections, derived from the catalog's rarity-then-name order: emit a header row
        // whenever the rarity changes, then fill 2-column rows within the section.
        const int columns = 2;
        int i = 0;
        while (i < all.Count)
        {
            AbilityRarity rarity = all[i].Rarity;
            int sectionEnd = i;
            int sectionFound = 0;
            while (sectionEnd < all.Count && all[sectionEnd].Rarity == rarity)
            {
                if (ProgressStore.HasSeenAbility(all[sectionEnd])) sectionFound++;
                sectionEnd++;
            }

            BuildAbilitySectionHeader(content, rarity, sectionFound, sectionEnd - i);

            for (int start = i; start < sectionEnd; start += columns)
            {
                RectTransform row = NewGridRow(content, AbilityRowHeight);
                for (int column = 0; column < columns && start + column < sectionEnd; column++)
                {
                    BuildAbilityCell(NewGridCell(row, column, columns), all[start + column]);
                }
            }
            i = sectionEnd;
        }
    }

    private static void BuildAbilitySectionHeader(Transform content, AbilityRarity rarity, int found, int total)
    {
        var row = NewGridRow(content, SectionRowHeight);
        Color color = AbilityRarityInfo.GetColor(rarity);
        VaultText(row, "Label", rarity.ToString().ToUpperInvariant(), 22,
            Color.Lerp(color, TextPrimary, .35f), 0f, .7f, VaultSideInset, 0f, -34f, 32f);
        VaultText(row, "Count", $"{found} / {total}", 21, TextMuted,
            .7f, 1f, 0f, -VaultSideInset, -34f, 32f, TextAnchor.MiddleRight);
    }

    private static void BuildAbilityCell(RectTransform cell, AbilityDefinition ability)
    {
        bool discovered = ProgressStore.HasSeenAbility(ability);
        Color accent = discovered ? AbilityRarityInfo.GetColor(ability.Rarity) : TextMuted;
        Image surface = cell.gameObject.AddComponent<Image>();
        surface.sprite = RuntimeSprites.RoundedPanel(); surface.type = Image.Type.Sliced;
        surface.color = discovered ? new Color(.065f, .08f, .095f, 1f) : new Color(.045f, .055f, .065f, 1f);
        surface.raycastTarget = discovered;
        VaultLine(cell, "RarityAccent", WithAlpha(accent, discovered ? .65f : .15f), .42f, .58f, 22f, 2f);
        var icon = CreateImage(cell, "Icon", ability.Icon != null ? ability.Icon : RuntimeSprites.AbilityGlyph(),
            discovered ? Color.white : new Color(.13f, .15f, .17f, 1f));
        SetRect(icon.rectTransform, new Vector2(0f, -44f), new Vector2(180f, 180f), new Vector2(.5f, 1f));
        icon.preserveAspect = true; icon.raycastTarget = false;
        var title = VaultText(cell, "Name", discovered ? ability.DisplayName : "???", 30,
            discovered ? TextPrimary : TextMuted, 0f, 1f, 20f, -20f, -234f, 76f, TextAnchor.MiddleCenter);
        title.font = RuntimeUiKit.TmpTitleFont;
        AutoSize(title, 23f, 30f);
        VaultText(cell, "Type", discovered ? AbilityTypeInfo.GetLabel(ability.Type) : "UNDISCOVERED", 18,
            discovered ? Color.Lerp(AbilityTypeInfo.GetColor(ability.Type), TextPrimary, .55f) : TextMuted,
            0f, 1f, 20f, -20f, -319f, 28f, TextAnchor.MiddleCenter);
        if (!discovered) return;
        var button = cell.gameObject.AddComponent<Button>(); button.targetGraphic = surface;
        button.onClick.AddListener(() => OpenAbilityDetail(ability));
        if (!ProgressStore.HasInspectedInVault(ability.name)) AddNewBadge(cell);
    }

    private static void BuildVaultEmptyBanner(Transform content, string title, string body)
    {
        var row = NewGridRow(content, 200f);
        VaultText(row, "Title", title, 30, TextPrimary,
            0f, 1f, VaultSideInset + 16f, -VaultSideInset, -30f, 46f);
        VaultText(row, "Body", body, 24, TextMuted,
            0f, 1f, VaultSideInset + 16f, -VaultSideInset, -88f, 90f);
    }

    // ---- detail modals ---------------------------------------------------------------------------

    private static GameObject CreateVaultDetailOverlay(ChapterDefinition chapter, out System.Action close)
    {
        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Vault Detail", 5600);
        GameObject captured = overlay;
        close = () => { if (captured != null) UnityEngine.Object.Destroy(captured); };

        Sprite backdropSprite = chapter != null ? chapter.MenuBackgroundImage : null;
        Image backdrop = CreateImage(overlay.transform, "Backdrop", null,
            new Color(0.02f, 0.02f, 0.03f, backdropSprite != null ? 0.82f : 0.92f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        return overlay;
    }

    private static void AddDetailClose(RectTransform panel, System.Action close)
    {
        Button closeButton = CreateRect(panel, "Close",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-16f, -16f), new Vector2(64f, 64f)).gameObject.AddComponent<Button>();
        Image closeImage = closeButton.gameObject.AddComponent<Image>();
        closeImage.sprite = MenuSprites.CircleBadge(WithAlpha(TextPrimary, 0.12f), WithAlpha(TextPrimary, 0.7f));
        closeButton.targetGraphic = closeImage;
        closeButton.transition = Selectable.Transition.None;
        closeButton.onClick.AddListener(() => close());
        TextMeshProUGUI x = CreateTmp(closeButton.transform, "X", "×", 38, TextPrimary,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        x.raycastTarget = false;
    }

    private static void OpenBrickDetail(BlockData variant, ChapterDefinition chapter)
    {
        SfxPlayer.Play("ui-button-click");
        string id = ProgressStore.BlockId(variant);
        ProgressStore.MarkInspectedInVault(id);

        GameObject overlay = CreateVaultDetailOverlay(chapter, out System.Action destroyOverlay);

        // The menu idles at timeScale 0, which freezes the demo's scaled-time skins and physics
        // pacing - run scaled time while the modal is up (no run exists behind the fullscreen
        // menu; the same window the poster service uses) and restore on close.
        float previousTimeScale = Time.timeScale;
        Time.timeScale = 1f;
        // A variant without a scenario (the Normal brick) shows its static pose instead of an
        // empty looping diorama.
        bool hasDemo = BlockDemoCatalog.HasDemo(variant);
        BlockDemoStage stage = hasDemo
            ? BlockDemoStage.Open(variant, chapter, 728, 546)
            : BlockDemoStage.OpenPose(variant, chapter, 546);

        bool closed = false;
        void Close()
        {
            if (closed) return;
            closed = true;
            stage.Close();
            if (LevelSelectionState.IsSelectionPending) Time.timeScale = previousTimeScale;
            destroyOverlay();
            BuildMenu(); // refresh the grid (the NEW badge just cleared)
        }

        Button backdropButton = overlay.transform.Find("Backdrop").gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(Close);

        RectTransform panel = CreateRect(overlay.transform, "Panel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(880f, 1320f));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        GameMenuStyle.StylePanel(panel.gameObject, MenuPresentationChapter);
        ModalSafeFrame.Attach(panel); // the one modal-panel treatment
        panelImage.raycastTarget = true;

        // The live looping demo across the top.
        var demoHolder = new GameObject("DemoFrame", typeof(RectTransform));
        RectTransform demoRect = (RectTransform)demoHolder.transform;
        demoRect.SetParent(panel, false);
        demoRect.anchorMin = new Vector2(0f, 1f);
        demoRect.anchorMax = new Vector2(1f, 1f);
        demoRect.pivot = new Vector2(0.5f, 1f);
        demoRect.offsetMin = new Vector2(32f, -792f);
        demoRect.offsetMax = new Vector2(-32f, -180f);
        MakeRoundedMask(demoRect);
        RawImage demo = CreateRawImage(demoRect, "Demo", stage.Texture, Color.white);
        Stretch(demo.rectTransform);
        demo.raycastTarget = false;
        if (!hasDemo)
        {
            // Keep the complete square pose visible within the wide demonstration area.
            AspectRatioFitter fit = demo.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1f;
        }

        VaultText(panel, "CollectionLabel", "THE COLLECTION  /  BRICKS", 19, MenuAccent,
            0f, 1f, 44f, -100f, -40f, 28f);
        string display = variant != null ? variant.DisplayName : "Normal";
        var nameText = CreateTmp(panel, "Name", display, 48, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(44f, -87f), new Vector2(704f, 74f), new Vector2(0f, 1f));
        AutoSize(nameText, 34f, 48f);

        string summary = variant != null && !string.IsNullOrWhiteSpace(variant.BehaviourSummary)
            ? variant.BehaviourSummary : BlockDemoCatalog.Caption(variant);
        var summaryText = CreateTmp(panel, "Summary", summary, 24, ChapterLight(chapter),
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(44f, -834f), new Vector2(792f, 74f), new Vector2(0f, 1f));

        string body = variant != null && !string.IsNullOrWhiteSpace(variant.VaultDescription)
            ? variant.VaultDescription : BlockDemoCatalog.Caption(variant);
        TextMeshProUGUI bodyText = CreateTmp(panel, "Body", body, 26,
            new Color(0.85f, 0.88f, 0.9f, 1f), TextAnchor.UpperLeft, FontStyle.Normal,
            RuntimeUiKit.DefaultFont, new Vector2(44f, -934f), new Vector2(792f, 194f), new Vector2(0f, 1f));
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        summaryText.textWrappingMode = TextWrappingModes.Normal;
        float summaryHeight = Mathf.Max(38f, summaryText.GetPreferredValues(summary, 792f, 0f).y);
        summaryText.rectTransform.sizeDelta = new Vector2(792f, summaryHeight);
        float bodyTop = 834f + summaryHeight + 28f;
        float bodyHeight = Mathf.Max(60f, bodyText.GetPreferredValues(body, 792f, 0f).y);
        bodyText.rectTransform.anchoredPosition = new Vector2(44f, -bodyTop);
        bodyText.rectTransform.sizeDelta = new Vector2(792f, bodyHeight);
        panel.sizeDelta = new Vector2(880f, Mathf.Max(1260f, bodyTop + bodyHeight + 212f));

        // Derived stat tiles - read from the real fields, so they can never drift from gameplay.
        BuildBrickStatTiles(panel, variant);

        AddDetailClose(panel, Close);
        UiEntranceFx.Play(panel.gameObject);
    }

    private static void BuildBrickStatTiles(RectTransform panel, BlockData variant)
    {
        float mass = variant != null ? variant.Mass : 1f;
        bool canRotate = variant == null || variant.CanRotate;
        bool hazard = variant != null && variant.IsHazard;
        bool inverted = variant != null && variant.InvertHorizontalControls;

        var tiles = new List<(string label, string value)>
        {
            ("MASS", $"×{mass:0.##}"),
            ("ROTATION", canRotate ? "FREE" : "LOCKED"),
            (hazard ? "THREAT" : "NATURE", hazard ? "HAZARD" : "HARMLESS"),
        };
        if (inverted) tiles[2] = ("STEERING", "REVERSED");

        for (int i = 0; i < tiles.Count; i++)
        {
            RectTransform tile = CreateRect(panel, $"Stat{i}",
                new Vector2(i / 3f, 0f), new Vector2((i + 1) / 3f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, Vector2.zero);
            tile.offsetMin = new Vector2(i == 0 ? 44f : 10f, 70f);
            tile.offsetMax = new Vector2(i == 2 ? -44f : -10f, 170f);
            VaultLine(tile, "Rule", WithAlpha(TextPrimary, .16f), .08f, .92f, 98f);

            CreateTmp(tile, "Label", tiles[i].label, 18, TextMuted, TextAnchor.MiddleCenter,
                FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(0f, -14f), new Vector2(200f, 24f), new Vector2(0.5f, 1f));
            CreateTmp(tile, "Value", tiles[i].value, 27, TextPrimary, TextAnchor.MiddleCenter,
                FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(0f, -48f), new Vector2(220f, 36f), new Vector2(0.5f, 1f));
        }
    }

    private static void OpenAbilityDetail(AbilityDefinition ability)
    {
        SfxPlayer.Play("ui-button-click");
        ProgressStore.MarkInspectedInVault(ability.name);

        ChapterDefinition chapter = _chapters.Length > 0 ? _chapters[_chapterIndex] : null;
        GameObject overlay = CreateVaultDetailOverlay(chapter, out System.Action destroyOverlay);

        void Close()
        {
            destroyOverlay();
            BuildMenu(); // refresh the grid (the NEW badge just cleared)
        }

        Button backdropButton = overlay.transform.Find("Backdrop").gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(Close);

        // A large icon, title, plain type label and one description. Height follows the copy.
        RectTransform panel = CreateRect(overlay.transform, "Panel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(680f, 660f)); // height finalized below
        Image panelImage = panel.gameObject.AddComponent<Image>();
        GameMenuStyle.StylePanel(panel.gameObject, MenuPresentationChapter);
        ModalSafeFrame.Attach(panel); // the one modal-panel treatment
        panelImage.raycastTarget = true;

        // The hero uses the actual ability icon and the shared entrance motion.
        AbilityCardView.AddIconHero(panel, ability, iconTop: -42f, iconSize: 228f);

        TextMeshProUGUI title = CreateTmp(panel, "Title", ability.DisplayName, 42,
            TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -296f), new Vector2(600f, 54f), new Vector2(0.5f, 1f));
        title.font = RuntimeUiKit.TmpTitleFont;
        title.characterSpacing = 0f;
        AutoSize(title, 26f, 42f);

        VaultText(panel, "Type", AbilityTypeInfo.GetLabel(ability.Type), 20,
            Color.Lerp(AbilityTypeInfo.GetColor(ability.Type), TextPrimary, .55f),
            0f, 1f, 50f, -50f, -370f, 32f, TextAnchor.MiddleCenter);

        const float DescTop = 440f;
        TextMeshProUGUI bodyText = CreateTmp(panel, "Body", ability.LongDescription, 25,
            new Color(0.85f, 0.88f, 0.9f, 1f), TextAnchor.UpperCenter, FontStyle.Normal,
            RuntimeUiKit.DefaultFont, new Vector2(0f, -DescTop), new Vector2(580f, 216f), new Vector2(0.5f, 1f));
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        float descH = Mathf.Clamp(bodyText.GetPreferredValues(ability.LongDescription, 580f, 0f).y, 36f, 320f);
        bodyText.rectTransform.sizeDelta = new Vector2(580f, descH);
        panel.sizeDelta = new Vector2(680f, DescTop + descH + 90f);

        AddDetailClose(panel, Close);
        UiEntranceFx.Play(panel.gameObject);
    }
}
