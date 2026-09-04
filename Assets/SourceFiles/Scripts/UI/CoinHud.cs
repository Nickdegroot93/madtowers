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
    // Layout: a HudSubCard under the top bar's LEFT segment - same left/right edges as the
    // objective card above it on every screen, content centered (see HudSubCard).

    private const int MaxFlightCoins = 7;
    private const float CoinSize = 32f;
    private const float BurstGravity = 1500f;   // canvas px/s²
    private const float HangSeconds = 0.26f;
    private const float HangStagger = 0.05f;
    private const float FlightSeconds = 0.38f;
    private const float PillFadeInSeconds = 0.25f;

    /// <summary>Tint for the procedural fallback coin (the real art is drawn untinted).</summary>
    public static readonly Color FallbackCoinGold = new Color(1f, 0.76f, 0.22f, 1f);

    /// <summary>The coin art + its fallback, shared by every surface that draws a coin
    /// (this HUD's flight/pill, the results card's coins row) so the art path and the
    /// fallback tint can never drift between them.</summary>
    public static Sprite CoinSprite(out bool isFallback)
    {
        Sprite sprite = Resources.Load<Sprite>("Menu/coin"); // the menu top bar's coin art
        isFallback = sprite == null;
        return isFallback ? RuntimeSprites.Bubble() : sprite;
    }

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
    private RectTransform _iconRect; // the coins' flight target
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

    // Scaled time normally - coins freeze WITH the world during the HitStop impact frames,
    // like everything else. But a full pause (completion card, pause menu) must not strand a
    // coin mid-flight on top of whatever screen just opened, so at timeScale 0 the flight
    // switches to real time, finishes, and deposits.
    private static float FlightDeltaTime => Time.timeScale > 0f ? Time.deltaTime : Time.unscaledDeltaTime;

    private void TickCoins()
    {
        if (_active.Count == 0) return;

        Vector2 target = FlightTarget();
        float dt = FlightDeltaTime;

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
        HudSubCard.MarkDirty(_valueText != null ? (RectTransform)_valueText.transform.parent : null);
        _pulseTime = 0f;

        // One quiet clink per BATCH, not per coin - restraint is the contract here.
        if (coin.BatchClink) SfxPlayer.Play("coin_settle_01", 0.22f, 0.04f);
    }

    private void TickPulse() => FxKit.TickSettlePop(_pill, ref _pulseTime, FlightDeltaTime);

    // ---- construction ----------------------------------------------------------------------

    private void EnsureBuilt()
    {
        if (_canvasRoot != null) return;

        _canvasRoot = RuntimeUiKit.CreateOverlayCanvas("CoinHud", 6900); // under GameOver (7100)
        _canvas = _canvasRoot.GetComponent<Canvas>();
        _canvasRect = (RectTransform)_canvasRoot.transform;

        _coinSprite = CoinSprite(out _coinSpriteIsFallback);

        BuildPill();
    }

    private void BuildPill()
    {
        _pill = HudSubCard.Create(_canvasRect, "CoinPill", HudSubCard.Side.Left);

        _pillGroup = _pill.gameObject.AddComponent<CanvasGroup>();
        _pillGroup.alpha = 0f;
        _pillGroup.interactable = false;
        _pillGroup.blocksRaycasts = false;

        RectTransform row = HudSubCard.CreateRow(_pill);
        Image icon = HudSubCard.AddIcon(row, "Coin", _coinSprite,
            _coinSpriteIsFallback ? FallbackCoinGold : Color.white);
        _iconRect = icon.rectTransform;
        _valueText = HudSubCard.AddText(row, "Value", "0", HudSubCard.ValueFontSize, Color.white);

        ApplyPillPosition();
        _pill.gameObject.SetActive(false); // no coins earned yet = no pill at all
    }

    private void ApplyPillPosition()
    {
        HudSubCard.Place(_pill, _canvas, 0);
    }

    // The coins' destination in the flight coins' coordinate space (top-left anchored, in
    // canvas units): the coin icon's live center. Read off the icon itself, since the card is
    // anchor-stretched to the bar geometry and has no meaningful anchoredPosition of its own.
    private Vector2 FlightTarget()
    {
        if (_iconRect == null || _canvasRect == null) return Vector2.zero;
        Vector2 local = _canvasRect.InverseTransformPoint(_iconRect.TransformPoint(_iconRect.rect.center));
        Rect canvasRect = _canvasRect.rect;
        return new Vector2(local.x - canvasRect.xMin, local.y - canvasRect.yMax);
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
        image.color = _coinSpriteIsFallback ? FallbackCoinGold : Color.white;
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
            return FlightTarget(); // degenerate: start at the counter
        }

        Vector3 screen = cam.WorldToScreenPoint(world);
        float scale = Mathf.Max(0.0001f, _canvas.scaleFactor);
        return new Vector2(screen.x / scale, -(Screen.height - screen.y) / scale);
    }
}
