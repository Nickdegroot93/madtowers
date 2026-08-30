using System;
using System.Collections;
using UnityEngine;
using Unity.Notifications;
#if UNITY_ANDROID
using Unity.Notifications.Android;
#elif UNITY_IOS
using Unity.Notifications.iOS;
#endif

/// <summary>
/// Local (on-device) notifications: lives-refilled, comeback reminders and the
/// one-level-left chapter nudge. Everything here is a LOCAL notification - the phone
/// schedules it for a future time; no server is involved (remote push is post-launch,
/// BACKEND.md territory).
///
/// The whole system is one pattern: <b>schedule at background, cancel on resume</b>.
/// While the app is open nothing is ever pending; the moment it goes to background we
/// predict the future (when lives fill, 24h/72h marks) and queue it; the moment the
/// player returns every prediction is stale, so everything is cancelled and the tray
/// is cleared. The meter only moves while the app is open, so a prediction made at
/// background time cannot be wrong, only cancelled.
///
/// Permission is never requested at boot (acceptance craters); the one entry point is
/// <see cref="RequestPermission"/>, called from the refill modal's contextual ask and
/// the Settings Alerts tab. In the editor everything no-ops (the package's Android
/// implementation is a stub there); a PlayerPrefs-faked permission keeps the UI flows
/// testable in play mode.
/// </summary>
public static class NotificationScheduler
{
    public enum PermissionState
    {
        /// <summary>Never asked - the contextual ask may be offered.</summary>
        NotRequested,
        Granted,
        /// <summary>Denied at the OS level - only the system settings screen can undo
        /// this, so UI should deep-link there instead of re-asking.</summary>
        Denied,
    }

    // Fixed identifiers: rescheduling with the same id replaces instead of stacking.
    private const int LivesFullId = 9101;
    private const int ComebackSoonId = 9102;
    private const int ComebackLateId = 9103;
    private const int ChapterNudgeId = 9104;
    private const int TestPingId = 9199;

    // Two comeback pings, gentle by design (approved 2026-08-11): the near one at 24h -
    // replaced by the more personal chapter nudge when one applies - and the far one at
    // 72h. Both die the instant the player returns; there is no third.
    private const double ComebackSoonHours = 24.0;
    private const double ComebackLateHours = 72.0;

    // Don't bother notifying about a refill that lands moments after backgrounding -
    // a ping while the phone is still in their hand reads as noise, not a favor.
    private const double MinLeadSeconds = 60.0;

    private const string EditorFakePermissionKey = "notifications.editorPermission";
    private const string SoftAskShownKey = "notifications.softAskShown";

    private static bool _initialized;
    private static NotificationHost _host;

    /// <summary>Real scheduling exists only on device; the editor fakes permission
    /// state (so the UI flows run) and skips the actual scheduling.</summary>
    private static bool OnDevice => !Application.isEditor;

    // ---- lifecycle --------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        _initialized = false;
        _host = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // Runs once per app/domain load (never per scene load), but guard anyway so a
        // second host + focusChanged subscription is structurally impossible.
        if (_host != null) return;
        GameObject go = new GameObject("[Notifications]");
        // HideInHierarchy, NOT HideAndDontSave: DontSave objects survive exiting play
        // mode, leaking one dead host into the editor session per play.
        go.hideFlags = HideFlags.HideInHierarchy;
        UnityEngine.Object.DontDestroyOnLoad(go);
        _host = go.AddComponent<NotificationHost>();
        Application.focusChanged += HandleFocusChanged;

