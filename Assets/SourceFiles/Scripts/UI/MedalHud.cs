using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The banked-medal pill (MEDALS.md §8): top-right under the lives card, the CoinHud pill's
/// EXACT size mirrored to the right edge (Nick 2026-08-29) - a quiet, constantly-present
/// "this rung is already yours" marker so the medal chase never feels like it could lose
/// what's banked. Shows only tiers earned THIS RUN (Nick 2026-08-29): a replay chasing
/// silver starts with no pill - the objective card's tier badge already names the chase,
/// and a pill for last week's bronze would read as this run's trophy. It appears when a
/// rung lands mid-run (TierEarned) and settle-pops on every new one - same restrained
/// pulse as the coin pill, no extra fanfare (JUICE.md).
///
/// The right corner has three tenants: the puzzle-wave countdown pill (WaveHud) and the
/// timed-goal clock card both outrank this one (they are live survival state), so on those
/// runs the medal pill sits one row further down. Placeholder look: the MedalStyle circle badge until Nick's rendered bronze/silver/
/// gold block icons land - MedalStyle.Sprite is the swap point, nothing here changes.
/// </summary>
public class MedalHud : MonoBehaviour
{
    // Layout (canvas reference space, 1080x1920). EXACT CoinHud pill metrics, right-anchored:
    // same 150x52 card, same 184 top offset (TopMarginBelowSafeArea 64 + BarHeight 104 + gap),
    // side margin aligned under the lives card (bar side margin 120 + inset).
    private const float TopOffsetBelowSafeArea = 184f;
    private const float RightMargin = 132f;
    private const float PillWidth = 150f;
    private const float PillHeight = 52f;
    private const float PillFadeInSeconds = 0.25f;
    // One row below the wave pill (its offset 184 + height 64 + gap) while a wave run owns
    // the corner slot.
    private const float BelowWavePillOffset = 184f + 64f + 12f;
    // One row below the timed-goal clock card (its offset 180 + height 66 + gap) while a
    // timed run owns the slot - the card and this pill otherwise share the same row.
    private const float BelowTimerCardOffset = 180f + 66f + 12f;

    private static readonly Color PillColor = new Color(0f, 0f, 0f, 0.78f); // UIManager BarInsetColor

    private GameObject _canvasRoot;
    private Canvas _canvas;
    private RectTransform _pill;
    private CanvasGroup _pillGroup;
    private Image _icon;
    private TextMeshProUGUI _label;

    private MedalTier? _shownTier;
    private float _pillShownTime = -1f;
    private float _popTime = float.PositiveInfinity;

    // ---- Debut fly-in (Nick 2026-08-29): a rung earned mid-run debuts as a DOUBLE-size pill
    // center-screen (overshoot pop), holds a beat, then flies into the corner slot and hands
    // off to the real pill - the medal art is the celebration, no text toast. Unscaled time;
    // deliberately restrained (no confetti mid-run, the game is still going).
    private const float DebutScale = 2f;
    private const float DebutPopSeconds = 0.4f;
    private const float DebutHoldSeconds = 0.9f;
    private const float DebutFlySeconds = 0.55f;

    private RectTransform _debut;
    private float _debutClock;
    private Vector3 _debutFlyStart;
    private bool _debutFlyStarted;

    private void OnEnable()
    {
        GameEvents.TierEarned += HandleTierEarned;
        // Build now, not lazily on first show: a canvas created mid-frame has not been laid
        // out yet (the first-frame trap CoinHud documents). Hidden until a rung is earned
        // THIS run - previously banked medals deliberately show nothing here.
        EnsureBuilt();
    }

    private void OnDisable()
    {
        GameEvents.TierEarned -= HandleTierEarned;
        if (_canvasRoot != null) Destroy(_canvasRoot);
        _canvasRoot = null;
        _pill = null;
        _debut = null; // destroyed with the canvas root
        _shownTier = null;
        _pillShownTime = -1f;
    }

    private void HandleTierEarned(LevelDefinition level, MedalTier tier)
    {
        if (level == null || level != LevelSelectionState.SelectedLevel) return;
        if (_shownTier.HasValue && tier <= _shownTier.Value) return; // rungs only climb

        // The top rung owns the victory card (RunResultsScreen, shown this same frame); a
        // center-screen debut would fly OVER it - this canvas sorts 6900, the victory card
        // 6500. The pill just updates in place so the corner still tells the truth after
        // the card. A lower rung's debut still mid-flight is superseded.
        if (tier >= LevelTiers.MaxTier)
        {
            AdoptTier(tier, fadeIn: true);
            return;
        }

        StartDebut(tier);
    }

