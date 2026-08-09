using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One rewarded-video placement exists in the whole game: "watch an ad → +2 attempts"
/// (SHOP.md §7). This interface is what that placement talks to; the real ad SDK
/// (Google AdMob direct, decided 2026-08-08 — see AdMobRewardedProvider) implements it.
/// Provider choice lives entirely behind this interface. onFinished(true)
/// means the ad was WATCHED TO COMPLETION and the reward may be requested - an early close
/// or a show failure reports false exactly once.
/// </summary>
public interface IRewardedAdProvider
{
    /// <summary>An ad is loaded and can show right now (SDKs preload; false = hide the button).</summary>
    bool IsReady { get; }
    void Show(Action<bool> onFinished);
}

/// <summary>
/// Facade over the rewarded-ad provider. No provider installed (today: every device build)
/// means ads are simply off and every ad surface hides - the game must never point at an ad
/// that cannot show. In the editor a simulated provider installs itself so the whole
/// out-of-attempts → watch → refill loop is playtestable before any SDK ships.
/// </summary>
public static class RewardedAds
{
    private static IRewardedAdProvider _provider;
    private static bool _showing;
    private static float _showStartedAt;

    // No real ad runs this long. A provider that never calls back at all - process
    // backgrounded mid-ad, the SDK's event lost on the way to the Unity thread - would
    // otherwise wedge _showing true forever and hide every ad surface for the rest of
    // the session. Exactly-once was enforced against double-fire but not zero-fire
    // (review 2026-08-08).
    private const float ShowWatchdogSeconds = 300f;

    /// <summary>Install the live provider at boot (AdMobBootstrap does, on device).</summary>
    public static void Install(IRewardedAdProvider provider) => _provider = provider;

    public static bool Available => _provider != null && _provider.IsReady && !IsShowing;

    /// <summary>Is an ad genuinely on screen right now? Self-heals a provider that never
    /// reported back: the reward is still never granted (no confirmed watch), the player
    /// just gets the affordance back instead of losing it permanently.</summary>
    private static bool IsShowing
    {
        get
        {
            if (_showing && UnityEngine.Time.unscaledTime - _showStartedAt > ShowWatchdogSeconds)
            {
                UnityEngine.Debug.LogWarning("[Ads] provider never reported back; unwedging.");
                _showing = false;
            }
            return _showing;
        }
    }

    /// <summary>Show the rewarded video. The callback fires exactly once; true = reward earned.
    /// The exactly-once is enforced HERE, not trusted: ad SDKs are third-party code, and a
    /// provider that throws or double-fires must not wedge _showing shut or double-grant.</summary>
    public static void Show(Action<bool> onFinished)
    {
        if (!Available)
        {
            onFinished?.Invoke(false);
            return;
        }
        _showing = true;
        _showStartedAt = UnityEngine.Time.unscaledTime;
        bool finished = false;
        void Finish(bool earned)
        {
            if (finished) return;
            finished = true;
            _showing = false;
            onFinished?.Invoke(earned);
        }
        try
        {
            _provider.Show(Finish);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Ads] rewarded provider threw on Show: {e}");
            Finish(false);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        _provider = null;
        _showing = false;
#if UNITY_EDITOR
        _provider = new SimulatedRewardedAdProvider();
#endif
    }
}

#if UNITY_EDITOR
/// <summary>
/// Editor-only stand-in for a real rewarded video: a full-screen "TEST AD" overlay with a
/// 5-second countdown. Closing early forfeits (the real SDK's skip path); sitting it out
/// turns the close button gold and pays out - both callback branches get exercised. Runs on
/// unscaled time (the menu lives at timeScale = 0).
/// </summary>
internal sealed class SimulatedRewardedAdProvider : IRewardedAdProvider
{
    public bool IsReady => true;

    public void Show(Action<bool> onFinished)
    {
        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Simulated Rewarded Ad", 9000);
        var driver = overlay.AddComponent<SimulatedAdDriver>();
        driver.OnFinished = onFinished;
    }

    private sealed class SimulatedAdDriver : MonoBehaviour
    {
        public Action<bool> OnFinished;

        private const float AdSeconds = 5f;
        private float _endsAt;
        private bool _done;             // countdown finished - the close now rewards
        private TextMeshProUGUI _countdown;
        private TextMeshProUGUI _closeLabel;
        private Image _closeBg;

        private void Start()
        {
            _endsAt = Time.unscaledTime + AdSeconds;

            Image backdrop = RuntimeUiKit.CreateImage(transform, "Backdrop", null,
                new Color(0.04f, 0.05f, 0.08f, 0.98f));
            RuntimeUiKit.Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;   // swallow every tap under the "ad"

            RuntimeUiKit.CreateTmp(transform, "Title", "TEST AD", 96,
                new Color(0.92f, 0.97f, 1f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold,
                RuntimeUiKit.TitleFont, new Vector2(0f, 60f), new Vector2(800f, 110f),
                new Vector2(0.5f, 0.5f));
            RuntimeUiKit.CreateTmp(transform, "Sub", "SIMULATED REWARDED VIDEO - EDITOR ONLY", 20,
                new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleCenter, FontStyle.Bold,
                RuntimeUiKit.TitleFont, new Vector2(0f, -20f), new Vector2(800f, 30f),
                new Vector2(0.5f, 0.5f));
            _countdown = RuntimeUiKit.CreateTmp(transform, "Countdown", "", 30,
                new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter, FontStyle.Bold,
                RuntimeUiKit.TitleFont, new Vector2(0f, -90f), new Vector2(800f, 40f),
                new Vector2(0.5f, 0.5f));

            // The close pill: an early tap forfeits, like a real skip. Flips gold at 0.
            _closeBg = RuntimeUiKit.CreateImage(transform, "Close", RuntimeSprites.RoundedPanel(),
                new Color(0.14f, 0.13f, 0.11f, 1f));
            _closeBg.type = Image.Type.Sliced;
            RuntimeUiKit.SetRect(_closeBg.rectTransform, new Vector2(-28f, -28f),
                new Vector2(320f, 84f), new Vector2(1f, 1f));
            _closeBg.raycastTarget = true;
            _closeLabel = RuntimeUiKit.CreateTmp(_closeBg.transform, "Label", "SKIP - NO REWARD", 22,
                new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter, FontStyle.Bold,
                RuntimeUiKit.TitleFont);
            Button close = _closeBg.gameObject.AddComponent<Button>();
            close.targetGraphic = _closeBg;
            close.onClick.AddListener(() => Finish(_done));
        }

        private void Update()
        {
            if (_done) return;
            float remaining = _endsAt - Time.unscaledTime;
            if (remaining > 0f)
            {
                _countdown.text = $"REWARD IN {Mathf.CeilToInt(remaining)}";
                return;
            }
            _done = true;
            _countdown.text = "REWARD EARNED";
            _closeLabel.text = "CLAIM +2";
            _closeLabel.color = new Color(0.10f, 0.08f, 0.03f, 1f);
            _closeBg.color = new Color(0.94f, 0.76f, 0.31f, 1f);
        }

        private void Finish(bool earned)
        {
            Action<bool> callback = OnFinished;
            OnFinished = null;               // exactly-once, even if Destroy defers
            Destroy(gameObject);
            callback?.Invoke(earned);
        }
    }
}
#endif