        // Fresh session: whatever last background queued is stale now, and anything
        // sitting in the tray has served its purpose (they're here).
        CancelEverything();
    }

    private sealed class NotificationHost : MonoBehaviour
    {
        private void OnDestroy() => Application.focusChanged -= HandleFocusChanged;
    }

    private static void HandleFocusChanged(bool hasFocus)
    {
        // Focus-loss also fires for overlays (ads, permission dialogs); that is fine -
        // the matching focus-gain cancels whatever got queued a moment later.
        if (hasFocus) CancelEverything();
        else ScheduleAllForBackground();
    }

    // ---- permission -------------------------------------------------------------------

    /// <summary>Current OS-level permission. Cheap enough to query per frame from UI.</summary>
    public static PermissionState Permission
    {
        get
        {
            if (!OnDevice)
            {
                return PlayerPrefs.GetInt(EditorFakePermissionKey, 0) == 1
                    ? PermissionState.Granted : PermissionState.NotRequested;
            }
#if UNITY_ANDROID
            switch (AndroidNotificationCenter.UserPermissionToPost)
            {
                case Unity.Notifications.Android.PermissionStatus.Allowed:
                    return PermissionState.Granted;
                case Unity.Notifications.Android.PermissionStatus.NotRequested:
                case Unity.Notifications.Android.PermissionStatus.RequestPending:
                    return PermissionState.NotRequested;
                default:
                    return PermissionState.Denied;
            }
#elif UNITY_IOS
            switch (iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus)
            {
                case AuthorizationStatus.Authorized:
                case AuthorizationStatus.Provisional:
                    return PermissionState.Granted;
                case AuthorizationStatus.NotDetermined:
                    return PermissionState.NotRequested;
                default:
                    return PermissionState.Denied;
            }
#else
            return PermissionState.Denied;
#endif
        }
    }

    /// <summary>May the refill modal offer its "want a ping when lives are full?" line?
    /// Self-limiting: the line exists only while the OS has never been asked, so it
    /// disappears forever after one tap (either verdict).</summary>
    public static bool CanOfferContextualAsk =>
        Permission == PermissionState.NotRequested && SettingsService.AlertsEnabled;

    /// <summary>Has the one-time "out of lives - want a ping?" sheet ever been shown?
    /// Separate from the OS permission state on purpose: declining the SOFT ask leaves
    /// the OS never-asked (so the refill modal's passive row and Settings can still
    /// offer), but the sheet itself must never pop again - one auto-interruption, ever.</summary>
    public static bool SoftAskShown => PlayerPrefs.GetInt(SoftAskShownKey, 0) == 1;

    public static void MarkSoftAskShown() => PlayerPrefs.SetInt(SoftAskShownKey, 1);

    /// <summary>Show the real OS permission dialog. Call ONLY from an explicit player
    /// action (the contextual ask / the settings toggle) - never at boot.</summary>
    public static void RequestPermission(Action<bool> onDone)
    {
        if (!OnDevice)
        {
            // Editor fake: instant grant, so the post-ask UI states are reachable.
            PlayerPrefs.SetInt(EditorFakePermissionKey, 1);
            Debug.Log("[Notifications] Editor fake permission granted.");
            onDone?.Invoke(true);
            return;
        }
        EnsureInitialized();
        if (_host != null) _host.StartCoroutine(RequestPermissionCo(onDone));
        else onDone?.Invoke(false);
    }

    private static IEnumerator RequestPermissionCo(Action<bool> onDone)
    {
        NotificationsPermissionRequest request = NotificationCenter.RequestPermission();
        yield return request;
        bool granted = request.Status == NotificationsPermissionStatus.Granted;
        Debug.Log($"[Notifications] Permission request finished: {request.Status}");
        onDone?.Invoke(granted);
    }

    /// <summary>Deep-link to the app's notification page in system settings - the only
    /// road back once the OS dialog was denied.</summary>
    public static void OpenSystemSettings()
    {
        if (!OnDevice)
        {
            PlayerPrefs.DeleteKey(EditorFakePermissionKey);   // editor: "re-enable"
            return;
        }
        EnsureInitialized();
        NotificationCenter.OpenNotificationSettings();
    }

    // ---- scheduling -------------------------------------------------------------------

    private static void EnsureInitialized()
    {
        if (_initialized || !OnDevice) return;
        NotificationCenterArgs args = NotificationCenterArgs.Default;
        // One channel for everything we send: these are all "come back and play"
        // reminders in OS terms. Per-type control lives in our own Settings toggles,
        // which gate the scheduling side.
        args.AndroidChannelId = "reminders";
        args.AndroidChannelName = "Reminders";
        args.AndroidChannelDescription = "Attempts refilled and comeback reminders";
        NotificationCenter.Initialize(args);
        _initialized = true;
    }

    private static void CancelEverything()
    {
        if (!OnDevice) return;
        EnsureInitialized();
        NotificationCenter.CancelAllScheduledNotifications();
        NotificationCenter.CancelAllDeliveredNotifications();
        NotificationCenter.ClearBadge();
    }

    private static void ScheduleAllForBackground()
    {
        if (!OnDevice) return;
        if (!SettingsService.AlertsEnabled || Permission != PermissionState.Granted) return;
        EnsureInitialized();

        // Belt and suspenders: HandleFocusChanged(true) already cancelled, but ad/dialog
        // overlays can produce loss->loss sequences; same-id replacement makes duplicates
        // impossible anyway.
        NotificationCenter.CancelAllScheduledNotifications();

        ScheduleLivesFull();
        ScheduleComebacks();
    }

    /// <summary>"Lives are full" at the predicted refill moment. The meter only moves
    /// while the app is open, so the prediction made here is exact.</summary>
    private static void ScheduleLivesFull()
    {
        if (!AttemptsService.MeterActive) return;   // premium / pre-meta-unlock: no meter
        int count = AttemptsService.Count;
        if (count >= AttemptsService.MaxAttempts) return;

        double seconds = AttemptsService.NextRegenIn.TotalSeconds
            + (AttemptsService.MaxAttempts - count - 1) * (double)AttemptsService.RegenSeconds;
        if (seconds < MinLeadSeconds) return;

        Schedule(LivesFullId, "Your attempts are full!",
            $"All {AttemptsService.MaxAttempts} attempts are back. The tower's waiting.",
            TimeSpan.FromSeconds(seconds));
    }

    /// <summary>The 24h/72h comeback pair. When the player is one level from finishing
    /// a chapter, the 24h slot carries that (far more personal) nudge instead of the
    /// generic line - never both, two pings max either way.</summary>
    private static void ScheduleComebacks()
    {
        string nudgeChapter = FindOneLevelLeftChapter();
        if (nudgeChapter != null)
        {
            Schedule(ChapterNudgeId, $"One level left in {nudgeChapter}!",
                "Finish it and see what unlocks next.",
                TimeSpan.FromHours(ComebackSoonHours));
        }
        else
        {
            Schedule(ComebackSoonId, "The tower misses you",
                "Your blocks are stacked and ready. One quick run?",
                TimeSpan.FromHours(ComebackSoonHours));
        }

        Schedule(ComebackLateId, "Still standing?",
            "Come see how high you can build today.",
            TimeSpan.FromHours(ComebackLateHours));
    }

    /// <summary>DisplayName of the first unlocked chapter with exactly one level left,
    /// or null. Computed fresh at background time so it can never go stale.</summary>
    private static string FindOneLevelLeftChapter()
    {
        ChapterDefinition[] chapters = Campaign.LoadChaptersInOrder();
        for (int i = 0; i < chapters.Length; i++)
        {
            ChapterDefinition chapter = chapters[i];
            if (chapter == null || !Campaign.IsChapterUnlocked(chapters, i)) continue;
            int remaining = 0;
            foreach (LevelDefinition level in chapter.Levels)
            {
                if (level != null && !ProgressStore.IsLevelCompleted(level)) remaining++;
            }
            if (remaining == 1) return chapter.DisplayName;
            if (remaining > 1) return null;   // stop at the frontier chapter
        }
        return null;
    }

    private static void Schedule(int id, string title, string text, TimeSpan delay,
        bool showInForeground = false)
    {
        Notification notification = new Notification
        {
            Identifier = id,
            Title = title,
            Text = text,
            // Real alerts never show in foreground - if they're in the app, the app IS
            // the message. Only the dev test ping opts in (so it can be seen at all).
            ShowInForeground = showInForeground,
        };
        NotificationCenter.ScheduleNotification(notification,
            new NotificationIntervalSchedule(delay));
    }

    /// <summary>
    /// DEVELOPMENT BUILDS ONLY (the Alerts tab's dev row): a ping 60 seconds out that
    /// proves the whole pipeline - permission, channel, delivery - without waiting out
    /// a real regen cycle. ShowInForeground because staying IN the app is the point:
    /// backgrounding would run ScheduleAllForBackground's cancel-and-reschedule and
    /// wipe this very ping.
    /// </summary>
    public static void ScheduleTestPing()
    {
        if (!OnDevice)
        {
            Debug.Log("[Notifications] Test ping is device-only (editor scheduling is stubbed).");
            return;
        }
        EnsureInitialized();
        Schedule(TestPingId, "Test ping", "The notification pipeline works.",
            TimeSpan.FromSeconds(60), showInForeground: true);
    }
}