    /// <summary>The corner pill takes over a rung - the ONE place that sequence lives:
    /// supersede any live debut, swap the pill's content, show it (fading in unless it is
    /// already on screen or a landed debut sits exactly on it) and settle-pop.</summary>
    private void AdoptTier(MedalTier tier, bool fadeIn)
    {
        if (_debut != null) { Destroy(_debut.gameObject); _debut = null; }
        _shownTier = tier;
        ApplyTierVisual(_icon, _label, tier);
        bool alreadyVisible = _pill.gameObject.activeSelf && _pillGroup != null && _pillGroup.alpha >= 1f;
        _pill.gameObject.SetActive(true);
        _pillShownTime = fadeIn && !alreadyVisible
            ? Time.unscaledTime
            : Time.unscaledTime - PillFadeInSeconds; // backdated past the fade window: instantly opaque
        _popTime = 0f;
    }

    private void StartDebut(MedalTier tier)
    {
        if (_pill == null) return;

        _shownTier = tier;

        // A pill not yet on screen carries the new rung NOW but stays transparent until the
        // debut arrives: active-but-invisible so ApplyPillPosition keeps steering the fly
        // target live (safe-area settle, the wave pill claiming the slot). A pill ALREADY
        // visible keeps its old rung until the fly lands - blanking it here would hard-cut
        // the corner empty for the debut's whole ride.
        bool pillVisible = _pill.gameObject.activeSelf && _pillGroup != null && _pillGroup.alpha > 0f;
        if (!pillVisible)
        {
            ApplyTierVisual(_icon, _label, tier);
            _pill.gameObject.SetActive(true);
            _pillShownTime = -1f;
            if (_pillGroup != null) _pillGroup.alpha = 0f;
        }

        // A faster rung mid-debut replaces the debut (rungs only climb, latest wins).
        if (_debut != null) Destroy(_debut.gameObject);
        _debut = BuildDebut(tier);
        _debutClock = 0f;
        _debutFlyStarted = false;
    }

