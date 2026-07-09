using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The run-coin counter and the coin flight (JUICE.md Phase 3). THE FLIGHT IS THE ENTIRE
/// CELEBRATION: when PlacementScout mints coins, a handful of small coins burst from the
/// placed block, hang for a beat, then curve into a quiet counter pill that ticks up with a
/// tiny elastic pulse and ONE soft muted clink per batch. No flash, no shake, no chime, no
/// extra haptic - that whole layer was rejected in playtest; keep this restrained.
///
/// The pill lives on its own overlay canvas, tucked under the HUD top bar's left segment,
/// and does not exist at all until the first coins are earned in a run.
/// </summary>
public class CoinHud : MonoBehaviour
{
    // Layout (canvas reference space, 1080x1920). Pill sits under the top bar's left segment:
    // top offset mirrors UIManager's TopMarginBelowSafeArea (64) + BarHeight (104) + a gap.
    private const float TopOffsetBelowSafeArea = 184f;
    private const float LeftMargin = 132f; // aligns under the blocks card (bar side margin 120 + inset)
    private const float PillWidth = 150f;
    private const float PillHeight = 52f;

    private const int MaxFlightCoins = 7;
    private const float CoinSize = 32f;
    private const float BurstGravity = 1500f;   // canvas px/s²
    private const float HangSeconds = 0.26f;
    private const float HangStagger = 0.05f;
    private const float FlightSeconds = 0.38f;
    private const float PillFadeInSeconds = 0.25f;

    private static readonly Color PillColor = new Color(0f, 0f, 0f, 0.78f); // UIManager BarInsetColor
    private static readonly Color CoinGold = new Color(1f, 0.76f, 0.22f, 1f);

    private struct FlightCoin
    {
        public RectTransform Rect;
        public Vector2 Velocity;      // burst phase
        public Vector2 FlightStart;   // bezier, captured when the flight phase begins
        public Vector2 FlightControl;
        public float Age;
        public float HangUntil;
        public int Value;             // coins this sprite deposits on arrival
        public bool Flying;
        public bool BatchClink;       // the one coin of its batch that plays the clink
    }

    private GameObject _canvasRoot;
    private Canvas _canvas;
    private RectTransform _canvasRect;
    private RectTransform _pill;
    private CanvasGroup _pillGroup;
    private TextMeshProUGUI _valueText;
    private Sprite _coinSprite;
    private bool _coinSpriteIsFallback;

    private readonly List<FlightCoin> _active = new List<FlightCoin>();
    private readonly Stack<RectTransform> _coinPool = new Stack<RectTransform>();

    private int _displayValue;
    private float _pillShownTime = -1f;
    private float _pulseTime = float.PositiveInfinity;

    private void OnEnable()
    {
        GameEvents.CoinsEarned += HandleCoinsEarned;
        // Build now, not lazily on the first earn: a canvas created mid-frame has not been
        // laid out yet, which turned the first burst's world->canvas conversion into garbage
        // (coins spawning at screen centre). The pill stays hidden until coins exist.
        EnsureBuilt();
    }

    private void OnDisable()
    {
        GameEvents.CoinsEarned -= HandleCoinsEarned;
        if (_canvasRoot != null) Destroy(_canvasRoot);
        _canvasRoot = null;
        _pill = null;
        _active.Clear();
        _coinPool.Clear();
        _displayValue = 0;
        _pillShownTime = -1f;
    }

