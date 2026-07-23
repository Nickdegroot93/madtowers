using System.Collections;
using UnityEngine;

/// <summary>
/// Background mirror of the ProgressStore document (BACKEND.md §5.2). The local save stays
/// the source of truth during play; this pushes it through the server's merge_progress
/// (union/max/last-writer-wins) and applies the merged result back, so two devices converge
/// with no conflict UI. Pull and push are the SAME idempotent exchange - the server is the
/// merger. Never blocks gameplay; failures leave a dirty flag retried on Ready/foreground.
/// </summary>
public static class ProgressSync
{
    private const float DebounceSeconds = 5f;

    private static bool _dirty;
    private static bool _inFlight;
    private static bool _loopRunning;
    private static float _lastSaveRealtime;
    private static float _backoffUntilRealtime;
    private static int _failStreak;

    // Rejection backoff ladder: a permanently-rejected push (schema drift, server guard)
    // must not hammer the server at the pusher's 1Hz tick forever.
    private static readonly float[] FailBackoff = { 5f, 15f, 60f };

    public static void Init()
    {
        ProgressStore.Saved -= HandleSaved; // idempotent re-init (domain-reload-off safety)
        ProgressStore.Saved += HandleSaved;
    }

    /// <summary>First successful auth of the session: pull-merge immediately, then keep a
    /// low-frequency pusher alive for debounced saves.</summary>
    public static void OnReady()
    {
        Merge();
        if (_loopRunning) return;
        _loopRunning = true;
        OnlineService.Run(PusherLoopCo());
    }

    /// <summary>App is going to background - push what we have now; the process may not
    /// come back (mobile). Fire and forget.</summary>
    public static void OnBackground()
    {
        if (_dirty && OnlineService.IsReady) Merge();
    }

    public static void OnFocusRegained()
    {
        if (_dirty && OnlineService.IsReady) Merge();
    }

    private static void HandleSaved()
    {
        _dirty = true;
        _lastSaveRealtime = Time.realtimeSinceStartup;
    }

    private static IEnumerator PusherLoopCo()
    {
        WaitForSecondsRealtime tick = new WaitForSecondsRealtime(1f);
        while (true)
        {
            yield return tick;
            if (!_dirty || _inFlight || !OnlineService.IsReady) continue;
            if (Time.realtimeSinceStartup - _lastSaveRealtime < DebounceSeconds) continue;
            if (Time.realtimeSinceStartup < _backoffUntilRealtime) continue;
            Merge();
        }
    }

    private static void Merge()
    {
        if (_inFlight) return;
        _inFlight = true;
        _dirty = false; // saves landing while in flight re-arm it

        // Snapshot marker: if any save lands during the round trip, the merged reply is
        // missing that write - applying it wholesale would erase the newer local data
        // (review finding). Skip the apply and let the dirty re-push re-merge; the server
        // merge is idempotent so this converges without loss.
        long sentMutation = ProgressStore.MutationCounter;

        string body = $"{{\"p_payload\":{ProgressStore.ExportPayloadJson()}," +
                      $"\"p_schema_version\":{ProgressStore.SchemaVersion}}}";
        OnlineService.RpcRaw("merge_progress", body,
            merged =>
            {
                _inFlight = false;
                _failStreak = 0;
                _backoffUntilRealtime = 0f;
                if (ProgressStore.MutationCounter == sentMutation)
                    ProgressStore.ApplyMergedPayload(merged);
                else
                    _dirty = true;
            },
            err =>
            {
                _inFlight = false;
                _dirty = true; // retried by the pusher loop / next foreground
                float delay = FailBackoff[Mathf.Min(_failStreak, FailBackoff.Length - 1)];
                _failStreak++;
                _backoffUntilRealtime = Time.realtimeSinceStartup + delay;
            });
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        _dirty = false;
        _inFlight = false;
        _loopRunning = false;
        _lastSaveRealtime = 0f;
        _backoffUntilRealtime = 0f;
        _failStreak = 0;
    }
}