    // The debut: the pill's exact anatomy at double size, centered on screen - built by the
    // same builder as the corner pill, so the handoff stays pixel-identical by construction.
    private RectTransform BuildDebut(MedalTier tier)
    {
        GameObject go = new GameObject("MedalDebut", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(_canvasRoot.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 120f); // a little above center, clear of the tower
        rect.sizeDelta = new Vector2(PillWidth * DebutScale, PillHeight * DebutScale);
        rect.localScale = Vector3.zero;

        (Image icon, TextMeshProUGUI label) = BuildPillVisual(rect, DebutScale);
        ApplyTierVisual(icon, label, tier);
        return rect;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime; // pops/flights must finish through pauses and cards

        if (_pill != null && _pill.gameObject.activeSelf)
        {
            ApplyPillPosition(); // safe area settles late + the wave pill can claim the slot mid-run

            if (_pillShownTime >= 0f && _pillGroup != null)
            {
                _pillGroup.alpha = Mathf.Clamp01((Time.unscaledTime - _pillShownTime) / PillFadeInSeconds);
            }

            FxKit.TickSettlePop(_pill, ref _popTime, dt);
        }

        TickDebut(dt);
    }

    private void TickDebut(float dt)
    {
        if (_debut == null) return;
        _debutClock += dt;

        if (_debutClock < DebutPopSeconds)
        {
            // Overshoot pop from zero - the same FxKit curve as the celebration cards' badge,
            // so the two moments feel identical.
            float scale = FxKit.EaseOutBack(_debutClock / DebutPopSeconds);
            _debut.localScale = new Vector3(scale, scale, 1f);
            return;
        }

        if (_debutClock < DebutPopSeconds + DebutHoldSeconds)
        {
            _debut.localScale = Vector3.one;
            return;
        }

        // Fly to the slot: ease toward the LIVE pill center (it can shift mid-flight) while
        // shrinking to the pill's exact size, then hand off - the swap is seamless because
        // the debut IS the pill at scale 1/DebutScale.
        if (!_debutFlyStarted)
        {
            _debutFlyStarted = true;
            _debutFlyStart = _debut.position;
        }
        float ft = Mathf.Clamp01((_debutClock - DebutPopSeconds - DebutHoldSeconds) / DebutFlySeconds);
        float eased = Mathf.SmoothStep(0f, 1f, ft); // soft leave, soft land
        _debut.position = Vector3.Lerp(_debutFlyStart, PillWorldCenter(), eased);
        float flyScale = Mathf.Lerp(1f, 1f / DebutScale, eased);
        _debut.localScale = new Vector3(flyScale, flyScale, 1f);

        if (ft >= 1f)
        {
            // The landing is where the corner pill takes over the NEW rung (a pill that was
            // already visible kept its old rung through the ride). No fade: the debut landed
            // exactly on the pill, a fade-in here would read as a blink.
            AdoptTier(_shownTier.Value, fadeIn: false);
        }
    }

    private Vector3 PillWorldCenter()
        => _pill.TransformPoint(_pill.rect.center);

    // ---- construction ----------------------------------------------------------------------

    private void EnsureBuilt()
    {
        if (_canvasRoot != null) return;

        _canvasRoot = RuntimeUiKit.CreateOverlayCanvas("MedalHud", 6900); // CoinHud/WaveHud tier, under GameOver (7100)
        _canvas = _canvasRoot.GetComponent<Canvas>();

        GameObject pill = new GameObject("MedalPill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _pill = (RectTransform)pill.transform;
        _pill.SetParent(_canvasRoot.transform, false);
        _pill.anchorMin = _pill.anchorMax = new Vector2(1f, 1f);
        _pill.pivot = new Vector2(1f, 1f);
        _pill.sizeDelta = new Vector2(PillWidth, PillHeight);

        _pillGroup = pill.AddComponent<CanvasGroup>();
        _pillGroup.alpha = 0f;
        _pillGroup.interactable = false;
        _pillGroup.blocksRaycasts = false;

        (_icon, _label) = BuildPillVisual(_pill, 1f);

        ApplyPillPosition();
        _pill.gameObject.SetActive(false); // no medal = no pill at all
    }

    // ONE pill anatomy, two sizes: the corner pill (scale 1) and the center-screen debut
    // (DebutScale). A single builder so the fly-in handoff stays pixel-identical by
    // construction. pixelsPerUnitMultiplier keeps the sliced corner radius proportional to
    // the scale - a sliced border is fixed in local units, so without it the landed debut's
    // rounding would snap to the pill's at the swap frame.
    private static (Image icon, TextMeshProUGUI label) BuildPillVisual(RectTransform rect, float scale)
    {
        Image bg = rect.GetComponent<Image>();
        bg.sprite = RuntimeSprites.RoundedPanel();
        bg.type = Image.Type.Sliced;
        bg.pixelsPerUnitMultiplier = 1f / scale;
        bg.color = PillColor;
        bg.raycastTarget = false;

        GameObject icon = new GameObject("Medal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = (RectTransform)icon.transform;
        iconRect.SetParent(rect, false);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(30f * scale, 0f); // CoinHud's icon seat
        iconRect.sizeDelta = new Vector2(34f * scale, 34f * scale);
        Image iconImage = icon.GetComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        GameObject label = new GameObject("Tier", typeof(RectTransform));
        RectTransform labelRect = (RectTransform)label.transform;
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(56f * scale, 0f);
        labelRect.offsetMax = new Vector2(-10f * scale, 0f);
        TextMeshProUGUI text = label.AddComponent<TextMeshProUGUI>();
        text.fontSize = 17f * scale;
        text.characterSpacing = 2f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        // "BRONZE" must never wrap inside the fixed pill (it rendered as "BRONZ/E"); the
        // same NoWrap+Overflow rule the objective value uses.
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;

        return (iconImage, text);
    }

    private static void ApplyTierVisual(Image icon, TextMeshProUGUI label, MedalTier tier)
    {
        icon.sprite = MedalStyle.Sprite(tier, earned: true);
        label.text = MedalStyle.DisplayName(tier);
        label.color = MedalStyle.TierColor(tier);
    }

    private void ApplyPillPosition()
    {
        float topInset = RuntimeUiKit.SafeAreaTopInset(_canvas);
        float topOffset = HeightLimitWavesModifier.ActiveRun != null ? BelowWavePillOffset
            : LevelRuntimeController.TimerCardVisible ? BelowTimerCardOffset
            : TopOffsetBelowSafeArea;
        _pill.anchoredPosition = new Vector2(-RightMargin, -(topInset + topOffset));
    }
}
