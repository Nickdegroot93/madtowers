using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// The Chapters page: the campaign atlas. Every chapter is a full-width poster card built from
// the live ChapterDefinition list (Campaign.LoadChaptersInOrder via _chapters), so reordering
// sortOrder or adding chapters reflows this page with zero UI changes. Unlocked chapters show
// their real background art, name and level progress; locked ones are blacked-out slabs with
// "???" - the reveal is part of the reward. Tapping an unlocked card jumps the Home screen to
// that chapter, so this page doubles as long-range navigation once the campaign grows.
// (partial of MainMenuRuntime, split from the main file for readability - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private const float ChapterCardSideInset = 60f;
    private const float ChapterListTopInset = 300f;
    private const float ChapterListBottomInset = 220f;
    private const float ChapterRowHeight = 400f;
    // Text block's left edge and the action badge's centre-inset from the card's right edge.
    private const float ChapterCardTextLeft = 36f;
    private const float ChapterCardActionInsetRight = 70f;

    private static void BuildChaptersScreen(Transform parent, ChapterDefinition chapter)
    {
        BuildChaptersHeader(parent, chapter);
        BuildChaptersList(parent, chapter);
    }

    // Completed / total playable levels of one chapter (null level slots don't count).
    private static (int done, int total) ChapterLevelCounts(ChapterDefinition chapter)
    {
        int done = 0;
        int total = 0;
        for (int i = 0; i < chapter.Levels.Count; i++)
        {
            LevelDefinition level = chapter.Levels[i];
            if (level == null) continue;
            total++;
            if (ProgressStore.IsLevelCompleted(level)) done++;
        }
        return (done, total);
    }

    private static bool IsChapterFullyCompleted(ChapterDefinition chapter)
    {
        (int done, int total) = ChapterLevelCounts(chapter);
        return total > 0 && done == total;
    }

    // The chapter the player is "on": the first unlocked chapter with unfinished levels,
    // falling back to the last unlocked one when everything shipped is beaten.
    private static int CurrentCampaignChapterIndex()
    {
        int lastUnlocked = 0;
        for (int i = 0; i < _chapters.Length; i++)
        {
            if (!Campaign.IsChapterUnlocked(_chapters, i)) continue;
            lastUnlocked = i;
            if (!IsChapterFullyCompleted(_chapters[i])) return i;
        }
        return lastUnlocked;
    }

    private static void BuildChaptersHeader(Transform parent, ChapterDefinition chapter)
    {
        TextMeshProUGUI title = CreateTmp(parent, "ChaptersTitle", "CHAPTERS", 60, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(76f, -196f), new Vector2(520f, 76f), new Vector2(0f, 1f));
        title.characterSpacing = 4f;

        int cleared = 0;
        for (int i = 0; i < _chapters.Length; i++)
        {
            if (IsChapterFullyCompleted(_chapters[i])) cleared++;
        }

        CreateTmp(parent, "ChaptersProgress", $"{cleared} / {_chapters.Length} CLEARED", 24,
            GoldBase, TextAnchor.MiddleRight, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(-ChapterCardSideInset, -206f), new Vector2(420f, 34f), new Vector2(1f, 1f));

        BuildCapsuleBar(parent, "ChaptersProgressTrack",
            new Vector2(-ChapterCardSideInset, -246f), new Vector2(300f, 8f), new Vector2(1f, 1f),
            _chapters.Length > 0 ? (float)cleared / _chapters.Length : 0f, GoldBase);
    }

    // Thin capsule progress bar (the Vault header's track + fractional fill, shared here so the
    // chapter cards can carry one each).
    private static void BuildCapsuleBar(Transform parent, string name, Vector2 anchoredPosition,
        Vector2 size, Vector2 anchor, float fraction, Color fillColor)
    {
        RectTransform track = CreateRect(parent, name, anchor, anchor, anchor, anchoredPosition, size);
        Image trackImage = track.gameObject.AddComponent<Image>();
        trackImage.sprite = RuntimeSprites.RoundedPanel();
        trackImage.type = Image.Type.Sliced;
        trackImage.pixelsPerUnitMultiplier = 6f;
        trackImage.color = WithAlpha(TextPrimary, 0.14f);
        trackImage.raycastTarget = false;

        if (fraction <= 0f) return;
        RectTransform fill = CreateRect(track, "Fill",
            new Vector2(0f, 0f), new Vector2(Mathf.Clamp01(fraction), 1f), new Vector2(0f, 0.5f),
            Vector2.zero, Vector2.zero);
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.sprite = RuntimeSprites.RoundedPanel();
        fillImage.type = Image.Type.Sliced;
        fillImage.pixelsPerUnitMultiplier = 6f;
        fillImage.color = fillColor;
        fillImage.raycastTarget = false;
    }

    private static void BuildChaptersList(Transform parent, ChapterDefinition chapter)
    {
        // The Vault grid's exact scroll stack: masked viewport + layout content + clamped
        // DirectionalScrollRect + a thin auto-hiding scrollbar in the right gutter.
        RectTransform viewport = CreateRect(parent, "ChaptersViewport",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.offsetMin = new Vector2(0f, ChapterListBottomInset);
        viewport.offsetMax = new Vector2(0f, -ChapterListTopInset);
        Image viewportHit = viewport.gameObject.AddComponent<Image>();
        viewportHit.color = Color.clear;
        viewportHit.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>().padding = new Vector4(0f, -12f, 0f, -12f);

        RectTransform content = CreateRect(viewport, "ChaptersContent",
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

        RectTransform sbar = CreateRect(parent, "ChaptersScrollbar",
            new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
        sbar.offsetMin = new Vector2(-42f, ChapterListBottomInset + 12f);
        sbar.offsetMax = new Vector2(-34f, -(ChapterListTopInset + 12f));
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

        int currentIndex = CurrentCampaignChapterIndex();
        for (int i = 0; i < _chapters.Length; i++)
        {
            RectTransform row = NewGridRow(content, ChapterRowHeight);
            BuildChapterCard(row, i, i == currentIndex);
        }
    }

    private static void BuildChapterCard(RectTransform row, int index, bool current)
    {
        ChapterDefinition chapter = _chapters[index];
        bool unlocked = Campaign.IsChapterUnlocked(_chapters, index);
        (int done, int total) = ChapterLevelCounts(chapter);
        bool completed = total > 0 && done == total;
        Color chapterLight = ChapterLight(chapter);
        Color green = new Color(0.56f, 0.74f, 0.5f, 1f);

        if (current && unlocked)
        {
            // The level cards' active halo: the 9-sliced GlowFrame ring stretched slightly past
            // the card, behind it, so "your chapter" blooms the same way "your level" does.
            const float grow = 12f;
            RectTransform halo = CreateRect(row, "ActiveHalo",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            halo.offsetMin = new Vector2(ChapterCardSideInset - grow, CellGap - grow);
            halo.offsetMax = new Vector2(-(ChapterCardSideInset - grow), -(CellGap - grow));
            Image haloImage = halo.gameObject.AddComponent<Image>();
            haloImage.sprite = MenuSprites.GlowFrame();
            haloImage.type = Image.Type.Sliced;
            haloImage.color = WithAlpha(Color.Lerp(chapterLight, Color.white, 0.2f), 0.6f);
            haloImage.raycastTarget = false;
        }

        RectTransform card = CreateRect(row, "Card",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        card.offsetMin = new Vector2(ChapterCardSideInset, CellGap);
        card.offsetMax = new Vector2(-ChapterCardSideInset, -CellGap);
        Color cardFill = unlocked
            ? new Color(0.05f, 0.06f, 0.065f, 1f)
            : new Color(0.028f, 0.028f, 0.034f, 0.97f);
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        cardImage.color = cardFill;

        if (unlocked) BuildChapterCardArt(card, chapter);

        Color border = current && unlocked
            ? WithAlpha(Color.Lerp(chapterLight, Color.white, 0.25f), 1f)
            : WithAlpha(TextPrimary, unlocked ? 0.34f : 0.14f);
        RuntimeUiKit.AddOutline(card, border);

        // Text block, bottom-left over the scrim: eyebrow / name / levels progress + capsule bar.
        Color eyebrowColor = unlocked ? Color.Lerp(chapterLight, TextPrimary, 0.45f) : LockedColor;
        TextMeshProUGUI eyebrow = CreateTmp(card, "Eyebrow",
            $"{TrackedUpper("Chapter", " ", "   ")}  {chapter.ChapterNumber}", 20, eyebrowColor,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(ChapterCardTextLeft, 158f), new Vector2(420f, 30f), new Vector2(0f, 0f));
        eyebrow.characterSpacing = 6f;

        // Locked chapters keep their secrets: no name, no art, no progress - just the slot.
        string displayTitle = unlocked ? chapter.DisplayName.ToUpperInvariant() : "? ? ?";
        TextMeshProUGUI title = CreateTmp(card, "Title", displayTitle, 48,
            unlocked ? TextPrimary : LockedColor, TextAnchor.MiddleLeft, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(ChapterCardTextLeft - 2f, 94f),
            new Vector2(640f, 62f), new Vector2(0f, 0f));
        title.characterSpacing = 2f;
        AutoSize(title, 30, 48);

        if (unlocked)
        {
            Color progressColor = completed ? green : chapterLight;
            string progressHex = ColorUtility.ToHtmlStringRGBA(completed ? green : TextPrimary);
            string suffixHex = ColorUtility.ToHtmlStringRGBA(WithAlpha(progressColor, 0.9f));
            CreateTmp(card, "Progress",
                $"<color=#{progressHex}>{done} / {total}</color> <size=20><color=#{suffixHex}>LEVELS</color></size>", 28,
                TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.DefaultFont,
                new Vector2(ChapterCardTextLeft, 58f), new Vector2(420f, 36f), new Vector2(0f, 0f));

            BuildCapsuleBar(card, "LevelsTrack",
                new Vector2(ChapterCardTextLeft, 36f), new Vector2(330f, 8f), new Vector2(0f, 0f),
                total > 0 ? (float)done / total : 0f, progressColor);
        }
        else
        {
            CreateTmp(card, "LockedHint", "COMPLETE THE PREVIOUS CHAPTER", 17,
                WithAlpha(LockedColor, 0.85f), TextAnchor.MiddleLeft, FontStyle.Bold,
                RuntimeUiKit.TitleFont, new Vector2(ChapterCardTextLeft, 52f),
                new Vector2(520f, 26f), new Vector2(0f, 0f));
        }

        BuildChapterCardBadge(card, unlocked, completed, chapterLight, green);

        if (!unlocked) return;

        // Tapping a chapter jumps Home to it - the page doubles as long-range navigation
        // (the same route the pager's commit callback takes, minus the slide).
        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = cardImage;
        int selected = index;
        button.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            _chapterIndex = selected;
            _activeTab = MenuTab.Home;
            BuildMenu();
        });

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
    }

    // The chapter's 9:16 background art, cover-cropped to the wide card (EnvelopeParent keeps
    // the sprite's aspect and clips the overflow - never squashed), with a bottom scrim so the
    // text block reads on any art.
    private static void BuildChapterCardArt(RectTransform card, ChapterDefinition chapter)
    {
        RectTransform artFrame = CreateRect(card, "Art",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        artFrame.offsetMin = Vector2.zero;
        artFrame.offsetMax = Vector2.zero;
        MakeRoundedMask(artFrame);

        Sprite sprite = chapter.MenuBackgroundImage;
        if (sprite == null)
        {
            Color top = Color.Lerp(chapter.MenuAccentSecondaryColor, Color.black, 0.35f);
            Color bottom = Color.Lerp(chapter.MenuAccentColor, Color.black, 0.68f);
            sprite = MenuSprites.Background(top, bottom, chapter.MenuAccentColor);
        }

        Image art = CreateImage(artFrame, "Image", sprite, Color.white);
        Stretch(art.rectTransform);
        AspectRatioFitter fit = art.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 1f;

        // Two stacked bottom-up fades: a tall soft veil plus a denser lower band, so the text
        // block reads on the brightest art (Sakura's pink sky) without flattening the card's
        // upper half. Both live inside the rounded mask.
        AddChapterCardScrim(artFrame, 0.8f, 0.82f);
        AddChapterCardScrim(artFrame, 0.58f, 0.95f);
    }

    private static void AddChapterCardScrim(RectTransform artFrame, float heightFraction, float bottomAlpha)
    {
        Image scrim = CreateImage(artFrame, "Scrim",
            MenuSprites.VerticalFade(new Color(0f, 0f, 0f, 0f), new Color(0.02f, 0.02f, 0.03f, bottomAlpha)),
            Color.white);
        RectTransform scrimRect = scrim.rectTransform;
        scrimRect.anchorMin = new Vector2(0f, 0f);
        scrimRect.anchorMax = new Vector2(1f, heightFraction);
        scrimRect.offsetMin = Vector2.zero;
        scrimRect.offsetMax = Vector2.zero;
    }

    // Right-edge state badge: green check = cleared, chevron = enter, lock = sealed.
    private static void BuildChapterCardBadge(RectTransform card, bool unlocked, bool completed,
        Color chapterLight, Color green)
    {
        Vector2 anchor = new Vector2(1f, 0.5f);
        Vector2 center = new Vector2(-ChapterCardActionInsetRight, 0f);

        if (completed)
        {
            Image glow = CreateImage(card, "BadgeGlow",
                MenuSprites.CircleBadge(WithAlpha(green, 0.10f), WithAlpha(green, 0.20f)), Color.white);
            SetCenteredAt(glow.rectTransform, anchor, center, new Vector2(86f, 86f));
        }

        // Dark fill in every state (bright art would wash a tinted one out); the border and
        // glyph carry the colour.
        Color fill = completed
            ? WithAlpha(Color.Lerp(Color.black, green, 0.22f), 0.8f)
            : WithAlpha(Color.black, 0.38f);
        Color border = completed
            ? WithAlpha(green, 0.95f)
            : (unlocked ? WithAlpha(ChapterEdge(chapterLight), 1f) : WithAlpha(LockedColor, 0.7f));
        Image badge = CreateImage(card, "Badge", MenuSprites.CircleBadge(fill, border), Color.white);
        SetCenteredAt(badge.rectTransform, anchor, center, new Vector2(74f, 74f));

        if (completed)
        {
            Image check = CreateImage(badge.transform, "Check", MenuSprites.CheckMark(green), Color.white);
            check.preserveAspect = true;
            SetCenteredAt(check.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
        }
        else if (unlocked)
        {
            Image chevron = CreateImage(badge.transform, "Chevron", MenuSprites.Chevron(TextPrimary), Color.white);
            chevron.preserveAspect = true;
            SetCenteredAt(chevron.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
        }
        else
        {
            Image lockIcon = CreateImage(badge.transform, "Lock", MenuSprites.Lock(LockedColor), Color.white);
            lockIcon.preserveAspect = true;
            SetCenteredAt(lockIcon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 38f));
        }
    }
}
