using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// The Chapters page: the campaign atlas. Every UNLOCKED chapter is a full-width poster card
// built from the live ChapterDefinition list (Campaign.LoadChaptersInOrder via _chapters), so
// reordering sortOrder or adding chapters reflows this page with zero UI changes. Locked
// chapters are NOT rendered at all - the campaign's size is a secret (Nick 2026-08-30): no
// "3/15", no row of sealed slabs to count. One dark locked-teaser card ends the list
// instead, and it stays even at the shipped content's edge so the end never announces
// itself. Tapping a card jumps the Home screen to that chapter, so this page doubles as
// long-range navigation once the campaign grows.
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

        // No "/ total" and no header progress bar: both would bound the campaign, and its
        // size is a secret (see the file header). Cleared count only, once there is one.
        int cleared = 0;
        for (int i = 0; i < _chapters.Length; i++)
        {
            if (IsChapterFullyCompleted(_chapters[i])) cleared++;
        }
        if (cleared > 0)
        {
            CreateTmp(parent, "ChaptersProgress", $"{cleared} CLEARED", 24,
                MenuAccent, TextAnchor.MiddleRight, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(-ChapterCardSideInset, -206f), new Vector2(420f, 34f), new Vector2(1f, 1f));
        }
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
            // The ambiguity rule: locked chapters simply don't exist on this page.
            if (!Campaign.IsChapterUnlocked(_chapters, i)) continue;
            RectTransform row = NewGridRow(content, ChapterRowHeight);
            BuildChapterCard(row, i, i == currentIndex);
        }

        BuildLockedTeaserCard(content, 300f, "LOCKED",
            "Finish the chapter above to continue your journey.");
    }

    // The ambiguity teaser closing the Chapters list and the Vault's brick list: a sealed
    // near-black slab promising MORE without ever counting it. Deliberately unconditional -
    // it stays even when everything shipped is unlocked, so the current content edge never
    // reads as "the end" (Nick 2026-08-30).
    private static void BuildLockedTeaserCard(Transform content, float rowHeight, string title, string body)
    {
        RectTransform row = NewGridRow(content, rowHeight);
        RectTransform card = CreateRect(row, "LockedTeaser",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        card.offsetMin = new Vector2(ChapterCardSideInset, CellGap);
        card.offsetMax = new Vector2(-ChapterCardSideInset, -CellGap);
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        cardImage.color = new Color(0.022f, 0.022f, 0.028f, 0.97f);
        cardImage.raycastTarget = false;
        RuntimeUiKit.AddOutline(card, WithAlpha(TextPrimary, 0.12f));

        Image badge = CreateImage(card, "Badge",
            MenuSprites.CircleBadge(WithAlpha(Color.black, 0.5f), WithAlpha(LockedColor, 0.65f)),
            Color.white);
        badge.raycastTarget = false;
        SetCenteredAt(badge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 58f), new Vector2(70f, 70f));
        Image lockIcon = CreateImage(badge.transform, "Lock", MenuSprites.Lock(LockedColor), Color.white);
        lockIcon.preserveAspect = true;
        lockIcon.raycastTarget = false;
        SetCenteredAt(lockIcon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 34f));

        TextMeshProUGUI titleText = CreateTmp(card, "Title", title, 28,
            Color.Lerp(LockedColor, TextPrimary, 0.55f), TextAnchor.MiddleCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(0f, -12f), new Vector2(640f, 38f), new Vector2(0.5f, 0.5f));
        titleText.characterSpacing = 4f;

        TextMeshProUGUI bodyText = CreateTmp(card, "Body", body, 20,
            WithAlpha(TextMuted, 0.85f), TextAnchor.MiddleCenter, FontStyle.Normal,
            RuntimeUiKit.DefaultFont, new Vector2(0f, -58f), new Vector2(660f, 40f), new Vector2(0.5f, 0.5f));
        bodyText.textWrappingMode = TextWrappingModes.Normal;
    }

    // Only ever called for UNLOCKED chapters - locked ones aren't rendered (the ambiguity
    // rule); the teaser card at the list's end is the sole locked-state surface.
    private static void BuildChapterCard(RectTransform row, int index, bool current)
    {
        ChapterDefinition chapter = _chapters[index];
        (int done, int total) = ChapterLevelCounts(chapter);
        bool completed = total > 0 && done == total;
        Color chapterLight = ChapterLight(chapter);
        Color green = new Color(0.56f, 0.74f, 0.5f, 1f);

        if (current)
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
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        cardImage.color = new Color(0.05f, 0.06f, 0.065f, 1f);

        BuildChapterCardArt(card, chapter);

        Color border = current
            ? WithAlpha(Color.Lerp(chapterLight, Color.white, 0.25f), 1f)
            : WithAlpha(TextPrimary, 0.34f);
        RuntimeUiKit.AddOutline(card, border);

        // Text block, bottom-left over the scrim: eyebrow / name / levels progress + capsule bar.
        TextMeshProUGUI eyebrow = CreateTmp(card, "Eyebrow",
            $"{TrackedUpper("Chapter", " ", "   ")}  {chapter.ChapterNumber}", 20,
            Color.Lerp(chapterLight, TextPrimary, 0.45f),
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(ChapterCardTextLeft, 158f), new Vector2(420f, 30f), new Vector2(0f, 0f));
        eyebrow.characterSpacing = 6f;

        TextMeshProUGUI title = CreateTmp(card, "Title", chapter.DisplayName.ToUpperInvariant(), 48,
            TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(ChapterCardTextLeft - 2f, 94f),
            new Vector2(640f, 62f), new Vector2(0f, 0f));
        title.characterSpacing = 2f;
        AutoSize(title, 30, 48);

        Color progressColor = completed ? green : chapterLight;
        string progressHex = ColorUtility.ToHtmlStringRGBA(completed ? green : TextPrimary);
        string suffixHex = ColorUtility.ToHtmlStringRGBA(WithAlpha(progressColor, 0.9f));
        CreateTmp(card, "Progress",
            $"<color=#{progressHex}>{done} / {total}</color> <size=20><color=#{suffixHex}>LEVELS</color></size>", 28,
            TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.DefaultFont,
            new Vector2(ChapterCardTextLeft, 58f), new Vector2(420f, 36f), new Vector2(0f, 0f));

        // One cube per level, tinted by that level's highest medal (ghost = not cleared, a
        // green check = cleared with no ladder) - Mario-map style: which levels still owe you
        // a rung reads at a glance. Replaced the capsule bar 2026-09-04. Totals INSIDE an
        // unlocked chapter are fine (its levels are listed); it is chapter totals that are secret.
        BuildChapterMedalStrip(card, chapter, new Vector2(ChapterCardTextLeft, 26f), 330f, green);

        BuildChapterCardBadge(card, completed, chapterLight, green);

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
        FitToCover(art, SpriteAspect(sprite));

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

    private static void BuildChapterMedalStrip(RectTransform card, ChapterDefinition chapter,
        Vector2 anchoredPosition, float maxWidth, Color green)
    {
        const float maxCube = 26f;
        const float gap = 6f;
        int n = 0;
        for (int i = 0; i < chapter.Levels.Count; i++) if (chapter.Levels[i] != null) n++;
        if (n == 0) return;
        float cube = Mathf.Min(maxCube, (maxWidth - (n - 1) * gap) / n);

        RectTransform strip = CreateRect(card, "MedalStrip", new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(0f, 0f), anchoredPosition, new Vector2(n * cube + (n - 1) * gap, cube));
        int slot = 0;
        for (int i = 0; i < chapter.Levels.Count; i++)
        {
            LevelDefinition level = chapter.Levels[i];
            if (level == null) continue;
            bool completed = ProgressStore.IsLevelCompleted(level);
            MedalTier? medal = completed ? LevelTiers.HighestEarned(level) : null;
            Sprite sprite;
            Color tint;
            if (medal.HasValue)
            {
                sprite = MedalStyle.Sprite(medal.Value, earned: true);
                tint = Color.white;
            }
            else if (completed)
            {
                sprite = MenuSprites.CheckMark(green);   // cleared, no ladder (Endless)
                tint = Color.white;
            }
            else
            {
                sprite = MedalStyle.Sprite(MedalTier.Bronze, earned: false);
                tint = MedalStyle.IconTint(false);
            }
            Image img = CreateImage(strip, "Level" + (i + 1), sprite, tint);
            img.preserveAspect = true;
            img.raycastTarget = false;
            SetCenteredAt(img.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(slot * (cube + gap) + cube * 0.5f, 0f), new Vector2(cube, cube));
            slot++;
        }
    }

    // Right-edge state badge: green check = cleared, chevron = enter. (No lock state - locked
    // chapters aren't rendered on this page.)
    private static void BuildChapterCardBadge(RectTransform card, bool completed,
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
            : WithAlpha(ChapterEdge(chapterLight), 1f);
        Image badge = CreateImage(card, "Badge", MenuSprites.CircleBadge(fill, border), Color.white);
        SetCenteredAt(badge.rectTransform, anchor, center, new Vector2(74f, 74f));

        if (completed)
        {
            Image check = CreateImage(badge.transform, "Check", MenuSprites.CheckMark(green), Color.white);
            check.preserveAspect = true;
            SetCenteredAt(check.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
        }
        else
        {
            Image chevron = CreateImage(badge.transform, "Chevron", MenuSprites.Chevron(TextPrimary), Color.white);
            chevron.preserveAspect = true;
            SetCenteredAt(chevron.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
        }
    }
}
