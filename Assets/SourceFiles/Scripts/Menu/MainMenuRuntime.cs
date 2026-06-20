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
    private const float LevelListTopInset = 485f;
    private const float LevelListBottomInset = 205f;
    private const float LevelRowHeight = 220f;
    private const float LevelCardWidth = 790f;
    private const float LevelCardHeight = 184f;
    private const float LevelCardX = 108f;
    private const float LevelCardTop = 6f;
    private const float RailX = 52f;
    // Text column region inside a card: starts right of the thumbnail/number plate, ends
    // before the action badge. The column itself flows with a layout group, so these are
    // the only two card-internal x values left to tune.
    private const float LevelCardTextLeft = 248f;
    private const float LevelCardTextRight = 124f;

    private static readonly Color TextPrimary = new Color(0.96f, 0.93f, 0.86f, 1f);
    private static readonly Color TextMuted = new Color(0.74f, 0.7f, 0.64f, 1f);
    private static readonly Color LockedColor = new Color(0.44f, 0.46f, 0.48f, 1f);
    private static readonly Color CardDark = new Color(0.07f, 0.06f, 0.05f, 0.76f);
    private static readonly Color NavDark = new Color(0.045f, 0.04f, 0.035f, 0.92f);
    private static readonly Color GoldBase = new Color(1f, 0.9f, 0.68f, 1f);
    private static readonly Color GlassBorder = new Color(1f, 0.92f, 0.74f, 0.18f);

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
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
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
            new Color(0.02f, 0.018f, 0.014f, 0.24f));
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

    private static void BuildTopStatusBar(Transform parent, ChapterDefinition chapter)
    {
        PlayerProfileStore.Snapshot profile = PlayerProfileStore.Current;
        Color chapterTint = chapter != null ? chapter.MenuAccentSecondaryColor : GoldBase;

        RectTransform bar = CreateRect(parent, "TopStatusBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -34f), new Vector2(-48f, 122f));
        Image barImage = bar.gameObject.AddComponent<Image>();
        barImage.sprite = RuntimeSprites.RoundedPanel();
        barImage.type = Image.Type.Sliced;
        barImage.color = WithAlpha(Color.Lerp(chapterTint, TextPrimary, 0.18f), 0.07f);
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
                new Color(0.32f, 0.30f, 0.26f, 0.36f),
                new Color(0.06f, 0.055f, 0.048f, 0.42f),
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

        BuildCurrencyCard(bar, "$", profile.Coins.ToString("N0", CultureInfo.InvariantCulture), null);
        BuildCurrencyCard(bar, null,
            $"{profile.Lives}/{profile.MaxLives}",
            $"{profile.LifeRefillRemaining.Minutes:00}:{profile.LifeRefillRemaining.Seconds:00}");
    }

    private static void BuildCurrencyCard(Transform parent, string coinGlyph, string primary, string secondary)
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
        cardImage.color = new Color(0.025f, 0.023f, 0.021f, 0.32f);
        RuntimeUiKit.AddOutline(card, GlassBorder);

        if (!string.IsNullOrEmpty(coinGlyph))
        {
            Image coin = CreateImage(card, "Coin", RuntimeSprites.Bubble(), new Color(1f, 0.7f, 0.16f, 1f));
            SetRect(coin.rectTransform, new Vector2(18f, 0f), new Vector2(46f, 46f), new Vector2(0f, 0.5f));
            CreateTmp(coin.transform, "CoinGlyph", coinGlyph, 20, new Color(0.28f, 0.15f, 0.02f, 1f),
                TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.DefaultFont);
        }
        else
        {
            Image heart = CreateImage(card, "Heart", RuntimeSprites.Heart(), new Color(1f, 0.22f, 0.15f, 1f));
            SetRect(heart.rectTransform, new Vector2(18f, 0f), new Vector2(50f, 50f), new Vector2(0f, 0.5f));
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

        Image divider = CreateImage(card, "Divider", RuntimeSprites.Square(), WithAlpha(TextPrimary, 0.24f));
        SetRect(divider.rectTransform, new Vector2(178f, 0f), new Vector2(1f, 36f), new Vector2(0f, 0.5f));
        CreateTmp(card, "Plus", "+", 32, TextPrimary, TextAnchor.MiddleCenter,
            FontStyle.Normal, RuntimeUiKit.DefaultFont, new Vector2(197f, 0f), new Vector2(40f, 42f), new Vector2(0f, 0.5f));
    }

    private static void BuildPlayScreen(Transform parent, ChapterDefinition chapter)
    {
        bool chapterUnlocked = Campaign.IsChapterUnlocked(_chapters, _chapterIndex);
        Color chapterMarkColor = Color.Lerp(chapter.MenuAccentSecondaryColor, chapter.MenuAccentColor, 0.62f);
        Color eyebrowColor = Color.Lerp(chapter.MenuAccentColor, TextPrimary, 0.42f);

        Image leftDiamond = CreateImage(parent, "ChapterDiamondLeft", RuntimeSprites.Square(), eyebrowColor);
        leftDiamond.color = chapterMarkColor;
        SetRect(leftDiamond.rectTransform, new Vector2(82f, -252f), new Vector2(8f, 8f), new Vector2(0f, 1f));
        leftDiamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        Image rightDiamond = CreateImage(parent, "ChapterDiamondRight", RuntimeSprites.Square(), eyebrowColor);
        rightDiamond.color = chapterMarkColor;
        SetRect(rightDiamond.rectTransform, new Vector2(267f, -252f), new Vector2(8f, 8f), new Vector2(0f, 1f));
        rightDiamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        TextMeshProUGUI eyebrow = CreateTmp(parent, "ChapterEyebrow", $"{TrackedUpper("Chapter", " ", "   ")}  {chapter.ChapterNumber}", 20,
            eyebrowColor, TextAnchor.MiddleLeft, FontStyle.Normal, RuntimeUiKit.TitleFont,
            new Vector2(105f, -232f), new Vector2(180f, 42f), new Vector2(0f, 1f));
        AutoSize(eyebrow, 16, 20);

        TextMeshProUGUI title = CreateTmp(parent, "ChapterTitle", chapter.DisplayName.ToUpperInvariant(), 68,
            TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(76f, -276f), new Vector2(700f, 104f), new Vector2(0f, 1f));
        title.characterSpacing = 6f;
        AutoSize(title, 40, 68);

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

        CreateTmp(card, "NextLabel", "NEXT CHAPTER", 15, TextMuted, TextAnchor.MiddleLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(28f, -22f), new Vector2(180f, 26f), new Vector2(0f, 1f));
        CreateTmp(card, "NextTitle", next.DisplayName.ToUpperInvariant(), 21, TextPrimary, TextAnchor.MiddleLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(28f, -50f), new Vector2(206f, 34f), new Vector2(0f, 1f));
        CreateTmp(card, "NextArrow", unlocked ? ">" : "LOCK", unlocked ? 42 : 18,
            unlocked ? TextPrimary : LockedColor, TextAnchor.MiddleCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(236f, -48f), new Vector2(44f, 54f), new Vector2(0f, 1f));

        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = cardImage;
        button.interactable = unlocked;
        button.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
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

            bool completed = ProgressStore.IsLevelCompleted(level);
            bool unlocked = completed || IsLevelVisuallyUnlocked(chapter, i);
            bool isCurrent = i == currentIndex;
            bool current = isCurrent && unlocked && !completed;

            BuildRailForRow(row, count, i, unlocked, completed, current,
                chapter.MenuAccentColor, chapter.MenuAccentSecondaryColor);
            BuildLevelCard(row, chapter, level, i, unlocked, completed, current);
        }
    }

    private static void BuildRailForRow(Transform row, int levelCount, int index,
        bool unlocked, bool completed, bool current, Color accentColor, Color secondaryColor)
    {
        float cardCenterY = -(LevelCardTop + LevelCardHeight * 0.5f);
        float lineTop = index == 0 ? -cardCenterY : 0f;
        float lineHeight = index == levelCount - 1 ? -cardCenterY : LevelRowHeight;
        Color railColor = Color.Lerp(accentColor, TextPrimary, 0.58f);
        Color currentColor = Color.Lerp(accentColor, TextPrimary, 0.45f);

        Image railGlow = CreateImage(row, "RailGlow", RuntimeSprites.RoundedPanel(), WithAlpha(railColor, 0.10f));
        railGlow.type = Image.Type.Sliced;
        SetRect(railGlow.rectTransform, new Vector2(RailX - 3f, -lineTop), new Vector2(6f, lineHeight),
            new Vector2(0f, 1f));

        Image rail = CreateImage(row, "RailSegment", RuntimeSprites.RoundedPanel(), WithAlpha(railColor, 0.80f));
        rail.type = Image.Type.Sliced;
        SetRect(rail.rectTransform, new Vector2(RailX - 1.5f, -lineTop), new Vector2(3f, lineHeight),
            new Vector2(0f, 1f));

        if (current)
        {
            // The active node is a glowing hollow diamond: a soft UIEffect-blurred halo, a crisp
            // diamond outline (the "line around the diamond"), and a small bright center dot.
            Image glow = CreateImage(row, "RailNodeGlow",
                MenuSprites.DiamondBadge(WithAlpha(currentColor, 0.9f), WithAlpha(currentColor, 0.9f)), Color.white);
            SetCentered(glow.rectTransform, new Vector2(RailX, cardCenterY), new Vector2(46f, 46f));
            UIEffect glowFx = glow.gameObject.AddComponent<UIEffect>();
            glowFx.samplingFilter = SamplingFilter.BlurMedium;
            glowFx.samplingIntensity = 1f;

            Image ring = CreateImage(row, "RailNodeRing",
                MenuSprites.DiamondRing(currentColor), Color.white);
            SetCentered(ring.rectTransform, new Vector2(RailX, cardCenterY), new Vector2(50f, 50f));

            Image center = CreateImage(row, "RailNodeCenter",
                MenuSprites.DiamondBadge(WithAlpha(currentColor, 0.95f), WithAlpha(TextPrimary, 0.9f)), Color.white);
            SetCentered(center.rectTransform, new Vector2(RailX, cardCenterY), new Vector2(16f, 16f));
            return;
        }

        if (completed || unlocked)
        {
            Image node = CreateImage(row, "RailNode",
                MenuSprites.DiamondBadge(WithAlpha(railColor, completed ? 0.88f : 0.60f),
                    WithAlpha(railColor, completed ? 1f : 0.82f)), Color.white);
            SetCentered(node.rectTransform, new Vector2(RailX, cardCenterY), new Vector2(24f, 24f));
            return;
        }

        Image lockedNode = CreateImage(row, "RailNodeLocked",
            MenuSprites.DiamondBadge(WithAlpha(Color.black, 0.01f), WithAlpha(railColor, 0.55f)), Color.white);
        SetCentered(lockedNode.rectTransform, new Vector2(RailX, cardCenterY), new Vector2(18f, 18f));
    }

    private static void BuildLevelCard(Transform parent, ChapterDefinition chapter, LevelDefinition level,
        int index, bool unlocked, bool completed, bool current)
    {
        Color chapterLight = ChapterLight(chapter);
        Color chapterDark = ChapterDark(chapter);

        RectTransform card = CreateRect(parent, $"LevelCard{index + 1}",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(LevelCardX, -LevelCardTop), new Vector2(LevelCardWidth, LevelCardHeight));
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        Color cardFill = MenuGlassFill(chapter, unlocked ? 0.62f : 0.50f);
        cardImage.color = cardFill;

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
        Image thumb = CreateImage(card, "Thumbnail", thumbSprite, unlocked ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.55f));
        SetRect(thumb.rectTransform, new Vector2(22f, -16f), new Vector2(132f, 152f), new Vector2(0f, 1f));
        thumb.preserveAspect = false;
        RuntimeUiKit.AddOutline(thumb.transform, WithAlpha(TextPrimary, unlocked ? 0.18f : 0.08f));

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
            FontStyle.Bold, RuntimeUiKit.DefaultFont);
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
        Color completeColor = new Color(0.68f, 0.9f, 0.24f, 1f);
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
        Vector2 center = new Vector2(718f, -LevelCardHeight * 0.5f);

        if (completed)
        {
            Image completedGlow = CreateImage(card, "ActionGlow",
                MenuSprites.CircleBadge(WithAlpha(green, 0.10f), WithAlpha(green, 0.20f)), Color.white);
            SetCentered(completedGlow.rectTransform, center, new Vector2(72f, 72f));
        }

        Color edgeColor = ChapterEdge(chapterLight);
        Color fill = completed ? WithAlpha(green, 0.20f) : WithAlpha(Color.black, 0.18f);
        Color border = completed ? WithAlpha(green, 0.95f) : WithAlpha(edgeColor, unlocked ? 1f : 0.42f);
        Image action = CreateImage(card, "Action", MenuSprites.CircleBadge(fill, border), Color.white);
        SetCentered(action.rectTransform, center, new Vector2(62f, 62f));

        string actionText = completed ? "\u2713" : (unlocked ? ">" : "LOCK");
        CreateTmp(action.transform, "ActionText", actionText, completed ? 31 : (unlocked ? 34 : 12),
            completed ? green : (unlocked ? TextPrimary : LockedColor),
            TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.DefaultFont);
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
            SfxPlayer.Play("ui-button-click");
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
        CreateTmp(slot, "Icon", icon, 34, color, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, -4f), new Vector2(width, 42f), new Vector2(0f, 1f));
        CreateTmp(slot, "Label", tab.ToString().ToUpperInvariant(), 17, color, TextAnchor.MiddleCenter,
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
            SfxPlayer.Play("ui-button-click");
            _activeTab = MenuTab.Play;
            BuildMenu();
        });

        Image triangle = CreateImage(buttonRect, "PlayIcon", MenuSprites.TrianglePlay(), Color.white);
        SetRect(triangle.rectTransform, new Vector2(0f, 54f), new Vector2(56f, 56f), new Vector2(0.5f, 0f));
        CreateTmp(buttonRect, "Label", "PLAY", 21, Color.white, TextAnchor.MiddleCenter,
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
        button.onClick.AddListener(() => { SfxPlayer.Play("ui-button-click"); onClick?.Invoke(); });
        CreateTmp(rect, "Label", label, 26, TextPrimary, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont);
        return button;
    }

    private static void OpenCustomGame()
    {
        TearDownRoot();
        _activeTab = MenuTab.Play;
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

        // Dimmed backdrop - a tap anywhere outside the panel closes the modal.
        Image backdrop = CreateImage(overlay.transform, "Backdrop", null, new Color(0f, 0f, 0f, 0.72f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        Button backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(Close);

        // Centered panel. Its own raycast target swallows taps so they don't reach the backdrop.
        RectTransform panel = CreateRect(overlay.transform, "Panel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(860f, 1120f));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = RuntimeSprites.RoundedPanel();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = CardDark;
        panelImage.raycastTarget = true;
        RuntimeUiKit.AddOutline(panel, GoldOutline(0.3f));

        // Level image.
        Sprite thumb = level.MenuThumbnail != null
            ? level.MenuThumbnail
            : MenuSprites.LevelThumbnail(index, chapter.MenuAccentColor, chapter.MenuAccentSecondaryColor);
        Image image = CreateImage(panel, "Image", thumb, Color.white);
        SetRect(image.rectTransform, new Vector2(0f, -60f), new Vector2(760f, 440f), new Vector2(0.5f, 1f));
        image.preserveAspect = false;
        RuntimeUiKit.AddOutline(image.transform, GoldOutline(0.2f));

        // Title + stat lines (same source the level cards use).
        CreateTmp(panel, "Title", level.DisplayName, 56, TextPrimary, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, -540f), new Vector2(780f, 72f), new Vector2(0.5f, 1f));
        LevelMenuPresentation.Snapshot presentation = LevelMenuPresentation.Build(level, completed);
        CreateTmp(panel, "Challenge", presentation.ChallengeLabel, 30, TextMuted, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, -618f), new Vector2(780f, 44f), new Vector2(0.5f, 1f));
        CreateTmp(panel, "Progress", presentation.ProgressLabel, 34, chapter.MenuAccentColor, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont, new Vector2(0f, -666f), new Vector2(780f, 48f), new Vector2(0.5f, 1f));

        // Close (X), top-right of the panel.
        Image closeBg = CreateImage(panel, "Close", RuntimeSprites.Bubble(), new Color(0.1f, 0.09f, 0.08f, 0.92f));
        SetRect(closeBg.rectTransform, new Vector2(-28f, -28f), new Vector2(72f, 72f), new Vector2(1f, 1f));
        closeBg.raycastTarget = true;
        CreateTmp(closeBg.transform, "X", "X", 34, TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button closeButton = closeBg.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeBg;
        closeButton.onClick.AddListener(Close);

        // Start Game, pinned to the bottom of the panel.
        Image startBg = CreateImage(panel, "StartGame", RuntimeSprites.RoundedPanel(), chapter.MenuAccentColor);
        startBg.type = Image.Type.Sliced;
        SetRect(startBg.rectTransform, new Vector2(0f, 40f), new Vector2(720f, 124f), new Vector2(0.5f, 0f));
        startBg.raycastTarget = true;
        CreateTmp(startBg.transform, "StartLabel", "START GAME", 42, new Color(0.08f, 0.07f, 0.05f, 1f),
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button startButton = startBg.gameObject.AddComponent<Button>();
        startButton.targetGraphic = startBg;
        LevelDefinition selected = level;
        startButton.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-start-game");
            SelectLevel(selected);
        });
    }

    private static void SelectLevel(LevelDefinition level)
    {
        LevelSelectionState.SelectLevel(level);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
