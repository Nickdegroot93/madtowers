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
    private static readonly Color NextPreviewTint = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color NextSecondaryTint = new Color(1f, 1f, 1f, 0.32f); // dimmer next-next slot
    // Top bar: one dark rounded master card with the OBJECTIVE card on the left ("62/100",
    // "WAVE 3/5", "12.4/30m" - what you're chasing and how far you are), the lives sockets +
    // pause on the right, and a taller NEXT card vertically centered between them.
    // Pure greyscale: near-opaque black tones so translucent layers stacking over each
    // other don't read as "weird lines" - each layer barely lets the one below through.
    private static readonly Color BarColor = new Color(0f, 0f, 0f, 0.62f);
    private static readonly Color BarInsetColor = new Color(0f, 0f, 0f, 0.78f);
    private static readonly Color NextCardColor = new Color(0f, 0f, 0f, 0.78f);
    private static readonly Color NextCardBorder = new Color(0.92f, 0.92f, 0.92f, 0.38f);
    private static readonly Color StatLabelColor = new Color(0.80f, 0.80f, 0.80f, 0.55f);
    private static readonly Color StatValueColor = new Color(0.97f, 0.97f, 0.97f, 1f);
    private static readonly Color PauseFillColor = new Color(0f, 0f, 0f, 0.45f);
    private static readonly Color PauseIconColor = new Color(0.85f, 0.85f, 0.85f, 0.85f);
    private const float BarHeight = 104f;
    private const float BarSideMargin = 120f; // breathing room per the design - nothing reserves this space
    private const float BarCardInset = 14f;   // stat cards float inside their segment on all sides
    private const float TopMarginBelowSafeArea = 64f;
    // Published geometry for the sub-cards that hang under the bar (HudSubCard): the bar's
    // bottom edge, and the inset cards' outer / center-facing edges, so a card below can share
    // the inset card's exact left and right edges on every screen (anchor-relative, no widths).
    public const float BarBottomBelowSafeArea = TopMarginBelowSafeArea + BarHeight;
    public const float InnerCardOuterMargin = BarSideMargin + BarCardInset;
    public const float InnerCardCenterOffset = NextCardWidth * 0.5f - BarSeamTuck + BarCardInset;
    private const float NextCardWidth = 200f;
    private const float NextCardOverhang = 24f; // how far it sticks out above AND below
    // Foresight widens the NEXT card DOWNWARD to a second, smaller/dimmer preview. The top
    // (immediate-next) slot is identical to the single-preview layout, so the default card
    // is pixel-for-pixel unchanged; only the second slot and the extra height are new.
    private const float OneSlotCardHeight = BarHeight + NextCardOverhang * 2f;
    private const float SecondSlotExtraHeight = 70f;
    private const float TwoSlotCardHeight = OneSlotCardHeight + SecondSlotExtraHeight;
    private const float NextSlotTopInset = 40f;          // space the "NEXT" label occupies
    private const float NextPrimarySlotSideInset = 30f;
    private const float NextPrimarySlotHeight = 94f;     // OneSlotCardHeight - top - 18 bottom pad
    private const float NextSlotGap = 6f;
    private const float NextSecondarySlotSideInset = 56f; // narrower => visibly smaller
    private const float NextSecondarySlotHeight = 64f;
    // Bar segments slip this far under the card edge. Exactly the half-width of the
    // card's border stroke: any deeper and the tucked bar shows through the translucent
    // card as a dark sliver inside the border; any shallower risks a sky-gap at the seam.
    private const float BarSeamTuck = 1f;
    private const float HeartSize = 44f;
    private const float HeartGap = 10f;
    private const int MaxHearts = RunState.MaxLives; // three fixed sockets, empty ones stay visible
    // The "/target" tail of the objective value, tinted down so the live number leads.
    private static readonly string TargetSuffixHex = ColorUtility.ToHtmlStringRGBA(StatLabelColor);
    private static readonly Color NudgePillColor = new Color(1f, 1f, 1f, 0.09f);
    private static readonly Color NudgeChevronColor = new Color(0.95f, 0.98f, 1f, 0.32f);
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
    private readonly System.Collections.Generic.List<(Image image, Color baseColor)> _nudgePillImages =
        new System.Collections.Generic.List<(Image, Color)>(4);
    private bool _nudgePillsDimmed;
    private GameObject _nextPanel;
    private Image[] _nextPreviews;
    private int _activeSlotCount = 1;
    private bool _nextPanelSuppressed;
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
    private int _targetHeightMeters;
    private int _shownWaveNumber = -1;

    private bool IsHeightObjective =>
        _objectiveType == LevelTargetType.ReachHeight || _objectiveType == LevelTargetType.TimedReachHeight;

    private void ResolveObjective()
    {
        LevelDefinition level = LevelSelectionState.SelectedLevel;
        _objectiveType = level != null ? level.TargetType : LevelTargetType.Endless;
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

        // The denominator is the NEXT UNEARNED medal tier, not the authored (bronze) target: a
        // replay with bronze banked opens straight onto silver's number, and a fully-golded
        // level keeps showing gold's as the best-chase reference. Rolls live via TierEarned.
        MedalTier? nextTier = LevelTiers.LowestUnearned(level);
        int target = Mathf.RoundToInt(
            LevelTiers.Threshold(level, nextTier ?? LevelTiers.MaxTier));
        switch (_objectiveType)
        {
            case LevelTargetType.PlaceBlocks:
            case LevelTargetType.TimedPlaceBlocks: _targetBlocks = target; break;
            case LevelTargetType.ReachHeight:
            case LevelTargetType.TimedReachHeight: _targetHeightMeters = target; break;
            case LevelTargetType.ClearWaves: _targetWaves = target; break;
        }
    }

    // A tier's hold-steady just completed: roll the objective denominator to the next rung.
    // The threshold comes from the event's level, never re-derived from the store - Custom
    // Game levels have no store identity, and the controller's session state isn't visible here.
    private void HandleTierEarned(LevelDefinition level, MedalTier tier)
    {
        if (tier >= LevelTiers.MaxTier) return; // the top rung owns the victory card; the label stays put
        if (level == null || level != LevelSelectionState.SelectedLevel) return;

        UpdateObjectiveTierIcon(tier + 1);
        int next = Mathf.RoundToInt(LevelTiers.Threshold(level, tier + 1));
        switch (_objectiveType)
        {
            case LevelTargetType.PlaceBlocks:
            case LevelTargetType.TimedPlaceBlocks:
                _targetBlocks = next;
                if (GameManager.Instance != null) HandleStandingBlocksChanged(GameManager.Instance.placedBlocks);
                break;
            case LevelTargetType.ReachHeight:
            case LevelTargetType.TimedReachHeight:
                _targetHeightMeters = next;
                if (GameManager.Instance != null) HandleHeightChanged(GameManager.Instance.liveTowerHeight);
                break;
            case LevelTargetType.ClearWaves:
                _targetWaves = next;
                _shownWaveNumber = -1; // the polled readout redraws with the new denominator
                break;
        }
    }

    private static string WithTarget(string current, string target) =>
        string.IsNullOrEmpty(target) ? current : $"{current}<color=#{TargetSuffixHex}>/{target}</color>";

    // The HUD total is the LIVE count of placed blocks still standing (drops when a block
    // is destroyed or falls off), not the cumulative progression score.
    private void HandleStandingBlocksChanged(int placedBlocks)
    {
        if (scoreText == null || _waveObjective || IsHeightObjective) return;
        scoreText.text = WithTarget(placedBlocks.ToString(),
            _targetBlocks > 0 ? _targetBlocks.ToString() : null);
    }

    private void HandleHeightChanged(float height)
    {
        if (scoreText == null || !IsHeightObjective) return;
        // Whole meters, floored: decimals overflow the tag, and rounding up would show the
        // target as reached (75/75m) while the tower is still short of it.
        scoreText.text = WithTarget(Mathf.FloorToInt(height).ToString(), $"{_targetHeightMeters}m");
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
        heart.sprite = full ? HeartSprites.Full() : HeartSprites.Empty();
        // With no dedicated socket asset yet, the empty state is the full art dimmed.
        heart.color = full || HeartSprites.HasDedicatedEmpty
            ? Color.white
            : new Color(0.25f, 0.22f, 0.22f, 0.55f);
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
        Sprite full = HeartSprites.Full();
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
        Sprite ghost = string.IsNullOrEmpty(shape) ? null : GetGhostSprite(shape);
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

    // Desaturated copy of the piece sprite so the preview reads as "coming up", not as a
    // brick already in play. Cached per shape and skin folder.
    private Sprite GetGhostSprite(string shape)
    {
        string cacheKey = $"{ChapterSkins.Folder}:{shape}";
        if (_ghostSprites.TryGetValue(cacheKey, out Sprite cached)) return cached;

        Sprite ghost = PieceGhost.Generate(shape, Desaturate);
        _ghostSprites[cacheKey] = ghost;
        return ghost;
    }

    // Desaturate toward the source so the preview reads as "coming up", keeping a hint of colour.
    private static void Desaturate(Color[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            Color c = pixels[i];
            float gray = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            pixels[i] = Color.Lerp(new Color(gray, gray, gray, c.a), c, 0.18f);
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

        BuildObjectiveCard(_barLeft);
        BuildLivesCard(_barRight);
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

    // Left segment's inset card: THE OBJECTIVE - fully rounded inside the segment (visible
    // corners and padding on every side - it never tucks under the NEXT card). The caption
    // names the metric ("BLOCKS" / "WAVE" / "HEIGHT"); the value carries current/target.
    private void BuildObjectiveCard(RectTransform barSegment)
    {
        RectTransform card = CreateBarCard(barSegment, "ObjectiveCard", BarInsetColor);
        card.anchorMin = Vector2.zero;
        card.anchorMax = Vector2.one;
        card.offsetMin = new Vector2(BarCardInset, BarCardInset);
        card.offsetMax = new Vector2(-BarCardInset, -BarCardInset);

        // The LEADING icon is the target's tier (Nick 2026-08-29): a bronze cube next to
        // "0/50" says what reaching 50 earns, and it rolls to silver the moment bronze lands
        // (HandleTierEarned) - one icon, no separate badge. Ladder-less levels (Endless)
        // keep the old grey cube on block goals and no icon on wave/height.
        LevelDefinition level = LevelSelectionState.SelectedLevel;
        bool tiered = LevelTiers.HasTiers(level);

        if ((_waveObjective || IsHeightObjective) && !tiered)
        {
            RectTransform group = CreateCenteredGroup(card, new Vector2(150f, 60f), 0f);
            CreateBarCaption(group, _waveObjective ? "WAVE" : "HEIGHT", new Vector2(0f, 16f));
            if (scoreText != null) PlaceBarValue(scoreText, group, new Vector2(0f, -12f));
            return;
        }

        string caption = _waveObjective ? "WAVE" : IsHeightObjective ? "HEIGHT" : "BLOCKS";
        RectTransform iconGroup = CreateCenteredGroup(card, new Vector2(186f, 60f), 0f);
        Image lead = CreateBarIcon(iconGroup, RuntimeSprites.CubeGlyph(), new Vector2(24f, 0f), 42f,
            new Color(0.90f, 0.90f, 0.90f, 0.85f));
        CreateBarCaption(iconGroup, caption, new Vector2(60f, 16f));
        if (scoreText != null) PlaceBarValue(scoreText, iconGroup, new Vector2(60f, -12f));

        if (tiered)
        {
            _objectiveTierIcon = lead;
            lead.color = MedalStyle.IconTint(earned: true); // pairs the medal art per MedalStyle's contract
            UpdateObjectiveTierIcon(LevelTiers.LowestUnearned(level) ?? LevelTiers.MaxTier);
        }
    }

    // The objective card's leading icon once a ladder exists: WHICH rung the "/target"
    // denominator belongs to. Rolls with the denominator via HandleTierEarned; on a
    // fully-earned ladder it stays on the top rung, matching the best-chase denominator.
    private Image _objectiveTierIcon;

    // Full tier colour on purpose: this badge NAMES the target's rung (a label), it does not
    // report earned state - the banked-state view is MedalHud's pill on the right.
    private void UpdateObjectiveTierIcon(MedalTier tier)
    {
        if (_objectiveTierIcon == null) return;
        _objectiveTierIcon.sprite = MedalStyle.Sprite(tier, earned: true);
    }

    // Right segment's inset card: the run's three life sockets and the pause glyph as ONE
    // centered cluster (mirrors the objective card's centered group). Lives took the old
    // HEIGHT slot: the left card owns the objective (height shows there when it IS the
    // objective), and the ever-visible dark sockets are what a zero-lives run has to offer
    // the shop to fill. The WHOLE card is the pause hitbox - the glyph is small and a
    // mid-run tap must not need precision, so a tap on the hearts pauses too.
    private void BuildLivesCard(RectTransform barSegment)
    {
        RectTransform card = CreateBarCard(barSegment, "LivesCard", BarInsetColor);
        card.anchorMin = Vector2.zero;
        card.anchorMax = Vector2.one;
        card.offsetMin = new Vector2(BarCardInset, BarCardInset);
        card.offsetMax = new Vector2(-BarCardInset, -BarCardInset);

        Image cardImage = card.GetComponent<Image>();
        cardImage.raycastTarget = true;
        Button cardButton = card.gameObject.AddComponent<Button>();
        cardButton.targetGraphic = cardImage;
        cardButton.transition = Selectable.Transition.None;
        cardButton.onClick.AddListener(OpenPauseMenu);

        float heartsWidth = MaxHearts * HeartSize + (MaxHearts - 1) * HeartGap;
        const float pauseGap = 18f;
        const float pauseSize = 54f;
        RectTransform group = CreateCenteredGroup(card,
            new Vector2(heartsWidth + pauseGap + pauseSize, 60f), 0f);
        BuildHearts(group, heartsWidth);
        BuildPauseButton(group);
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

    private Image CreateBarIcon(RectTransform parent, Sprite sprite, Vector2 center, float size, Color color)
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
        return image;
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
        caption.color = StatLabelColor; // neutral grey + translucent: greyscale overlay-blend look
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
        // "62/100" must never wrap inside the fixed value box; overflow spills right instead.
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
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
        rect.sizeDelta = new Vector2(54f, 54f);

        Image fill = buttonObject.GetComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = PauseFillColor;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = fill;
        button.onClick.AddListener(OpenPauseMenu);

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
        card.sizeDelta = new Vector2(NextCardWidth, OneSlotCardHeight);
        // The published bottom edge starts over with the card: statics outlive scene reloads,
        // and a fresh run must not inherit the previous run's Foresight height.
        NextCardBottomBelowSafeArea = TopMarginBelowSafeArea - NextCardOverhang + OneSlotCardHeight;

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

    // A top-pinned preview box (stretches horizontally, fixed height). anchoredPosition.y
    // places its TOP `topInset` below the card's top; sizeDelta.x of -2*sideInset insets
    // both sides. preserveAspect keeps each piece's proportions within its slot.
    private Image CreatePreviewSlot(RectTransform card, string name,
        float sideInset, float topInset, float height, Color tint)
    {
        GameObject slot = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)slot.transform;
        rect.SetParent(card, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(-2f * sideInset, height);
        rect.anchoredPosition = new Vector2(0f, -topInset);

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
        fill.color = NudgeHintColor(NudgePillColor, dimmed: false);
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
        chevron.color = NudgeHintColor(NudgeChevronColor, dimmed: false);
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
                scoreText.text = WithTarget(wave.ToString(),
                    _targetWaves > 0 ? _targetWaves.ToString() : null);
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

        bool dim = BlockController.NudgeLockoutRemaining > 0f;
        if (dim == _nudgePillsDimmed) return;
        _nudgePillsDimmed = dim;
        ApplyNudgeHintColors();
    }

    // Re-tint every nudge hint from its base colour, the player's opacity setting, and the current
    // lockout dim. Called on lockout changes and on SettingsService.Changed (live opacity edits).
    private void ApplyNudgeHintColors()
    {
        for (int i = 0; i < _nudgePillImages.Count; i++)
        {
            (Image image, Color baseColor) = _nudgePillImages[i];
            if (image != null) image.color = NudgeHintColor(baseColor, _nudgePillsDimmed);
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

    private static Color NudgeHintColor(Color baseColor, bool dimmed)
    {
        float dimFactor = dimmed ? NudgeLockoutDimFactor : 1f;
        float alpha = baseColor.a * SettingsService.NudgeGuideOpacity;
        alpha = Mathf.Lerp(alpha, Mathf.Min(1f, baseColor.a * NudgeGuideBoostAlphaFactor), _nudgeGuideBoost);
        return new Color(baseColor.r, baseColor.g, baseColor.b, alpha * dimFactor);
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
