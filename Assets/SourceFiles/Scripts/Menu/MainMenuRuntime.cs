using System;
using System.Collections.Generic;
using System.Globalization;
using Coffee.UIEffects;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using static RuntimeUiKit;

/// <summary>
/// Runtime-built main menu: top profile/status bar, Play chapter screen, bottom navigation,
/// and placeholder pages for tabs that do not have real content yet.
/// </summary>
public static class MainMenuRuntime
{
    private const string LevelsResourcesPath = "Levels";

    // Frosted-glass darkness: alpha of the dark wash drawn over each panel's blur (higher = more
    // black / more readable). Tuned per surface; see AddFrostedGlass.
    private const float TopBarFrostWash = 0.68f;
    private const float CurrencyCardFrostWash = 0.82f;
    private const float LevelCardFrostWash = 0.95f;

    private const float LevelListTopInset = 485f;
    private const float LevelListBottomInset = 205f;
    private const float LevelRowHeight = 220f;
    private const float LevelCardHeight = 184f;
    private const float LevelCardTop = 6f;
    // Phone-edge padding: the gap from each screen edge to a level card. Cards stretch to fill
    // the row between these insets, so their width tracks the screen width on any device. Matches
    // the chapter title's left inset (see ChapterTitle x) so the cards line up under the title.
    private const float LevelCardSideInset = 76f;
    // Action badge (the play / check / lock circle) centre, measured in from the card's RIGHT
    // edge so it rides the edge as the card widens.
    private const float LevelCardActionInsetRight = 72f;
    // Text column region inside a card: starts right of the thumbnail/number plate, ends
    // before the action badge. The column itself flows with a layout group, so these are
    // the only two card-internal x values left to tune.
    private const float LevelCardTextLeft = 248f;
    private const float LevelCardTextRight = 124f;

    private static readonly Color TextPrimary = new Color(0.96f, 0.93f, 0.86f, 1f);
    private static readonly Color TextMuted = new Color(0.74f, 0.7f, 0.64f, 1f);
    private static readonly Color LockedColor = new Color(0.44f, 0.46f, 0.48f, 1f);
    private static readonly Color CardDark = new Color(0.07f, 0.06f, 0.05f, 0.76f);
    private static readonly Color GoldBase = new Color(1f, 0.9f, 0.68f, 1f);
    private static readonly Color GlassBorder = new Color(1f, 0.92f, 0.74f, 0.18f);

    private enum MenuTab
    {
        Shop,
        Chapters,
        Home,
        Vault,
        Settings
    }

    private static GameObject _root;
    private static Transform _backgroundLayer;
    private static Transform _contentLayer;
    private static Transform _topStatusLayer;
    private static Transform _navLayer;
    private static GameObject _contentRoot;
    private static GameObject _topStatusRoot;
    private static GameObject _navRoot;
    private static ChapterDefinition _backgroundChapter;
    private static MenuChapterPager _pager;
    private static ChapterDefinition[] _chapters = Array.Empty<ChapterDefinition>();
    private static int _chapterIndex;
    private static bool _chapterIndexInitialized;
    private static MenuTab _activeTab = MenuTab.Home;
    private static RenderTexture _videoTexture;

