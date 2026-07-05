using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// The Vault: the player's collection of discovered bricks and abilities. Locked entries are
// silhouettes + "???" (the name is part of the reward); discovered bricks get a live-rendered
// showcase poster and a detail modal with the SAME looping demo the in-game debut shows
// (BLOCKPREVIEWS.md's codex surface); discovered abilities get their real glass-slab card.
// (partial of MainMenuRuntime, split from the main file for readability - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private enum VaultTab { Bricks, Abilities }
    private static VaultTab _activeVaultTab = VaultTab.Bricks;

    private const float VaultSideInset = 60f;
    private const float VaultSwitcherY = -302f;
    private const float VaultSwitcherHeight = 92f;
    private const float VaultGridTopInset = 430f;
    private const float VaultGridBottomInset = 220f;
    private const float BrickRowHeight = 268f;
    private const float AbilityRowHeight = 470f;
    private const float SectionRowHeight = 92f;
    private const float CellGap = 12f;

    // ---- screen ------------------------------------------------------------------------------

    private static void BuildVaultScreen(Transform parent, ChapterDefinition chapter)
    {
        BuildVaultHeader(parent, chapter);
        BuildVaultSwitcher(parent, chapter);
        BuildVaultGrid(parent, chapter);
    }

    private static void BuildVaultHeader(Transform parent, ChapterDefinition chapter)
    {
        TextMeshProUGUI title = CreateTmp(parent, "VaultTitle", "VAULT", 60, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(76f, -196f), new Vector2(420f, 76f), new Vector2(0f, 1f));
        title.characterSpacing = 4f;

        (int discovered, int total) = _activeVaultTab == VaultTab.Bricks
            ? BrickCollectionCounts()
            : AbilityCollectionCounts();

        CreateTmp(parent, "VaultProgress", $"{discovered} / {total} DISCOVERED", 24,
            GoldBase, TextAnchor.MiddleRight, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(-VaultSideInset, -206f), new Vector2(420f, 34f), new Vector2(1f, 1f));

        // Thin capsule progress bar under the counter.
        RectTransform track = CreateRect(parent, "ProgressTrack",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-VaultSideInset, -246f), new Vector2(300f, 8f));
        Image trackImage = track.gameObject.AddComponent<Image>();
        trackImage.sprite = RuntimeSprites.RoundedPanel();
        trackImage.type = Image.Type.Sliced;
        trackImage.pixelsPerUnitMultiplier = 6f;
        trackImage.color = WithAlpha(TextPrimary, 0.14f);
        trackImage.raycastTarget = false;

        float fraction = total > 0 ? (float)discovered / total : 0f;
        if (fraction > 0f)
        {
            RectTransform fill = CreateRect(track, "Fill",
                new Vector2(0f, 0f), new Vector2(fraction, 1f), new Vector2(0f, 0.5f),
                Vector2.zero, Vector2.zero);
            Image fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = RuntimeSprites.RoundedPanel();
            fillImage.type = Image.Type.Sliced;
            fillImage.pixelsPerUnitMultiplier = 6f;
            fillImage.color = GoldBase;
            fillImage.raycastTarget = false;
        }
    }

    private static void BuildVaultSwitcher(Transform parent, ChapterDefinition chapter)
    {
        RectTransform bar = CreateRect(parent, "VaultTabs",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, Vector2.zero);
        bar.offsetMin = new Vector2(VaultSideInset, VaultSwitcherY - VaultSwitcherHeight);
        bar.offsetMax = new Vector2(-VaultSideInset, VaultSwitcherY);
        Image barImage = bar.gameObject.AddComponent<Image>();
        barImage.sprite = RuntimeSprites.RoundedPanel();
        barImage.type = Image.Type.Sliced;
        barImage.color = MenuGlassFill(chapter, 0.55f);
        RuntimeUiKit.AddOutline(bar, GlassBorder);

        (int bricksFound, int bricksTotal) = BrickCollectionCounts();
        (int abilitiesFound, int abilitiesTotal) = AbilityCollectionCounts();
        BuildVaultTabHalf(bar, 0, $"BRICKS  {bricksFound}/{bricksTotal}", VaultTab.Bricks);
        BuildVaultTabHalf(bar, 1, $"ABILITIES  {abilitiesFound}/{abilitiesTotal}", VaultTab.Abilities);
    }

    private static void BuildVaultTabHalf(RectTransform bar, int index, string label, VaultTab tab)
    {
        RectTransform half = CreateRect(bar, $"Tab{tab}",
            new Vector2(index * 0.5f, 0f), new Vector2((index + 1) * 0.5f, 1f),
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        half.offsetMin = new Vector2(8f, 8f);
        half.offsetMax = new Vector2(-8f, -8f);

        bool selected = _activeVaultTab == tab;
        Image fill = half.gameObject.AddComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = selected ? WithAlpha(GoldBase, 0.16f) : Color.clear;
        if (selected) RuntimeUiKit.AddOutline(half, WithAlpha(GoldBase, 0.55f));

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

        CreateTmp(half, "Label", label, 24, selected ? TextPrimary : TextMuted,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
    }

    // ---- the grid ------------------------------------------------------------------------------

    private static void BuildVaultGrid(Transform parent, ChapterDefinition chapter)
    {
        // The level list's exact scroll stack: masked viewport + layout content + clamped
        // DirectionalScrollRect + a thin auto-hiding scrollbar in the right gutter.
        RectTransform viewport = CreateRect(parent, "VaultViewport",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.offsetMin = new Vector2(0f, VaultGridBottomInset);
        viewport.offsetMax = new Vector2(0f, -VaultGridTopInset);
        Image viewportHit = viewport.gameObject.AddComponent<Image>();
        viewportHit.color = Color.clear;
        viewportHit.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>().padding = new Vector4(0f, -12f, 0f, -12f);

        RectTransform content = CreateRect(viewport, "VaultContent",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, Vector2.zero);
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 0f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.gameObject.AddComponent<DirectionalScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 34f;

        RectTransform sbar = CreateRect(parent, "VaultScrollbar",
            new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
        sbar.offsetMin = new Vector2(-42f, VaultGridBottomInset + 12f);
        sbar.offsetMax = new Vector2(-34f, -(VaultGridTopInset + 12f));
        Image track = sbar.gameObject.AddComponent<Image>();
        track.sprite = RuntimeSprites.RoundedPanel();
        track.type = Image.Type.Sliced;
        track.pixelsPerUnitMultiplier = 6f;
        track.color = WithAlpha(TextPrimary, 0.10f);
        track.raycastTarget = false;
        Scrollbar scrollbar = sbar.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        RectTransform handle = CreateRect(sbar, "Handle",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.sprite = RuntimeSprites.RoundedPanel();
        handleImage.type = Image.Type.Sliced;
        handleImage.pixelsPerUnitMultiplier = 6f;
        handleImage.color = WithAlpha(ChapterLight(chapter), 0.55f);
        handleImage.raycastTarget = false;
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        if (_activeVaultTab == VaultTab.Bricks) BuildBrickRows(content, chapter);
        else BuildAbilityRows(content, chapter);
    }

    private static RectTransform NewGridRow(Transform content, float height)
    {
        RectTransform row = CreateRect(content, "Row",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, height));
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return row;
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
        RectTransform badge = CreateRect(cell, "NewBadge",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(6f, 10f), new Vector2(86f, 40f));
        Image pill = badge.gameObject.AddComponent<Image>();
        pill.sprite = RuntimeSprites.RoundedPanel();
        pill.type = Image.Type.Sliced;
        pill.pixelsPerUnitMultiplier = 3f;
        pill.color = GoldBase;
        pill.raycastTarget = false;
        TextMeshProUGUI text = CreateTmp(badge, "Text", "NEW", 18,
            new Color(0.12f, 0.1f, 0.05f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont);
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

        (int found, int total) = BrickCollectionCounts();
        if (found <= 1) BuildVaultEmptyBanner(content, "YOUR VAULT AWAITS",
            "Special bricks join your collection the first time they drop in play.");

        // One long horizontal card per brick: thumbnail on the left, name + description on the
        // right (Nick's layout - a single readable column instead of a 2-up grid).
        foreach (BlockData entry in entries)
        {
            RectTransform row = NewGridRow(content, BrickRowHeight);
            RectTransform card = CreateRect(row, "Card",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            card.offsetMin = new Vector2(VaultSideInset, CellGap);
            card.offsetMax = new Vector2(-VaultSideInset, -CellGap);
            BuildBrickCell(card, entry, chapter);
        }
    }

    private static void BuildBrickCell(RectTransform cell, BlockData variant, ChapterDefinition chapter)
    {
        bool discovered = IsBrickDiscovered(variant);
        string id = ProgressStore.BlockId(variant);

        Image plate = cell.gameObject.AddComponent<Image>();
        plate.sprite = RuntimeSprites.RoundedPanel();
        plate.type = Image.Type.Sliced;
        plate.color = discovered ? CardDark : WithAlpha(CardDark, 0.92f);
        RuntimeUiKit.AddOutline(cell, discovered ? GlassBorder : WithAlpha(GlassBorder, 0.5f));

        // The square thumbnail zone fills the card's left end (card height minus padding), the
        // text block takes the rest.
        float thumb = BrickRowHeight - CellGap * 2f - 28f; // = 216 at reference
        float textLeft = 14f + thumb + 26f;

        if (!discovered)
        {
            // Silhouette + ??? + how-to-unlock hint, in the same left/right layout. Inert.
            Image shape = CreateImage(cell, "Silhouette", RuntimeSprites.RoundedPanel(),
                new Color(0.03f, 0.03f, 0.035f, 1f));
            shape.type = Image.Type.Sliced;
            SetCenteredAt(shape.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(14f + thumb * 0.5f, 0f), new Vector2(thumb * 0.72f, thumb * 0.72f));
            Image lockIcon = CreateImage(cell, "Lock", MenuSprites.Lock(LockedColor), Color.white);
            lockIcon.preserveAspect = true;
            SetCenteredAt(lockIcon.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(14f + thumb * 0.5f + 58f, -58f), new Vector2(46f, 46f));

            CreateTmp(cell, "Name", "???", 34, LockedColor, TextAnchor.MiddleLeft, FontStyle.Bold,
                RuntimeUiKit.TitleFont, new Vector2(textLeft, 26f), new Vector2(220f, 44f), new Vector2(0f, 0.5f));
            CreateTmp(cell, "Hint", "DISCOVER IN PLAY", 16, WithAlpha(TextMuted, 0.8f),
                TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(textLeft, -22f), new Vector2(320f, 26f), new Vector2(0f, 0.5f));
            return;
        }

        // Left: the square live-rendered showcase (the T brick in its real skin), rounded.
        var posterHolder = new GameObject("PosterFrame", typeof(RectTransform));
        RectTransform posterRect = (RectTransform)posterHolder.transform;
        posterRect.SetParent(cell, false);
        posterRect.anchorMin = new Vector2(0f, 0.5f);
        posterRect.anchorMax = new Vector2(0f, 0.5f);
        posterRect.pivot = new Vector2(0f, 0.5f);
        posterRect.anchoredPosition = new Vector2(14f, 0f);
        posterRect.sizeDelta = new Vector2(thumb, thumb);
        MakeRoundedMask(posterRect);
        RawImage poster = CreateRawImage(posterRect, "Poster", null, WithAlpha(Color.black, 0.35f));
        Stretch(poster.rectTransform);
        poster.raycastTarget = false;
        VaultPosterService.Assign(variant, chapter, poster);

        // Right: name, hazard chip beside it when applicable, then the summary line(s).
        string display = variant != null ? variant.DisplayName : "Normal";
        CreateTmp(cell, "Name", display.ToUpperInvariant(), 32, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(textLeft, 62f), new Vector2(380f, 42f), new Vector2(0f, 0.5f));

        if (variant != null && variant.IsHazard)
        {
            RectTransform chip = CreateRect(cell, "HazardChip",
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-22f, 62f), new Vector2(112f, 34f));
            Image chipImage = chip.gameObject.AddComponent<Image>();
            chipImage.sprite = RuntimeSprites.RoundedPanel();
            chipImage.type = Image.Type.Sliced;
            chipImage.pixelsPerUnitMultiplier = 3f;
            chipImage.color = new Color(0.5f, 0.14f, 0.12f, 0.9f);
            chipImage.raycastTarget = false;
            CreateTmp(chip, "Text", "HAZARD", 16, new Color(1f, 0.82f, 0.78f, 1f),
                TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        }

        string summary = variant != null && !string.IsNullOrWhiteSpace(variant.BehaviourSummary)
            ? variant.BehaviourSummary
            : (id == "Normal" ? "The dependable standard brick" : BlockDemoCatalog.Caption(variant));
        RectTransform summaryRect = CreateRect(cell, "SummaryArea",
            new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(0f, 1f),
            Vector2.zero, Vector2.zero);
        summaryRect.offsetMin = new Vector2(textLeft, 12f);
        summaryRect.offsetMax = new Vector2(-22f, 34f);
        TextMeshProUGUI line = CreateTmp(summaryRect, "Summary", summary, 20, TextMuted,
            TextAnchor.UpperLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont);
        line.textWrappingMode = TextWrappingModes.Normal;
        line.overflowMode = TextOverflowModes.Ellipsis;

        Button button = cell.gameObject.AddComponent<Button>();
        button.targetGraphic = plate;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
        BlockData captured = variant;
        button.onClick.AddListener(() => OpenBrickDetail(captured, chapter));

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
        if (found == 0) BuildVaultEmptyBanner(content, "NO ABILITIES SEEN YET",
            "Every ability shown in an offer joins your collection - picked or not.");

        // Rarity sections, derived from the catalog's rarity-then-name order: emit a header row
        // whenever the rarity changes, then fill 3-column rows within the section.
        const int columns = 3;
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
        RectTransform row = NewGridRow(content, SectionRowHeight);
        Color color = AbilityRarityInfo.GetColor(rarity);

        CreateTmp(row, "Label", $"{rarity.ToString().ToUpperInvariant()}  —  {found} / {total}", 26,
            Color.Lerp(color, TextPrimary, 0.25f), TextAnchor.MiddleLeft, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(VaultSideInset, 0f), new Vector2(520f, 40f), new Vector2(0f, 0.5f));

        Image bar = CreateImage(row, "Bar", RuntimeSprites.SoftHorizontalBar(0.1f), WithAlpha(color, 0.4f));
        RectTransform barRect = bar.rectTransform;
        barRect.anchorMin = new Vector2(0.55f, 0.5f);
        barRect.anchorMax = new Vector2(1f, 0.5f);
        barRect.offsetMin = new Vector2(0f, -2f);
        barRect.offsetMax = new Vector2(-VaultSideInset, 2f);
        bar.raycastTarget = false;
    }

    private static void BuildAbilityCell(RectTransform cell, AbilityDefinition ability)
    {
        bool discovered = ProgressStore.HasSeenAbility(ability);
        GameObject card = AbilityCardView.CreateCollectionCard(cell, ability, discovered, large: false);

        if (!discovered) return;

        // The whole card opens the detail view (grid cards carry no DETAILS sub-button).
        Image hit = cell.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Button button = cell.gameObject.AddComponent<Button>();
        button.targetGraphic = card.GetComponent<Image>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
        AbilityDefinition captured = ability;
        button.onClick.AddListener(() => OpenAbilityDetail(captured));

        if (!ProgressStore.HasInspectedInVault(ability.name)) AddNewBadge(cell);
    }

    private static void BuildVaultEmptyBanner(Transform content, string title, string body)
    {
        RectTransform row = NewGridRow(content, 190f);
        RectTransform banner = CreateRect(row, "Banner",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        banner.offsetMin = new Vector2(VaultSideInset, 14f);
        banner.offsetMax = new Vector2(-VaultSideInset, -14f);
        Image image = banner.gameObject.AddComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
        image.color = WithAlpha(CardDark, 0.9f);
        RuntimeUiKit.AddOutline(banner, GoldOutline(0.4f));

        CreateTmp(banner, "Title", title, 32, GoldBase, TextAnchor.MiddleCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(0f, -46f), new Vector2(700f, 44f), new Vector2(0.5f, 1f));
        CreateTmp(banner, "Body", body, 22, TextMuted, TextAnchor.MiddleCenter, FontStyle.Normal,
            RuntimeUiKit.DefaultFont, new Vector2(0f, -102f), new Vector2(760f, 40f), new Vector2(0.5f, 1f));
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
            Vector2.zero, new Vector2(880f, 1240f));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = RuntimeSprites.RoundedPanel();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color(0.075f, 0.065f, 0.058f, 1f);
        panelImage.raycastTarget = true;
        RuntimeUiKit.AddOutline(panel, GoldOutline(0.22f));

        // The live looping demo across the top.
        var demoHolder = new GameObject("DemoFrame", typeof(RectTransform));
        RectTransform demoRect = (RectTransform)demoHolder.transform;
        demoRect.SetParent(panel, false);
        demoRect.anchorMin = new Vector2(0f, 1f);
        demoRect.anchorMax = new Vector2(1f, 1f);
        demoRect.pivot = new Vector2(0.5f, 1f);
        demoRect.offsetMin = new Vector2(20f, -646f);
        demoRect.offsetMax = new Vector2(-20f, -20f);
        MakeRoundedMask(demoRect);
        RawImage demo = CreateRawImage(demoRect, "Demo", stage.Texture, Color.white);
        Stretch(demo.rectTransform);
        demo.raycastTarget = false;
        if (!hasDemo)
        {
            // Square pose texture in a wide holder: envelope + crop, never stretch.
            AspectRatioFitter fit = demo.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = 1f;
        }

        string display = variant != null ? variant.DisplayName : "Normal";
        CreateTmp(panel, "Name", display.ToUpperInvariant(), 52, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(44f, -700f), new Vector2(600f, 64f), new Vector2(0f, 1f));

        string summary = variant != null && !string.IsNullOrWhiteSpace(variant.BehaviourSummary)
            ? variant.BehaviourSummary : BlockDemoCatalog.Caption(variant);
        CreateTmp(panel, "Summary", summary, 24, ChapterLight(chapter),
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(44f, -768f), new Vector2(792f, 34f), new Vector2(0f, 1f));

        string body = variant != null && !string.IsNullOrWhiteSpace(variant.VaultDescription)
            ? variant.VaultDescription : BlockDemoCatalog.Caption(variant);
        TextMeshProUGUI bodyText = CreateTmp(panel, "Body", body, 26,
            new Color(0.85f, 0.88f, 0.9f, 1f), TextAnchor.UpperLeft, FontStyle.Normal,
            RuntimeUiKit.DefaultFont, new Vector2(44f, -820f), new Vector2(792f, 250f), new Vector2(0f, 1f));
        bodyText.textWrappingMode = TextWrappingModes.Normal;

        // Derived stat tiles - read from the real fields, so they can never drift from gameplay.
        BuildBrickStatTiles(panel, variant);

        AddDetailClose(panel, Close);
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
            tile.offsetMin = new Vector2(i == 0 ? 44f : 10f, 36f);
            tile.offsetMax = new Vector2(i == 2 ? -44f : -10f, 136f);
            Image tileImage = tile.gameObject.AddComponent<Image>();
            tileImage.sprite = RuntimeSprites.RoundedPanel();
            tileImage.type = Image.Type.Sliced;
            tileImage.color = new Color(0.11f, 0.1f, 0.09f, 1f);
            tileImage.raycastTarget = false;

            CreateTmp(tile, "Label", tiles[i].label, 17, TextMuted, TextAnchor.MiddleCenter,
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

        RectTransform panel = CreateRect(overlay.transform, "Panel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(880f, 1160f));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = RuntimeSprites.RoundedPanel();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color(0.075f, 0.065f, 0.058f, 1f);
        panelImage.raycastTarget = true;
        RuntimeUiKit.AddOutline(panel, GoldOutline(0.22f));

        // The glass-slab card, large (rarity chrome + type chip + short description built in).
        RectTransform cardHolder = CreateRect(panel, "CardHolder",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -36f), new Vector2(470f, 640f));
        AbilityCardView.CreateCollectionCard(cardHolder, ability, discovered: true, large: true);

        TextMeshProUGUI bodyText = CreateTmp(panel, "Body", ability.LongDescription, 26,
            new Color(0.85f, 0.88f, 0.9f, 1f), TextAnchor.UpperCenter, FontStyle.Normal,
            RuntimeUiKit.DefaultFont, new Vector2(0f, -716f), new Vector2(760f, 380f), new Vector2(0.5f, 1f));
        bodyText.textWrappingMode = TextWrappingModes.Normal;

        AddDetailClose(panel, Close);
    }
}
