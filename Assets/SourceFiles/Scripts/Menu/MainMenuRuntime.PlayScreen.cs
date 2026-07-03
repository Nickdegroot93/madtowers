using System;
using Coffee.UIEffects;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// The Play screen: chapter content, pager, level list and cards.
// (partial of MainMenuRuntime, split from the main file for readability - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private static void BuildPlayScreen(Transform parent, ChapterDefinition chapter)
    {
        // Full-screen, transparent swipe catcher behind the chapter content. As the parent
        // of every play-screen graphic it picks up drags that bubble up from buttons and
        // empty space; the level list's DirectionalScrollRect forwards its horizontal drags
        // here too. Taps fall through to the buttons on top. The drag is handed to the pager,
        // which slides this content root and the background together so a swipe feels
        // continuous instead of snapping.
        Image swipeCatcher = parent.gameObject.AddComponent<Image>();
        swipeCatcher.color = Color.clear;
        swipeCatcher.raycastTarget = true;
        MenuSwipeArea swipe = parent.gameObject.AddComponent<MenuSwipeArea>();
        if (_pager != null)
        {
            ConfigurePager((RectTransform)parent);
            swipe.OnPanBegin = _pager.BeginPan;
            swipe.OnPanMove = _pager.PanMove;
            swipe.OnPanEnd = _pager.EndPan;
        }

        BuildChapterContent(parent, chapter, _chapterIndex);
    }

    // A soft drop shadow under light text, so titles stay legible on bright / low-contrast
    // backdrops (which vary per chapter theme). Same UIEffect shadow the level cards use.
    private static void AddTextShadow(TextMeshProUGUI text, float alpha, Vector2 distance, float blur)
    {
        UIEffect fx = text.gameObject.AddComponent<UIEffect>();
        fx.shadowMode = ShadowMode.Shadow;
        fx.shadowColorFilter = ColorFilter.Replace;
        fx.shadowColor = new Color(0f, 0f, 0f, alpha);
        fx.shadowDistance = distance;
        fx.shadowBlurIntensity = blur;
        // High iteration spreads the blur wide so the shadow reads as a soft feathered halo, not
        // a crisp offset echo - keep alpha/distance low and let the blur do the work.
        fx.shadowIteration = 8;
    }

    // Builds a single chapter's foreground (title block, next-chapter card, level list) into
    // an arbitrary container. The live screen passes the content root; the pager passes an
    // off-screen neighbour panel so the incoming chapter is fully rendered while it slides in.
    private static void BuildChapterContent(Transform parent, ChapterDefinition chapter, int chapterIndex)
    {
        bool chapterUnlocked = Campaign.IsChapterUnlocked(_chapters, chapterIndex);
        Color eyebrowColor = Color.Lerp(chapter.MenuAccentColor, TextPrimary, 0.42f);

        // "CHAPTER N" eyebrow, left-aligned with the title below and sitting close to it.
        TextMeshProUGUI eyebrow = CreateTmp(parent, "ChapterEyebrow", $"{TrackedUpper("Chapter", " ", "   ")}  {chapter.ChapterNumber}", 20,
            eyebrowColor, TextAnchor.MiddleLeft, FontStyle.Normal, RuntimeUiKit.TitleFont,
            new Vector2(76f, -252f), new Vector2(180f, 42f), new Vector2(0f, 1f));
        AutoSize(eyebrow, 16, 20);
        AddTextShadow(eyebrow, 0.18f, new Vector2(0f, -1f), 1f);

        TextMeshProUGUI title = CreateTmp(parent, "ChapterTitle", chapter.DisplayName.ToUpperInvariant(), 68,
            TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(76f, -276f), new Vector2(700f, 104f), new Vector2(0f, 1f));
        title.characterSpacing = 6f;
        AutoSize(title, 40, 68);
        AddTextShadow(title, 0.28f, new Vector2(0f, -2f), 1f);

        if (!chapterUnlocked)
        {
            BuildLockedChapterMessage(parent);
        }
        else
        {
            int currentIndex = CurrentLevelIndex(chapter);
            BuildLevelList(parent, chapter, currentIndex);
        }

        // Built last so it renders on top of the level list and stays tappable in its
        // bottom-right home (the list's scroll viewport would otherwise sit over it).
        BuildNextChapterCard(parent, chapter, chapterIndex);
    }

    // Hands the pager everything it needs to drive a chapter transition against the freshly
    // built content root. Re-run on every BuildMenu so the pager never holds a stale root.
    private static void ConfigurePager(RectTransform contentRoot)
    {
        RectTransform bgTrack = _backgroundLayer != null
            ? _backgroundLayer.Find("BgTrack") as RectTransform
            : null;

        _pager.Configure(
            contentRoot,
            bgTrack,
            (RectTransform)_contentLayer,
            _chapters.Length,
            ResolveSwipeTarget,
            (panel, index) => BuildChapterContent(panel, _chapters[index], index),
            index => BuildNeighborBackgroundImage(_chapters[index]),
            index =>
            {
                SfxPlayer.Play("ui-button-click");
                _chapterIndex = index;
                _activeTab = MenuTab.Home;
                BuildMenu();
            });
    }

    // Swipe target for one step: +1 forward (only if unlocked), -1 back (always allowed -
    // reaching the current chapter unlocked it). No wrap; returns -1 when there is nowhere
    // to go that way, which the pager renders as a rubber-band resist.
    private static int ResolveSwipeTarget(int direction)
    {
        if (_chapters == null || _chapters.Length <= 1) return -1;

        int target = _chapterIndex + direction;
        if (target < 0 || target >= _chapters.Length) return -1;
        if (direction > 0 && !Campaign.IsChapterUnlocked(_chapters, target)) return -1;
        return target;
    }

    // A lightweight background for the incoming chapter (static image only - no video) that
    // rides the background track during a transition. Parented into the track, it sits below
    // the fixed dimming overlays and so shares the same dimming as the current background.
    private static RectTransform BuildNeighborBackgroundImage(ChapterDefinition chapter)
    {
        if (_backgroundLayer == null || chapter == null) return null;

        Transform track = _backgroundLayer.Find("BgTrack");
        if (track == null) return null;

        Sprite sprite = chapter.MenuBackgroundImage;
        if (sprite == null)
        {
            Color top = Color.Lerp(chapter.MenuAccentSecondaryColor, Color.black, 0.35f);
            Color bottom = Color.Lerp(chapter.MenuAccentColor, Color.black, 0.68f);
            sprite = MenuSprites.Background(top, bottom, chapter.MenuAccentColor);
        }

        Image image = CreateImage(track, "NeighborBackground", sprite, Color.white);
        Stretch(image.rectTransform);
        image.preserveAspect = false;
        return image.rectTransform;
    }

    private static void BuildNextChapterCard(Transform parent, ChapterDefinition current, int chapterIndex)
    {
        if (_chapters.Length <= 1) return;

        int nextIndex = (chapterIndex + 1) % _chapters.Length;
        ChapterDefinition next = _chapters[nextIndex];
        bool unlocked = Campaign.IsChapterUnlocked(_chapters, nextIndex);

        RectTransform card = CreateRect(parent, "NextChapterCard",
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-60f, 210f), new Vector2(300f, 160f));
        Color cardFill = new Color(0.05f, 0.06f, 0.065f, 1f);
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        cardImage.color = cardFill;
        RuntimeUiKit.AddOutline(card, GoldOutline(0.24f));

        Sprite preview = current.NextChapterPreviewImage != null
            ? current.NextChapterPreviewImage
            : next.MenuBackgroundImage;
        if (preview != null)
        {
            CreateCoverImage(card, "Preview", preview, new Color(1f, 1f, 1f, 0.42f),
                Vector2.zero, new Vector2(300f, 160f), new Vector2(0.5f, 0.5f));
        }

        CreateTmp(card, "NextLabel", "NEXT CHAPTER", 15, TextMuted, TextAnchor.MiddleLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(28f, -22f), new Vector2(180f, 26f), new Vector2(0f, 1f));
        CreateTmp(card, "NextTitle", next.DisplayName.ToUpperInvariant(), 21, TextPrimary, TextAnchor.MiddleLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(28f, -50f), new Vector2(206f, 34f), new Vector2(0f, 1f));
        if (unlocked)
        {
            Image nextArrow = CreateImage(card, "NextArrow", MenuSprites.Chevron(TextPrimary), Color.white);
            nextArrow.preserveAspect = true;
            SetCentered(nextArrow.rectTransform, new Vector2(258f, -75f), new Vector2(40f, 40f));
        }
        else
        {
            Image lockIcon = CreateImage(card, "NextLock", MenuSprites.Lock(LockedColor), Color.white);
            lockIcon.preserveAspect = true;
            SetCentered(lockIcon.rectTransform, new Vector2(258f, -75f), new Vector2(34f, 34f));
        }

        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = cardImage;
        button.interactable = unlocked;
        button.onClick.AddListener(() =>
        {
            // Slide to the next chapter (entering from the right) through the same transition
            // a swipe uses, so the card and the gesture feel identical.
            if (_pager != null) _pager.AnimateToChapter(nextIndex, 1);
        });

        // Keep the card fully opaque in every state. Without this, a locked (non-interactable)
        // card falls back to Unity's default disabledColor (alpha 0.5), which the ColorTint
        // transition applies over the fill and makes the card look see-through.
        ColorBlock colors = button.colors;
        colors.normalColor = cardFill;
        colors.highlightedColor = WithAlpha(Color.Lerp(cardFill, TextPrimary, 0.08f), 1f);
        colors.pressedColor = WithAlpha(Color.Lerp(cardFill, Color.black, 0.12f), 1f);
        colors.disabledColor = cardFill;
        button.colors = colors;
    }

    private static void BuildLockedChapterMessage(Transform parent)
    {
        RectTransform panel = CreateRect(parent, "LockedChapterPanel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, -90f), new Vector2(760f, 220f));
        Image image = panel.gameObject.AddComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
        image.color = CardDark;
        RuntimeUiKit.AddOutline(panel, GoldOutline(0.18f));
        CreateTmp(panel, "Locked", "LOCKED", 40, LockedColor, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont);
    }

    private static int CurrentLevelIndex(ChapterDefinition chapter)
    {
        int fallback = 0;
        for (int i = 0; i < chapter.Levels.Count; i++)
        {
            LevelDefinition level = chapter.Levels[i];
            if (level == null) continue;
            fallback = i;
            if (IsLevelVisuallyUnlocked(chapter, i) && !ProgressStore.IsLevelCompleted(level)) return i;
        }
        return fallback;
    }

    private static bool IsLevelVisuallyUnlocked(ChapterDefinition chapter, int levelIndex)
    {
        if (Campaign.UnlockAllForTesting) return true;
        if (chapter == null) return false;
        if (chapter.AlwaysUnlocked) return true;
        if (levelIndex <= 0) return true;

        LevelDefinition previous = chapter.Levels[levelIndex - 1];
        return previous == null || ProgressStore.IsLevelCompleted(previous);
    }

    private static void BuildLevelList(Transform parent, ChapterDefinition chapter, int currentIndex)
    {
        RectTransform viewport = CreateRect(parent, "LevelListViewport",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        // Full width horizontally so cards reach the phone padding themselves (and the active
        // card's glow bleeds past its edge without the mask clipping it); the mask only trims
        // the list top and bottom.
        viewport.offsetMin = new Vector2(0f, LevelListBottomInset);
        viewport.offsetMax = new Vector2(0f, -LevelListTopInset);
        Image viewportHitTarget = viewport.gameObject.AddComponent<Image>();
        viewportHitTarget.color = Color.clear;
        viewportHitTarget.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = CreateRect(viewport, "LevelListContent",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, Vector2.zero);

        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 0f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.gameObject.AddComponent<DirectionalScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 34f;

        int count = chapter.Levels.Count;
        for (int i = 0; i < count; i++)
        {
            LevelDefinition level = chapter.Levels[i];
            if (level == null) continue;

            RectTransform row = CreateRect(content, $"LevelRow{i + 1}",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, LevelRowHeight));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = LevelRowHeight;

            bool completed = ProgressStore.IsLevelCompleted(level);
            bool unlocked = completed || IsLevelVisuallyUnlocked(chapter, i);
            bool isCurrent = i == currentIndex;
            bool current = isCurrent && unlocked && !completed;

            BuildLevelCard(row, chapter, level, i, unlocked, completed, current);
        }
    }

    private static void BuildLevelCard(Transform parent, ChapterDefinition chapter, LevelDefinition level,
        int index, bool unlocked, bool completed, bool current)
    {
        Color chapterLight = ChapterLight(chapter);
        Color chapterDark = ChapterDark(chapter);

        // Stretch across the row between the side insets so the card width tracks the screen;
        // height stays fixed, hung LevelCardTop below the row's top.
        RectTransform card = CreateRect(parent, $"LevelCard{index + 1}",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, Vector2.zero);
        card.offsetMin = new Vector2(LevelCardSideInset, -(LevelCardTop + LevelCardHeight));
        card.offsetMax = new Vector2(-LevelCardSideInset, -LevelCardTop);
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        Color cardFill = MenuGlassFill(chapter, unlocked ? 0.80f : 0.68f);
        cardImage.color = cardFill;

        AddFrostedGlass(card, chapter != null ? chapter.MenuBackgroundImage : null, LevelCardFrostWash);

        // Every card gets a thin cream border; the active one uses the CHAPTER colour (a bright
        // warm gold) at full strength plus the glow, so it reads as "this chapter's" highlight.
        Color cardBorder = current
            ? WithAlpha(Color.Lerp(chapterLight, Color.white, 0.25f), 1f)
            : WithAlpha(TextPrimary, unlocked ? 0.34f : 0.18f);
        RuntimeUiKit.AddOutline(card, cardBorder);

        if (current)
        {
            // Outer glow via UIEffect on the FILLED card silhouette (a blurred thin outline is too
            // faint to read): a zero-distance, Replace-tinted shadow of the whole rounded rect is
            // a broad soft halo behind the card. uGUI has no box-shadow, so this is the halo; the
            // AddOutline above stays the crisp border on top.
            UIEffect glowFx = cardImage.gameObject.AddComponent<UIEffect>();
            glowFx.shadowMode = ShadowMode.Shadow;
            glowFx.shadowDistance = Vector2.zero;
            glowFx.shadowIteration = 5;
            glowFx.shadowBlurIntensity = 1f;
            glowFx.shadowColorFilter = ColorFilter.Replace;
            glowFx.shadowColor = WithAlpha(chapterLight, 0.85f);
        }

        Sprite thumbSprite = level.MenuThumbnail != null
            ? level.MenuThumbnail
            : MenuSprites.LevelThumbnail(index, chapter.MenuAccentColor, chapter.MenuAccentSecondaryColor);
        RectTransform thumb = CreateCoverImage(card, "Thumbnail", thumbSprite,
            unlocked ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.55f),
            new Vector2(22f, -16f), new Vector2(132f, 152f), new Vector2(0f, 1f));
        RuntimeUiKit.AddOutline(thumb, WithAlpha(TextPrimary, unlocked ? 0.18f : 0.08f));

        // Hollow diamond outline (faint fill, bright crisp cream border), aligned to the TITLE
        // row near the top of the card - sitting in the gap between the thumbnail and the title.
        Color edgeColor = ChapterEdge(chapterLight);
        Image numberPlate = CreateImage(card, "NumberPlate",
            MenuSprites.DiamondBadge(MenuGlassFill(chapter, unlocked ? 0.16f : 0.10f),
                WithAlpha(edgeColor, unlocked ? 1f : 0.42f)),
            Color.white);
        SetCentered(numberPlate.rectTransform, new Vector2(200f, -52f), new Vector2(62f, 62f));
        CreateTmp(numberPlate.transform, "Number", (index + 1).ToString(), 26, unlocked ? TextPrimary : LockedColor,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.DefaultFont);

        Color titleColor = unlocked ? TextPrimary : LockedColor;
        LevelMenuPresentation.Snapshot presentation = LevelMenuPresentation.Build(level, completed);

        // Text column: title / challenge type / progress, stacked by a VerticalLayoutGroup and
        // TOP-aligned (the reference sits the text high with breathing room below). One shared
        // left edge, one flow - no per-line y positions. The card stays a positioned panel (its
        // placement is tied to the rail node), but its contents flow.
        RectTransform column = CreateRect(card, "TextColumn",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        column.offsetMin = new Vector2(LevelCardTextLeft, 14f);
        column.offsetMax = new Vector2(-LevelCardTextRight, -22f);

        VerticalLayoutGroup columnLayout = column.gameObject.AddComponent<VerticalLayoutGroup>();
        columnLayout.spacing = 4f;
        columnLayout.childAlignment = TextAnchor.UpperLeft;
        columnLayout.childControlWidth = true;
        columnLayout.childControlHeight = true;
        columnLayout.childForceExpandWidth = true;
        columnLayout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateTmp(column, "Title", level.DisplayName, 40, titleColor, TextAnchor.LowerLeft,
            FontStyle.Normal, RuntimeUiKit.TitleFont);
        AutoSize(title, 28, 40);
        LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
        titleLayout.preferredHeight = 48f;
        titleLayout.flexibleHeight = 0f;

        // Type label: real TMP letter-spacing instead of the old "inject spaces" hack.
        TextMeshProUGUI challenge = CreateTmp(column, "Challenge", presentation.ChallengeLabel.ToUpperInvariant(), 17,
            unlocked ? chapterLight : LockedColor, TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.DefaultFont);
        challenge.characterSpacing = 5f;
        LayoutElement challengeLayout = challenge.gameObject.AddComponent<LayoutElement>();
        challengeLayout.preferredHeight = 24f;
        challengeLayout.flexibleHeight = 0f;

        BuildProgressLine(column, presentation, unlocked, completed, chapterDark, chapterLight);

        BuildActionBadge(card, unlocked, completed, chapterLight);

        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = cardImage;
        button.interactable = Campaign.IsLevelUnlocked(chapter, index);
        LevelDefinition selected = level;
        int selectedIndex = index;
        bool selectedCompleted = completed;
        button.onClick.AddListener(() => OpenLevelSummary(chapter, selected, selectedIndex, selectedCompleted));

        ColorBlock colors = button.colors;
        colors.normalColor = cardFill;
        colors.highlightedColor = WithAlpha(Color.Lerp(cardFill, TextPrimary, 0.08f), cardFill.a);
        colors.pressedColor = WithAlpha(Color.Lerp(cardFill, Color.black, 0.12f), cardFill.a);
        colors.disabledColor = MenuGlassFill(chapter, 0.36f);
        button.colors = colors;
    }

    private static void BuildProgressLine(Transform column, LevelMenuPresentation.Snapshot presentation,
        bool unlocked, bool completed, Color primaryColor, Color suffixColor)
    {
        Color completeColor = new Color(0.56f, 0.74f, 0.5f, 1f);
        Color valueColor = !unlocked ? LockedColor : (completed ? completeColor : primaryColor);
        Color restColor = !unlocked ? LockedColor : (completed ? Color.Lerp(completeColor, TextPrimary, 0.18f) : suffixColor);

        // The value ("20") and suffix ("/ 100 Blocks") are ONE rich-text label, not two boxes:
        // an inline <size> tag shrinks the suffix while it stays on the same text line, so the
        // two share a real baseline automatically (the old two-box approach drifted because a
        // larger font box carries more space below its baseline than a smaller one).
        string primaryHex = ColorUtility.ToHtmlStringRGBA(valueColor);
        string suffixHex = ColorUtility.ToHtmlStringRGBA(restColor);
        string markup = string.IsNullOrEmpty(presentation.ProgressSuffix)
            ? $"<color=#{primaryHex}>{presentation.ProgressPrimary}</color>"
            : $"<color=#{primaryHex}>{presentation.ProgressPrimary}</color> <size=22><color=#{suffixHex}>{presentation.ProgressSuffix}</color></size>";

        TextMeshProUGUI progress = CreateTmp(column, "Progress", markup, 34, valueColor,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.DefaultFont);
        LayoutElement progressLayout = progress.gameObject.AddComponent<LayoutElement>();
        progressLayout.preferredHeight = 44f;
        progressLayout.flexibleHeight = 0f;
    }

    private static void BuildActionBadge(Transform card, bool unlocked, bool completed, Color chapterLight)
    {
        Color green = new Color(0.58f, 0.86f, 0.18f, 1f);
        // Pinned to the card's top-right corner (anchor (1, 1)) and offset in by the right inset,
        // so the badge stays glued to the edge however wide the stretched card becomes.
        Vector2 anchor = new Vector2(1f, 1f);
        Vector2 center = new Vector2(-LevelCardActionInsetRight, -LevelCardHeight * 0.5f);

        if (completed)
        {
            Image completedGlow = CreateImage(card, "ActionGlow",
                MenuSprites.CircleBadge(WithAlpha(green, 0.10f), WithAlpha(green, 0.20f)), Color.white);
            SetCenteredAt(completedGlow.rectTransform, anchor, center, new Vector2(86f, 86f));
        }

        Color edgeColor = ChapterEdge(chapterLight);
        Color fill = completed ? WithAlpha(green, 0.20f) : WithAlpha(Color.black, 0.18f);
        Color border = completed ? WithAlpha(green, 0.95f) : WithAlpha(edgeColor, unlocked ? 1f : 0.42f);
        Image action = CreateImage(card, "Action", MenuSprites.CircleBadge(fill, border), Color.white);
        SetCenteredAt(action.rectTransform, anchor, center, new Vector2(74f, 74f));

        if (completed)
        {
            Image check = CreateImage(action.transform, "ActionCheck", MenuSprites.CheckMark(green), Color.white);
            check.preserveAspect = true;
            SetCenteredAt(check.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
        }
        else if (unlocked)
        {
            Image chevron = CreateImage(action.transform, "ActionChevron", MenuSprites.Chevron(TextPrimary), Color.white);
            chevron.preserveAspect = true;
            SetCenteredAt(chevron.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
        }
        else
        {
            Image lockIcon = CreateImage(action.transform, "ActionLock", MenuSprites.Lock(LockedColor), Color.white);
            lockIcon.preserveAspect = true;
            SetCenteredAt(lockIcon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 38f));
        }
    }

}
