using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The puzzle-wave countdown pill: blocks still to STAND before the laser rises, top-right
/// under the lives card - CoinHud's exact mirror (own overlay canvas, corner-anchored pill
/// below the top bar). It replaced the old world-space number that rode the line: a
/// world-space renderer is composited UNDER every screen-space overlay canvas no matter its
/// sorting order, so the player-arranged consumable slots could always occlude it. Grouping
/// it under lives is deliberate (both are survival state), and being HUD it stays readable
/// through a blackout - LEVELS.md allows only the lantern, ability overlays and the HUD to
/// pierce the dark, and keeping the bill visible while playing against a memorized line is
/// the point.
///
/// One system, two views: the pill's outline and urgency tint borrow the laser's resolved
/// chapter colour, so "this number" and "that line" read as the same mechanism. Restraint
/// per JUICE.md: a small settle-pop when the number falls, a brief outline flash when it
/// RISES (a destroyed block re-owes the bill - worth an honest signal), a colour shift for
/// the last 3 and a gentle breathing pulse for the last 2. No sounds - the zap already owns
/// one. State is POLLED off HeightLimitWavesModifier.ActiveRun, the same way the objective
/// card reads the wave number: the count changes inside a timed confirm where no HUD event
/// fires.
/// </summary>
public class WaveHud : MonoBehaviour
{
    // Layout: a HudSubCard under the top bar's RIGHT segment - same left/right edges as the
    // lives card above it on every screen, "NEXT WAVE" + count as one centered cluster.
    private const float PillFadeInSeconds = 0.25f;

    private const int UrgencyTintAt = 3;   // the value takes the laser's colour
    private const int UrgencyPulseAt = 2;  // plus a slow breathing pulse - never constant
    private const float DeficitFlashSeconds = 0.6f;

    private GameObject _canvasRoot;
    private Canvas _canvas;
    private RectTransform _pill;
    private CanvasGroup _pillGroup;
    private Image _outline;
    private TextMeshProUGUI _valueText;
    private RectTransform _row;

    private HeightLimitWavesModifier _run; // the clone we styled for; a retry makes a new one
    private Color _tint = Color.white;
    private int _shownRemaining = -1;
    private float _pillShownTime = -1f;
    private float _popTime = float.PositiveInfinity;
    private float _deficitFlash;
    private Vector3 _screenKey = -Vector3.one;

    private void OnEnable()
    {
        // Build now, not lazily on first show: a canvas created mid-frame has not been laid
        // out yet (the same first-frame trap CoinHud documents). The pill stays hidden until
        // a wave run is live.
        EnsureBuilt();
    }

    private void OnDisable()
    {
        if (_canvasRoot != null) Destroy(_canvasRoot);
        _canvasRoot = null;
        _pill = null;
        _run = null;
        _shownRemaining = -1;
        _pillShownTime = -1f;
    }

    private void Update()
    {
        if (_pill == null) return;

        HeightLimitWavesModifier run = HeightLimitWavesModifier.ActiveRun;
        if (run == null)
        {
            if (_run != null) HideRun();
            return;
        }

        if (!ReferenceEquals(run, _run))
        {
            _run = run;
            _tint = run.LaserColor;
            _tint.a = 1f;
            _shownRemaining = -1;
            _pill.gameObject.SetActive(true);
            _pillShownTime = Time.unscaledTime;
        }

        // Safe area / resolution can settle late on boot and change mid-run; reposition only
        // when the screen actually changed (UIManager's screen-key convention).
        Vector3 screenKey = new Vector3(Screen.width, Screen.height, Screen.safeArea.yMax);
        if (screenKey != _screenKey)
        {
            _screenKey = screenKey;
            ApplyPillPosition();
        }

        if (_pillShownTime >= 0f)
        {
            float fade = Mathf.Clamp01((Time.unscaledTime - _pillShownTime) / PillFadeInSeconds);
            _pillGroup.alpha = fade;
            if (fade >= 1f) _pillShownTime = -1f; // fade done - stop touching the group
        }

        int remaining = run.BlocksRemaining;
        if (remaining != _shownRemaining)
        {
            bool rose = _shownRemaining >= 0 && remaining > _shownRemaining;
            _shownRemaining = remaining;
            _valueText.text = remaining.ToString();
            HudSubCard.MarkDirty(_row);
            _popTime = 0f;
            if (rose) _deficitFlash = DeficitFlashSeconds; // the bill reopened - flag it honestly
        }

        TickAnimation(remaining);
    }

    private void HideRun()
    {
        _run = null;
        _shownRemaining = -1;
        _pillShownTime = -1f;
        _pill.gameObject.SetActive(false);
    }

    private void TickAnimation(int remaining)
    {
        float dt = Time.unscaledDeltaTime;

        FxKit.TickSettlePop(_pill, ref _popTime, dt);

        _deficitFlash = Mathf.Max(0f, _deficitFlash - dt);
        Color edge = _tint;
        edge.a = Mathf.Lerp(0.55f, 1f, _deficitFlash / DeficitFlashSeconds);
        _outline.color = edge;

        // Urgency ramp, double-coded (colour AND motion, never colour alone): the laser's
        // tint for the last 3, plus a slow breathing pulse for the last 2. Alpha never
        // drops far - a countdown that blinks off reads as a glitch, not urgency.
        Color value = remaining <= UrgencyTintAt
            ? Color.Lerp(_tint, Color.white, 0.25f)
            : Color.white;
        if (remaining <= UrgencyPulseAt && remaining > 0)
        {
            value.a = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 4f);
        }
        _valueText.color = value;
    }

    // ---- construction ----------------------------------------------------------------------

    private void EnsureBuilt()
    {
        if (_canvasRoot != null) return;

        // Same tier as CoinHud (6900, under GameOver at 7100); opposite corners, never overlap.
        _canvasRoot = RuntimeUiKit.CreateOverlayCanvas("WaveHud", 6900);
        _canvas = _canvasRoot.GetComponent<Canvas>();

        _pill = HudSubCard.Create(_canvasRoot.transform, "WavePill", HudSubCard.Side.Right);
        _outline = RuntimeUiKit.AddOutline(_pill, new Color(1f, 1f, 1f, 0.55f));

        _pillGroup = _pill.gameObject.AddComponent<CanvasGroup>();
        _pillGroup.alpha = 0f;
        _pillGroup.interactable = false;
        _pillGroup.blocksRaycasts = false;

        // Text and number only (Nick 2026-09-04: no glyph) - the caption in the bar's own
        // caption style, the count in the shared value size, one centered cluster.
        _row = HudSubCard.CreateRow(_pill);
        HudSubCard.AddText(_row, "Caption", "NEXT WAVE", HudSubCard.CaptionFontSize,
            HudSubCard.CaptionColor, characterSpacing: 8f);
        _valueText = HudSubCard.AddText(_row, "Value", "0", HudSubCard.ValueFontSize, Color.white);

        ApplyPillPosition();
        _pill.gameObject.SetActive(false); // no wave run live = no pill at all
    }

    private void ApplyPillPosition()
    {
        HudSubCard.Place(_pill, _canvas, 0);
    }
}
