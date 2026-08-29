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
/// The right corner has two tenants: the puzzle-wave countdown pill (WaveHud) outranks this
/// one (it is live survival state), so on a wave run the medal pill sits one row further
/// down. Placeholder look: the MedalStyle circle badge until Nick's rendered bronze/silver/
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
        _shownTier = null;
        _pillShownTime = -1f;
    }

    private void HandleTierEarned(LevelDefinition level, MedalTier tier)
    {
        if (level == null || level != LevelSelectionState.SelectedLevel) return;
        if (_shownTier.HasValue && tier <= _shownTier.Value) return; // rungs only climb
        ShowTier(tier);
    }

    private void ShowTier(MedalTier tier)
    {
        if (_pill == null) return;

        _shownTier = tier;
        _icon.sprite = MedalStyle.Sprite(tier, earned: true);
        _label.text = MedalStyle.DisplayName(tier);
        _label.color = MedalStyle.TierColor(tier);

        if (!_pill.gameObject.activeSelf)
        {
            _pill.gameObject.SetActive(true);
            _pillShownTime = Time.unscaledTime;
        }
        _popTime = 0f;
    }

    private void Update()
    {
        if (_pill == null || !_pill.gameObject.activeSelf) return;

        ApplyPillPosition(); // safe area settles late + the wave pill can claim the slot mid-run

        if (_pillShownTime >= 0f && _pillGroup != null)
        {
            _pillGroup.alpha = Mathf.Clamp01((Time.unscaledTime - _pillShownTime) / PillFadeInSeconds);
        }

        // Unscaled: the pop must finish even when a rung lands right before a pause/card.
        FxKit.TickSettlePop(_pill, ref _popTime, Time.unscaledDeltaTime);
    }

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

        Image bg = pill.GetComponent<Image>();
        bg.sprite = RuntimeSprites.RoundedPanel();
        bg.type = Image.Type.Sliced;
        bg.color = PillColor;
        bg.raycastTarget = false;

        _pillGroup = pill.AddComponent<CanvasGroup>();
        _pillGroup.alpha = 0f;
        _pillGroup.interactable = false;
        _pillGroup.blocksRaycasts = false;

        GameObject icon = new GameObject("Medal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = (RectTransform)icon.transform;
        iconRect.SetParent(_pill, false);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(30f, 0f); // CoinHud's icon seat
        iconRect.sizeDelta = new Vector2(34f, 34f);
        _icon = icon.GetComponent<Image>();
        _icon.preserveAspect = true;
        _icon.raycastTarget = false;

        GameObject label = new GameObject("Tier", typeof(RectTransform));
        RectTransform labelRect = (RectTransform)label.transform;
        labelRect.SetParent(_pill, false);
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(56f, 0f);
        labelRect.offsetMax = new Vector2(-10f, 0f);
        _label = label.AddComponent<TextMeshProUGUI>();
        _label.fontSize = 17f;
        _label.characterSpacing = 2f;
        _label.fontStyle = FontStyles.Bold;
        _label.alignment = TextAlignmentOptions.MidlineLeft;
        // "BRONZE" must never wrap inside the fixed pill (it rendered as "BRONZ/E"); the
        // same NoWrap+Overflow rule the objective value uses.
        _label.textWrappingMode = TextWrappingModes.NoWrap;
        _label.overflowMode = TextOverflowModes.Overflow;
        _label.raycastTarget = false;

        ApplyPillPosition();
        _pill.gameObject.SetActive(false); // no medal = no pill at all
    }

    private void ApplyPillPosition()
    {
        float topInset = RuntimeUiKit.SafeAreaTopInset(_canvas);
        bool waveRunLive = HeightLimitWavesModifier.ActiveRun != null;
        float topOffset = waveRunLive ? BelowWavePillOffset : TopOffsetBelowSafeArea;
        _pill.anchoredPosition = new Vector2(-RightMargin, -(topInset + topOffset));
    }
}
