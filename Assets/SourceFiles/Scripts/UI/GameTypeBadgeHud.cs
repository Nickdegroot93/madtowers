using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The game-type hazard badge: a quiet, constantly-present pill hanging centered under the
/// NEXT card (riding its live bottom edge, so Foresight's taller card pushes it down) naming
/// the invisible ruleset the run is under - so mid-run nobody forgets they are playing
/// Airtight and seals a pocket by accident (Nick 2026-08-30). Self-evident game types (Void
/// Zones, Blackout) never claim it; the modifier that does registers its per-run clone on
/// <see cref="ActiveSource"/> (see IGameTypeBadgeProvider).
///
/// Every run OPENS with a debut (Nick 2026-08-30): the pill at double-ish size in the retired
/// goal banner's slot with one plain-words rule line under it, held a few seconds, then flown
/// into the corner slot - the rule must land BEFORE the first sealed pocket, because the
/// pre-run modal's one-liner alone teaches it only after it has already exploded. This debut
/// is the "game-type logo at level start" the banner retirement note reserved the slot for.
/// Never blocks input; the game is already running under it.
///
/// Two states, one element: static = "this rule applies"; while a live hazard burns
/// (BadgeDanger01 > 0, an armed pocket's fuse) the pill pulses toward ember - the standing
/// reminder doubles as the go-fix-it alarm, no extra UI. Restrained on purpose (JUICE.md):
/// a pulse, not a flash.
/// </summary>
public class GameTypeBadgeHud : MonoBehaviour
{
    /// <summary>The run's badge claimant, set by the owning modifier's run clone at level
    /// start and cleared at level end. Null = no badge, the pill hides.</summary>
    public static IGameTypeBadgeProvider ActiveSource;

    /// <summary>One short plain-words rule sentence for the run-start debut. Kept here (not on
    /// the provider) until a second game type claims a badge and proves what the general shape
    /// needs to be.</summary>
    private const string AirtightRuleText = "Don't seal gaps in your tower -\ntrapped air explodes!";

    // Layout (canvas reference space, 1080x1920): centered under the NEXT card, hanging off
    // UIManager.NextCardBottomBelowSafeArea so Foresight's taller card pushes it down instead
    // of being overlapped. Wider than the 150 pill standard: "AIRTIGHT" is the longest pill
    // word on screen.
    private const float GapBelowNextCard = 12f;
    private const float PillWidth = 172f;
    private const float PillHeight = 52f;
    private const float PillFadeInSeconds = 0.25f;
    private const float DangerPulseHz = 2.6f;

    // Debut: the banner slot (anchor 0.74, where the retired goal text lived), unscaled time
    // (a run can open paused on an ability choice). Hold long enough to READ the rule line -
    // it is the one shot at teaching before the first mistake.
    private const float DebutScale = 1.8f;
    private const float DebutFadeInSeconds = 0.35f;
    private const float DebutHoldSeconds = 3.2f;
    private const float DebutFlySeconds = 0.55f;

    private static readonly Color PillColor = new Color(0f, 0f, 0f, 0.78f);   // UIManager BarInsetColor
    private static readonly Color EmberColor = new Color(0.45f, 0.12f, 0.02f, 0.88f);
    private static readonly Color LabelColor = new Color(1f, 0.66f, 0.34f, 1f); // the icon's flame family

    private GameObject _canvasRoot;
    private Canvas _canvas;
    private RectTransform _pill;
    private CanvasGroup _pillGroup;
    private Image _background;
    private RectTransform _iconRect;
    private float _pillShownTime = -1f;
    private bool _visible;

    private RectTransform _debutRoot;   // whole debut group (pill twin + rule text)
    private RectTransform _debutPill;
    private CanvasGroup _debutGroup;
    private CanvasGroup _debutTextGroup;
    private float _debutClock;
    private Vector3 _debutFlyStart;
    private bool _debutFlyStarted;

    private void OnEnable()
    {
        // Build now, not lazily on first show - the first-frame layout trap CoinHud documents.
        EnsureBuilt();
    }

    private void OnDisable()
    {
        if (_canvasRoot != null) Destroy(_canvasRoot);
        _canvasRoot = null;
        _pill = null;
        _debutRoot = null; // destroyed with the canvas root
        _visible = false;
        _pillShownTime = -1f;
    }

