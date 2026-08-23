using UnityEngine;

/// <summary>
/// The one-lifetime store review ask (DEVLETTER.md beat 3), behind the same tiny-facade
/// pattern as RewardedAds/PremiumStore. Fired at the first Chapter 3 completion - a
/// positive milestone, player not mid-task - and NEVER again from us: the flag is marked
/// the moment the OS API is called, because whether a dialog actually appeared is the
/// OS's secret (iOS may silently show nothing; both platforms quota-limit).
///
/// Store-policy rules baked in here, not left to call sites (DEVLETTER.md §4):
/// official APIs only (Google In-App Review / SKStoreReviewController), no custom dialog
/// before the call, no sentiment gating, no reward, no "review instead of buying" - the
/// call is silent and the OS owns everything the player sees.
/// </summary>
public static class StoreReview
{
    // Editor sessions simulate the beat without touching the save: marking the real flag
    // from a play-mode test consumes the once-ever ask for the actual device (and the
    // cloud merge is max, so it never un-consumes - review 2026-08-22, it happened).
    private static bool _editorAskedThisSession;

    /// <summary>Has the ask been consumed, in this context? Editor: this session only.</summary>
    public static bool Asked
        => Application.isEditor ? _editorAskedThisSession : ProgressStore.WasReviewAsked();

    /// <summary>Fire the platform review flow once per lifetime. Safe to call on every
    /// menu visit - the save flag (monotonic timestamp, cloud-synced) makes it a no-op
    /// after the first call.</summary>
    public static void RequestReviewOnce()
    {
        if (Asked) return;
        if (Application.isEditor)
        {
            _editorAskedThisSession = true;
            Debug.Log("[StoreReview] Editor: review ask would fire here (simulated; save untouched).");
            return;
        }
        ProgressStore.MarkReviewAsked();

#if UNITY_IOS
        // Built into the engine; Apple caps it at 3 shows/user/365 days and may no-op.
        UnityEngine.iOS.Device.RequestStoreReview();
#elif UNITY_ANDROID
        // The Play flow is a two-step async (fetch ReviewInfo, then launch); it needs a
        // living MonoBehaviour. The host outlives the menu scene on purpose - the fetch
        // takes a moment and a player tapping into a run must not cancel the ask forever.
        GameObject host = new GameObject("[StoreReview]");
        host.hideFlags = HideFlags.HideInHierarchy;
        Object.DontDestroyOnLoad(host);
        host.AddComponent<PlayReviewRunner>();
#endif
    }

#if UNITY_ANDROID
    private sealed class PlayReviewRunner : MonoBehaviour
    {
        private void Start() => StartCoroutine(Run());

        private System.Collections.IEnumerator Run()
        {
            var manager = new Google.Play.Review.ReviewManager();
            var request = manager.RequestReviewFlow();
            yield return request;
            // The Play-services fetch takes seconds; the player may have tapped into a run
            // meanwhile. A review dialog over live gameplay violates the binding "positive
            // milestone, not mid-task" rule (DEVLETTER.md §4) - the ask is simply lost
            // (the flag is already marked, and once-ever means we accept a lost one).
            if (!LevelSelectionState.IsSelectionPending)
            {
                Debug.Log("[StoreReview] Skipped launch: player left the menu during the fetch.");
            }
            else if (request.Error == Google.Play.Review.ReviewErrorCode.NoError)
            {
                var launch = manager.LaunchReviewFlow(request.GetResult());
                yield return launch;
                Debug.Log($"[StoreReview] Play review flow finished: {launch.Error}");
            }
            else
            {
                // Quota'd or unavailable - the OS said no, and once-ever means we accept it.
                Debug.Log($"[StoreReview] Play review flow unavailable: {request.Error}");
            }
            Destroy(gameObject);
        }
    }
#endif
}
