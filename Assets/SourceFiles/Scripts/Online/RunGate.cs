using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The run handshake (BACKEND.md §6): start_run before the launch reload, finish_run at run
/// end. The server-issued run_id is the anti-cheat spine - a score only lands against a run
/// the server saw start. ActiveRunId rides a plain static across the scene reload
/// (RunSuppliesState pattern). Failed finish reports persist to disk and retry (the attempt
/// refund / score must survive a dropped connection at the results screen, BACKEND.md §5.1).
/// Custom Game never talks to the server (no level identity - ProgressStore.LevelId is null).
/// </summary>
public static class RunGate
{
    /// <summary>The server-granted id of the run in progress; null when none.</summary>
    public static string ActiveRunId { get; private set; }

    /// <summary>The current run was granted by the server (campaign, online). False for
    /// Custom Game and for runs started with the online layer disabled.</summary>
    public static bool ActiveRunServerBacked { get; private set; }

    public struct GateResult
    {
        public bool Allowed;
        public string DeniedReason;    // server verdict, e.g. "out_of_attempts"
        public int SecondsUntilNext;   // countdown for the out-of-attempts message
        public bool Offline;           // couldn't reach the server: campaign requires online
    }

    [Serializable]
    private class StartRunDto
    {
        public bool allowed;
        public string run_id;
        public int attempts;
        public int seconds_until_next;
        public string reason;
        public bool premium;
        public bool meter_charged;
    }

    [Serializable]
    private class FinishRunDto
    {
        public bool accepted;
        public string reason;
        public int attempts;
        public bool new_best;
    }

    [Serializable]
    private class PendingFinish
    {
        public string runId;
        public bool won;
        public int score;
        public float height;
    }

    [Serializable]
    private class PendingFinishFile
    {
        public List<PendingFinish> items = new List<PendingFinish>();
    }

    private const int MaxQueuedFinishes = 100;

    private static PendingFinishFile _queue;
    private static readonly HashSet<string> _inFlight = new HashSet<string>();
    private static bool _grantPending;

    private static string QueuePath => Path.Combine(Application.persistentDataPath, "pending_finish.json");

    /// <summary>Ask to start a run. Campaign online → start_run RPC; Custom Game or online
    /// layer disabled → immediate local allow. done always fires exactly once, main thread.</summary>
    public static void BeginRun(LevelDefinition level, bool boosted, string loadoutJson,
                                Action<GateResult> done)
    {
        string levelId = ProgressStore.LevelId(level);
        if (levelId == null || !OnlineService.Enabled)
        {
            // No identity = Custom Game (never server-gated); disabled = old local behaviour.
            ClearActiveRun();
            done?.Invoke(new GateResult { Allowed = true });
            return;
        }
        if (!OnlineService.IsReady)
        {
            done?.Invoke(new GateResult { Offline = true });
            return;
        }
        // One grant in flight, ever: a second BeginRun during the window (close the modal,
        // reopen, tap PLAY again on a slow connection) would charge a second attempt and
        // whichever grant landed last would reload the scene over the other's run. Callers
        // treat "busy" as a quiet no-op; the pending grant still launches when it lands.
        if (_grantPending)
        {
            done?.Invoke(new GateResult { DeniedReason = "busy" });
            return;
        }

        ClearActiveRun();
        _grantPending = true;

        string board = boosted ? "boosted" : "clean";
        string body = $"{{\"p_level_id\":\"{SupabaseHttp.JsonEscape(levelId)}\"," +
                      $"\"p_board\":\"{board}\",\"p_loadout\":{loadoutJson ?? "null"}}}";

        OnlineService.RpcObject<StartRunDto>("start_run", body,
            dto =>
            {
                _grantPending = false;
                // start_run restates the full meter verdict - apply it whole so a denial
                // renders the OUT OF ATTEMPTS state even when the cache was stale.
                AttemptsSync.ApplyServer(dto.attempts, dto.seconds_until_next,
                    dto.premium, dto.meter_charged);
                if (dto.allowed && !string.IsNullOrEmpty(dto.run_id))
                {
                    ActiveRunId = dto.run_id;
                    ActiveRunServerBacked = true;
                    done?.Invoke(new GateResult { Allowed = true });
                }
                else
                {
                    done?.Invoke(new GateResult
                    {
                        DeniedReason = string.IsNullOrEmpty(dto.reason) ? "denied" : dto.reason,
                        SecondsUntilNext = dto.seconds_until_next,
                    });
                }
            },
            err =>
            {
                _grantPending = false;
                done?.Invoke(new GateResult { Offline = true });
            });
    }

