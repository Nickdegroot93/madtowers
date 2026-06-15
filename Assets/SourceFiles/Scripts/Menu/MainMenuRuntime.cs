using System;
using System.Collections.Generic;
using System.Globalization;
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
    private const float LevelListTopInset = 485f;
    private const float LevelListBottomInset = 205f;
    private const float LevelRowHeight = 205f;
    private const float LevelCardWidth = 790f;
    private const float LevelCardHeight = 155f;
    private const float LevelCardX = 108f;
    private const float LevelCardTop = 18f;
    private const float RailX = 52f;

    private static readonly Color TextPrimary = new Color(0.96f, 0.93f, 0.86f, 1f);
    private static readonly Color TextMuted = new Color(0.74f, 0.7f, 0.64f, 1f);
    private static readonly Color LockedColor = new Color(0.44f, 0.46f, 0.48f, 1f);
    private static readonly Color CardDark = new Color(0.07f, 0.06f, 0.05f, 0.76f);
    private static readonly Color NavDark = new Color(0.045f, 0.04f, 0.035f, 0.92f);
    private static readonly Color GoldBase = new Color(1f, 0.9f, 0.68f, 1f);

    private enum MenuTab
    {
        Shop,
        Missions,
        Play,
        Heroes,
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
    private static ChapterDefinition[] _chapters = Array.Empty<ChapterDefinition>();
    private static int _chapterIndex;
    private static bool _chapterIndexInitialized;
    private static MenuTab _activeTab = MenuTab.Play;
    private static RenderTexture _videoTexture;

    private static Color GoldOutline(float alpha)
    {
        Color color = GoldBase;
        color.a = alpha;
        return color;
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
        _activeTab = MenuTab.Play;
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
        BuildTopStatusBar(topRoot);

        Transform contentRoot = RecreateSection(ref _contentRoot, _contentLayer, "ContentRoot");
        if (_activeTab == MenuTab.Play) BuildPlayScreen(contentRoot, chapter);
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
        ReleaseVideoTexture();
    }

    private static void EnsureRoot()
    {
        if (_root != null) return;

        _root = RuntimeUiKit.CreateOverlayCanvas("Main Menu", 5000);
        _backgroundLayer = CreateLayer(_root.transform, "BackgroundLayer");
        _contentLayer = CreateLayer(_root.transform, "ContentLayer");
        _topStatusLayer = CreateLayer(_root.transform, "TopStatusLayer");
        _navLayer = CreateLayer(_root.transform, "NavigationLayer");
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

        if (chapter.MenuBackgroundImage != null)
        {
            Image image = CreateImage(parent, "BackgroundImage", chapter.MenuBackgroundImage, Color.white);
            Stretch(image.rectTransform);
            image.preserveAspect = false;
        }
        else
        {
            Image fallback = CreateImage(parent, "GeneratedBackground",
                MenuSprites.Background(top, bottom, chapter.MenuAccentColor), Color.white);
            Stretch(fallback.rectTransform);
        }

        if (chapter.MenuBackgroundVideo != null)
        {
            _videoTexture = new RenderTexture(720, 1280, 0, RenderTextureFormat.ARGB32);
            _videoTexture.name = "MenuBackgroundVideoRT";
            _videoTexture.hideFlags = HideFlags.HideAndDontSave;
            _videoTexture.Create();

            RawImage videoImage = CreateRawImage(parent, "BackgroundVideo", _videoTexture, Color.white);
            Stretch(videoImage.rectTransform);
            videoImage.color = new Color(1f, 1f, 1f, 0f);

            GameObject playerObject = new GameObject("BackgroundVideoPlayer");
            playerObject.transform.SetParent(parent, false);
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
            new Color(0.02f, 0.018f, 0.014f, 0.38f));
        Stretch(dim.rectTransform);

        Image bottomShade = CreateImage(parent, "BottomShade", RuntimeSprites.Square(),
            new Color(0.02f, 0.018f, 0.014f, 0.42f));
        RectTransform shadeRect = bottomShade.rectTransform;
        shadeRect.anchorMin = new Vector2(0f, 0f);
        shadeRect.anchorMax = new Vector2(1f, 0f);
        shadeRect.pivot = new Vector2(0.5f, 0f);
        shadeRect.anchoredPosition = Vector2.zero;
        shadeRect.sizeDelta = new Vector2(0f, 360f);
    }

    private static void BuildTopStatusBar(Transform parent)
    {
        PlayerProfileStore.Snapshot profile = PlayerProfileStore.Current;

        RectTransform bar = CreateRect(parent, "TopStatusBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -34f), new Vector2(-48f, 110f));
        Image barImage = bar.gameObject.AddComponent<Image>();
        barImage.sprite = RuntimeSprites.RoundedPanel();
        barImage.type = Image.Type.Sliced;
        barImage.color = new Color(0.05f, 0.045f, 0.038f, 0.72f);
        RuntimeUiKit.AddOutline(bar, GoldOutline(0.16f));

        HorizontalLayoutGroup layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 11, 11);
        layout.spacing = 20f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        Image badge = CreateImage(bar, "LevelBadge",
            MenuSprites.HexButton(new Color(0.23f, 0.21f, 0.18f, 1f), new Color(0.08f, 0.075f, 0.07f, 1f)),
            Color.white);
        LayoutElement badgeLayout = badge.gameObject.AddComponent<LayoutElement>();
        badgeLayout.preferredWidth = 88f;
        badgeLayout.preferredHeight = 88f;
        CreateText(badge.transform, "LevelText", profile.PlayerLevel.ToString(), 34, TextPrimary,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);

        RectTransform profileColumn = CreateRect(bar, "ProfileInfo",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        LayoutElement profileLayout = profileColumn.gameObject.AddComponent<LayoutElement>();
        profileLayout.minWidth = 230f;
        profileLayout.flexibleWidth = 1f;
        profileLayout.preferredHeight = 88f;

        VerticalLayoutGroup profileStack = profileColumn.gameObject.AddComponent<VerticalLayoutGroup>();
        profileStack.padding = new RectOffset(0, 0, 14, 16);
        profileStack.spacing = 9f;
        profileStack.childAlignment = TextAnchor.MiddleLeft;
        profileStack.childControlWidth = true;
        profileStack.childControlHeight = true;
        profileStack.childForceExpandWidth = true;
        profileStack.childForceExpandHeight = false;

        Text playerName = CreateText(profileColumn, "PlayerName", profile.PlayerName, 19, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            Vector2.zero, new Vector2(0f, 28f), new Vector2(0f, 1f));
        playerName.resizeTextForBestFit = true;
        playerName.resizeTextMinSize = 14;
        playerName.resizeTextMaxSize = 19;
        playerName.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        Image expTrack = CreateImage(profileColumn, "ExpTrack", RuntimeSprites.RoundedPanel(),
            new Color(0.04f, 0.04f, 0.035f, 0.9f));
        expTrack.type = Image.Type.Sliced;
        expTrack.gameObject.AddComponent<LayoutElement>().preferredHeight = 9f;
        Image expFill = CreateImage(expTrack.transform, "ExpFill", RuntimeSprites.RoundedPanel(),
            new Color(1f, 0.67f, 0.23f, 1f));
        expFill.type = Image.Type.Sliced;
        RectTransform expFillRect = expFill.rectTransform;
        expFillRect.anchorMin = new Vector2(0f, 0f);
        expFillRect.anchorMax = new Vector2(Mathf.Clamp01(profile.Experience01), 1f);
        expFillRect.offsetMin = Vector2.zero;
        expFillRect.offsetMax = Vector2.zero;

        BuildCurrencyCard(bar, "$", profile.Coins.ToString("N0", CultureInfo.InvariantCulture), null);
        BuildCurrencyCard(bar, null,
            $"{profile.Lives}/{profile.MaxLives}",
            $"{profile.LifeRefillRemaining.Minutes:00}:{profile.LifeRefillRemaining.Seconds:00}");
    }

    private static void BuildCurrencyCard(Transform parent, string coinGlyph, string primary, string secondary)
    {
        RectTransform card = CreateRect(parent, "StatusCard",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(225f, 66f));
        LayoutElement cardLayout = card.gameObject.AddComponent<LayoutElement>();
        cardLayout.preferredWidth = 225f;
        cardLayout.preferredHeight = 66f;
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        cardImage.color = new Color(0.04f, 0.038f, 0.034f, 0.9f);
        RuntimeUiKit.AddOutline(card, GoldOutline(0.18f));

        if (!string.IsNullOrEmpty(coinGlyph))
        {
            Image coin = CreateImage(card, "Coin", RuntimeSprites.Bubble(), new Color(1f, 0.7f, 0.16f, 1f));
            SetRect(coin.rectTransform, new Vector2(18f, -10f), new Vector2(46f, 46f), new Vector2(0f, 1f));
            CreateText(coin.transform, "CoinGlyph", coinGlyph, 22, new Color(0.28f, 0.15f, 0.02f, 1f),
                TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        }
        else
        {
            Image heart = CreateImage(card, "Heart", RuntimeSprites.Heart(), new Color(1f, 0.22f, 0.15f, 1f));
            SetRect(heart.rectTransform, new Vector2(18f, -11f), new Vector2(48f, 48f), new Vector2(0f, 1f));
        }

        Text primaryText = CreateText(card, "Primary", primary, 24, TextPrimary, TextAnchor.MiddleLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(76f, -6f), new Vector2(100f, 38f), new Vector2(0f, 1f));
        primaryText.resizeTextForBestFit = true;
        primaryText.resizeTextMinSize = 16;
        primaryText.resizeTextMaxSize = 24;
        if (!string.IsNullOrEmpty(secondary))
        {
            CreateText(card, "Secondary", secondary, 18, TextMuted, TextAnchor.MiddleLeft,
                FontStyle.Normal, RuntimeUiKit.DefaultFont, new Vector2(76f, -36f), new Vector2(100f, 25f), new Vector2(0f, 1f));
        }

        Image divider = CreateImage(card, "Divider", RuntimeSprites.Square(), GoldOutline(0.22f));
        SetRect(divider.rectTransform, new Vector2(174f, -16f), new Vector2(2f, 34f), new Vector2(0f, 1f));
        CreateText(card, "Plus", "+", 34, TextPrimary, TextAnchor.MiddleCenter,
            FontStyle.Normal, RuntimeUiKit.DefaultFont, new Vector2(184f, -10f), new Vector2(34f, 42f), new Vector2(0f, 1f));
    }

    private static void BuildPlayScreen(Transform parent, ChapterDefinition chapter)
    {
        bool chapterUnlocked = Campaign.IsChapterUnlocked(_chapters, _chapterIndex);

        CreateText(parent, "ChapterEyebrow", $"*  CHAPTER {chapter.ChapterNumber}  *", 24,
            chapter.MenuAccentColor, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(78f, -230f), new Vector2(360f, 40f), new Vector2(0f, 1f));

        Text title = CreateText(parent, "ChapterTitle", chapter.DisplayName.ToUpperInvariant(), 72,
            TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(76f, -282f), new Vector2(610f, 96f), new Vector2(0f, 1f));
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = 42;
        title.resizeTextMaxSize = 72;

        BuildNextChapterCard(parent, chapter);

        if (!chapterUnlocked)
        {
            BuildLockedChapterMessage(parent);
            return;
        }

        int currentIndex = CurrentLevelIndex(chapter);
        BuildLevelList(parent, chapter, currentIndex);
    }

    private static void BuildNextChapterCard(Transform parent, ChapterDefinition current)
    {
        if (_chapters.Length <= 1) return;

        int nextIndex = (_chapterIndex + 1) % _chapters.Length;
        ChapterDefinition next = _chapters[nextIndex];
        bool unlocked = Campaign.IsChapterUnlocked(_chapters, nextIndex);

        RectTransform card = CreateRect(parent, "NextChapterCard",
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-60f, -238f), new Vector2(300f, 160f));
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        cardImage.color = new Color(0.05f, 0.06f, 0.065f, 0.72f);
        RuntimeUiKit.AddOutline(card, GoldOutline(0.24f));

        Sprite preview = current.NextChapterPreviewImage != null
            ? current.NextChapterPreviewImage
            : next.MenuBackgroundImage;
        if (preview != null)
        {
            Image previewImage = CreateImage(card, "Preview", preview, new Color(1f, 1f, 1f, 0.42f));
            Stretch(previewImage.rectTransform);
            previewImage.preserveAspect = false;
        }

        CreateText(card, "NextLabel", "NEXT CHAPTER", 15, TextMuted, TextAnchor.MiddleLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(28f, -22f), new Vector2(180f, 26f), new Vector2(0f, 1f));
        CreateText(card, "NextTitle", next.DisplayName.ToUpperInvariant(), 21, TextPrimary, TextAnchor.MiddleLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(28f, -50f), new Vector2(206f, 34f), new Vector2(0f, 1f));
        CreateText(card, "NextArrow", unlocked ? ">" : "LOCK", unlocked ? 42 : 18,
            unlocked ? TextPrimary : LockedColor, TextAnchor.MiddleCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(236f, -48f), new Vector2(44f, 54f), new Vector2(0f, 1f));

        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = cardImage;
        button.interactable = unlocked;
        button.onClick.AddListener(() =>
        {
            _chapterIndex = nextIndex;
            _activeTab = MenuTab.Play;
            BuildMenu();
        });
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
        CreateText(panel, "Locked", "LOCKED", 40, LockedColor, TextAnchor.MiddleCenter,
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
            if (Campaign.IsLevelUnlocked(chapter, i) && !ProgressStore.IsLevelCompleted(level)) return i;
        }
        return fallback;
    }

    private static void BuildLevelList(Transform parent, ChapterDefinition chapter, int currentIndex)
    {
        RectTransform viewport = CreateRect(parent, "LevelListViewport",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.offsetMin = new Vector2(42f, LevelListBottomInset);
        viewport.offsetMax = new Vector2(-42f, -LevelListTopInset);
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

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
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

            bool unlocked = Campaign.IsLevelUnlocked(chapter, i);
            bool completed = ProgressStore.IsLevelCompleted(level);
            bool isCurrent = i == currentIndex;
            bool current = isCurrent && unlocked && !completed;

            BuildRailForRow(row, count, i, completed, current, chapter.MenuAccentColor);
            BuildLevelCard(row, chapter, level, i, unlocked, completed, current);
        }
    }

    private static void BuildRailForRow(Transform row, int levelCount, int index,
        bool completed, bool current, Color accentColor)
    {
        float cardCenterY = -(LevelCardTop + LevelCardHeight * 0.5f);
        float lineTop = index == 0 ? -cardCenterY : 0f;
        float lineHeight = index == levelCount - 1 ? -cardCenterY : LevelRowHeight;

        Image rail = CreateImage(row, "RailSegment", RuntimeSprites.RoundedPanel(), GoldOutline(0.48f));
        rail.type = Image.Type.Sliced;
        SetRect(rail.rectTransform, new Vector2(RailX - 3f, -lineTop), new Vector2(6f, lineHeight), new Vector2(0f, 1f));

        Image node = CreateImage(row, "RailNode", RuntimeSprites.Bubble(),
            current ? accentColor : (completed ? new Color(0.8f, 1f, 0.42f, 0.92f) : GoldOutline(0.82f)));
        RectTransform nodeRect = node.rectTransform;
        nodeRect.anchorMin = new Vector2(0f, 1f);
        nodeRect.anchorMax = new Vector2(0f, 1f);
        nodeRect.pivot = new Vector2(0.5f, 0.5f);
        nodeRect.anchoredPosition = new Vector2(RailX, cardCenterY);
        nodeRect.sizeDelta = current ? new Vector2(56f, 56f) : new Vector2(28f, 28f);
    }

    private static void BuildLevelCard(Transform parent, ChapterDefinition chapter, LevelDefinition level,
        int index, bool unlocked, bool completed, bool current)
    {
        RectTransform card = CreateRect(parent, $"LevelCard{index + 1}",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(LevelCardX, -LevelCardTop), new Vector2(LevelCardWidth, LevelCardHeight));
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        Color panel = chapter.MenuPanelColor;
        panel.a = unlocked ? 0.78f : 0.46f;
        cardImage.color = panel;

        Color outline = current ? chapter.MenuAccentColor : GoldOutline(unlocked ? 0.18f : 0.08f);
        RuntimeUiKit.AddOutline(card, outline);

        Sprite thumbSprite = level.MenuThumbnail != null
            ? level.MenuThumbnail
            : MenuSprites.LevelThumbnail(index, chapter.MenuAccentColor, chapter.MenuAccentSecondaryColor);
        Image thumb = CreateImage(card, "Thumbnail", thumbSprite, unlocked ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.55f));
        SetRect(thumb.rectTransform, new Vector2(24f, -18f), new Vector2(118f, 118f), new Vector2(0f, 1f));
        thumb.preserveAspect = false;
        RuntimeUiKit.AddOutline(thumb.transform, GoldOutline(0.18f));

        Image numberPlate = CreateImage(card, "NumberPlate", RuntimeSprites.Bubble(),
            new Color(0.07f, 0.055f, 0.045f, 0.8f));
        SetRect(numberPlate.rectTransform, new Vector2(176f, -52f), new Vector2(54f, 54f), new Vector2(0f, 1f));
        CreateText(numberPlate.transform, "Number", (index + 1).ToString(), 23, unlocked ? TextPrimary : LockedColor,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);

        Color titleColor = unlocked ? TextPrimary : LockedColor;
        LevelMenuPresentation.Snapshot presentation = LevelMenuPresentation.Build(level, completed);
        CreateText(card, "Title", level.DisplayName, 38, titleColor, TextAnchor.MiddleLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(246f, -24f), new Vector2(405f, 52f), new Vector2(0f, 1f));
        CreateText(card, "Challenge", presentation.ChallengeLabel, 17, unlocked ? TextMuted : LockedColor,
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(246f, -78f), new Vector2(310f, 30f), new Vector2(0f, 1f));
        CreateText(card, "Progress", presentation.ProgressLabel, 26,
            unlocked ? chapter.MenuAccentColor : LockedColor, TextAnchor.MiddleLeft, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(246f, -108f), new Vector2(330f, 36f), new Vector2(0f, 1f));

        Image action = CreateImage(card, "Action", RuntimeSprites.Bubble(),
            completed ? new Color(0.58f, 0.9f, 0.2f, 0.92f) : new Color(0.04f, 0.036f, 0.03f, 0.66f));
        SetRect(action.rectTransform, new Vector2(700f, -48f), new Vector2(62f, 62f), new Vector2(0f, 1f));
        string actionText = completed ? "OK" : (unlocked ? ">" : "LOCK");
        CreateText(action.transform, "ActionText", actionText, completed ? 18 : (unlocked ? 36 : 14),
            completed ? new Color(0.12f, 0.24f, 0.02f, 1f) : (unlocked ? TextPrimary : LockedColor),
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);

        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = cardImage;
        button.interactable = unlocked;
        LevelDefinition selected = level;
        button.onClick.AddListener(() => SelectLevel(selected));

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        colors.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.65f);
        button.colors = colors;
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

        CreateText(panel, "Title", tab.ToString().ToUpperInvariant(), 58, TextPrimary,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -44f), new Vector2(740f, 82f), new Vector2(0.5f, 1f));
        CreateText(panel, "Status", "COMING SOON", 24, TextMuted,
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

    private static void BuildBottomNav(Transform parent)
    {
        RectTransform nav = CreateRect(parent, "BottomNavigation",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 38f), new Vector2(940f, 145f));
        Image navImage = nav.gameObject.AddComponent<Image>();
        navImage.sprite = RuntimeSprites.RoundedPanel();
        navImage.type = Image.Type.Sliced;
        navImage.color = NavDark;
        RuntimeUiKit.AddOutline(nav, GoldOutline(0.18f));

        MenuTab[] tabs = { MenuTab.Shop, MenuTab.Missions, MenuTab.Play, MenuTab.Heroes, MenuTab.Settings };
        for (int i = 0; i < tabs.Length; i++)
        {
            if (tabs[i] == MenuTab.Play) BuildPlayNavButton(nav, i, tabs.Length);
            else BuildNavButton(nav, tabs[i], i, tabs.Length);
        }
    }

    private static void BuildNavButton(Transform nav, MenuTab tab, int index, int count)
    {
        float width = 940f / count;
        RectTransform slot = CreateRect(nav, $"{tab}Nav",
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(index * width, 14f), new Vector2(width, 116f));
        Image target = slot.gameObject.AddComponent<Image>();
        target.color = Color.clear;

        Button button = slot.gameObject.AddComponent<Button>();
        button.targetGraphic = target;
        button.onClick.AddListener(() =>
        {
            _activeTab = tab;
            BuildMenu();
        });

        Color color = _activeTab == tab ? TextPrimary : TextMuted;
        string icon = tab switch
        {
            MenuTab.Shop => "$",
            MenuTab.Missions => "*",
            MenuTab.Heroes => "H",
            MenuTab.Settings => "#",
            _ => ""
        };
        CreateText(slot, "Icon", icon, 34, color, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, -4f), new Vector2(width, 42f), new Vector2(0f, 1f));
        CreateText(slot, "Label", tab.ToString().ToUpperInvariant(), 17, color, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, -62f), new Vector2(width, 34f), new Vector2(0f, 1f));
    }

    private static void BuildPlayNavButton(Transform nav, int index, int count)
    {
        float width = 940f / count;
        RectTransform buttonRect = CreateRect(nav, "PlayNav",
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0f),
            new Vector2(index * width + width * 0.5f, -18f), new Vector2(174f, 190f));
        Image image = buttonRect.gameObject.AddComponent<Image>();
        ChapterDefinition chapter = _chapters.Length > 0 ? _chapters[_chapterIndex] : null;
        image.sprite = chapter != null
            ? MenuSprites.HexButton(chapter.PlayButtonTopColor, chapter.PlayButtonBottomColor)
            : MenuSprites.HexButton(new Color(1f, 0.72f, 0.27f, 1f), new Color(0.88f, 0.38f, 0.08f, 1f));
        image.color = _activeTab == MenuTab.Play ? Color.white : new Color(0.82f, 0.82f, 0.82f, 1f);

        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            _activeTab = MenuTab.Play;
            BuildMenu();
        });

        Image triangle = CreateImage(buttonRect, "PlayIcon", MenuSprites.TrianglePlay(), Color.white);
        SetRect(triangle.rectTransform, new Vector2(0f, 54f), new Vector2(56f, 56f), new Vector2(0.5f, 0f));
        CreateText(buttonRect, "Label", "PLAY", 21, Color.white, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, 22f), new Vector2(112f, 34f), new Vector2(0.5f, 0f));
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
        button.onClick.AddListener(onClick);
        CreateText(rect, "Label", label, 26, TextPrimary, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont);
        return button;
    }

    private static void OpenCustomGame()
    {
        TearDownRoot();
        _activeTab = MenuTab.Play;
        CustomGameMenu.Show(BuildMenu);
    }

    private static void SelectLevel(LevelDefinition level)
    {
        LevelSelectionState.SelectLevel(level);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