    private void HandleCoinsEarned(int amount, Vector3 worldPosition, int runTotal)
    {
        EnsureBuilt();
        if (_pill == null) return;

        if (!_pill.gameObject.activeSelf)
        {
            _pill.gameObject.SetActive(true);
            _pillShownTime = Time.unscaledTime;
        }

        Vector2 origin = WorldToCanvas(worldPosition);

        int count = Mathf.Clamp(2 + amount / 3, 3, MaxFlightCoins);
        int share = amount / count;
        for (int i = 0; i < count; i++)
        {
            RectTransform coin = GetCoin();
            coin.anchoredPosition = origin;
            coin.localScale = Vector3.one * Random.Range(0.85f, 1.1f);

            _active.Add(new FlightCoin
            {
                Rect = coin,
                // A soft upward fan with a little sideways spread - reads as "knocked loose",
                // not as an explosion.
                Velocity = new Vector2(Random.Range(-170f, 170f), Random.Range(260f, 430f)),
                Age = 0f,
                HangUntil = HangSeconds + i * HangStagger,
                Value = i == count - 1 ? amount - share * (count - 1) : share, // remainder on the last
                Flying = false,
                BatchClink = i == 0,
            });
        }
    }

    private void Update()
    {
        if (_pill == null) return;

        ApplyPillPosition(); // safe area can settle late; keeping this live also handles rotation

        // Fade-in on first appearance.
        if (_pillShownTime >= 0f && _pillGroup != null)
        {
            _pillGroup.alpha = Mathf.Clamp01((Time.unscaledTime - _pillShownTime) / PillFadeInSeconds);
        }

        TickCoins();
        TickPulse();
    }