    private void Update()
    {
        if (_pill == null) return;

        IGameTypeBadgeProvider source = ActiveSource;
        bool show = source != null && source.BadgeIcon != null;
        if (show != _visible)
        {
            _visible = show;
            _pill.gameObject.SetActive(show);
            if (show)
            {
                Image icon = _iconRect.GetComponent<Image>();
                icon.sprite = source.BadgeIcon;
                TextMeshProUGUI label = _pill.GetComponentInChildren<TextMeshProUGUI>();
                label.text = source.BadgeLabel;
                StartDebut(source);
            }
            else if (_debutRoot != null)
            {
                Destroy(_debutRoot.gameObject); // run ended mid-debut
                _debutRoot = null;
            }
        }
        if (!show) return;

        ApplyPillPosition(); // the safe area settles late on some devices

        if (_pillGroup != null && _pillShownTime >= 0f)
        {
            _pillGroup.alpha = Mathf.Clamp01((Time.unscaledTime - _pillShownTime) / PillFadeInSeconds);
        }

        // Danger pulse: amplitude follows the fuse, so a fresh seal murmurs and a nearly-full
        // pocket burns. Calm snaps everything back to the quiet pill.
        float danger = Mathf.Clamp01(source.BadgeDanger01);
        float glow = danger <= 0f
            ? 0f
            : danger * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * DangerPulseHz * 2f * Mathf.PI));
        _background.color = Color.Lerp(PillColor, EmberColor, glow);
        float iconScale = 1f + 0.16f * glow;
        _iconRect.localScale = new Vector3(iconScale, iconScale, 1f);

        TickDebut();
    }

    // ---- run-start debut ---------------------------------------------------------------------

    // The corner pill exists from frame one but stays transparent while the debut plays: it
    // keeps steering the fly target live (safe-area settle, Foresight resizing the NEXT card
    // mid-debut) - the MedalHud debut's trick.
    private void StartDebut(IGameTypeBadgeProvider source)
    {
        _pillShownTime = -1f;
        if (_pillGroup != null) _pillGroup.alpha = 0f;

        if (_debutRoot != null) Destroy(_debutRoot.gameObject);
        _debutRoot = (RectTransform)new GameObject("BadgeDebut", typeof(RectTransform)).transform;
        _debutRoot.SetParent(_canvasRoot.transform, false);
        _debutRoot.anchorMin = _debutRoot.anchorMax = new Vector2(0.5f, 0.74f); // the banner slot
        _debutRoot.pivot = new Vector2(0.5f, 0.5f);
        _debutRoot.sizeDelta = Vector2.zero;

        _debutGroup = _debutRoot.gameObject.AddComponent<CanvasGroup>();
        _debutGroup.alpha = 0f;
        _debutGroup.interactable = false;
        _debutGroup.blocksRaycasts = false; // the game is live underneath - never block a tap

        GameObject pillGo = new GameObject("Pill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _debutPill = (RectTransform)pillGo.transform;
        _debutPill.SetParent(_debutRoot, false);
        _debutPill.sizeDelta = new Vector2(PillWidth * DebutScale, PillHeight * DebutScale);
        BuildPillVisual(_debutPill, DebutScale, out Image debutIcon, out TextMeshProUGUI debutLabel);
        debutIcon.sprite = source.BadgeIcon;
        debutLabel.text = source.BadgeLabel;

        // The rule line, shadow-twinned for legibility over the world (UI.Shadow ignores TMP).
        GameObject textGo = new GameObject("Rule", typeof(RectTransform));
        RectTransform textRect = (RectTransform)textGo.transform;
        textRect.SetParent(_debutRoot, false);
        textRect.anchoredPosition = new Vector2(0f, -(PillHeight * DebutScale * 0.5f + 74f));
        textRect.sizeDelta = new Vector2(860f, 120f);
        _debutTextGroup = textGo.AddComponent<CanvasGroup>();
        _debutTextGroup.blocksRaycasts = false;
        TextMeshProUGUI ruleShadow = BuildRuleText(textRect, new Color(0f, 0f, 0f, 0.55f));
        ruleShadow.rectTransform.anchoredPosition = new Vector2(0f, -4f);
        BuildRuleText(textRect, new Color(0.97f, 0.95f, 0.92f, 1f));

        _debutClock = 0f;
        _debutFlyStarted = false;
    }

    private static TextMeshProUGUI BuildRuleText(RectTransform parent, Color color)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = AirtightRuleText;
        text.fontSize = 36f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private void TickDebut()
    {
        if (_debutRoot == null) return;
        _debutClock += Time.unscaledDeltaTime;

        if (_debutClock < DebutFadeInSeconds)
        {
            _debutGroup.alpha = _debutClock / DebutFadeInSeconds;
            return;
        }
        if (_debutClock < DebutFadeInSeconds + DebutHoldSeconds)
        {
            _debutGroup.alpha = 1f;
            return;
        }

        // Fly to the slot: ease toward the LIVE corner pill centre while shrinking to its
        // exact size; the rule text fades out on the ride. The swap is seamless because the
        // debut pill IS the corner pill at scale 1/DebutScale.
        if (!_debutFlyStarted)
        {
            _debutFlyStarted = true;
            _debutFlyStart = _debutPill.position;
        }
        float ft = Mathf.Clamp01((_debutClock - DebutFadeInSeconds - DebutHoldSeconds) / DebutFlySeconds);
        float eased = Mathf.SmoothStep(0f, 1f, ft);
        _debutPill.position = Vector3.Lerp(_debutFlyStart, _pill.TransformPoint(_pill.rect.center), eased);
        float flyScale = Mathf.Lerp(1f, 1f / DebutScale, eased);
        _debutPill.localScale = new Vector3(flyScale, flyScale, 1f);
        _debutTextGroup.alpha = 1f - eased;

        if (ft >= 1f)
        {
            // Handoff: the corner pill takes over instantly opaque - the debut landed exactly
            // on it, a fade-in here would read as a blink.
            Destroy(_debutRoot.gameObject);
            _debutRoot = null;
            _pillShownTime = Time.unscaledTime - PillFadeInSeconds; // backdated: instantly opaque
        }
    }

    // ---- construction --------------------------------------------------------------------------

    private void EnsureBuilt()
    {
        if (_canvasRoot != null) return;

        _canvasRoot = RuntimeUiKit.CreateOverlayCanvas("GameTypeBadgeHud", 6900); // CoinHud/MedalHud tier
        _canvas = _canvasRoot.GetComponent<Canvas>();

        GameObject pill = new GameObject("BadgePill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _pill = (RectTransform)pill.transform;
        _pill.SetParent(_canvasRoot.transform, false);
        _pill.anchorMin = _pill.anchorMax = new Vector2(0.5f, 1f);
        _pill.pivot = new Vector2(0.5f, 1f);
        _pill.sizeDelta = new Vector2(PillWidth, PillHeight);

        _pillGroup = pill.AddComponent<CanvasGroup>();
        _pillGroup.alpha = 0f;
        _pillGroup.interactable = false;
        _pillGroup.blocksRaycasts = false;

        BuildPillVisual(_pill, 1f, out Image iconImage, out _);
        _background = pill.GetComponent<Image>();
        _iconRect = iconImage.rectTransform;

        ApplyPillPosition();
        _pill.gameObject.SetActive(false); // no claimant = no pill at all
    }

    // ONE pill anatomy, two sizes: the corner pill (scale 1) and the run-start debut
    // (DebutScale) - a single builder so the fly-in handoff stays pixel-identical by
    // construction. pixelsPerUnitMultiplier keeps the sliced corner radius proportional
    // (the MedalHud debut's lesson).
    private static void BuildPillVisual(RectTransform rect, float scale, out Image iconImage, out TextMeshProUGUI text)
    {
        Image bg = rect.GetComponent<Image>();
        bg.sprite = RuntimeSprites.RoundedPanel();
        bg.type = Image.Type.Sliced;
        bg.pixelsPerUnitMultiplier = 1f / scale;
        bg.color = PillColor;
        bg.raycastTarget = false;

        GameObject icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = (RectTransform)icon.transform;
        iconRect.SetParent(rect, false);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(30f * scale, 0f); // the shared pill icon seat
        iconRect.sizeDelta = new Vector2(34f * scale, 34f * scale);
        iconImage = icon.GetComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        GameObject label = new GameObject("Label", typeof(RectTransform));
        RectTransform labelRect = (RectTransform)label.transform;
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(56f * scale, 0f);
        labelRect.offsetMax = new Vector2(-10f * scale, 0f);
        text = label.AddComponent<TextMeshProUGUI>();
        text.fontSize = 17f * scale;
        text.characterSpacing = 2f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.color = LabelColor;
        // The fixed pill must never wrap its word (the MedalHud "BRONZ/E" lesson).
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
    }

    private void ApplyPillPosition()
    {
        float topInset = RuntimeUiKit.SafeAreaTopInset(_canvas);
        _pill.anchoredPosition = new Vector2(0f,
            -(topInset + UIManager.NextCardBottomBelowSafeArea + GapBelowNextCard));
    }
}
