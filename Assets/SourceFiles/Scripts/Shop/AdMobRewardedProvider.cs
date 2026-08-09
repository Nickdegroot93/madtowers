using System;
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

/// <summary>
/// The live rewarded-video provider: Google AdMob, direct (no mediation). Chosen over
/// LevelPlay because BACKEND.md §6.4 grants the +2 attempts from an AdMob SSV callback,
/// which is direct-integration territory - mediation would put a middleman between the
/// watch and the server grant. Swapping to mediation later is this one class:
/// <see cref="IRewardedAdProvider"/> is the whole contract the game knows about.
///
/// AD UNIT IDS ARE GOOGLE'S PUBLIC TEST UNITS. They serve real fullscreen video, fire the
/// real callbacks, and are tied to no account - so the whole loop is testable on device
/// today. They must be swapped for the account's real units before release; see
/// <see cref="AdUnitId"/>. Test units on a shipped build earn nothing; real units on a
/// dev build earn an invalid-traffic ban, so this is the safe default of the two.
/// </summary>
public sealed class AdMobRewardedProvider : IRewardedAdProvider
{
    // Google's documented sample units (developers.google.com/admob/unity/test-ads).
    // Not tied to any account, so they cannot generate invalid traffic.
    private const string TestRewardedAndroid = "ca-app-pub-3940256099942544/5224354917";
    private const string TestRewardedIos = "ca-app-pub-3940256099942544/1712485313";

    // The real units (AdMob console, "attempts_refill", added 2026-08-09).
    private const string LiveRewardedAndroid = "ca-app-pub-4384624714813425/2353049753";
    private const string LiveRewardedIos = "ca-app-pub-4384624714813425/9768505345";

    /// <summary>
    /// Live units ONLY in a release build. Requesting real ads from a machine you are
    /// developing on is how AdMob accounts get flagged for invalid traffic - and the two
    /// failure modes are not symmetric: shipping test units costs revenue for a release
    /// (recoverable in an update), while testing on live units risks the account itself.
    /// So the gate fails toward test ads, and a Development Build always gets them.
    /// </summary>
    private static bool UseLiveAds => !Debug.isDebugBuild;

    private static string AdUnitId
    {
        get
        {
#if UNITY_IOS && !UNITY_EDITOR
            return UseLiveAds ? LiveRewardedIos : TestRewardedIos;
#else
            return UseLiveAds ? LiveRewardedAndroid : TestRewardedAndroid;
#endif
        }
    }

    // Retry schedule after a failed load, in seconds. No fill and no network at cold
    // start are both routine on mobile, and without a retry a single failed load kills
    // rewarded ads for the entire session: the button hides, so no ad is ever shown, so
    // nothing ever triggers the next load (review 2026-08-08). The last value repeats.
    private static readonly float[] RetryBackoff = { 5f, 15f, 45f, 120f, 300f };

    private RewardedAd _ad;
    private bool _loading;
    private int _failures;
    private float _nextLoadAt;

    /// <summary>Hides every ad surface until a video is genuinely in hand - the game must
    /// never point at an ad that cannot show (RewardedAds' contract).</summary>
    public bool IsReady => _ad != null && _ad.CanShowAd();

    /// <summary>Drives loading. Every load in the provider's life comes from here, so
    /// there is exactly one place that can decide "no ad in hand, time to fetch one" -
    /// the previous design had two callers and a dead end between them.</summary>
    public void Tick(float unscaledNow)
    {
        if (_ad != null || _loading || unscaledNow < _nextLoadAt) return;
        BeginLoad();
    }

    /// <summary>Returning from background is the cheapest moment to recover a placement
    /// that has been failing: the network is usually back and the player is right here.</summary>
    public void RetryNow() => _nextLoadAt = 0f;

    public void Show(Action<bool> onFinished)
    {
        RewardedAd ad = _ad;
        if (ad == null || !ad.CanShowAd())
        {
            // The cached ad has gone stale or been destroyed under us. Drop it, or Tick's
            // "_ad != null" guard would refuse to fetch a replacement and the placement
            // would stay silently dead for the session (review 2026-08-08).
            _ad = null;
            _nextLoadAt = 0f;
            onFinished?.Invoke(false);
            return;
        }

        // SSV attribution is stamped HERE, not at load. An ad is preloaded at boot and can
        // sit in hand for the best part of an hour; on a fresh install the anonymous
        // sign-in has not returned yet at that point, so a load-time stamp would be empty
        // for the whole session - the player watches a full video, Google posts a callback
        // with no custom_data, and the reward is dropped in silence. Reading the session at
        // SHOW time means it is whatever is true when the ad is actually watched, and the
        // account cannot have been deleted out from under it either (review 2026-08-09).
        string userId = SupabaseSession.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            // Refusing beats showing: a watched ad that can never pay is worse than a
            // hidden button, and the caller reports failure rather than a silent nothing.
            Debug.LogWarning("[Ads] no session at show time - refusing to burn an unpayable ad.");
            onFinished?.Invoke(false);
            return;
        }
        // Plain fields in this plugin version, not the Builder the docs show.
        ad.SetServerSideVerificationOptions(new ServerSideVerificationOptions { CustomData = userId });

        // A rewarded ad is single-use: hand it off now so nothing can show it twice, and
        // start the next load the moment this one closes.
        _ad = null;

        bool earned = false;
        bool reported = false;
        void Report(bool value)
        {
            if (reported) return;             // closed-then-failed both firing is legal
            reported = true;
            ad.Destroy();
            // A presentation happened, so the placement is healthy: clear any backoff and
            // let Tick fetch the next one on the following frame.
            _failures = 0;
            _nextLoadAt = 0f;
            onFinished?.Invoke(value);
        }