    private void TickCoins()
    {
        if (_active.Count == 0) return;

        Vector2 target = _pill.anchoredPosition + new Vector2(PillWidth * 0.22f, -PillHeight * 0.5f);
        float dt = Time.deltaTime;

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            FlightCoin coin = _active[i];
            coin.Age += dt;

            if (!coin.Flying)
            {
                // Burst phase: a small ballistic hop.
                coin.Velocity.y -= BurstGravity * dt;
                coin.Rect.anchoredPosition += coin.Velocity * dt;

                if (coin.Age >= coin.HangUntil)
                {
                    coin.Flying = true;
                    coin.Age = 0f;
                    coin.FlightStart = coin.Rect.anchoredPosition;
                    // Curved approach: bow the path sideways so coins arc rather than beeline.
                    Vector2 mid = (coin.FlightStart + target) * 0.5f;
                    Vector2 dir = (target - coin.FlightStart).normalized;
                    Vector2 perp = new Vector2(-dir.y, dir.x);
                    coin.FlightControl = mid + perp * Random.Range(-140f, 140f);
                }
                _active[i] = coin;
                continue;
            }

            float t = Mathf.Clamp01(coin.Age / FlightSeconds);
            float eased = t * t; // accelerate into the counter
            Vector2 a = Vector2.Lerp(coin.FlightStart, coin.FlightControl, eased);
            Vector2 b = Vector2.Lerp(coin.FlightControl, target, eased);
            coin.Rect.anchoredPosition = Vector2.Lerp(a, b, eased);

            if (t >= 1f)
            {
                Arrive(coin);
                _active.RemoveAt(i);
            }
            else
            {
                _active[i] = coin;
            }
        }
    }

    private void Arrive(FlightCoin coin)
    {
        coin.Rect.gameObject.SetActive(false);
        _coinPool.Push(coin.Rect);

        _displayValue += coin.Value;
        if (_valueText != null) _valueText.text = _displayValue.ToString();
        _pulseTime = 0f;

        // One quiet clink per BATCH, not per coin - restraint is the contract here.
        if (coin.BatchClink) SfxPlayer.Play("coin_settle_01", 0.22f, 0.04f);
    }

    private void TickPulse()
    {
        if (float.IsPositiveInfinity(_pulseTime)) return;

        _pulseTime += Time.deltaTime;
        if (_pulseTime > 0.5f)
        {
            _pill.localScale = Vector3.one;
            _pulseTime = float.PositiveInfinity;
            return;
        }

        _pill.localScale = Vector3.one * FxKit.Elastic(_pulseTime, 0.1f, 9f, 24f);
    }

    // ---- construction ----------------------------------------------------------------------

    private void EnsureBuilt()
    {
        if (_canvasRoot != null) return;

        _canvasRoot = RuntimeUiKit.CreateOverlayCanvas("CoinHud", 6900); // under GameOver (7100)
        _canvas = _canvasRoot.GetComponent<Canvas>();
        _canvasRect = (RectTransform)_canvasRoot.transform;

        _coinSprite = Resources.Load<Sprite>("Menu/coin"); // the menu top bar's coin art
        _coinSpriteIsFallback = _coinSprite == null;
        if (_coinSpriteIsFallback) _coinSprite = RuntimeSprites.Bubble();

        BuildPill();
    }

    private void BuildPill()
    {
        GameObject pill = new GameObject("CoinPill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _pill = (RectTransform)pill.transform;
        _pill.SetParent(_canvasRect, false);
        _pill.anchorMin = _pill.anchorMax = new Vector2(0f, 1f);
        _pill.pivot = new Vector2(0f, 1f);
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

        GameObject icon = new GameObject("Coin", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = (RectTransform)icon.transform;
        iconRect.SetParent(_pill, false);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(30f, 0f);
        iconRect.sizeDelta = new Vector2(34f, 34f);
        Image iconImage = icon.GetComponent<Image>();
        iconImage.sprite = _coinSprite;
        iconImage.preserveAspect = true;
        iconImage.color = _coinSpriteIsFallback ? CoinGold : Color.white;
        iconImage.raycastTarget = false;

        GameObject value = new GameObject("Value", typeof(RectTransform));
        RectTransform valueRect = (RectTransform)value.transform;
        valueRect.SetParent(_pill, false);
        valueRect.anchorMin = new Vector2(0f, 0f);
        valueRect.anchorMax = new Vector2(1f, 1f);
        valueRect.offsetMin = new Vector2(56f, 0f);
        valueRect.offsetMax = new Vector2(-10f, 0f);
        _valueText = value.AddComponent<TextMeshProUGUI>();
        _valueText.text = "0";
        _valueText.fontSize = 30f;
        _valueText.fontStyle = FontStyles.Bold;
        _valueText.alignment = TextAlignmentOptions.MidlineLeft;
        _valueText.color = Color.white;
        _valueText.raycastTarget = false;

        ApplyPillPosition();
        _pill.gameObject.SetActive(false); // no coins earned yet = no pill at all
    }

    private void ApplyPillPosition()
    {
        float topInset = RuntimeUiKit.SafeAreaTopInset(_canvas);
        _pill.anchoredPosition = new Vector2(LeftMargin, -(topInset + TopOffsetBelowSafeArea));
    }

    private RectTransform GetCoin()
    {
        while (_coinPool.Count > 0)
        {
            RectTransform pooled = _coinPool.Pop();
            if (pooled == null) continue;
            pooled.gameObject.SetActive(true);
            return pooled;
        }

        GameObject coin = new GameObject("FlightCoin", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)coin.transform;
        rect.SetParent(_canvasRect, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(CoinSize, CoinSize);
        Image image = coin.GetComponent<Image>();
        image.sprite = _coinSprite;
        image.preserveAspect = true;
        image.color = _coinSpriteIsFallback ? CoinGold : Color.white;
        image.raycastTarget = false;
        return rect;
    }

    // World position -> this canvas's top-left-anchored coordinate space. Uses the scaler's
    // scale factor directly (exact for a ScreenSpaceOverlay canvas) instead of rect math, so
    // it cannot depend on canvas layout having run.
    private Vector2 WorldToCanvas(Vector3 world)
    {
        Camera cam = TowerCameraController.Camera != null ? TowerCameraController.Camera : Camera.main;
        if (cam == null || _canvas == null)
        {
            return _pill != null ? _pill.anchoredPosition : Vector2.zero; // degenerate: start at the counter
        }

        Vector3 screen = cam.WorldToScreenPoint(world);
        float scale = Mathf.Max(0.0001f, _canvas.scaleFactor);
        return new Vector2(screen.x / scale, -(Screen.height - screen.y) / scale);
    }
}
