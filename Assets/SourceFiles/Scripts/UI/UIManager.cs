using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("HUD Elements")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI heightText;
    [SerializeField] private TextMeshProUGUI livesText;     // legacy; hidden, replaced by heart icons
    [SerializeField] private TextMeshProUGUI nextBlockText; // legacy; hidden, replaced by the ghost preview

    [Header("Game Over UI")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI finalScoreText;

    // Style values are code-owned (not serialized) so tweaks always take effect —
    // serialized defaults go stale in Unity's import caches (see memory/PHYSICS.md §2).
    private static readonly Color HudTextColor = new Color(0.95f, 0.98f, 1f, 1f);
    private static readonly Color HeartColor = new Color(0.93f, 0.29f, 0.34f, 1f);
    private static readonly Color NextPreviewTint = new Color(1f, 1f, 1f, 0.6f);
    // Top bar: one dark rounded master card, two darker stat cards inside it, and a
    // taller NEXT card vertically centered on it (equal overhang above and below).
    // Warm near-opaque tones: translucent layers stacking over each other is what read
    // as "weird lines" - the mockup's layers barely let each other through.
    private static readonly Color BarColor = new Color(0.16f, 0.13f, 0.10f, 0.62f);
    private static readonly Color BarInsetColor = new Color(0.10f, 0.08f, 0.06f, 0.78f);
    private static readonly Color NextCardColor = new Color(0.17f, 0.14f, 0.11f, 0.78f);
    private static readonly Color NextCardBorder = new Color(0.95f, 0.92f, 0.86f, 0.38f);
    private static readonly Color StatLabelColor = new Color(0.88f, 0.80f, 0.70f, 0.55f);
    private static readonly Color StatValueColor = new Color(0.99f, 0.97f, 0.93f, 1f);
    private static readonly Color PauseFillColor = new Color(0f, 0f, 0f, 0.45f);
    private static readonly Color PauseIconColor = new Color(0.88f, 0.80f, 0.70f, 0.85f);
    private const float BarHeight = 104f;
    private const float BarSideMargin = 120f; // breathing room per the design - nothing reserves this space
    private const float BarCardInset = 14f;   // stat cards float inside their segment on all sides
    private const float TopMarginBelowSafeArea = 64f;
    private const float NextCardWidth = 200f;
    private const float NextCardOverhang = 24f; // how far it sticks out above AND below
    // Bar segments slip this far under the card edge. Exactly the half-width of the
    // card's border stroke: any deeper and the tucked bar shows through the translucent
    // card as a dark sliver inside the border; any shallower risks a sky-gap at the seam.
    private const float BarSeamTuck = 1f;
    private const float HeartSize = 44f;
    private const float HeartGap = 10f;
    private const int MaxHearts = 12;
    private static readonly Color NudgePillColor = new Color(1f, 1f, 1f, 0.09f);
    private static readonly Color NudgeChevronColor = new Color(0.95f, 0.98f, 1f, 0.32f);
    private const float NudgePillInset = 10f;
    private const float NudgeChevronSize = 30f;

    // Dimmed while a failed nudge's rebound lockout runs, so a dead corner button reads
    // as "rebounding", never as unresponsive UI.
    private const float NudgeLockoutDimFactor = 0.3f;

    private Spawner _spawner;
    private RectTransform _heartsContainer;
    private Image[] _hearts = System.Array.Empty<Image>();
    // base color is captured at creation, where it is actually known - the dim must not
    // have to guess an image's identity back from its sprite
    private readonly System.Collections.Generic.List<(Image image, Color baseColor)> _nudgePillImages =
        new System.Collections.Generic.List<(Image, Color)>(4);
    private bool _nudgePillsDimmed;
    private GameObject _nextPanel;
    private Image _nextPreview;
    private GameObject _pauseButton;
    private PauseMenuController _pauseMenu;
    private RectTransform _hudRoot;
    private RectTransform _barLeft;
    private RectTransform _barRight;
    private bool _topBarPositioned;
    private Vector3 _lastScreenState;
    private readonly System.Collections.Generic.Dictionary<string, Sprite> _ghostSprites =
        new System.Collections.Generic.Dictionary<string, Sprite>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        ConfigureHudStyle();
        _spawner = Object.FindAnyObjectByType<Spawner>();

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    private void OnEnable()
    {
        GameEvents.ScoreChanged += HandleScoreChanged;
        GameEvents.HeightChanged += HandleHeightChanged;
        GameEvents.LivesChanged += HandleLivesChanged;
        GameEvents.NextBlockChanged += HandleNextBlockChanged;
        GameEvents.GameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        GameEvents.ScoreChanged -= HandleScoreChanged;
        GameEvents.HeightChanged -= HandleHeightChanged;
        GameEvents.LivesChanged -= HandleLivesChanged;
        GameEvents.NextBlockChanged -= HandleNextBlockChanged;
        GameEvents.GameOver -= HandleGameOver;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        // Ghost sprites are generated HideAndDontSave copies (they survive scene loads);
        // destroy the ones we created - never the source piece sprites (cache stores the
        // source itself when the texture wasn't readable).
        foreach (Sprite ghost in _ghostSprites.Values)
        {
            if (ghost == null || !ghost.texture.hideFlags.HasFlag(HideFlags.HideAndDontSave)) continue;
            Destroy(ghost.texture);
            Destroy(ghost);
        }
        _ghostSprites.Clear();
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            HandleScoreChanged(GameManager.Instance.score);
            // towerHeight (meters above the floor), not maxHeight (world Y - the floor sits
            // at -11.5 world, which briefly showed as "-11.5m" before the first block).
            HandleHeightChanged(GameManager.Instance.towerHeight);
            HandleLivesChanged(GameManager.Instance.lives);
        }

        if (_spawner != null) HandleNextBlockChanged(_spawner.GetNextBlockName());
    }

    private void HandleScoreChanged(int score)
    {
        if (scoreText != null) scoreText.text = score.ToString();
    }

    private void HandleHeightChanged(float height)
    {
        if (heightText != null) heightText.text = $"{height:F1}m";
    }

    private void HandleLivesChanged(int lives)
    {
        EnsureHearts();
        if (_heartsContainer == null) return;

        for (int i = 0; i < _hearts.Length; i++)
        {
            if (_hearts[i] != null) _hearts[i].enabled = i < lives;
        }
    }

    private void HandleNextBlockChanged(string blockName)
    {
        if (_nextPreview == null) return;

        string shape = ThemeSkins.ExtractShapeToken(blockName);
        Sprite ghost = string.IsNullOrEmpty(shape) ? null : GetGhostSprite(shape);

        // The card itself stays put (it's part of the bar's silhouette); only the
        // ghost inside comes and goes.
        _nextPreview.sprite = ghost;
        _nextPreview.enabled = ghost != null;
    }

    // Desaturated copy of the piece sprite so the preview reads as "coming up", not as a
    // brick already in play. Cached per shape and skin folder.
    private Sprite GetGhostSprite(string shape)
    {
        string cacheKey = $"{ThemeSkins.Folder}:{shape}";
        if (_ghostSprites.TryGetValue(cacheKey, out Sprite cached)) return cached;

        Sprite source = ThemeSkins.LoadPiece(shape);
        Sprite ghost = source;
        if (source != null && source.texture.isReadable)
        {
            Texture2D src = source.texture;
            Texture2D tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Color[] pixels = src.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                float gray = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                pixels[i] = Color.Lerp(new Color(gray, gray, gray, c.a), c, 0.18f);
            }
            tex.SetPixels(pixels);
            tex.Apply();

            ghost = Sprite.Create(tex, new Rect(0, 0, src.width, src.height), new Vector2(0.5f, 0.5f), 256f);
            ghost.hideFlags = HideFlags.HideAndDontSave;
        }

        _ghostSprites[cacheKey] = ghost;
        return ghost;
    }

    private void HandleGameOver(int finalScore, float maxHeight)
    {
        ShowGameOver(finalScore, maxHeight);
    }

    private void ShowGameOver(int finalScore, float maxHeight)
    {
        if (gameOverPanel != null && !gameOverPanel.activeSelf)
        {
            gameOverPanel.SetActive(true);
            if (finalScoreText != null)
            {
                finalScoreText.text = $"Final Score: {finalScore}\nMax Height: {maxHeight:F1}m";
            }
        }
    }

    public void RestartGame()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.RestartGame();
        }
    }

    private void ConfigureHudStyle()
    {
        // No opt-out: the bar carries the game's ONLY pause entry point and the next
        // preview - a style toggle must never be able to remove those.
        BuildTopBar();

        if (livesText != null) livesText.gameObject.SetActive(false);
        if (nextBlockText != null) nextBlockText.gameObject.SetActive(false);

        EnsureNudgeButtons();
        StyleGameOverText();
    }

    // ---- Top bar -------------------------------------------------------------------------
    // Safe-area aware: phones with cameras/notches push the bar down by the OS inset,
    // plus a small fixed margin so it never kisses the screen edge on clean displays.
    // The raw inset is CLAMPED to 10% of the screen: Screen.safeArea can report a
    // degenerate rect when read during early Awake (editor/simulator timing), and an
    // unclamped read positioned the whole bar a full screen below the top - invisible,
    // no exception. The position is also re-applied on the first Update, when both the
    // safe area and the canvas scale factor are guaranteed settled.
    private float SafeAreaTopOffset()
    {
        Canvas canvas = HudRoot() != null ? HudRoot().GetComponentInParent<Canvas>() : null;
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        float insetPixels = Mathf.Clamp(Screen.height - Screen.safeArea.yMax, 0f, Screen.height * 0.1f);
        return insetPixels / Mathf.Max(0.01f, scale) + TopMarginBelowSafeArea;
    }

    private void BuildTopBar()
    {
        RectTransform root = HudRoot();
        if (root == null) return;

        // TWO bar segments, not one: the bar must not exist behind the NEXT card, or
        // the card's translucency shows the bar instead of the game. Each segment's
        // INNER edge is square (half-rounded sprite) and tucks just under the card's
        // border, so the two segments read as one continuous bar passing behind it.
        _barLeft = CreateBarSegment(root, "TopBarLeft", innerEdgeOnRight: true);
        _barLeft.anchorMin = new Vector2(0f, 1f);
        _barLeft.anchorMax = new Vector2(0.5f, 1f);

        _barRight = CreateBarSegment(root, "TopBarRight", innerEdgeOnRight: false);
        _barRight.anchorMin = new Vector2(0.5f, 1f);
        _barRight.anchorMax = new Vector2(1f, 1f);

        BuildBlocksCard(_barLeft);
        BuildHeightCard(_barRight);
        BuildNextCard(root);

        ApplyTopBarPosition();
    }

    private void ApplyTopBarPosition()
    {
        if (_barLeft == null || _barRight == null) return;

        float topOffset = SafeAreaTopOffset();
        float innerEnd = NextCardWidth * 0.5f - BarSeamTuck;

        _barLeft.offsetMin = new Vector2(BarSideMargin, -topOffset - BarHeight);
        _barLeft.offsetMax = new Vector2(-innerEnd, -topOffset);
        _barRight.offsetMin = new Vector2(innerEnd, -topOffset - BarHeight);
        _barRight.offsetMax = new Vector2(-BarSideMargin, -topOffset);

        if (_nextPanel != null)
        {
            // Vertically centered on the bar: equal overhang above and below.
            ((RectTransform)_nextPanel.transform).anchoredPosition = new Vector2(0f, -topOffset + NextCardOverhang);
        }
    }

    private static RectTransform CreateBarCard(Transform parent, string name, Color color)
    {
        GameObject card = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)card.transform;
        rect.SetParent(parent, false);
        Image image = card.GetComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    // A bar segment: plain container + a half-rounded FILL child (square inner edge).
    // The fill is a child (not the root) because the right segment's sprite is the left
    // one rotated 180 degrees - rotating the root would rotate the stat card with it.
    private static RectTransform CreateBarSegment(Transform parent, string name, bool innerEdgeOnRight)
    {
        GameObject segment = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)segment.transform;
        rect.SetParent(parent, false);

        GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform fillRect = (RectTransform)fillObject.transform;
        fillRect.SetParent(rect, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        if (!innerEdgeOnRight) fillRect.localEulerAngles = new Vector3(0f, 0f, 180f);

        Image image = fillObject.GetComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanelSquareRight();
        image.type = Image.Type.Sliced;
        image.color = BarColor;
        image.raycastTarget = false;
        return rect;
    }

    // Left segment's inset card: fully rounded inside the segment (visible corners and
    // padding on every side - it never tucks under the NEXT card).
    private void BuildBlocksCard(RectTransform barSegment)
    {
        RectTransform card = CreateBarCard(barSegment, "BlocksCard", BarInsetColor);
        card.anchorMin = Vector2.zero;
        card.anchorMax = Vector2.one;
        card.offsetMin = new Vector2(BarCardInset, BarCardInset);
        card.offsetMax = new Vector2(-BarCardInset, -BarCardInset);

        // Icon + caption + value as one center-anchored group.
        RectTransform group = CreateCenteredGroup(card, new Vector2(186f, 60f), 0f);
        CreateBarIcon(group, RuntimeSprites.CubeGlyph(), new Vector2(24f, 0f), 42f,
            new Color(0.92f, 0.86f, 0.78f, 0.85f));
        CreateBarCaption(group, "BLOCKS", new Vector2(60f, 16f));
        if (scoreText != null) PlaceBarValue(scoreText, group, new Vector2(60f, -12f));
    }

    // Right segment's inset card: height value with the pause button at its right edge.
    private void BuildHeightCard(RectTransform barSegment)
    {
        RectTransform card = CreateBarCard(barSegment, "HeightCard", BarInsetColor);
        card.anchorMin = Vector2.zero;
        card.anchorMax = Vector2.one;
        card.offsetMin = new Vector2(BarCardInset, BarCardInset);
        card.offsetMax = new Vector2(-BarCardInset, -BarCardInset);

        RectTransform group = CreateCenteredGroup(card, new Vector2(130f, 60f), -30f);
        CreateBarCaption(group, "HEIGHT", new Vector2(0f, 16f));
        if (heightText != null) PlaceBarValue(heightText, group, new Vector2(0f, -12f));

        BuildPauseButton(card);
    }

    private static RectTransform CreateCenteredGroup(RectTransform parent, Vector2 size, float xOffset)
    {
        GameObject group = new GameObject("Group", typeof(RectTransform));
        RectTransform rect = (RectTransform)group.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(xOffset, 0f);
        rect.sizeDelta = size;
        return rect;
    }

    private void CreateBarIcon(RectTransform parent, Sprite sprite, Vector2 center, float size, Color color)
    {
        GameObject icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)icon.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.anchoredPosition = center;
        rect.sizeDelta = new Vector2(size, size);
        Image image = icon.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.color = color;
        image.raycastTarget = false;
    }

    private void CreateBarCaption(RectTransform parent, string text, Vector2 position)
    {
        GameObject label = new GameObject("Caption", typeof(RectTransform));
        RectTransform rect = (RectTransform)label.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(150f, 20f);

        TextMeshProUGUI caption = label.AddComponent<TextMeshProUGUI>();
        if (scoreText != null) caption.font = scoreText.font;
        caption.text = text;
        caption.fontSize = 15f;
        caption.characterSpacing = 16f;
        caption.fontStyle = FontStyles.Bold;
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.color = StatLabelColor; // warm + translucent: the mockup's overlay-blend look
        caption.raycastTarget = false;
    }

    // Reparent the scene's stat text into the bar group and restyle it as a card value.
    private void PlaceBarValue(TextMeshProUGUI text, RectTransform group, Vector2 position)
    {
        RectTransform rect = text.rectTransform;
        rect.SetParent(group, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(150f, 38f);

        text.color = StatValueColor;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.fontStyle = FontStyles.Bold;
        text.enableAutoSizing = false;
        text.fontSize = 33f;
        text.raycastTarget = false;
    }

    // Pause lives inside the right card: darker than its card, warm pause bars.
    private void BuildPauseButton(RectTransform card)
    {
        GameObject buttonObject = new GameObject("PauseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)buttonObject.transform;
        rect.SetParent(card, false);
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-12f, 0f);
        rect.sizeDelta = new Vector2(54f, 54f);

        Image fill = buttonObject.GetComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = PauseFillColor;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.onClick.AddListener(() =>
        {
            if (_pauseMenu == null && GameManager.Instance != null)
            {
                _pauseMenu = GameManager.Instance.GetComponent<PauseMenuController>();
            }
            if (_pauseMenu != null) _pauseMenu.ShowPauseMenu();
        });

        for (int i = 0; i < 2; i++)
        {
            GameObject barObject = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform barRect = (RectTransform)barObject.transform;
            barRect.SetParent(rect, false);
            barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0.5f);
            barRect.anchoredPosition = new Vector2(i == 0 ? -7f : 7f, 0f);
            barRect.sizeDelta = new Vector2(7f, 22f);
            Image barImage = barObject.GetComponent<Image>();
            barImage.color = PauseIconColor;
            barImage.raycastTarget = false;
        }

        _pauseButton = buttonObject;
    }

    // Center NEXT card: taller than the bar, lighter and translucent - what shows
    // through it is the GAME (the bar segments stop at its edges), framed by a single
    // thin off-white border. Positioned by ApplyTopBarPosition alongside the segments.
    private void BuildNextCard(RectTransform root)
    {
        _nextPanel = new GameObject("NextCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform card = (RectTransform)_nextPanel.transform;
        card.SetParent(root, false);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 1f);
        card.pivot = new Vector2(0.5f, 1f);
        card.sizeDelta = new Vector2(NextCardWidth, BarHeight + NextCardOverhang * 2f);

        Image fill = _nextPanel.GetComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = NextCardColor;
        fill.raycastTarget = false;

        RuntimeUiKit.AddOutline(card, NextCardBorder);

        if (scoreText != null)
        {
            GameObject label = new GameObject("NextLabel", typeof(RectTransform));
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.SetParent(card, false);
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -11f);
            labelRect.sizeDelta = new Vector2(0f, 20f);

            TextMeshProUGUI labelText = label.AddComponent<TextMeshProUGUI>();
            labelText.font = scoreText.font;
            labelText.text = "NEXT";
            labelText.fontSize = 15f;
            labelText.characterSpacing = 18f;
            labelText.fontStyle = FontStyles.Bold;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.color = StatLabelColor; // same overlay-blend treatment as the stat captions
            labelText.raycastTarget = false;
        }

        GameObject preview = new GameObject("NextPiecePreview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform previewRect = (RectTransform)preview.transform;
        previewRect.SetParent(card, false);
        previewRect.anchorMin = Vector2.zero;
        previewRect.anchorMax = Vector2.one;
        previewRect.offsetMin = new Vector2(30f, 18f);
        previewRect.offsetMax = new Vector2(-30f, -40f);

        _nextPreview = preview.GetComponent<Image>();
        _nextPreview.preserveAspect = true;
        _nextPreview.raycastTarget = false;
        _nextPreview.color = NextPreviewTint;
        _nextPreview.enabled = false;
    }

    // The nudge zones' "ghost buttons": a soft rounded translucent pill filling each
    // bottom-corner zone with a faint chevron pointing the dash direction - reads as a
    // touchable glass surface instead of stray grid lines. Pure hints (raycast off; the
    // touch handling lives in TouchGestureInput), anchored at the SAME screen fractions
    // as the gesture constants so the visual never lies about the hitbox.
    private void EnsureNudgeButtons()
    {
        if (HudRoot() == null) return;

        const float w = TouchGestureInput.NudgeZoneWidthFraction;
        const float h = TouchGestureInput.NudgeZoneHeightFraction;

        CreateNudgeButton("NudgeHintL", new Vector2(0f, 0f), new Vector2(w, h), pointsLeft: true);
        CreateNudgeButton("NudgeHintR", new Vector2(1f - w, 0f), new Vector2(1f, h), pointsLeft: false);
    }

    private void CreateNudgeButton(string name, Vector2 anchorMin, Vector2 anchorMax, bool pointsLeft)
    {
        GameObject pill = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)pill.transform;
        rect.SetParent(HudRoot(), false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        // the glass sits just inside the touch zone: generous hitbox, calmer look
        rect.offsetMin = new Vector2(NudgePillInset, NudgePillInset);
        rect.offsetMax = new Vector2(-NudgePillInset, -NudgePillInset);

        Image fill = pill.GetComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = NudgePillColor;
        fill.raycastTarget = false;
        _nudgePillImages.Add((fill, NudgePillColor));

        GameObject icon = new GameObject("Chevron", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = (RectTransform)icon.transform;
        iconRect.SetParent(rect, false);
        iconRect.sizeDelta = new Vector2(NudgeChevronSize, NudgeChevronSize);
        // the chevron sprite points left; the right button is the same sprite rotated
        // (vertically symmetric, so a 180-degree turn is a clean mirror)
        if (!pointsLeft) iconRect.localEulerAngles = new Vector3(0f, 0f, 180f);

        Image chevron = icon.GetComponent<Image>();
        chevron.sprite = RuntimeSprites.Chevron();
        chevron.color = NudgeChevronColor;
        chevron.raycastTarget = false;
        _nudgePillImages.Add((chevron, NudgeChevronColor));
    }

    private void Update()
    {
        // Safe area + canvas scale are only trustworthy once the first frame runs, and
        // both can change later (rotation, window resize, multitasking) - re-apply the
        // bar position whenever the screen geometry differs from the last applied one.
        Vector3 screenState = new Vector3(Screen.width, Screen.height, Screen.safeArea.yMax);
        if (!_topBarPositioned || screenState != _lastScreenState)
        {
            _topBarPositioned = true;
            _lastScreenState = screenState;
            ApplyTopBarPosition();
        }

        // The bar's pause button only shows during live play (same predicate the old
        // floating button used; the logic moved here with the button).
        if (_pauseButton != null)
        {
            bool show = PauseMenuController.PauseAvailable;
            if (_pauseButton.activeSelf != show) _pauseButton.SetActive(show);
        }

        bool dim = BlockController.NudgeLockoutRemaining > 0f;
        if (dim == _nudgePillsDimmed) return;
        _nudgePillsDimmed = dim;

        float factor = dim ? NudgeLockoutDimFactor : 1f;
        for (int i = 0; i < _nudgePillImages.Count; i++)
        {
            (Image image, Color baseColor) = _nudgePillImages[i];
            if (image == null) continue;

            // identity lives in alpha; scale it, keep the tint
            image.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * factor);
        }
    }

    private RectTransform HudRoot()
    {
        // Cached on first use: the top bar REPARENTS scoreText into a stat card, so
        // deriving the root from its parent is only valid before the bar is built.
        if (_hudRoot == null && scoreText != null)
        {
            _hudRoot = scoreText.rectTransform.parent as RectTransform;
        }
        return _hudRoot;
    }

    private void EnsureHearts()
    {
        if (_heartsContainer != null || HudRoot() == null) return;

        GameObject container = new GameObject("Hearts", typeof(RectTransform));
        _heartsContainer = (RectTransform)container.transform;
        _heartsContainer.SetParent(HudRoot(), false);
        _heartsContainer.anchorMin = Vector2.zero;
        _heartsContainer.anchorMax = Vector2.zero;
        _heartsContainer.pivot = Vector2.zero;
        _heartsContainer.anchoredPosition = new Vector2(24f, 22f);
        _heartsContainer.sizeDelta = new Vector2(MaxHearts * (HeartSize + HeartGap), HeartSize);

        _hearts = new Image[MaxHearts];
        for (int i = 0; i < MaxHearts; i++)
        {
            GameObject heart = new GameObject($"Heart{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform heartRect = (RectTransform)heart.transform;
            heartRect.SetParent(_heartsContainer, false);
            heartRect.anchorMin = Vector2.zero;
            heartRect.anchorMax = Vector2.zero;
            heartRect.pivot = Vector2.zero;
            heartRect.anchoredPosition = new Vector2(i * (HeartSize + HeartGap), 0f);
            heartRect.sizeDelta = new Vector2(HeartSize, HeartSize);

            Image image = heart.GetComponent<Image>();
            image.sprite = RuntimeSprites.Heart();
            image.color = HeartColor;
            image.raycastTarget = false;
            image.enabled = false;
            _hearts[i] = image;
        }
    }

    private void StyleGameOverText()
    {
        if (finalScoreText == null) return;

        finalScoreText.color = HudTextColor;
        finalScoreText.enableAutoSizing = true;
        finalScoreText.fontSizeMin = 22f;
        finalScoreText.fontSizeMax = 38f;
    }
}
