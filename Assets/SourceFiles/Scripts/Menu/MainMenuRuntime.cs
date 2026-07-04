using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static RuntimeUiKit;

/// <summary>
/// Runtime-built main menu: top profile/status bar, Play chapter screen, bottom navigation,
/// and placeholder pages for tabs that do not have real content yet.
/// </summary>
public static partial class MainMenuRuntime
{
    private const string LevelsResourcesPath = "Levels";

    // Frosted-glass darkness: alpha of the dark wash drawn over each panel's blur (higher = more
    // black / more readable). Tuned per surface; see AddFrostedGlass.
    private const float TopBarFrostWash = 0.68f;
    private const float CurrencyCardFrostWash = 0.82f;
    private const float LevelCardFrostWash = 0.95f;

    private const float LevelListTopInset = 485f;
    // The list's scroll area must end ABOVE the next-chapter card (card bottom 232 + height 160
    // + a small gap): long chapters scroll behind neither the card nor the nav, and a partly
    // visible next row peeks out at this edge as the natural "there's more" cue.
    private const float LevelListBottomInset = 410f;
    private const float LevelRowHeight = 220f;
    private const float LevelCardHeight = 184f;
    private const float LevelCardTop = 6f;
    // Phone-edge padding: the gap from each screen edge to a level card. Cards stretch to fill
    // the row between these insets, so their width tracks the screen width on any device. Matches
    // the chapter title's left inset (see ChapterTitle x) so the cards line up under the title.
    private const float LevelCardSideInset = 76f;
    private const float LevelRailGutter = 64f; // the timeline rail's column, left of the cards
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
        _activeSettingsTab = SettingsTab.Sound;
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
        else if (_activeTab == MenuTab.Settings) BuildSettingsScreen(contentRoot, chapter);
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

}