    private static Color GoldOutline(float alpha)
    {
        Color color = GoldBase;
        color.a = alpha;
        return color;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private static Color ChapterLight(ChapterDefinition chapter)
    {
        return chapter != null ? Color.Lerp(chapter.MenuAccentColor, TextPrimary, 0.46f) : GoldBase;
    }

    // Brighter, near-cream edge used for crisp thin borders (number diamond, action circle).
    private static Color ChapterEdge(Color chapterLight) => Color.Lerp(chapterLight, TextPrimary, 0.5f);

    private static Color ChapterDark(ChapterDefinition chapter)
    {
        if (chapter == null) return GoldBase;
        return Color.Lerp(chapter.MenuAccentSecondaryColor, chapter.MenuAccentColor, 0.22f);
    }

    private static Color MenuGlassFill(ChapterDefinition chapter, float alpha)
    {
        Color panel = chapter != null ? chapter.MenuPanelColor : CardDark;
        Color fill = Color.Lerp(new Color(0.012f, 0.011f, 0.01f, 1f), panel, 0.34f);
        fill.a = alpha;
        return fill;
    }

    private static string TrackedUpper(string value, string letterGap, string wordGap)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        string[] words = value.ToUpperInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int w = 0; w < words.Length; w++)
        {
            char[] chars = words[w].ToCharArray();
            words[w] = string.Join(letterGap, Array.ConvertAll(chars, c => c.ToString()));
        }
        return string.Join(wordGap, words);
    }

    private static void SetCentered(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
        => SetCenteredAt(rect, new Vector2(0f, 1f), anchoredPosition, size);

    // As SetCentered but lets the caller pick which parent corner the element is pinned to, so a
    // badge can ride the right edge (anchor (1, 1)) of a stretched card instead of a fixed x.
    private static void SetCenteredAt(RectTransform rect, Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        LevelSelectionState.ClearSelection();
        _root = null;
        _backgroundLayer = null;
        _contentLayer = null;
        _topStatusLayer = null;
        _navLayer = null;
        _contentRoot = null;
        _topStatusRoot = null;
        _navRoot = null;
        _backgroundChapter = null;
        _chapters = Array.Empty<ChapterDefinition>();
        _chapterIndex = 0;
        _chapterIndexInitialized = false;
        _activeTab = MenuTab.Home;
        ReleaseVideoTexture();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void PrepareSelection()
    {
        LevelSelectionState.BeginSelectionIfNeeded();
    }

    /// <summary>
    /// Quit the current run back to the main menu. RuntimeInitializeOnLoadMethod hooks only
    /// fire at app start, so this re-shows the menu manually after the scene reload.
    /// </summary>
    public static void ReturnToMenu()
    {
        LevelSelectionState.ClearSelection();
        LevelSelectionState.BeginSelectionIfNeeded();
        Time.timeScale = 1f;
        SceneManager.sceneLoaded += ShowMenuOnceAfterLoad;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private static void ShowMenuOnceAfterLoad(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= ShowMenuOnceAfterLoad;
        ShowMenuIfNeeded();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ShowMenuIfNeeded()
    {
        if (!LevelSelectionState.IsSelectionPending) return;

        LevelDefinition[] levels = Resources.LoadAll<LevelDefinition>(LevelsResourcesPath);
        if (levels == null || levels.Length == 0)
        {
            LevelSelectionState.SelectLevel(null);
            Time.timeScale = 1f;
            return;
        }

        Time.timeScale = 0f;
        RuntimeUiKit.EnsureEventSystem();
        BuildMenu();
        MusicPlayer.PlayMenu(); // menu soundtrack plays everywhere outside a level
    }

    private static void BuildMenu()
    {
        _chapters = LoadChaptersWithLevels();

        if (_chapters.Length == 0)
        {
            if (ContentCatalog.IsAvailable)
            {
                OpenCustomGame();
                return;
            }

            LevelSelectionState.SelectLevel(null);
            Time.timeScale = 1f;
            return;
        }

        if (!_chapterIndexInitialized)
        {
            _chapterIndex = DefaultChapterIndex(_chapters);
            _chapterIndexInitialized = true;
        }
        _chapterIndex = Mathf.Clamp(_chapterIndex, 0, _chapters.Length - 1);
        ChapterDefinition chapter = _chapters[_chapterIndex];

        EnsureRoot();
        RefreshBackground(chapter);

        Transform topRoot = RecreateSection(ref _topStatusRoot, _topStatusLayer, "TopStatusRoot");
        BuildTopStatusBar(topRoot, chapter);

        Transform contentRoot = RecreateSection(ref _contentRoot, _contentLayer, "ContentRoot");
        if (_activeTab == MenuTab.Home) BuildPlayScreen(contentRoot, chapter);
        else BuildDummyScreen(contentRoot, _activeTab);

        Transform navRoot = RecreateSection(ref _navRoot, _navLayer, "BottomNavRoot");
        BuildBottomNav(navRoot);
    }

    private static ChapterDefinition[] LoadChaptersWithLevels()
    {
        List<ChapterDefinition> chapters = new List<ChapterDefinition>();
        foreach (ChapterDefinition chapter in Campaign.LoadChaptersInOrder())
        {
            if (chapter != null && chapter.Levels != null && chapter.Levels.Count > 0)
            {
                chapters.Add(chapter);
            }
        }
        return chapters.ToArray();
    }

    private static int DefaultChapterIndex(ChapterDefinition[] chapters)
    {
        for (int i = 0; i < chapters.Length; i++)
        {
            if (Campaign.IsChapterUnlocked(chapters, i) && chapters[i].Levels.Count > 1) return i;
        }
        for (int i = 0; i < chapters.Length; i++)
        {
            if (Campaign.IsChapterUnlocked(chapters, i)) return i;
        }
        return 0;
    }

    private static void TearDownRoot()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null;
        _backgroundLayer = null;
        _contentLayer = null;
        _topStatusLayer = null;
        _navLayer = null;
        _contentRoot = null;
        _topStatusRoot = null;
        _navRoot = null;
        _backgroundChapter = null;
        _pager = null;
        ReleaseVideoTexture();
    }

    private static void EnsureRoot()
    {
        if (_root != null) return;

        _root = RuntimeUiKit.CreateOverlayCanvas("Main Menu", 5000);
        // The background bleeds full-screen behind any notch/cutout; everything readable and
        // interactive lives inside a SafeAreaFitter so the top bar clears the camera and the
        // bottom nav clears the home indicator on every phone (no-op on notchless screens).
        _backgroundLayer = CreateLayer(_root.transform, "BackgroundLayer");
        Transform safeLayer = CreateLayer(_root.transform, "SafeAreaLayer");
        safeLayer.gameObject.AddComponent<SafeAreaFitter>();
        _contentLayer = CreateLayer(safeLayer, "ContentLayer");
        _topStatusLayer = CreateLayer(safeLayer, "TopStatusLayer");
        _navLayer = CreateLayer(safeLayer, "NavigationLayer");
        _pager = _root.AddComponent<MenuChapterPager>();
    }

    private static Transform CreateLayer(Transform parent, string name)
    {
        RectTransform layer = CreateRect(parent, name,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(layer);
        return layer;
    }

    private static Transform RecreateSection(ref GameObject current, Transform parent, string name)
    {
        if (current != null) UnityEngine.Object.Destroy(current);

        RectTransform section = CreateRect(parent, name,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(section);
        current = section.gameObject;
        return section;
    }

    private static void RefreshBackground(ChapterDefinition chapter)
    {
        if (_backgroundLayer == null) return;
        if (_backgroundChapter == chapter && _backgroundLayer.childCount > 0) return;

        ClearChildren(_backgroundLayer);
        ReleaseVideoTexture();
        _backgroundChapter = chapter;
        BuildBackground(_backgroundLayer, chapter);
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }
    }

    private static void ReleaseVideoTexture()
    {
        if (_videoTexture == null) return;
        _videoTexture.Release();
        UnityEngine.Object.Destroy(_videoTexture);
        _videoTexture = null;
    }

    private static void BuildBackground(Transform parent, ChapterDefinition chapter)
    {
        Color top = Color.Lerp(chapter.MenuAccentSecondaryColor, Color.black, 0.35f);
        Color bottom = Color.Lerp(chapter.MenuAccentColor, Color.black, 0.68f);

        // The chapter imagery (image + video) lives on a movable track so a swipe can slide
        // it - and the incoming chapter's background, parented into the same track - as one
        // motion with the foreground. The dimming overlays below sit on the fixed layer so
        // the whole screen stays evenly dimmed no matter where the track is panned.
        RectTransform track = (RectTransform)CreateLayer(parent, "BgTrack");

        if (chapter.MenuBackgroundImage != null)
        {
            Image image = CreateImage(track, "BackgroundImage", chapter.MenuBackgroundImage, Color.white);
            Stretch(image.rectTransform);
            image.preserveAspect = false;
        }
        else
        {
            Image fallback = CreateImage(track, "GeneratedBackground",
                MenuSprites.Background(top, bottom, chapter.MenuAccentColor), Color.white);
            Stretch(fallback.rectTransform);
        }

        if (chapter.MenuBackgroundVideo != null)
        {
            _videoTexture = new RenderTexture(720, 1280, 0, RenderTextureFormat.ARGB32);
            _videoTexture.name = "MenuBackgroundVideoRT";
            _videoTexture.hideFlags = HideFlags.HideAndDontSave;
            _videoTexture.Create();

            RawImage videoImage = CreateRawImage(track, "BackgroundVideo", _videoTexture, Color.white);
            Stretch(videoImage.rectTransform);
            videoImage.color = new Color(1f, 1f, 1f, 0f);

            GameObject playerObject = new GameObject("BackgroundVideoPlayer");
            playerObject.transform.SetParent(track, false);
            VideoPlayer player = playerObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = _videoTexture;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.clip = chapter.MenuBackgroundVideo;
            player.prepareCompleted += source =>
            {
                if (videoImage == null || source == null) return;
                videoImage.color = Color.white;
                source.Play();
            };
            player.Prepare();
        }

        Image dim = CreateImage(parent, "ReadabilityOverlay", RuntimeSprites.Square(),
            new Color(0.02f, 0.018f, 0.014f, 0.24f));
        Stretch(dim.rectTransform);
    }

    private static void BuildTopStatusBar(Transform parent, ChapterDefinition chapter)
    {
        PlayerProfileStore.Snapshot profile = PlayerProfileStore.Current;
        Color chapterTint = chapter != null ? chapter.MenuAccentSecondaryColor : GoldBase;
        Sprite statBackground = chapter != null ? chapter.MenuBackgroundImage : null;

        RectTransform bar = CreateRect(parent, "TopStatusBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -34f), new Vector2(-48f, 122f));
        Image barImage = bar.gameObject.AddComponent<Image>();
        barImage.sprite = RuntimeSprites.RoundedPanel();
        barImage.type = Image.Type.Sliced;
        barImage.color = WithAlpha(Color.Lerp(chapterTint, TextPrimary, 0.18f), 0.07f);
        AddFrostedGlass(bar, statBackground, TopBarFrostWash);
        RuntimeUiKit.AddOutline(bar, GlassBorder);

        HorizontalLayoutGroup layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 14, 14);
        layout.spacing = 24f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        Image badge = CreateImage(bar, "LevelBadge",
            MenuSprites.PointHexBadge(
                new Color(0.10f, 0.095f, 0.085f, 0.62f),
                new Color(0.035f, 0.032f, 0.028f, 0.72f),
                GlassBorder),
            Color.white);
        LayoutElement badgeLayout = badge.gameObject.AddComponent<LayoutElement>();
        badgeLayout.preferredWidth = 82f;
        badgeLayout.preferredHeight = 82f;
        CreateTmp(badge.transform, "LevelText", profile.PlayerLevel.ToString(), 30, TextPrimary,
            TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.DefaultFont);

        RectTransform profileColumn = CreateRect(bar, "ProfileInfo",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        LayoutElement profileLayout = profileColumn.gameObject.AddComponent<LayoutElement>();
        profileLayout.minWidth = 210f;
        profileLayout.preferredWidth = 210f;
        profileLayout.preferredHeight = 82f;

        VerticalLayoutGroup profileStack = profileColumn.gameObject.AddComponent<VerticalLayoutGroup>();
        profileStack.padding = new RectOffset(0, 0, 16, 17);
        profileStack.spacing = 8f;
        profileStack.childAlignment = TextAnchor.MiddleLeft;
        profileStack.childControlWidth = true;
        profileStack.childControlHeight = true;
        profileStack.childForceExpandWidth = true;
        profileStack.childForceExpandHeight = false;

        TextMeshProUGUI playerName = CreateTmp(profileColumn, "PlayerName", profile.PlayerName, 18, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont,
            Vector2.zero, new Vector2(0f, 27f), new Vector2(0f, 1f));
        AutoSize(playerName, 14, 18);
        playerName.gameObject.AddComponent<LayoutElement>().preferredHeight = 27f;

        Image expTrack = CreateImage(profileColumn, "ExpTrack", RuntimeSprites.RoundedPanel(),
            new Color(0.02f, 0.019f, 0.017f, 0.36f));
        expTrack.type = Image.Type.Sliced;
        LayoutElement expLayout = expTrack.gameObject.AddComponent<LayoutElement>();
        expLayout.preferredWidth = 195f;
        expLayout.preferredHeight = 7f;
        expLayout.flexibleWidth = 0f;
        Image expFill = CreateImage(expTrack.transform, "ExpFill", RuntimeSprites.RoundedPanel(),
            new Color(1f, 0.72f, 0.32f, 1f));
        expFill.type = Image.Type.Sliced;
        RectTransform expFillRect = expFill.rectTransform;
        expFillRect.anchorMin = new Vector2(0f, 0f);
        expFillRect.anchorMax = new Vector2(Mathf.Clamp01(profile.Experience01), 1f);
        expFillRect.offsetMin = Vector2.zero;
        expFillRect.offsetMax = Vector2.zero;

        RectTransform spacer = CreateRect(bar, "StatusSpacer",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        LayoutElement spacerLayout = spacer.gameObject.AddComponent<LayoutElement>();
        spacerLayout.minWidth = 24f;
        spacerLayout.flexibleWidth = 1f;

        BuildCurrencyCard(bar, statBackground, "$", profile.Coins.ToString("N0", CultureInfo.InvariantCulture), null);
        BuildCurrencyCard(bar, statBackground, null,
            $"{profile.Lives}/{profile.MaxLives}",
            $"{profile.LifeRefillRemaining.Minutes:00}:{profile.LifeRefillRemaining.Seconds:00}");
    }

    // Turns a freshly-built card (root = RoundedPanel fill) into a frosted-glass panel: a blurred
    // copy of the chapter background, clipped to the card's rounded silhouette and kept aligned to
    // the screen as the card scrolls/swipes, under a dark wash for legibility. Call right after the
    // fill image and BEFORE adding content so content draws on top. No-op without a background
    // (the card keeps its plain darkened fill).
    private static void AddFrostedGlass(RectTransform card, Sprite background, float washAlpha, float blurScale = 2f)
    {
        if (background == null) return;

        // Rounded clip frame, ignored by layout groups (e.g. the top bar's HorizontalLayoutGroup)
        // so it never counts as a layout item.
        RectTransform frame = CreateRect(card, "FrostedGlass",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        MakeRoundedMask(frame);
        frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        // Blurred backdrop: the chapter background, screen-locked (so each card shows the slice
        // behind it) and blurred via UIEffect (which blurs the element's own texture).
        Image blur = CreateImage(frame, "Blur", background, Color.white);
        blur.gameObject.AddComponent<MenuFrostedBackdrop>();
        UIEffect blurFx = blur.gameObject.AddComponent<UIEffect>();
        blurFx.samplingFilter = SamplingFilter.BlurFast;
        blurFx.samplingScale = blurScale;

        // Dark wash over the blur so text stays readable against bright backgrounds.
        Image wash = CreateImage(frame, "Wash", RuntimeSprites.RoundedPanel(), new Color(0.03f, 0.028f, 0.025f, washAlpha));
        wash.type = Image.Type.Sliced;
        Stretch(wash.rectTransform);
    }

    private static void BuildCurrencyCard(Transform parent, Sprite background, string coinGlyph, string primary, string secondary)
    {
        RectTransform card = CreateRect(parent, "StatusCard",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(232f, 70f));
        LayoutElement cardLayout = card.gameObject.AddComponent<LayoutElement>();
        cardLayout.preferredWidth = 232f;
        cardLayout.preferredHeight = 70f;
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        cardImage.color = new Color(0.02f, 0.018f, 0.016f, 0.68f);
        AddFrostedGlass(card, background, CurrencyCardFrostWash);
        RuntimeUiKit.AddOutline(card, GlassBorder);

        if (!string.IsNullOrEmpty(coinGlyph))
        {
            Sprite coinIcon = MenuIcon("coin");
            if (coinIcon != null)
            {
                Image coin = CreateImage(card, "Coin", coinIcon, Color.white);
                coin.preserveAspect = true;
                SetRect(coin.rectTransform, new Vector2(18f, 0f), new Vector2(48f, 48f), new Vector2(0f, 0.5f));
            }
            else
            {
                // Fallback if the coin art is missing: the procedural golden bubble with a "$".
                Image coin = CreateImage(card, "Coin", RuntimeSprites.Bubble(), new Color(1f, 0.7f, 0.16f, 1f));
                SetRect(coin.rectTransform, new Vector2(18f, 0f), new Vector2(46f, 46f), new Vector2(0f, 0.5f));
                CreateTmp(coin.transform, "CoinGlyph", coinGlyph, 20, new Color(0.28f, 0.15f, 0.02f, 1f),
                    TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.DefaultFont);
            }
        }
        else
        {
            Sprite heartIcon = MenuIcon("heart");
            if (heartIcon != null)
            {
                Image heart = CreateImage(card, "Heart", heartIcon, Color.white);
                heart.preserveAspect = true;
                SetRect(heart.rectTransform, new Vector2(18f, 0f), new Vector2(50f, 50f), new Vector2(0f, 0.5f));
            }
            else
            {
                // Fallback if the heart art is missing: the procedural heart sprite.
                Image heart = CreateImage(card, "Heart", RuntimeSprites.Heart(), new Color(1f, 0.22f, 0.15f, 1f));
                SetRect(heart.rectTransform, new Vector2(18f, 0f), new Vector2(50f, 50f), new Vector2(0f, 0.5f));
            }
        }

        Vector2 primaryPosition = string.IsNullOrEmpty(secondary) ? new Vector2(78f, 0f) : new Vector2(78f, 12f);
        TextMeshProUGUI primaryText = CreateTmp(card, "Primary", primary, 23, TextPrimary, TextAnchor.MiddleLeft,
            FontStyle.Normal, RuntimeUiKit.DefaultFont, primaryPosition, new Vector2(96f, 34f), new Vector2(0f, 0.5f));
        AutoSize(primaryText, 16, 23);
        if (!string.IsNullOrEmpty(secondary))
        {
            CreateTmp(card, "Secondary", secondary, 17, TextMuted, TextAnchor.MiddleLeft,
                FontStyle.Normal, RuntimeUiKit.DefaultFont, new Vector2(78f, -14f), new Vector2(96f, 24f), new Vector2(0f, 0.5f));
        }

        // Divider + add button, pinned to the card's RIGHT edge (pivot-centred) rather than a
        // fixed left offset. Both cards then match exactly and the "+" keeps an even margin from
        // the edge instead of overflowing it - independent of the card's laid-out width.
        Image divider = CreateImage(card, "Divider", RuntimeSprites.Square(), WithAlpha(TextPrimary, 0.28f));
        SetCenteredAt(divider.rectTransform, new Vector2(1f, 0.5f), new Vector2(-52f, 0f), new Vector2(1.5f, 38f));
        TextMeshProUGUI plus = CreateTmp(card, "Plus", "+", 32, TextPrimary, TextAnchor.MiddleCenter,
            FontStyle.Normal, RuntimeUiKit.DefaultFont);
        SetCenteredAt(plus.rectTransform, new Vector2(1f, 0.5f), new Vector2(-26f, 0f), new Vector2(44f, 44f));
    }

    // Cached menu icons loaded by short name from Resources/Menu (e.g. "coin", "heart"). Drop a
    // transparent PNG into Assets/Resources/Menu and it imports as a sprite automatically (see
    // MenuArtImportSettings); a missing file returns null so call sites can fall back.
    private static readonly Dictionary<string, Sprite> MenuIconCache = new Dictionary<string, Sprite>();

    private static Sprite MenuIcon(string name)
    {
        if (!MenuIconCache.TryGetValue(name, out Sprite sprite))
        {
            sprite = Resources.Load<Sprite>($"Menu/{name}");
            MenuIconCache[name] = sprite;
        }
        return sprite;
    }

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

    // Level pre-launch summary modal: tapping a level no longer launches it directly - it
    // opens this (level image + stats + a big Start Game button). Close button (top-right) or
    // a tap on the dimmed backdrop dismisses it. Styling is intentionally minimal for now.
    private static void OpenLevelSummary(ChapterDefinition chapter, LevelDefinition level, int index, bool completed)
    {
        SfxPlayer.Play("ui-button-click");

        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Level Summary", 5500);
        void Close() => UnityEngine.Object.Destroy(overlay);

        Color lightChapter = ChapterLight(chapter);                 // labels + challenge type
        Color darkChapter = chapter.MenuAccentColor;                // "your best" value (the amber)

        // Heavily blurred, darkened copy of the chapter backdrop so the sharp menu behind reads as
        // fully out of focus; the modal itself stays 100% opaque on top of it.
        Sprite backdropSprite = chapter.MenuBackgroundImage;
        if (backdropSprite != null)
        {
            Image blur = CreateImage(overlay.transform, "BlurBackdrop", backdropSprite, Color.white);
            Stretch(blur.rectTransform);
            blur.preserveAspect = false;
            UIEffect blurFx = blur.gameObject.AddComponent<UIEffect>();
            blurFx.samplingFilter = SamplingFilter.BlurFast;
            blurFx.samplingScale = 7f;
        }
        Image backdrop = CreateImage(overlay.transform, "Backdrop", null,
            new Color(0.02f, 0.02f, 0.03f, backdropSprite != null ? 0.58f : 0.92f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        Button backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(Close);

        // Centered, fully opaque panel. Its own raycast target swallows taps so they don't reach
        // the backdrop. Layout flows top-down: thumbnail, challenge type, title, stat cards,
        // description, then the play / ranks buttons pinned to the bottom.
        const float W = 880f;
        const float H = 840f;
        const float pad = 44f;
        const float contentW = W - pad * 2f;
        Color panelColor = new Color(0.075f, 0.065f, 0.058f, 1f);
        RectTransform panel = CreateRect(overlay.transform, "Panel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(W, H));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = RuntimeSprites.RoundedPanel();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = panelColor;
        panelImage.raycastTarget = true;
        RuntimeUiKit.AddOutline(panel, GoldOutline(0.22f));

        // Thumbnail, full-bleed across the top (rounded corners match the panel).
        const float imgH = 360f;
        Sprite thumb = level.MenuThumbnail != null
            ? level.MenuThumbnail
            : MenuSprites.LevelThumbnail(index, chapter.MenuAccentColor, chapter.MenuAccentSecondaryColor);
        CreateCoverImage(panel, "Image", thumb, Color.white,
            new Vector2(0f, 0f), new Vector2(W, imgH), new Vector2(0.5f, 1f));

        // Scrim: the image's lower half fades to the panel colour, so the type/title read on it
        // and the bottom edge blends seamlessly into the panel body.
        Image scrim = CreateImage(panel, "ImageScrim",
            MenuSprites.VerticalFade(WithAlpha(panelColor, 0f), panelColor), Color.white);
        SetRect(scrim.rectTransform, new Vector2(0f, -120f), new Vector2(W, imgH - 120f), new Vector2(0.5f, 1f));
        scrim.raycastTarget = false;

        // Challenge type + title sit on the image's lower-left, over the scrim.
        LevelMenuPresentation.Snapshot presentation = LevelMenuPresentation.Build(level, completed);
        TextMeshProUGUI challenge = CreateTmp(panel, "Challenge", presentation.ChallengeLabel, 20,
            lightChapter, TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(pad, -258f), new Vector2(contentW, 28f), new Vector2(0f, 1f));
        challenge.characterSpacing = 4f;

        // Title (bold white), baseline near the image bottom.
        CreateTmp(panel, "Title", level.DisplayName.ToUpperInvariant(), 50, TextPrimary, TextAnchor.UpperLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(pad, -290f), new Vector2(contentW, 64f), new Vector2(0f, 1f));

        // Target + Your Best stat cards.
        DeriveTargetAndBest(level, presentation, completed, out string targetText, out string bestText);
        float cardW = (contentW - 18f) / 2f;
        BuildSummaryStat(panel, "Target", new Vector2(pad, -394f), cardW, "TARGET", targetText, lightChapter, TextPrimary);
        BuildSummaryStat(panel, "Best", new Vector2(pad + cardW + 18f, -394f), cardW, "YOUR BEST", bestText, lightChapter, darkChapter);

        // Description (thin, muted) - the level's instruction line.
        if (!string.IsNullOrWhiteSpace(level.Instruction))
        {
            CreateTmp(panel, "Description", level.Instruction, 23, new Color(0.78f, 0.75f, 0.70f, 1f),
                TextAnchor.UpperLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont,
                new Vector2(pad, -528f), new Vector2(contentW, 130f), new Vector2(0f, 1f));
        }

        // Play (gradient gold) + Ranks (dark) buttons, pinned to the bottom.
        LevelDefinition selected = level;
        float playW = 524f;
        Image playBg = CreateImage(panel, "Play", MenuSprites.RoundedGradient(
            Color.Lerp(chapter.PlayButtonTopColor, Color.white, 0.06f), chapter.PlayButtonBottomColor), Color.white);
        playBg.type = Image.Type.Sliced;
        SetRect(playBg.rectTransform, new Vector2(pad, 44f), new Vector2(playW, 112f), new Vector2(0f, 0f));
        playBg.raycastTarget = true;
        Image playIcon = CreateImage(playBg.transform, "PlayIcon", MenuSprites.TrianglePlay(), TextPrimary);
        SetCenteredAt(playIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-64f, 0f), new Vector2(38f, 38f));
        CreateTmp(playBg.transform, "PlayLabel", "PLAY", 36, TextPrimary,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(24f, 0f), new Vector2(220f, 48f), new Vector2(0.5f, 0.5f));
        Button playButton = playBg.gameObject.AddComponent<Button>();
        playButton.targetGraphic = playBg;
        playButton.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-start-game");
            SelectLevel(selected);
        });

        float ranksX = pad + playW + 18f;
        float ranksW = contentW - playW - 18f;
        Image ranksBg = CreateImage(panel, "Ranks", RuntimeSprites.RoundedPanel(), new Color(0.13f, 0.12f, 0.10f, 1f));
        ranksBg.type = Image.Type.Sliced;
        SetRect(ranksBg.rectTransform, new Vector2(ranksX, 44f), new Vector2(ranksW, 112f), new Vector2(0f, 0f));
        ranksBg.raycastTarget = true;
        RuntimeUiKit.AddOutline(ranksBg.transform, WithAlpha(lightChapter, 0.4f));
        Image trophy = CreateImage(ranksBg.transform, "RanksIcon", MenuSprites.Trophy(lightChapter), Color.white);
        trophy.preserveAspect = true;
        SetCenteredAt(trophy.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-58f, 0f), new Vector2(36f, 36f));
        CreateTmp(ranksBg.transform, "RanksLabel", "RANKS", 28, lightChapter, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(16f, 0f), new Vector2(150f, 40f), new Vector2(0.5f, 0.5f));
        Button ranksButton = ranksBg.gameObject.AddComponent<Button>();
        ranksButton.targetGraphic = ranksBg;
        ranksButton.onClick.AddListener(() => SfxPlayer.Play("ui-button-click")); // leaderboards: TODO

        // Close (X), top-right - a solid translucent dark circle (not a ring), over the thumbnail.
        Color closeFill = new Color(0.03f, 0.03f, 0.04f, 0.55f);
        Image closeBg = CreateImage(panel, "Close", MenuSprites.CircleBadge(closeFill, closeFill), Color.white);
        SetRect(closeBg.rectTransform, new Vector2(-24f, -24f), new Vector2(64f, 64f), new Vector2(1f, 1f));
        closeBg.raycastTarget = true;
        CreateTmp(closeBg.transform, "X", "X", 30, TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button closeButton = closeBg.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeBg;
        closeButton.onClick.AddListener(Close);
    }

    // A small stat card (TARGET / YOUR BEST): label on top in the light chapter colour, value
    // below. The two callers pass different value colours (white target, amber best).
    private static void BuildSummaryStat(Transform panel, string name, Vector2 anchoredPosition,
        float width, string label, string value, Color labelColor, Color valueColor)
    {
        RectTransform card = CreateRect(panel, $"Stat{name}",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            anchoredPosition, new Vector2(width, 104f));
        Image fill = card.gameObject.AddComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.11f, 0.10f, 0.085f, 1f);
        RuntimeUiKit.AddOutline(card, GlassBorder);

        TextMeshProUGUI labelText = CreateTmp(card, "Label", label, 16, labelColor, TextAnchor.UpperLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(22f, -18f), new Vector2(width - 36f, 24f), new Vector2(0f, 1f));
        labelText.characterSpacing = 3f;
        CreateTmp(card, "Value", value, 30, valueColor, TextAnchor.UpperLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(22f, -48f), new Vector2(width - 36f, 40f), new Vector2(0f, 1f));
    }

    // Goal text + the player's best (an em dash when never attempted). Handles the two built-in
    // target types directly; provider-driven levels fall back to the presentation's parts.
    private static void DeriveTargetAndBest(LevelDefinition level, LevelMenuPresentation.Snapshot presentation,
        bool completed, out string targetText, out string bestText)
    {
        ProgressStore.LevelBest best = ProgressStore.GetBest(level);
        bool attempted = completed || (best != null && (best.bestScore > 0 || best.bestHeightMeters > 0f));

        switch (level.TargetType)
        {
            case LevelTargetType.PlaceBlocks:
            {
                int target = Mathf.RoundToInt(level.TargetValue);
                targetText = $"{target} Blocks";
                int reached = best != null && best.bestScore > 0 ? best.bestScore : target;
                bestText = attempted ? $"{reached} Blocks" : "-";
                break;
            }
            case LevelTargetType.ReachHeight:
            {
                int target = Mathf.RoundToInt(level.TargetValue);
                targetText = $"{target}m";
                float reached = best != null && best.bestHeightMeters > 0f ? best.bestHeightMeters : level.TargetValue;
                bestText = attempted ? $"{Mathf.RoundToInt(reached)}m" : "-";
                break;
            }
            default:
            {
                string suffix = presentation.ProgressSuffix.StartsWith("/")
                    ? presentation.ProgressSuffix.Substring(1).Trim()
                    : presentation.ProgressSuffix;
                targetText = string.IsNullOrWhiteSpace(suffix) ? "Endless" : suffix;
                bestText = attempted ? presentation.ProgressPrimary : "-";
                break;
            }
        }
    }

    private static void SelectLevel(LevelDefinition level)
    {
        LevelSelectionState.SelectLevel(level);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
