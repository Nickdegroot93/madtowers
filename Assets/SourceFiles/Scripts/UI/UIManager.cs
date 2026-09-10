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
    [SerializeField] private GameObject gameOverPanel; // legacy scene panel; force-hidden (RunResultsScreen owns game over)

    // Style values are code-owned (not serialized) so tweaks always take effect —
    // serialized defaults go stale in Unity's import caches (see memory/PHYSICS.md §2).
    private static Color NextPreviewTint => GameMenuStyle.WithAlpha(HudVisualStyle.Current.Secondary, .85f);
    private static Color NextSecondaryTint => GameMenuStyle.WithAlpha(HudVisualStyle.Current.Secondary, .5f);
    // Three open groups, sharing horizontal anchors with the secondary readouts.
    private const float BarHeight = 120f;
    private const float BarSideMargin = 64f;
    private const float TopMarginBelowSafeArea = 62f;
    public const float BarBottomBelowSafeArea = TopMarginBelowSafeArea + BarHeight;
    public const float InnerCardOuterMargin = BarSideMargin;
    public const float InnerCardCenterOffset = NextCardWidth * .5f + 26f;
    private const float NextCardWidth = 204f;
    private const float NextCardOverhang = 18f;
    private const float OneSlotCardHeight = 176f;
    private const float SecondSlotExtraHeight = 78f;
    private const float TwoSlotCardHeight = OneSlotCardHeight + SecondSlotExtraHeight;
    private const float NextSlotTopInset = 22f;
    private const float NextPrimarySlotSideInset = 30f;
    private const float NextPrimarySlotHeight = 100f;
    private const float NextSlotGap = 8f;
    private const float NextSecondarySlotSideInset = 55f;
    private const float NextSecondarySlotHeight = 66f;
    private const float HeartSize = 45f;
    private const float HeartGap = 14f;
    private const int MaxHearts = RunState.MaxLives;
    private TextMeshProUGUI _objectiveCaption;
    private bool _hasRemainingGoal;
    private bool _introductionObjective;
    private bool _tutorialTeaching;
    private bool _welcomeHidden;
    private CanvasGroup _tutorialHudGroup;

    public void SetTutorialPresentation(bool welcoming, bool teaching)
    {
        _welcomeHidden = welcoming;
        _tutorialTeaching = teaching;
        var root = HudRoot();
        if (root != null && _tutorialHudGroup == null)
            _tutorialHudGroup = root.GetComponent<CanvasGroup>() ?? root.gameObject.AddComponent<CanvasGroup>();
        if (_tutorialHudGroup != null)
        {
            if (welcoming) _tutorialHudGroup.alpha = 0f;
            _tutorialHudGroup.blocksRaycasts = !welcoming;
            _tutorialHudGroup.interactable = !welcoming;
        }
        UpdateObjectiveCaption();
        ApplyNudgeHintColors();
    }

    private MedalTier? _chaseTier;
    private static Color NudgePillColor => HudVisualStyle.NudgeFill;
    private static Color NudgeChevronColor => HudVisualStyle.NudgeChevron;
    private const float NudgeChevronSize = 30f;

    // Dimmed while a failed nudge's rebound lockout runs. The base opacity (including fully hidden)
    // is the player's Nudge Guides setting (SettingsService.NudgeGuideOpacity); this dim multiplies
    // on top of it. The guides are visual only - the touch zones live in TouchGestureInput.
    private const float NudgeLockoutDimFactor = 0.3f;

    private Spawner _spawner;
    private RectTransform _heartsContainer;
    private Image[] _hearts = System.Array.Empty<Image>();
    // base color is captured at creation, where it is actually known - the dim must not
    // have to guess an image's identity back from its sprite
    private readonly System.Collections.Generic.List<(Image image, Color baseColor, int side)> _nudgePillImages =
        new System.Collections.Generic.List<(Image, Color, int)>(4);
    private bool _nudgePillsDimmed;
    private bool _nudgeIntroShown;
    private float _nudgeIntroAge = float.PositiveInfinity;
    private readonly float[] _nudgeTapAges = { float.PositiveInfinity, float.PositiveInfinity };
    private readonly float[] _nudgeReveal = new float[2];
    private GameObject _nextPanel;
    private Image[] _nextPreviews;
    private int _activeSlotCount = 1;
    private bool _nextPanelSuppressed;
    private GameObject _pauseButton;
    private PauseMenuController _pauseMenu;
    private RectTransform _hudRoot;
    private RectTransform _barLeft;
    private RectTransform _barRight;
    private RectTransform _skyFade;
    private bool _topBarPositioned;
    private Vector3 _lastScreenState;
    private Rect _lastSafeArea;
    private readonly System.Collections.Generic.Dictionary<string, Sprite> _previewSprites =
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

        ResolveObjective();
        ConfigureHudStyle();
        _spawner = Object.FindAnyObjectByType<Spawner>();

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    private void OnEnable()
    {
        GameEvents.StandingBlocksChanged += HandleStandingBlocksChanged;
        GameEvents.HeightChanged += HandleHeightChanged;
        GameEvents.LivesChanged += HandleLivesChanged;
        GameEvents.NextBlockChanged += HandleNextBlockChanged;
        GameEvents.TierEarned += HandleTierEarned;
        SettingsService.Changed += ApplyNudgeHintColors; // live nudge-guide opacity
    }

    private void OnDisable()
    {
        GameEvents.StandingBlocksChanged -= HandleStandingBlocksChanged;
        GameEvents.HeightChanged -= HandleHeightChanged;
        GameEvents.LivesChanged -= HandleLivesChanged;
        GameEvents.NextBlockChanged -= HandleNextBlockChanged;
        GameEvents.TierEarned -= HandleTierEarned;
        SettingsService.Changed -= ApplyNudgeHintColors;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        // Only PieceGhost's generated copies belong to this view, never imported art.
        foreach (Sprite sprite in _previewSprites.Values)
        {
            if (sprite == null || !sprite.hideFlags.HasFlag(HideFlags.HideAndDontSave)) continue;
            Destroy(sprite.texture);
            Destroy(sprite);
        }
        _previewSprites.Clear();
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            HandleStandingBlocksChanged(GameManager.Instance.placedBlocks);
            // liveTowerHeight (standing meters above the floor), not maxHeight (world Y - the
            // floor sits at -11.5 world, which briefly showed as "-11.5m" before the first
            // block) and not towerHeight (the monotonic record - the counter must come back
            // DOWN after a collapse, matching the live signal GameManager publishes).
            HandleHeightChanged(GameManager.Instance.liveTowerHeight);
            HandleLivesChanged(GameManager.Instance.lives);
        }

        if (_spawner != null) HandleNextBlockChanged(_spawner.GetUpcomingBlockNames());
    }

    // ---- objective readout (the left card) -----------------------------------------------
    // What this run is chasing and how far along it is, in the win condition's OWN metric:
    // PlaceBlocks counts STANDING blocks (BLOCKS.md - the live count IS the goal's numerator),
    // waves come from the live modifier, height from the same signal the HUD always showed.
    // Resolved once from the selected level; the captions are baked into the bar at build time.

    private LevelTargetType _objectiveType = LevelTargetType.Endless;
    private bool _waveObjective;
    private int _targetBlocks;
    private int _targetWaves;
    private float _targetHeightMeters;
    private int _shownWaveNumber = -1;

    private bool IsHeightObjective =>
        _objectiveType == LevelTargetType.ReachHeight || _objectiveType == LevelTargetType.TimedReachHeight;

    private void ResolveObjective()
    {
        LevelDefinition level = LevelSelectionState.SelectedLevel;
        _objectiveType = level != null ? level.TargetType : LevelTargetType.Endless;
        _introductionObjective = level != null && level.IsIntroduction;
        if (level == null) return;

        // Endless levels running the wave modifier still get the wave counter - just unsuffixed.
        _waveObjective = _objectiveType == LevelTargetType.ClearWaves;
        if (!_waveObjective && level.Modifiers != null)
        {
            for (int i = 0; i < level.Modifiers.Count; i++)
            {
                if (level.Modifiers[i] is HeightLimitWavesModifier) _waveObjective = true;
            }
        }

        // Remaining follows the next unearned rung. After gold, show the live total.
        // Keep height thresholds fractional; rounding the goal can report zero too soon.
        MedalTier? nextTier = LevelTiers.LowestUnearned(level);
        _chaseTier = nextTier;
        _hasRemainingGoal = level.IsIntroduction || nextTier.HasValue;
        float target = LevelTiers.Threshold(level, nextTier ?? LevelTiers.MaxTier);
        switch (_objectiveType)
        {
            case LevelTargetType.PlaceBlocks:
            case LevelTargetType.TimedPlaceBlocks: _targetBlocks = Mathf.CeilToInt(target); break;
            case LevelTargetType.ReachHeight:
            case LevelTargetType.TimedReachHeight: _targetHeightMeters = target; break;
            case LevelTargetType.ClearWaves: _targetWaves = Mathf.CeilToInt(target); break;
        }
    }

    // A tier's hold-steady just completed: roll remaining to the next rung.
    // The threshold comes from the event's level, never re-derived from the store - Custom
    // Game levels have no store identity, and the controller's session state isn't visible here.
    private void HandleTierEarned(LevelDefinition level, MedalTier tier)
    {
        if (level == null || level != LevelSelectionState.SelectedLevel) return;

        _hasRemainingGoal = tier < LevelTiers.MaxTier;
        _chaseTier = _hasRemainingGoal ? tier + 1 : (MedalTier?)null;
        UpdateObjectiveCaption();
        if (!_hasRemainingGoal)
        {
            if (GameManager.Instance != null)
            {
                HandleStandingBlocksChanged(GameManager.Instance.placedBlocks);
                HandleHeightChanged(GameManager.Instance.liveTowerHeight);
            }
            return;
        }
        float next = LevelTiers.Threshold(level, tier + 1);
        switch (_objectiveType)
        {
            case LevelTargetType.PlaceBlocks:
            case LevelTargetType.TimedPlaceBlocks:
                _targetBlocks = Mathf.CeilToInt(next);
                if (GameManager.Instance != null) HandleStandingBlocksChanged(GameManager.Instance.placedBlocks);
                break;
            case LevelTargetType.ReachHeight:
            case LevelTargetType.TimedReachHeight:
                _targetHeightMeters = next;
                if (GameManager.Instance != null) HandleHeightChanged(GameManager.Instance.liveTowerHeight);
                break;
            case LevelTargetType.ClearWaves:
                _targetWaves = Mathf.CeilToInt(next);
                _shownWaveNumber = -1; // redraw the polled wave readout
                break;
        }
    }

    // Presentation only: read the live counters and the armed tier's existing threshold.
    public static int BlocksRemaining(int target, int standing) => Mathf.Max(0, target - standing);
    public static int MetersRemaining(float target, float height) => Mathf.CeilToInt(Mathf.Max(0f, target - height));
    private void HandleStandingBlocksChanged(int placedBlocks)
    {
        if (scoreText == null || _waveObjective || IsHeightObjective) return;
        scoreText.text = _introductionObjective
            ? $"{Mathf.Max(0, placedBlocks)}<size=50%> / {_targetBlocks}</size>"
            : (_hasRemainingGoal ? BlocksRemaining(_targetBlocks, placedBlocks) : placedBlocks).ToString();
    }
    private void HandleHeightChanged(float height)
    {
        if (scoreText == null || !IsHeightObjective) return;
        int value = _hasRemainingGoal ? MetersRemaining(_targetHeightMeters, height) : Mathf.FloorToInt(Mathf.Max(0, height));
        scoreText.text = value + "<size=55%>m</size>";
    }
    private void UpdateObjectiveCaption()
    {
        if (_objectiveCaption == null) return;
        if (_introductionObjective)
        {
            _objectiveCaption.text = _tutorialTeaching ? "TUTORIAL" : "PRACTICE";
            return;
        }
        string objective = _waveObjective ? "WAVE" : IsHeightObjective ? "HEIGHT" : "BLOCKS";
        _objectiveCaption.text = _chaseTier.HasValue
            ? $"{objective} · {MedalStyle.DisplayName(_chaseTier.Value)}"
            : objective;
    }

    private int _shownLives = -1;
    private bool[] _heartFull;

    // Two-state hearts (SHOP.md §2) in three FIXED sockets (RunState.MaxLives): a held life
    // is the FULL heart, a missing one stays visible as the dark socket. Most runs start at
    // ZERO lives (lives are bought supplies; the Flood grants all 3), so the row must read
    // as "empty slots to fill" from the first frame - never as UI that appears only once a
    // life exists.
    private void HandleLivesChanged(int lives)
    {
        if (_heartsContainer == null) return;

        lives = Mathf.Min(lives, _hearts.Length);
        int previous = _shownLives < 0 ? lives : _shownLives;
        _shownLives = lives;

        for (int i = 0; i < _hearts.Length; i++)
        {
            if (_hearts[i] == null) continue;
            bool full = i < lives;
            bool wasFull = _heartFull[i];
            _heartFull[i] = full;

            if (full)
            {
                SetHeartState(_hearts[i], full: true);
                if (!wasFull && lives > previous) StartCoroutine(PopHeart(_hearts[i])); // gained: pop in
            }
            else if (wasFull && lives < previous)
            {
                StartCoroutine(BreakHeart(_hearts[i]));                          // lost: shatter to socket
            }
            else
            {
                SetHeartState(_hearts[i], full: false);
            }
        }

        if (lives < previous) SfxPlayer.Play("life_lost", 0.8f, 0.03f);
    }

    private void SetHeartState(Image heart, bool full)
    {
        heart.sprite = HudGlyphs.Get(full ? HudGlyphs.Mark.Heart : HudGlyphs.Mark.EmptyHeart);
        heart.color = full ? HudVisualStyle.Current.Heart : HudVisualStyle.Current.EmptyHeart;
    }

    // The lost heart swells for a beat, then SHATTERS: it swaps to the empty socket while
    // four UV-quadrant shards of the full art fly out, spin and dissolve. Unscaled time so
    // the pause/game-over freeze can't rob the player of the feedback. Restrained per
    // JUICE.md: shards and one existing sfx, no flash, no shake.
    private System.Collections.IEnumerator BreakHeart(Image heart)
    {
        const float swellSeconds = 0.14f;
        Vector3 baseScale = heart.rectTransform.localScale;
        float age = 0f;
        while (age < swellSeconds && heart != null)
        {
            age += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(age / swellSeconds);
            heart.rectTransform.localScale = baseScale * (1f + 0.35f * t);
            yield return null;
        }
        if (heart == null) yield break;

        SpawnHeartShards(heart);
        heart.rectTransform.localScale = baseScale;
        SetHeartState(heart, full: false);
    }

    // Four quadrant shards cut straight from the full-heart sprite's texture, so the break
    // always matches the art - no separate cracked asset to keep in sync.
    private void SpawnHeartShards(Image heart)
    {
        Sprite full = HudGlyphs.Get(HudGlyphs.Mark.Heart);
        if (full == null || _heartsContainer == null) return;

        Rect r = full.rect;
        Vector2 halfSize = ((RectTransform)heart.transform).sizeDelta * 0.5f;
        Vector2 center = ((RectTransform)heart.transform).anchoredPosition + halfSize;
        for (int q = 0; q < 4; q++)
        {
            int qx = q % 2;
            int qy = q / 2;
            var quadrant = new Rect(r.x + qx * r.width * 0.5f, r.y + qy * r.height * 0.5f,
                r.width * 0.5f, r.height * 0.5f);
            Sprite shardSprite = Sprite.Create(full.texture, quadrant, new Vector2(0.5f, 0.5f));

            GameObject shard = new GameObject("HeartShard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = (RectTransform)shard.transform;
            rect.SetParent(_heartsContainer, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = halfSize;
            rect.anchoredPosition = center + new Vector2((qx - 0.5f) * halfSize.x, (qy - 0.5f) * halfSize.y);

            Image image = shard.GetComponent<Image>();
            image.sprite = shardSprite;
            image.color = heart.color;
            image.raycastTarget = false;

            Vector2 fling = new Vector2((qx - 0.5f) * 2f + Random.Range(-0.4f, 0.4f),
                (qy - 0.5f) * 2f + Random.Range(0.2f, 0.9f)) * Random.Range(90f, 140f);
            StartCoroutine(AnimateHeartShard(rect, image, shardSprite, fling,
                Random.Range(-260f, 260f)));
        }
    }

    private System.Collections.IEnumerator AnimateHeartShard(RectTransform rect, Image image,
        Sprite sprite, Vector2 velocity, float spinDegPerSec)
    {
        const float duration = 0.55f;
        const float gravity = -420f;
        float age = 0f;
        Color baseColor = image.color;
        while (age < duration && rect != null)
        {
            float dt = Time.unscaledDeltaTime;
            age += dt;
            velocity.y += gravity * dt;
            rect.anchoredPosition += velocity * dt;
            rect.localRotation = Quaternion.Euler(0f, 0f, rect.localEulerAngles.z + spinDegPerSec * dt);
            float t = Mathf.Clamp01(age / duration);
            Color c = baseColor;
            c.a = baseColor.a * (1f - t * t);
            image.color = c;
            yield return null;
        }
        if (rect != null) Destroy(rect.gameObject);
        if (sprite != null) Destroy(sprite); // the quadrant Sprite wrapper is ours to free
    }

    private System.Collections.IEnumerator PopHeart(Image heart)
    {
        const float duration = 0.3f;
        Vector3 baseScale = heart.rectTransform.localScale;
        float age = 0f;
        while (age < duration && heart != null)
        {
            age += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(age / duration);
            heart.rectTransform.localScale = baseScale * (0.5f + 0.5f * t + 0.18f * Mathf.Sin(t * Mathf.PI));
            yield return null;
        }
        if (heart != null) heart.rectTransform.localScale = baseScale;
    }

    private void HandleNextBlockChanged(System.Collections.Generic.IReadOnlyList<string> blockNames)
    {
        if (_nextPreviews == null) return;
        if (OverdrawSession.SuppressesNextPreview)
        {
            SetNextPanelSuppressed(true);
            return;
        }

        int count = blockNames != null ? blockNames.Count : 0;
        EnsureSlotLayout(count);

        // The card itself stays put (it's part of the bar's silhouette); only the ghosts
        // inside come and go. Slots beyond the supplied count clear.
        for (int i = 0; i < _nextPreviews.Length; i++)
        {
            if (_nextPreviews[i] == null) continue;
            SetSlotSprite(_nextPreviews[i], i < count ? blockNames[i] : null);
        }
    }

    private void SetNextPanelSuppressed(bool suppressed)
    {
        if (_nextPanelSuppressed == suppressed) return;

        _nextPanelSuppressed = suppressed;
        if (_nextPanel != null) _nextPanel.SetActive(!suppressed);

        if (!suppressed && _spawner != null)
        {
            HandleNextBlockChanged(_spawner.GetUpcomingBlockNames());
        }
    }

    private void SetSlotSprite(Image slot, string blockName)
    {
        string shape = ChapterSkins.ExtractShapeToken(blockName);
        Sprite ghost = string.IsNullOrEmpty(shape) ? null : GetPreviewSprite(shape);
        slot.sprite = ghost;
        slot.enabled = ghost != null;
    }

    /// <summary>The NEXT card's LIVE bottom edge, in canvas px below the safe-area top - grows
    /// when Foresight adds the second slot. GameTypeBadgeHud hangs its pill off this, so the
    /// badge rides the card instead of overlapping it.</summary>
    public static float NextCardBottomBelowSafeArea { get; private set; } =
        TopMarginBelowSafeArea - NextCardOverhang + OneSlotCardHeight;

    // Grows/shrinks the NEXT card to fit the previewed count (1 vs 2 slots). Touches the
    // card only when the count actually changes, so the common per-spawn update is O(1)
    // with no layout churn. The card is top-pivoted, so the extra height extends downward.
    private void EnsureSlotLayout(int nameCount)
    {
        int layout = Mathf.Clamp(nameCount, 1, _nextPreviews.Length);
        if (layout == _activeSlotCount || _nextPanel == null) return;

        _activeSlotCount = layout;
        float height = layout >= 2 ? TwoSlotCardHeight : OneSlotCardHeight;
        ((RectTransform)_nextPanel.transform).sizeDelta = new Vector2(NextCardWidth, height);
        NextCardBottomBelowSafeArea = TopMarginBelowSafeArea - NextCardOverhang + height;
    }

    // Retain stone relief, but remove the playable piece's colour. The Image applies the
    // same chapter ink as NEXT/brackets. Generated once per shape, freed with this view.
    private Sprite GetPreviewSprite(string shape)
    {
        string cacheKey = $"{ChapterSkins.Folder}:{shape}";
        if (_previewSprites.TryGetValue(cacheKey, out Sprite cached)) return cached;
        Sprite sprite = PieceGhost.Generate(shape, NeutralizePreview);
        if (sprite != null && sprite.hideFlags.HasFlag(HideFlags.HideAndDontSave))
        {
            // Chapter art can have uneven transparent bleed. Fit the visible silhouette,
            // not the padded texture, so every shape shares the slot's visual centre.
            Vector4 padding = UnityEngine.Sprites.DataUtility.GetPadding(sprite);
            Rect bounds = sprite.rect;
            bounds.x += padding.x;
            bounds.y += padding.y;
            bounds.width -= padding.x + padding.z;
            bounds.height -= padding.y + padding.w;
            if (bounds.width > 0 && bounds.height > 0)
            {
                Sprite trimmed = Sprite.Create(sprite.texture, bounds, new Vector2(.5f, .5f),
                    sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                trimmed.hideFlags = HideFlags.HideAndDontSave;
                Destroy(sprite); // keep the generated texture, now owned by trimmed
                sprite = trimmed;
            }
        }
        _previewSprites[cacheKey] = sprite;
        return sprite;
    }

    private static void NeutralizePreview(Color[] pixels)
    {
        float peak = .001f;
        foreach (Color pixel in pixels)
            if (pixel.a > .05f) peak = Mathf.Max(peak, pixel.grayscale);

        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];
            // A restrained value range preserves seams and weathering without restoring
            // the strong coloured/block-outline treatment of the active piece.
            float value = Mathf.Lerp(.35f, 1f, Mathf.Clamp01(pixel.grayscale / peak));
            pixels[i] = new Color(value, value, value, pixel.a);
        }
    }

    private void ConfigureHudStyle()
    {
        // No opt-out: the bar carries the game's ONLY pause entry point and the next
        // preview - a style toggle must never be able to remove those.
        BuildTopBar();

        if (livesText != null) livesText.gameObject.SetActive(false);
        if (nextBlockText != null) nextBlockText.gameObject.SetActive(false);
        // Legacy too since the lives took its card: height now shows on the LEFT when (and
        // only when) it is the objective - "in most cases height is completely irrelevant".
        if (heightText != null) heightText.gameObject.SetActive(false);

        EnsureNudgeButtons();
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
        return RuntimeUiKit.SafeAreaTopInset(HudCanvas()) + TopMarginBelowSafeArea;
    }

    private Canvas HudCanvas()
    {
        return HudRoot() != null ? HudRoot().GetComponentInParent<Canvas>() : null;
    }

    private static RectTransform Group(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform; rect.SetParent(parent, false); return rect;
    }
    private void BuildTopBar()
    {
        var root = HudRoot(); if (root == null) return;
        _skyFade = HudVisualStyle.AddSkyFade(root);
        _barLeft = Group(root, "ObjectiveGroup");
        _barLeft.anchorMin = new Vector2(0,1); _barLeft.anchorMax = new Vector2(.5f,1);
        _barRight = Group(root, "LivesGroup");
        _barRight.anchorMin = new Vector2(.5f,1); _barRight.anchorMax = new Vector2(1,1);
        BuildObjectiveCard(_barLeft); BuildLivesCard(_barRight); BuildNextCard(root);
        ApplyTopBarPosition();
    }
    private void ApplyTopBarPosition()
    {
        if (_barLeft == null || _barRight == null) return;
        Canvas canvas = HudCanvas(); float top = SafeAreaTopOffset();
        if (_skyFade != null) _skyFade.sizeDelta = new Vector2(0, 480 + RuntimeUiKit.SafeAreaTopInset(canvas));
        float left = BarSideMargin + RuntimeUiKit.SafeAreaLeftInset(canvas);
        float right = BarSideMargin + RuntimeUiKit.SafeAreaRightInset(canvas);
        _barLeft.offsetMin = new Vector2(left, -top-BarHeight);
        _barLeft.offsetMax = new Vector2(-InnerCardCenterOffset, -top);
        _barRight.offsetMin = new Vector2(InnerCardCenterOffset, -top-BarHeight);
        _barRight.offsetMax = new Vector2(-right, -top);
        if (_nextPanel != null) ((RectTransform)_nextPanel.transform).anchoredPosition = new Vector2(0,-top+NextCardOverhang);
    }
    private void BuildObjectiveCard(RectTransform parent)
    {
        var level = LevelSelectionState.SelectedLevel;
        CreateBarIcon(parent, HudGlyphs.Get(HudGlyphs.ForLevel(level)), new Vector2(35,0), 72, HudVisualStyle.Current.Ink);
        if (scoreText == null) scoreText = Group(parent,"ObjectiveValue").gameObject.AddComponent<TextMeshProUGUI>();
        var rect = scoreText.rectTransform; rect.SetParent(parent,false);
        rect.anchorMin = new Vector2(0,.5f); rect.anchorMax = new Vector2(1,.5f);
        rect.pivot = new Vector2(0,.5f); rect.offsetMin = new Vector2(88,-29); rect.offsetMax = new Vector2(-8,67);
        HudVisualStyle.Text(scoreText); scoreText.fontSize=60; scoreText.enableAutoSizing=false;
        scoreText.alignment=TextAlignmentOptions.MidlineLeft;
        var caption=Group(parent,"ObjectiveCaption");
        caption.anchorMin=new Vector2(0,.5f);caption.anchorMax=new Vector2(1,.5f);
        caption.offsetMin=new Vector2(90,-49);caption.offsetMax=new Vector2(-8,-21);
        _objectiveCaption=caption.gameObject.AddComponent<TextMeshProUGUI>();
        HudVisualStyle.Text(_objectiveCaption,true);_objectiveCaption.fontSize=20;_objectiveCaption.characterSpacing=3;
        _objectiveCaption.color=HudVisualStyle.Current.Secondary;
        _objectiveCaption.alignment=TextAlignmentOptions.MidlineLeft;
        UpdateObjectiveCaption();
    }
    private void BuildLivesCard(RectTransform parent)
    {
        // Preserve the forgiving whole-cluster pause hitbox; only its painted card is gone.
        var hit=parent.gameObject.AddComponent<Image>();hit.color=Color.clear;
        var button=parent.gameObject.AddComponent<Button>();button.targetGraphic=hit;
        button.transition=Selectable.Transition.None;button.onClick.AddListener(OpenPauseMenu);
        float heartsWidth=MaxHearts*HeartSize+(MaxHearts-1)*HeartGap;
        var group=Group(parent,"HealthAndPause");
        group.anchorMin=group.anchorMax=new Vector2(1,.5f);group.pivot=new Vector2(1,.5f);
        group.sizeDelta=new Vector2(heartsWidth+36+72,72);group.anchoredPosition=Vector2.zero;
        BuildHearts(group,heartsWidth);BuildPauseButton(group);
    }
    private Image CreateBarIcon(RectTransform parent, Sprite sprite, Vector2 center, float size, Color color)
    {
        var rect=Group(parent,"Icon");rect.anchorMin=rect.anchorMax=new Vector2(0,.5f);
        rect.anchoredPosition=center;rect.sizeDelta=new Vector2(size,size);
        var image=rect.gameObject.AddComponent<Image>();image.sprite=sprite;image.preserveAspect=true;
        image.color=color;image.raycastTarget=false;return image;
    }

    // Shared by the glyph and the whole-card hitbox. Guarded on availability: the glyph
    // hides itself when pausing is off the table, but the card stays tappable and must
    // quietly do nothing then.
    private void OpenPauseMenu()
    {
        if (!PauseMenuController.PauseAvailable) return;
        if (_pauseMenu == null && GameManager.Instance != null)
        {
            _pauseMenu = GameManager.Instance.GetComponent<PauseMenuController>();
        }
        if (_pauseMenu != null) _pauseMenu.ShowPauseMenu();
    }

    // The pause glyph at the right end of the lives cluster: darker than its card, warm bars.
    private void BuildPauseButton(RectTransform group)
    {
        GameObject buttonObject = new GameObject("PauseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)buttonObject.transform;
        rect.SetParent(group, false);
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(72f, 72f);

        Image fill = buttonObject.GetComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = Color.clear;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(OpenPauseMenu);

        for (int i = 0; i < 2; i++)
        {
            GameObject barObject = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform barRect = (RectTransform)barObject.transform;
            barRect.SetParent(rect, false);
            barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0.5f);
            barRect.anchoredPosition = new Vector2(i == 0 ? -9f : 9f, 0f);
            barRect.sizeDelta = new Vector2(7f, 32f);
            Image barImage = barObject.GetComponent<Image>();
            barImage.color = HudVisualStyle.Current.Ink;
            barImage.raycastTarget = false;
        }

        _pauseButton = buttonObject;
    }

    // Open NEXT brackets and a footer label. Each preview is centered in its own slot.
    private void BuildNextCard(RectTransform root)
    {
        _nextPanel = new GameObject("NextCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform card = (RectTransform)_nextPanel.transform;
        card.SetParent(root, false);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 1f);
        card.pivot = new Vector2(0.5f, 1f);
        card.sizeDelta = new Vector2(NextCardWidth, OneSlotCardHeight);
        // The published bottom edge starts over with the card: statics outlive scene reloads,
        // and a fresh run must not inherit the previous run's Foresight height.
        NextCardBottomBelowSafeArea = TopMarginBelowSafeArea - NextCardOverhang + OneSlotCardHeight;

        Image fill = _nextPanel.GetComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = Color.clear;
        fill.raycastTarget = false;

        HudVisualStyle.Brackets(card);

        if (scoreText != null)
        {
            GameObject label = new GameObject("NextLabel", typeof(RectTransform));
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.SetParent(card, false);
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, -9.5f);
            labelRect.sizeDelta = new Vector2(-58f, 40f);

            TextMeshProUGUI labelText = label.AddComponent<TextMeshProUGUI>();
            HudVisualStyle.Text(labelText,true);
            labelText.text = "NEXT";
            labelText.fontSize = 20f;
            labelText.characterSpacing = 9f;
            labelText.fontStyle = FontStyles.Normal;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.color = HudVisualStyle.Current.Secondary; // same overlay-blend treatment as the stat captions
            labelText.raycastTarget = false;
        }

        // Two stacked preview slots, both pinned to the card's TOP so the second extends
        // the card downward (Foresight). Slot 0 (immediate-next) matches the single-preview
        // layout exactly; slot 1 (next-next) is smaller and dimmer, hidden until the queue
        // widens. The array length tracks MaxVisibleQueueDepth, but the card layout below is
        // tuned for two: the data layer (Spawner queue + NextBlockChanged list) scales to any
        // depth on its own, the VIEW does not - a depth of 3+ also needs a third slot built
        // here and a taller-card case in EnsureSlotLayout.
        _nextPreviews = new Image[Spawner.MaxVisibleQueueDepth];
        _nextPreviews[0] = CreatePreviewSlot(card, "NextPiecePreview",
            NextPrimarySlotSideInset, NextSlotTopInset, NextPrimarySlotHeight, NextPreviewTint);
        if (_nextPreviews.Length > 1)
        {
            _nextPreviews[1] = CreatePreviewSlot(card, "NextNextPiecePreview",
                NextSecondarySlotSideInset, NextSlotTopInset + NextPrimarySlotHeight + NextSlotGap,
                NextSecondarySlotHeight, NextSecondaryTint);
        }
        _activeSlotCount = 1;
    }

    // A top-anchored box with a CENTER pivot. Image.preserveAspect uses the pivot to align
    // any spare space: a top pivot pins wide/short pieces (especially I) to the top edge.
    // Offset by half the slot height to keep the box itself in the same location.
    private Image CreatePreviewSlot(RectTransform card, string name,
        float sideInset, float topInset, float height, Color tint)
    {
        GameObject slot = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)slot.transform;
        rect.SetParent(card, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(-2f * sideInset, height);
        rect.anchoredPosition = new Vector2(0f, -topInset - height * .5f);

        Image image = slot.GetComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = tint;
        image.enabled = false;
        return image;
    }

    // The nudge zones' hint buttons: invisible by default, but still built to exactly fill
    // each bottom-corner touch zone. A later settings toggle can raise NudgeHintVisibility
    // / user state and reveal these same objects without changing the gesture contract.
    // Pure hints (raycast off; the touch handling lives in TouchGestureInput), anchored at
    // the SAME screen fractions as the gesture constants so the visual never lies about the
    // hitbox.
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
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image fill = pill.GetComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        int side = pointsLeft ? 0 : 1;
        fill.color = NudgeHintColor(NudgePillColor, false, side);
        fill.raycastTarget = false;
        _nudgePillImages.Add((fill, NudgePillColor, side));

        GameObject icon = new GameObject("Chevron", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = (RectTransform)icon.transform;
        iconRect.SetParent(rect, false);
        iconRect.sizeDelta = new Vector2(NudgeChevronSize, NudgeChevronSize);
        // the chevron sprite points left; the right button is the same sprite rotated
        // (vertically symmetric, so a 180-degree turn is a clean mirror)
        if (!pointsLeft) iconRect.localEulerAngles = new Vector3(0f, 0f, 180f);

        Image chevron = icon.GetComponent<Image>();
        chevron.sprite = RuntimeSprites.Chevron();
        chevron.color = NudgeHintColor(NudgeChevronColor, false, side);
        chevron.raycastTarget = false;
        _nudgePillImages.Add((chevron, NudgeChevronColor, side));
    }

    private void Update()
    {
        if (_tutorialHudGroup != null)
            _tutorialHudGroup.alpha = Mathf.MoveTowards(_tutorialHudGroup.alpha,
                _welcomeHidden ? 0f : 1f, Time.unscaledDeltaTime * 2.5f);
        // Safe area + canvas scale are only trustworthy once the first frame runs, and
        // both can change later (rotation, window resize, multitasking) - re-apply the
        // bar position whenever the screen geometry differs from the last applied one.
        Canvas canvas = _hudRoot != null ? _hudRoot.GetComponentInParent<Canvas>() : null;
        Vector3 screenState = new Vector3(Screen.width, Screen.height, canvas != null ? canvas.scaleFactor : 1f);
        if (!_topBarPositioned || screenState != _lastScreenState || Screen.safeArea != _lastSafeArea)
        {
            _topBarPositioned = true;
            _lastScreenState = screenState;
            _lastSafeArea = Screen.safeArea;
            ApplyTopBarPosition(); // the hearts ride the bar card, no separate reposition
        }

        // Wave objective: the wave number advances from a timed confirm (no HUD event fires
        // at that moment), so the readout polls the live modifier - a comparison per frame.
        if (_waveObjective && scoreText != null)
        {
            HeightLimitWavesModifier run = HeightLimitWavesModifier.ActiveRun;
            int wave = (run != null ? run.WavesCleared : 0) + 1;
            if (wave != _shownWaveNumber)
            {
                _shownWaveNumber = wave;
                scoreText.text = wave.ToString();
            }
        }

        // The bar's pause button only shows during live play (same predicate the old
        // floating button used; the logic moved here with the button).
        if (_pauseButton != null)
        {
            bool show = PauseMenuController.PauseAvailable;
            if (_pauseButton.activeSelf != show) _pauseButton.SetActive(show);
        }

        SetNextPanelSuppressed(OverdrawSession.SuppressesNextPreview);

        UpdateNudgeHints();
    }

    public void FlashNudgeButton(int direction)
    {
        _nudgeTapAges[direction < 0 ? 0 : 1] = 0f;
    }

    // One smooth reminder on the first playable brick, then half-second feedback on
    // the pressed corner. Pause freezes these clocks; neither cue changes saved opacity.
    private void UpdateNudgeHints()
    {
        bool playing = Time.timeScale > 0f &&
            (GameManager.Instance == null || !GameManager.Instance.IsGamePaused);
        if (!_nudgeIntroShown && playing && !TouchGestureInput.Suspended &&
            BlockController.ActiveControlled != null &&
            (BlockController.AllowedGestures & PieceGestures.Nudge) != 0)
        {
            _nudgeIntroShown = true;
            _nudgeIntroAge = 0f;
        }

        float dt = playing ? Time.unscaledDeltaTime : 0f;
        _nudgeIntroAge += dt;
        float intro = NudgeRevealEnvelope(_nudgeIntroAge, 1f, .16f, .18f);
        bool dim = BlockController.NudgeLockoutRemaining > 0f;
        bool changed = dim != _nudgePillsDimmed;
        _nudgePillsDimmed = dim;
        for (int side = 0; side < 2; side++)
        {
            _nudgeTapAges[side] += dt;
            float reveal = Mathf.Max(intro, NudgeRevealEnvelope(_nudgeTapAges[side], .5f, .06f, .04f));
            changed |= !Mathf.Approximately(_nudgeReveal[side], reveal);
            _nudgeReveal[side] = reveal;
        }
        if (changed) ApplyNudgeHintColors();
    }

    private static float NudgeRevealEnvelope(float age, float duration, float fadeIn, float hold)
    {
        if (age < 0f || age >= duration) return 0f;
        if (age < fadeIn) return Mathf.SmoothStep(0f, 1f, age / fadeIn);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeIn + hold, duration, age));
    }

    // Re-tint every nudge hint from its base colour, the player's opacity setting, and the current
    // lockout dim. Called on lockout changes and on SettingsService.Changed (live opacity edits).
    private void ApplyNudgeHintColors()
    {
        for (int i = 0; i < _nudgePillImages.Count; i++)
        {
            (Image image, Color baseColor, int side) = _nudgePillImages[i];
            if (image != null) image.color = NudgeHintColor(baseColor, _nudgePillsDimmed, side);
        }
    }

    // Tutorial spotlight for the corner pills: the nudge step must be able to SHOW the
    // otherwise-invisible buttons, whatever the player's Nudge Guides setting says. 0..1
    // blends each hint toward a clearly visible version of itself. Owners must set it back
    // to 0 (the tutorial does in its teardown); reset per run for safety.
    private const float NudgeGuideBoostAlphaFactor = 3.5f;
    private static float _nudgeGuideBoost;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetNudgeGuideBoost() => _nudgeGuideBoost = 0f;

    public static void SetNudgeGuideBoost(float boost)
    {
        boost = Mathf.Clamp01(boost);
        if (Mathf.Approximately(_nudgeGuideBoost, boost)) return;
        _nudgeGuideBoost = boost;
        if (Instance != null) Instance.ApplyNudgeHintColors();
    }

    private Color NudgeHintColor(Color baseColor, bool dimmed, int side)
    {
        float dimFactor = dimmed ? NudgeLockoutDimFactor : 1f;
        float alpha = baseColor.a * SettingsService.NudgeGuideOpacity;
        alpha = Mathf.Lerp(alpha, Mathf.Min(1f, baseColor.a * NudgeGuideBoostAlphaFactor), _nudgeGuideBoost);
        // Keep presses legible during cooldown, including when the idle guide is hidden.
        alpha = Mathf.Max(alpha * dimFactor,
            Mathf.Min(1f, baseColor.a * NudgeGuideBoostAlphaFactor) * _nudgeReveal[side]);
        return new Color(baseColor.r, baseColor.g, baseColor.b, _welcomeHidden ? 0f : alpha);
    }

    private readonly Vector3[] _hudCornerBuffer = new Vector3[4];

    /// <summary>
    /// World-space Y of the LOWEST edge of the top HUD (bar segments + the NEXT card, whichever
    /// hangs lowest), for the given gameplay camera. Lets a gameplay overlay (the Fission shard
    /// queue) sit clear of the HUD on any aspect / safe-area instead of guessing a screen fraction.
    /// Returns false if the bar has not been built yet.
    /// </summary>
    public bool TryGetTopHudBottomWorldY(Camera worldCamera, out float worldY)
    {
        worldY = 0f;
        if (worldCamera == null) return false;

        Canvas canvas = HudRoot() != null ? HudRoot().GetComponentInParent<Canvas>() : null;
        Camera uiCamera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? canvas.worldCamera
            : null;
        float depth = Mathf.Abs(worldCamera.transform.position.z);

        bool any = false;
        float lowestWorldY = float.MaxValue;
        RectTransform nextRect = _nextPanel != null && _nextPanel.activeInHierarchy && !OverdrawSession.SuppressesNextPreview
            ? (RectTransform)_nextPanel.transform
            : null;
        any |= AccumulateLowestBottom(_barLeft, uiCamera, worldCamera, depth, ref lowestWorldY);
        any |= AccumulateLowestBottom(_barRight, uiCamera, worldCamera, depth, ref lowestWorldY);
        any |= AccumulateLowestBottom(nextRect, uiCamera, worldCamera, depth, ref lowestWorldY);
        if (!any) return false;

        worldY = lowestWorldY;
        return true;
    }

    private bool AccumulateLowestBottom(RectTransform rect, Camera uiCamera, Camera worldCamera, float depth, ref float lowestWorldY)
    {
        if (rect == null) return false;

        rect.GetWorldCorners(_hudCornerBuffer); // [0]=bottom-left, [3]=bottom-right
        float screenBottomY = Mathf.Min(
            RectTransformUtility.WorldToScreenPoint(uiCamera, _hudCornerBuffer[0]).y,
            RectTransformUtility.WorldToScreenPoint(uiCamera, _hudCornerBuffer[3]).y);
        float wy = worldCamera.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f, screenBottomY, depth)).y;
        if (wy < lowestWorldY) lowestWorldY = wy;
        return true;
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

    // The three life sockets, filling the left side of the centered lives cluster (they ride
    // the bar's safe-area offset - no separate positioning). All sockets render from the
    // first frame: full hearts fill in as lives are bought or earned.
    private void BuildHearts(RectTransform group, float heartsWidth)
    {
        GameObject container = new GameObject("Hearts", typeof(RectTransform));
        _heartsContainer = (RectTransform)container.transform;
        _heartsContainer.SetParent(group, false);
        _heartsContainer.anchorMin = new Vector2(0f, 0.5f);
        _heartsContainer.anchorMax = new Vector2(0f, 0.5f);
        _heartsContainer.pivot = new Vector2(0f, 0.5f);
        _heartsContainer.anchoredPosition = Vector2.zero;
        _heartsContainer.sizeDelta = new Vector2(heartsWidth, HeartSize);

        _hearts = new Image[MaxHearts];
        _heartFull = new bool[MaxHearts];
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
            image.color = Color.white;
            image.raycastTarget = false;
            _hearts[i] = image;
            SetHeartState(image, full: false);
        }
    }

}