    /// <summary>Report the active run's outcome (win refund + score submission happen
    /// server-side in one exchange). Fire-and-forget: network failures queue to disk and
    /// retry; the run_id makes retries idempotent.</summary>
    public static void ReportFinish(bool won, int score, float height)
    {
        if (!ActiveRunServerBacked || string.IsNullOrEmpty(ActiveRunId)) return;

        PendingFinish finish = new PendingFinish
        {
            runId = ActiveRunId,
            won = won,
            score = score,
            height = height,
        };
        ClearActiveRun();

        // Overflow drops the INCOMING report, not queued ones: the queue only fills when
        // every send has failed for ~100 runs straight, and the old entries hold refunds
        // the player already earned.
        if (Queue.items.Count >= MaxQueuedFinishes)
        {
            Debug.LogWarning("[Online] Finish queue full; dropping newest report.");
            return;
        }
        Queue.items.Add(finish);
        SaveQueue();
        TrySend(finish);
    }

    /// <summary>Drop the active-run view (quit to menu without finishing keeps the attempt
    /// spent - matching the loss-only rule; the run row stays open server-side).</summary>
    public static void ClearActiveRun()
    {
        ActiveRunId = null;
        ActiveRunServerBacked = false;
    }

    /// <summary>Resend queued finish reports (called on Ready and on app-focus regain).</summary>
    public static void RetryPendingFinishes()
    {
        if (!OnlineService.IsReady || Queue.items.Count == 0) return;
        // Snapshot: sends mutate the queue as verdicts come back.
        List<PendingFinish> snapshot = new List<PendingFinish>(Queue.items);
        foreach (PendingFinish finish in snapshot) TrySend(finish);
    }

    private static void TrySend(PendingFinish finish)
    {
        if (!OnlineService.IsReady || !_inFlight.Add(finish.runId)) return;

        string body = $"{{\"p_run_id\":\"{SupabaseHttp.JsonEscape(finish.runId)}\"," +
                      $"\"p_won\":{(finish.won ? "true" : "false")}," +
                      $"\"p_score\":{finish.score}," +
                      "\"p_height\":" + finish.height.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}";

        OnlineService.RpcObject<FinishRunDto>("finish_run", body,
            dto =>
            {
                _inFlight.Remove(finish.runId);
                // Any server verdict - accepted or rejected - is final; only network
                // failures stay queued.
                RemoveQueued(finish.runId);
                if (dto.accepted) AttemptsSync.ApplyFinishCounts(dto.attempts);
                else Debug.LogWarning($"[Online] finish_run rejected: {dto.reason}");
            },
            err => _inFlight.Remove(finish.runId));
    }

    private static PendingFinishFile Queue
    {
        get
        {
            if (_queue != null) return _queue;
            try
            {
                if (File.Exists(QueuePath))
                    _queue = JsonUtility.FromJson<PendingFinishFile>(File.ReadAllText(QueuePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Online] Could not read finish queue: {e.Message}");
            }
            return _queue ??= new PendingFinishFile();
        }
    }

    private static void RemoveQueued(string runId)
    {
        Queue.items.RemoveAll(p => p.runId == runId);
        SaveQueue();
    }

    private static void SaveQueue()
    {
        try
        {
            // Atomic: a kill mid-write must not truncate the queue - it holds refunds and
            // scores the player already earned (same pattern as SupabaseSession.Store).
            string tmp = QueuePath + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(Queue));
            if (File.Exists(QueuePath)) File.Replace(tmp, QueuePath, null);
            else File.Move(tmp, QueuePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Online] Finish queue save failed: {e.Message}");
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        ActiveRunId = null;
        ActiveRunServerBacked = false;
        _queue = null;          // reloaded from disk on demand; pending reports persist
        _inFlight.Clear();
        _grantPending = false;
    }
}
