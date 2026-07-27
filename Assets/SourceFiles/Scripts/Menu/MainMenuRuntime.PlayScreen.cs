using System;
using System.Collections.Generic;
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

        // Only the LIVE play screen may play an unlock reveal - resolved here and cleared right
        // after, so the pager's off-screen neighbour builds (same BuildChapterContent) never do.
        ResolvePendingReveal(chapter, _chapterIndex);
        BuildChapterContent(parent, chapter, _chapterIndex);
        ClearResolvedReveal();
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
        // Adaptive ink: chapters whose background TOP is light (Desert sand, Sakura sky) render
        // the header in dark ink; dark tops (Jungle canopy) keep cream. No drop shadows - the
        // ink itself carries the contrast (the shadows were a patch for light-on-light).
        bool lightTop = chapter.MenuTopIsLight;
        Color titleInk = lightTop ? new Color(0.16f, 0.13f, 0.10f, 1f) : TextPrimary;
        Color eyebrowColor = lightTop
            ? Color.Lerp(chapter.MenuAccentColor, Color.black, 0.35f)
            : Color.Lerp(chapter.MenuAccentColor, TextPrimary, 0.42f);

        // "- CHAPTER N -" eyebrow flanked by tiny accent diamonds (the concepts' ornament),
        // left-aligned with the title below and sitting close to it.
        RectTransform eyebrowRow = CreateRect(parent, "ChapterEyebrowRow",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(76f, -252f), new Vector2(420f, 42f));
        HorizontalLayoutGroup eyebrowGroup = eyebrowRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        eyebrowGroup.spacing = 10f;
        eyebrowGroup.childAlignment = TextAnchor.MiddleLeft;
        eyebrowGroup.childControlWidth = false;
        eyebrowGroup.childControlHeight = false;
        eyebrowGroup.childForceExpandWidth = false;
        eyebrowGroup.childForceExpandHeight = false;

        Image leftDiamond = CreateImage(eyebrowRow, "EyebrowDiamondL",
            MenuSprites.DiamondBadge(eyebrowColor, eyebrowColor), Color.white);
        leftDiamond.raycastTarget = false;
        leftDiamond.rectTransform.sizeDelta = new Vector2(13f, 13f);

        TextMeshProUGUI eyebrow = CreateTmp(eyebrowRow, "ChapterEyebrow",
            $"{TrackedUpper("Chapter", " ", "   ")}  {chapter.ChapterNumber}", 21,
            eyebrowColor, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont);
        eyebrow.characterSpacing = 6f;
        eyebrow.rectTransform.sizeDelta = new Vector2(230f, 42f);
        // Hug the actual tracked text width so the right diamond sits snug against it.
        eyebrow.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        Image rightDiamond = CreateImage(eyebrowRow, "EyebrowDiamondR",
            MenuSprites.DiamondBadge(eyebrowColor, eyebrowColor), Color.white);
        rightDiamond.raycastTarget = false;
        rightDiamond.rectTransform.sizeDelta = new Vector2(13f, 13f);

        TextMeshProUGUI title = CreateTmp(parent, "ChapterTitle", chapter.DisplayName.ToUpperInvariant(), 62,
            titleInk, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(76f, -276f), new Vector2(760f, 104f), new Vector2(0f, 1f));
        title.characterSpacing = 3f;
        AutoSize(title, 38, 62);

        if (!chapterUnlocked)
        {
            BuildLockedChapterMessage(parent);
        }
        else
        {
            int currentIndex = CurrentLevelIndex(chapter);
            BuildLevelList(parent, chapter, currentIndex);
        }

        // Built last so they render on top of the level list and stay tappable in their
        // bottom corners (the list's scroll viewport would otherwise sit over them).
        BuildPreviousChapterCard(parent, chapterIndex);
        BuildNextChapterCard(parent, chapter, chapterIndex);
    }

    // Hands the pager everything it needs to drive a chapter transition against the freshly
    // built content root. Re-run on every BuildMenu so the pager never holds a stale root.
    private static void ConfigurePager(RectTransform contentRoot)
    {
        _pager.Configure(
            contentRoot,
            _backgroundTrack,
            (RectTransform)_contentLayer,
            _chapters.Length,
            ResolveSwipeTarget,
            (panel, index) => BuildChapterContent(panel, _chapters[index], index),
            index => BuildNeighborBackgroundImage(_chapters[index]),
            index =>
            {
                _chapterIndex = index;
                _activeTab = MenuTab.Home;
                BuildMenu();
            },
            OnChapterBlend);
    }

    // Cross-fades the chapter-tinted chrome while a swipe is in flight, so the accent colours
    // travel with the page instead of snapping at commit. Two mechanisms, chosen per element:
    //
    // - Plain Graphic colours (nav outline, dividers, HOME label, top-bar wash) lerp in place
    //   from their as-built colour to the incoming chapter's formula. No layering artifacts.
    // - Sprite-baked colours (Home hexagon gradient + house glyph, frosted-glass backdrops)
    //   can't be re-tinted, so a ghost TWIN - the same object cloned in place, re-skinned for
    //   the incoming chapter - fades in DIRECTLY above its original. Cloning in place keeps
    //   draw order intact: nothing ever draws over elements it sat under in the real bar
    //   (a whole-bar overlay was tried first and mid-fade its bar panel washed out the
    //   overhanging Home hexagon).
    //
    // The real graphics stay fully opaque underneath the twins, so opacity never dips; the
    // commit rebuild lands on exactly the colours the fade reached (shared formula helpers),
    // and BuildMenu sweeping the sections away kills twins and registry together.
    private static int _chromeBlendTarget = -1;

    // Registry - populated by BuildBottomNav / BuildTopStatusBar on every real build.
    private static Image _navOutline;
    private static readonly List<Image> _navDividers = new List<Image>();
    private static Image _navHexImage;
    private static Image _navHouseImage;
    private static Graphic _navHomeLabel;
    private static Image _topBarWashImage;
    private static readonly List<Image> _chromeFrostBlurs = new List<Image>();

    // Live blend state.
    private sealed class ChromeLerp { public Graphic G; public Color From; public Color To; }
    private static readonly List<ChromeLerp> _chromeLerps = new List<ChromeLerp>();
    private static readonly List<CanvasGroup> _chromeTwins = new List<CanvasGroup>();

    private static void OnChapterBlend(int targetIndex, float progress)
    {
        if (progress <= 0.001f || _chapters == null || targetIndex < 0 || targetIndex >= _chapters.Length)
        {
            TearDownChromeBlend();
            return;
        }

        // (Re)arm on a new target, or when a mid-swipe BuildMenu destroyed the twins.
        bool twinsDead = _chromeTwins.Count > 0 && _chromeTwins[0] == null;
        if (_chromeBlendTarget != targetIndex || (_chromeTwins.Count == 0 && _chromeLerps.Count == 0) || twinsDead)
        {
            SetUpChromeBlend(targetIndex);
        }

        float p = Mathf.Clamp01(progress);
        foreach (ChromeLerp lerp in _chromeLerps)
        {
            if (lerp.G != null) lerp.G.color = Color.Lerp(lerp.From, lerp.To, p);
        }
        foreach (CanvasGroup twin in _chromeTwins)
        {
            if (twin != null) twin.alpha = p;
        }
    }

    private static void SetUpChromeBlend(int targetIndex)
    {
        TearDownChromeBlend();
        _chromeBlendTarget = targetIndex;
        ChapterDefinition target = _chapters[targetIndex];
        Color gold = ChapterLight(target);

        AddChromeLerp(_navOutline, WithAlpha(gold, 0.55f));
        foreach (Image divider in _navDividers) AddChromeLerp(divider, WithAlpha(gold, 0.34f));
        AddChromeLerp(_navHomeLabel, HomeGlyphColor(target, active: true));
        if (_topBarWashImage != null)
        {
            AddChromeLerp(_topBarWashImage,
                WithAlpha(Color.Lerp(target.MenuAccentSecondaryColor, TextPrimary, 0.18f), 0.07f));
        }

        // Home hexagon: clone the whole hex (gradient + glyph + label ride along), re-skin
        // the clone for the incoming chapter.
        if (_navHexImage != null)
        {
            Image ghostHex = CreateChromeTwin(_navHexImage);
            ghostHex.sprite = MenuSprites.HexButton(HomeHexTopColor(target), HomeHexBottomColor(target));
            Color glyph = HomeGlyphColor(target, active: true);
            Transform house = ghostHex.transform.Find("HomeIcon");
            if (house != null) house.GetComponent<Image>().sprite = MenuSprites.NavHouse(glyph);
            Transform label = ghostHex.transform.Find("Label");
            if (label != null) label.GetComponent<Graphic>().color = glyph;
        }

        // Frosted-glass backdrops in the top bar show the chapter's backdrop slice.
        Sprite targetBackdrop = target.MenuBackgroundImage;
        if (targetBackdrop != null)
        {
            foreach (Image blur in _chromeFrostBlurs)
            {
                if (blur == null) continue;
                CreateChromeTwin(blur).sprite = targetBackdrop;
            }
        }
    }

    private static void AddChromeLerp(Graphic graphic, Color to)
    {
        if (graphic == null) return;
        _chromeLerps.Add(new ChromeLerp { G = graphic, From = graphic.color, To = to });
    }

    // Clones a chrome element in place, directly above the original in draw order, faded out
    // and raycast-dead. Component copies (MenuFrostedBackdrop, UIEffect) ride along, which the
    // frost twins rely on for their screen-locking and blur.
    private static Image CreateChromeTwin(Image real)
    {
        GameObject clone = UnityEngine.Object.Instantiate(real.gameObject, real.transform.parent);
        clone.name = real.gameObject.name + "Twin";
        clone.transform.SetSiblingIndex(real.transform.GetSiblingIndex() + 1);
        CanvasGroup group = clone.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        _chromeTwins.Add(group);
        return clone.GetComponent<Image>();
    }

    private static void TearDownChromeBlend()
    {
        foreach (ChromeLerp lerp in _chromeLerps)
        {
            if (lerp.G != null) lerp.G.color = lerp.From;   // swipe cancelled: restore
        }
        _chromeLerps.Clear();
        foreach (CanvasGroup twin in _chromeTwins)
        {
            if (twin != null) UnityEngine.Object.Destroy(twin.gameObject);
        }
        _chromeTwins.Clear();
        _chromeBlendTarget = -1;
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
        if (chapter == null) return null;

        // The stored live reference, not a Find: a name lookup can hit the previous, dying
        // track for a frame after a chapter change (see _backgroundTrack).
        RectTransform track = _backgroundTrack;
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

        // A pending chapter reveal builds the card in its locked-mystery look and animates it
        // open a beat later; the swap re-runs FillNextChapterContent with unlocked visuals.
        bool reveal = _revealKind == RevealKind.Chapter;

        RectTransform card = CreateRect(parent, "NextChapterCard",
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            // Bottom edge clears the nav bar (top at 204 with the raised NavBottomMargin) by a
            // real margin, so the card never sits glued to the bar.
            new Vector2(-60f, 232f), new Vector2(300f, 160f));
        Color cardFill = new Color(0.05f, 0.06f, 0.065f, 1f);
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        cardImage.color = cardFill;
        RuntimeUiKit.AddOutline(card, GoldOutline(0.24f));

        // The state-dependent visuals live in one stretched container so the unlock reveal can
        // swap locked -> unlocked content without touching the card root (fill, outline, button).
        RectTransform content = CreateRect(card, "Content",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(content);
        FillNextChapterContent(content, current, next, unlocked && !reveal);

        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = cardImage;
        button.interactable = unlocked && !reveal;
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

        if (reveal) AttachChapterReveal(parent, card, content, current, next, button);
    }

    // The next-chapter card's two faces. Locked is a deliberate MYSTERY: no name, no preview -
    // the player earns even the sight of what comes next, which is what makes the chapter
    // reveal (preview sweeping in, name appearing) worth the wait.
    private static void FillNextChapterContent(RectTransform content, ChapterDefinition current,
        ChapterDefinition next, bool unlocked)
    {
        if (unlocked)
        {
            Sprite preview = current.NextChapterPreviewImage != null
                ? current.NextChapterPreviewImage
                : next.MenuBackgroundImage;
            if (preview != null)
            {
                CreateCoverImage(content, "Preview", preview, new Color(1f, 1f, 1f, 0.42f),
                    Vector2.zero, new Vector2(300f, 160f), new Vector2(0.5f, 0.5f));
            }

            CreateTmp(content, "NextLabel", "NEXT CHAPTER", 15, TextMuted, TextAnchor.MiddleLeft,
                FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(28f, -22f), new Vector2(180f, 26f), new Vector2(0f, 1f));
            CreateTmp(content, "NextTitle", next.DisplayName.ToUpperInvariant(), 21, TextPrimary, TextAnchor.MiddleLeft,
                FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(28f, -50f), new Vector2(206f, 34f), new Vector2(0f, 1f));

            Image nextArrow = CreateImage(content, "NextArrow", MenuSprites.Chevron(TextPrimary), Color.white);
            nextArrow.preserveAspect = true;
            SetCentered(nextArrow.rectTransform, new Vector2(258f, -75f), new Vector2(40f, 40f));
            return;
        }

        CreateTmp(content, "NextLabel", "NEXT CHAPTER", 15, WithAlpha(TextMuted, 0.75f), TextAnchor.MiddleLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(28f, -22f), new Vector2(180f, 26f), new Vector2(0f, 1f));
        CreateTmp(content, "NextTitle", "LOCKED", 21, LockedColor, TextAnchor.MiddleLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(28f, -50f), new Vector2(206f, 34f), new Vector2(0f, 1f));

        Image lockIcon = CreateImage(content, "NextLock", MenuSprites.Lock(LockedColor), Color.white);
        lockIcon.preserveAspect = true;
        SetCentered(lockIcon.rectTransform, new Vector2(258f, -75f), new Vector2(34f, 34f));
    }

    // The next-chapter card's bottom-LEFT mirror: the way back. Chapter 1 has nowhere to go
    // back to, so it carries no card; going back is always allowed (reaching this chapter
    // unlocked the previous one), matching the swipe rule in ResolveSwipeTarget.
    private static void BuildPreviousChapterCard(Transform parent, int chapterIndex)
    {
        if (chapterIndex <= 0) return;

        int prevIndex = chapterIndex - 1;
        ChapterDefinition prev = _chapters[prevIndex];

        RectTransform card = CreateRect(parent, "PreviousChapterCard",
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(60f, 232f), new Vector2(300f, 160f));
        Color cardFill = new Color(0.05f, 0.06f, 0.065f, 1f);
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        cardImage.color = cardFill;
        RuntimeUiKit.AddOutline(card, GoldOutline(0.24f));

        if (prev.MenuBackgroundImage != null)
        {
            CreateCoverImage(card, "Preview", prev.MenuBackgroundImage, new Color(1f, 1f, 1f, 0.42f),
                Vector2.zero, new Vector2(300f, 160f), new Vector2(0.5f, 0.5f));
        }

        // Mirrored layout: texts hug the RIGHT edge, the (flipped) chevron sits bottom-left.
        CreateTmp(card, "PrevLabel", "PREVIOUS", 15, TextMuted, TextAnchor.MiddleRight,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(-28f, -22f), new Vector2(180f, 26f), new Vector2(1f, 1f));
        CreateTmp(card, "PrevTitle", prev.DisplayName.ToUpperInvariant(), 21, TextPrimary, TextAnchor.MiddleRight,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(-28f, -50f), new Vector2(206f, 34f), new Vector2(1f, 1f));

        Image prevArrow = CreateImage(card, "PrevArrow", MenuSprites.Chevron(TextPrimary), Color.white);
        prevArrow.preserveAspect = true;
        SetCentered(prevArrow.rectTransform, new Vector2(42f, -75f), new Vector2(40f, 40f));
        prevArrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, 180f);

        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = cardImage;
        button.onClick.AddListener(() =>
        {
            // Slide back to the previous chapter (entering from the left) through the same
            // transition a swipe uses, so the card and the gesture feel identical.
            if (_pager != null) _pager.AnimateToChapter(prevIndex, -1);
        });

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
        // The mask trims the list for scrolling, but the FIRST card sits only LevelCardTop (6 px)
        // below the viewport top - the active card's halo pokes past that and would get sheared
        // into a hard horizontal line. Relax the mask a touch top and bottom; the list content
        // fits the viewport in normal chapters, so nothing meaningful leaks.
        viewport.gameObject.AddComponent<RectMask2D>().padding = new Vector4(0f, -12f, 0f, -12f);

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

        // A thin auto-hiding scrollbar in the gutter right of the cards, so a chapter with many
        // levels visibly READS as scrollable (on top of the half-card peeking past the mask).
        // AutoHide keeps it entirely absent while the list fits the viewport. Sibling of the
        // viewport, not a child - the mask must not clip it.
        RectTransform sbar = CreateRect(parent, "LevelScrollbar",
            new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
        sbar.offsetMin = new Vector2(-42f, LevelListBottomInset + 12f);
        sbar.offsetMax = new Vector2(-34f, -(LevelListTopInset + 12f));
        Image track = sbar.gameObject.AddComponent<Image>();
        track.sprite = RuntimeSprites.RoundedPanel();
        track.type = Image.Type.Sliced;
        track.pixelsPerUnitMultiplier = 6f; // shrink the panel's corner radius to capsule scale
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
        handleImage.color = WithAlpha(chapter != null ? ChapterLight(chapter) : GoldBase, 0.55f);
        handleImage.raycastTarget = false;
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

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

            // The level this build's unlock reveal targets starts in its LOCKED look; the
            // attached runner animates it open to the real state a beat later.
            if (_revealKind == RevealKind.Level && i == _revealLevelIndex)
            {
                BuildLevelRail(row, i, count, chapter, false, false, false);
                BuildLevelCard(row, chapter, level, i, false, false, false);
                AttachLevelReveal(row, parent, chapter, level, i, count);
                continue;
            }

            BuildLevelRail(row, i, count, chapter, unlocked, completed, current);
            BuildLevelCard(row, chapter, level, i, unlocked, completed, current);
        }
    }

    // The progress rail beside the cards (the concepts' timeline): a thin cream line through
    // every row with one node per level - filled diamond = completed, glowing diamond = the
    // level you are on, small hollow circle = still locked. Purely visual, never intercepts taps.
    private static void BuildLevelRail(Transform row, int index, int count, ChapterDefinition chapter,
        bool unlocked, bool completed, bool current)
    {
        Color chapterLight = ChapterLight(chapter);
        float railX = LevelCardSideInset + 16f;
        float nodeY = -(LevelCardTop + LevelCardHeight * 0.5f);
        float nodeHalf = current ? 26f : (completed ? 17f : 11f);
        Color lineColor = WithAlpha(TextPrimary, 0.30f);

        if (index > 0)
        {
            RectTransform seg = CreateRect(row, "RailUp",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 1f),
                new Vector2(railX, 0f), new Vector2(3f, -nodeY - nodeHalf));
            Image segImage = seg.gameObject.AddComponent<Image>();
            segImage.color = lineColor;
            segImage.raycastTarget = false;
        }
        if (index < count - 1)
        {
            float top = -nodeY + nodeHalf;
            RectTransform seg = CreateRect(row, "RailDown",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 1f),
                new Vector2(railX, -top), new Vector2(3f, LevelRowHeight - top));
            Image segImage = seg.gameObject.AddComponent<Image>();
            segImage.color = lineColor;
            segImage.raycastTarget = false;
        }

        if (current)
        {
            Image glow = CreateImage(row, "RailGlow", RuntimeSprites.SoftBlob(), WithAlpha(chapterLight, 0.6f));
            glow.raycastTarget = false;
            SetCenteredAt(glow.rectTransform, new Vector2(0f, 1f), new Vector2(railX, nodeY), new Vector2(96f, 96f));
            Image node = CreateImage(row, "RailNode",
                MenuSprites.DiamondBadge(Color.Lerp(chapterLight, Color.white, 0.35f), Color.white), Color.white);
            node.raycastTarget = false;
            SetCenteredAt(node.rectTransform, new Vector2(0f, 1f), new Vector2(railX, nodeY), new Vector2(44f, 44f));
        }
        else if (completed)
        {
            Image node = CreateImage(row, "RailNode",
                MenuSprites.DiamondBadge(WithAlpha(TextPrimary, 0.92f), WithAlpha(chapterLight, 0.9f)), Color.white);
            node.raycastTarget = false;
            SetCenteredAt(node.rectTransform, new Vector2(0f, 1f), new Vector2(railX, nodeY), new Vector2(30f, 30f));
        }
        else
        {
            Image node = CreateImage(row, "RailNode",
                MenuSprites.CircleBadge(WithAlpha(TextPrimary, 0.35f), WithAlpha(TextPrimary, 0.95f)), Color.white);
            node.raycastTarget = false;
            SetCenteredAt(node.rectTransform, new Vector2(0f, 1f), new Vector2(railX, nodeY), new Vector2(20f, 20f));
        }
    }

    // Which little glyph sits before the challenge label (the concepts' cube/waves/mountain marks).
    private static string GoalGlyphKind(string challengeLabel)
    {
        string label = challengeLabel != null ? challengeLabel.ToUpperInvariant() : "";
        if (label.Contains("TIMED")) return "timer";
        if (label.Contains("WAVE")) return "waves";
        if (label.Contains("HEIGHT")) return "mountain";
        return "cube";
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
        card.offsetMin = new Vector2(LevelCardSideInset + LevelRailGutter, -(LevelCardTop + LevelCardHeight));
        card.offsetMax = new Vector2(-LevelCardSideInset, -LevelCardTop);
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        // Locked cards are deliberately DARKER and near-opaque (not lighter glass): the contrast
        // against the live cards is what makes "locked" legible at a glance, and what gives the
        // unlock reveal its before/after pop.
        Color cardFill = MenuGlassFill(chapter, unlocked ? 0.80f : 0.94f);
        cardImage.color = cardFill;

        AddFrostedGlass(card, chapter != null ? chapter.MenuBackgroundImage : null, LevelCardFrostWash);

        // Every card gets a thin cream border; the active one uses the CHAPTER colour (a bright
        // warm gold) at full strength plus the glow, so it reads as "this chapter's" highlight.
        Color cardBorder = current
            ? WithAlpha(Color.Lerp(chapterLight, Color.white, 0.25f), 1f)
            : WithAlpha(TextPrimary, unlocked ? 0.34f : 0.10f);
        RuntimeUiKit.AddOutline(card, cardBorder);

        if (current)
        {
            // A REAL glow: the 9-sliced GlowFrame ring (soft baked falloff) stretched slightly
            // past the card. Reads as a thin bright rim that blooms outward - never a slab.
            // Kept SMALL and quiet: the crisp border above carries the highlight, the halo
            // just breathes a few pixels past it.
            const float grow = 12f;
            RectTransform halo = CreateRect(parent, "ActiveHalo",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, Vector2.zero);
            halo.offsetMin = new Vector2(LevelCardSideInset + LevelRailGutter - grow, -(LevelCardTop + LevelCardHeight + grow));
            halo.offsetMax = new Vector2(-(LevelCardSideInset - grow), -(LevelCardTop - grow));
            halo.SetAsFirstSibling(); // behind the card
            Image haloImage = halo.gameObject.AddComponent<Image>();
            haloImage.sprite = MenuSprites.GlowFrame();
            haloImage.type = Image.Type.Sliced;
            haloImage.color = WithAlpha(Color.Lerp(chapterLight, Color.white, 0.2f), 0.6f);
            haloImage.raycastTarget = false;
        }

        Sprite thumbSprite = level.MenuThumbnail != null
            ? level.MenuThumbnail
            : MenuSprites.LevelThumbnail(index, chapter.MenuAccentColor, chapter.MenuAccentSecondaryColor);
        RectTransform thumb = CreateCoverImage(card, "Thumbnail", thumbSprite,
            unlocked ? Color.white : new Color(0.30f, 0.30f, 0.34f, 0.45f),
            new Vector2(22f, -16f), new Vector2(132f, 152f), new Vector2(0f, 1f));
        RuntimeUiKit.AddOutline(thumb, WithAlpha(TextPrimary, unlocked ? 0.18f : 0.08f));

        // Hollow diamond outline (faint fill, bright crisp cream border), aligned to the TITLE
        // row near the top of the card - sitting in the gap between the thumbnail and the title.
        Color edgeColor = ChapterEdge(chapterLight);
        Image numberPlate = CreateImage(card, "NumberPlate",
            MenuSprites.DiamondBadge(MenuGlassFill(chapter, unlocked ? 0.16f : 0.10f),
                WithAlpha(edgeColor, unlocked ? 1f : 0.28f)),
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

        // Type row: a small goal glyph (cube/waves/mountain/timer) + the label with real TMP
        // letter-spacing - the concepts' "icon in front of the game type".
        Color challengeColor = unlocked ? chapterLight : LockedColor;
        RectTransform challengeRow = CreateRect(column, "ChallengeRow",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        LayoutElement challengeLayout = challengeRow.gameObject.AddComponent<LayoutElement>();
        challengeLayout.preferredHeight = 24f;
        challengeLayout.flexibleHeight = 0f;
        HorizontalLayoutGroup challengeGroup = challengeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        challengeGroup.spacing = 8f;
        challengeGroup.childAlignment = TextAnchor.MiddleLeft;
        challengeGroup.childControlWidth = false;
        challengeGroup.childControlHeight = false;
        challengeGroup.childForceExpandWidth = false;
        challengeGroup.childForceExpandHeight = false;

        Image goalIcon = CreateImage(challengeRow, "GoalIcon",
            MenuSprites.GoalGlyph(GoalGlyphKind(presentation.ChallengeLabel), challengeColor), Color.white);
        goalIcon.raycastTarget = false;
        goalIcon.rectTransform.sizeDelta = new Vector2(22f, 22f);

        TextMeshProUGUI challenge = CreateTmp(challengeRow, "Challenge", presentation.ChallengeLabel.ToUpperInvariant(), 17,
            challengeColor, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.DefaultFont);
        challenge.characterSpacing = 5f;
        challenge.rectTransform.sizeDelta = new Vector2(420f, 24f);

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
        // Locked cards render their own (dark, near-opaque) fill untinted; the old translucent
        // disabled wash made locked read LIGHTER than live cards.
        colors.disabledColor = Color.white;
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