        ad.OnAdFullScreenContentClosed += () => Report(earned);
        ad.OnAdFullScreenContentFailed += err =>
        {
            Debug.LogWarning($"[Ads] rewarded failed to present: {err?.GetMessage()}");
            Report(false);
        };

        // Fires only on a watch-to-completion. An early close never reaches here, so the
        // reward flag stays false and the player is not paid - matching the SSV grant.
        ad.Show(_ => earned = true);
    }

    private void BeginLoad()
    {
        _loading = true;
        RewardedAd.Load(AdUnitId, new AdRequest(), (ad, error) =>
        {
            _loading = false;
            if (error != null || ad == null)
            {
                // Backoff, not a retry storm: a failed fill is normal and is never shown
                // to the player, but it must not be terminal either.
                _failures++;
                float wait = RetryBackoff[Mathf.Min(_failures - 1, RetryBackoff.Length - 1)];
                _nextLoadAt = Time.unscaledTime + wait;
                Debug.Log($"[Ads] rewarded load failed ({_failures}), retrying in {wait}s: " +
                          $"{error?.GetMessage()}");
                return;
            }
            _failures = 0;
            _ad = ad;
        });
    }
}

/// <summary>
/// Boots the ad stack on device: consent first (never before), then the SDK, then a
/// preload. The editor deliberately keeps its simulated provider (RewardedAds installs
/// that itself), so the out-of-attempts → watch → refill loop stays playtestable without
/// the SDK in the way.
/// </summary>
public static class AdMobBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
#if !UNITY_EDITOR
        // Ad SDK callbacks arrive on a background thread by default; everything they reach
        // in this game touches UI, so marshal them before anything else is configured.
        MobileAds.RaiseAdEventsOnUnityMainThread = true;

        // The driver is created BEFORE consent, not inside InitializeAds. It owns both
        // retry paths, and the consent step is the one that can fail with nothing else
        // alive to recover it - a driver that only exists after success cannot retry
        // the failure that stopped it existing (review 2026-08-09).
        var driver = new GameObject("AdLoadDriver").AddComponent<AdLoadDriver>();
        UnityEngine.Object.DontDestroyOnLoad(driver.gameObject);
        _driver = driver;

        RequestConsentThenInitialize();
#endif
    }

    private static AdLoadDriver _driver;
    private static bool _consentInFlight;

    /// <summary>Re-run the consent step if it never completed. Cheap to call often: it
    /// no-ops once the SDK is initialised or while an attempt is outstanding.</summary>
    public static void RetryConsentIfNeeded()
    {
        if (_initialized || _consentInFlight) return;
        RequestConsentThenInitialize();
    }

    /// <summary>
    /// UMP (Google's consent SDK) decides whether a form is needed and shows it. This is
    /// not optional in the EU/UK, and it must run BEFORE the ads SDK initializes - an ad
    /// requested without consent is the violation, not the ad that gets shown.
    /// </summary>
    private static void RequestConsentThenInitialize()
    {
        // A returning player who already consented can request ads while the refresh runs.
        if (ConsentInformation.CanRequestAds())
        {
            InitializeAds();
        }

        _consentInFlight = true;
        ConsentInformation.Update(new ConsentRequestParameters(), consentError =>
        {
            _consentInFlight = false;
            if (consentError != null)
            {
                // Consent state unknown: no ads YET. Failing closed costs a few rewarded
                // views; failing open costs a GDPR complaint. Not terminal though - the
                // driver retries this on the next foreground.
                Debug.LogWarning($"[Ads] consent update failed (will retry): {consentError.Message}");
                return;
            }

            ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
            {
                if (formError != null)
                {
                    Debug.LogWarning($"[Ads] consent form failed: {formError.Message}");
                }
                // Checked either way: a form that failed to present can still leave a
                // state that permits ads (a returning player, or no form required at
                // all), and Google's own sample re-checks here rather than returning.
                if (ConsentInformation.CanRequestAds())
                {
                    InitializeAds();
                }
            });
        });
    }

    /// <summary>Ticks the provider's retry schedule and gives it a free recovery attempt
    /// whenever the app comes back to the foreground.</summary>
    private sealed class AdLoadDriver : MonoBehaviour
    {
        internal AdMobRewardedProvider Provider;

        private void Update() => Provider?.Tick(Time.unscaledTime);

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) return;
            Provider?.RetryNow();
            // Consent may never have completed (no network at launch is routine). The
            // provider's own backoff cannot help there - without consent the SDK is never
            // initialised and no provider exists at all, so the retry has to live one
            // layer up (review 2026-08-09).
            AdMobBootstrap.RetryConsentIfNeeded();
        }
    }

    private static bool _initialized;

    private static void InitializeAds()
    {
        if (_initialized) return;
        _initialized = true;

        MobileAds.Initialize(_ =>
        {
            // Say it out loud once: "ads work on my phone but earn nothing" and "my account
            // got flagged" are both this line being the value you did not expect.
            Debug.Log(Debug.isDebugBuild
                ? "[Ads] development build - TEST ads (no revenue, no invalid-traffic risk)."
                : "[Ads] release build - LIVE ads. Do not tap these yourself.");

            var provider = new AdMobRewardedProvider();
            RewardedAds.Install(provider);
            // The driver already exists (created at boot); hand it the provider so its
            // Update starts driving the load schedule.
            if (_driver != null) _driver.Provider = provider;
        });
    }
}
