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
        public int xp_gained;
        public long xp_total;
    }

    [Serializable]
    private class PendingFinish
    {
        public string runId;
        public bool won;
        public int score;
        public float height;
        public float progress;   // unclamped goal progress for the XP award (XP.md); pre-XP queue files default to 0
        // Loss telemetry (runs.fail_cause): 'lives'/'flood'/'timeout'/'abandon'/'other',
        // null on wins and in pre-telemetry queue files (the server nulls unknowns too).
        public string failCause;
        // Post-victory Keep Playing report: improve_run_score instead of finish_run.
        // Older queue files default to false, i.e. a plain finish - correct for them.
        public bool improve;
    }

    [Serializable]
    private class PendingFinishFile
    {
        public List<PendingFinish> items = new List<PendingFinish>();
        // The armed post-victory window lives on DISK with the queue. "Win, keep playing
        // for two minutes, OS kills the backgrounded app" is routine on mobile, and a
        // RAM-only window loses the whole post-victory score when it happens.
        public string improvableRunId;
        public string improvableLevelId;
    }

    private const int MaxQueuedFinishes = 100;

    private static PendingFinishFile _queue;
    private static readonly HashSet<string> _inFlight = new HashSet<string>();
    private static bool _grantPending;

    /// <summary>The last WON run, still open to a post-victory score improvement, and the
    /// level it belonged to. BOTH are checked at report time: clearing alone is not enough,
    /// because the id outlives paths that never start a server run at all (Custom Game,
    /// online disabled, a denied grant, premium-offline). Without the level check, winning
    /// level 5 online and later toppling out of an UNRANKED level 12 would post level 12's
    /// score against level 5's run_id - the server would accept it, since the run really is
    /// finished, won and inside 24h (review 2026-08-08). Persisted with the queue so an
    /// app kill during Keep Playing does not throw the session's score away.</summary>
    private static string _improvableRunId => Queue.improvableRunId;
    private static string _improvableLevelId => Queue.improvableLevelId;

    private static void ArmImprovableRun(string runId, string levelId)
    {
        // JsonUtility round-trips a null string as "", so null and empty are the same
        // state here; comparing them naively would re-save the queue on every clear.
        static bool Same(string a, string b) => (a ?? string.Empty) == (b ?? string.Empty);
        if (Same(Queue.improvableRunId, runId) && Same(Queue.improvableLevelId, levelId)) return;
        Queue.improvableRunId = runId;
        Queue.improvableLevelId = levelId;
        SaveQueue();
    }

    /// <summary>The level the active server-backed run belongs to (carried into
    /// _improvableLevelId when that run is won).</summary>
    private static string _activeLevelId;

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
        // One grant in flight, ever: a second BeginRun during the window (close the modal,
        // reopen, tap PLAY again on a slow connection) would charge a second attempt and
        // whichever grant landed last would reload the scene over the other's run. Callers
        // treat "busy" as a quiet no-op; the pending grant still launches when it lands.
        // Checked BEFORE the offline fallback: the premium local-allow below is synchronous,
        // and letting it race a pending grant would double-launch (review 2026-07-30).
        if (_grantPending)
        {
            done?.Invoke(new GateResult { DeniedReason = "busy" });
            return;
        }
        if (!OnlineService.IsReady)
        {
            // Premium owns offline play (SHOP.md §7, Nick 2026-07-30): the run starts
            // locally and UNRANKED - no server run_id means ReportFinish no-ops, so the
            // score can never reach a leaderboard. Free players stay online-required.
            if (PremiumStore.IsPremium)
            {
                ClearActiveRun();
                done?.Invoke(new GateResult { Allowed = true });
                return;
            }
            done?.Invoke(new GateResult { Offline = true });
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
                    _activeLevelId = levelId;
                    // A new run closes the previous one's improvement window, so a late
                    // Keep Playing report can never be attributed to the wrong run.
                    ClearImprovableRun();
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
                // No premium local-allow HERE, deliberately (review 2026-07-30): callers
                // launch on Allowed even after the modal closed - correct for a charged
                // grant, a surprise scene-load for an uncharged local run that can arrive
                // ~10s after the player walked away. Premium answers Offline like everyone;
                // the NEXT tap lands in the synchronous offline branch above and plays.
                done?.Invoke(new GateResult { Offline = true });
            });
    }

    /// <summary>Report the active run's outcome (win refund + score submission + the XP award
    /// happen server-side in one exchange). <paramref name="progress"/> is the run's peak
    /// unclamped goal progress (1 = at target; XP.md); <paramref name="cause"/> is loss
    /// telemetry (runs.fail_cause - the server nulls it on wins). Fire-and-forget: network
    /// failures queue to disk and retry; the run_id makes retries idempotent.</summary>
    public static void ReportFinish(bool won, int score, float height, float progress,
        RunEndCause cause = RunEndCause.Other)
    {
        if (!ActiveRunServerBacked || string.IsNullOrEmpty(ActiveRunId)) return;

        PendingFinish finish = new PendingFinish
        {
            runId = ActiveRunId,
            won = won,
            score = score,
            height = height,
            progress = Mathf.Clamp(progress, 0f, 2f),
            // Lowercase names are the server's check-constraint vocabulary; wins carry
            // none (pre-telemetry queue files deserialize to null - also fine).
            failCause = won ? null : cause.ToString().ToLowerInvariant(),
        };
        // A WON run stays improvable: the victory banks the refund and XP immediately
        // (XP.md win timing), but the player is then invited to Keep Playing, and every
        // point earned after this used to be dropped on the floor. Captured BEFORE the
        // clear and re-armed after it, because ClearActiveRun now closes this window too.
        string wonRunId = won ? ActiveRunId : null;
        string wonLevelId = won ? _activeLevelId : null;
        ClearActiveRun();

        // Overflow drops the INCOMING report, not queued ones: the queue only fills when
        // every send has failed for ~100 runs straight, and the old entries hold refunds
        // the player already earned. The improvement window is armed only if the finish
        // was actually queued - improving a run the server never finished just earns a
        // not_finished rejection (review 2026-08-08).
        if (Queue.items.Count >= MaxQueuedFinishes)
        {
            Debug.LogWarning("[Online] Finish queue full; dropping newest report.");
            return;
        }
        Queue.items.Add(finish);
        ArmImprovableRun(wonRunId, wonLevelId);
        SaveQueue();
        TrySend(finish);
    }

    /// <summary>
    /// Report the score of a post-victory "Keep Playing" session. The win itself was
    /// already banked at verification; this only raises the score/height/progress, and
    /// the server pays the XP difference above what the run was already paid for.
    ///
    /// If the original finish is STILL QUEUED (won while offline, then kept playing), the
    /// two collapse into one report rather than becoming two queue entries: the queue is
    /// keyed by run_id, so a second entry would be clobbered when the first resolved.
    /// </summary>
    public static void ReportScoreImprovement(string levelId, int score, float height, float progress)
    {
        if (string.IsNullOrEmpty(_improvableRunId)) return;
        // The window belongs to exactly one level. A mismatch means the id outlived its
        // run, and posting here would write this level's score onto that one's board.
        if (!string.IsNullOrEmpty(_improvableLevelId) && levelId != _improvableLevelId)
        {
            Debug.LogWarning("[Online] Score improvement ignored: level mismatch.");
            return;
        }

        string runId = _improvableRunId;
        float clamped = Mathf.Clamp(progress, 0f, 2f);

        // Collapse into a still-queued finish (won offline, then kept playing) so one
        // report carries the final numbers - but ONLY while it is off the wire. Mutating
        // an in-flight entry loses the new values: the verdict for the older request
        // deletes the row before the improved figures are ever sent.
        for (int i = 0; i < Queue.items.Count; i++)
        {
            PendingFinish queued = Queue.items[i];
            if (queued.runId != runId || queued.improve) continue;
            if (_inFlight.Contains(Key(queued))) break;   // on the wire: queue separately

            // Never let a late report lower an earlier one - the server enforces this too,
            // but an un-sent queue entry has no server to defend it.
            queued.score = Mathf.Max(queued.score, score);
            queued.height = Mathf.Max(queued.height, height);
            queued.progress = Mathf.Max(queued.progress, clamped);
            ClearImprovableRun();
            SaveQueue();
            TrySend(queued);
            return;
        }

        // Capacity checked BEFORE the window is closed, so the armed window survives and a
        // later report for the same run can still land. Nothing re-drives it on its own,
        // though: if the queue is still full at the next ClearActiveRun the score is gone.
        // That needs ~100 consecutive failed sends to reach, and the queue holds refunds
        // the player already earned, so the old entries win the tie.
        if (Queue.items.Count >= MaxQueuedFinishes)
        {
            Debug.LogWarning("[Online] Finish queue full; score improvement not queued.");
            return;
        }

        PendingFinish improvement = new PendingFinish
        {
            runId = runId,
            won = true,                          // improvements only exist for won runs
            score = score,
            height = height,
            progress = clamped,
            improve = true,
        };
        ClearImprovableRun();
        Queue.items.Add(improvement);
        SaveQueue();
        TrySend(improvement);
    }

    private static void ClearImprovableRun() => ArmImprovableRun(null, null);

    /// <summary>Queue/in-flight identity. A finish and an improvement for the SAME run are
    /// different reports and must not share a key, or resolving one deletes the other.</summary>
    private static string Key(PendingFinish p) => p.improve ? p.runId + "|improve" : p.runId;

    /// <summary>Drop the active-run view (quit to menu without finishing keeps the attempt
    /// spent - matching the loss-only rule; the run row stays open server-side).</summary>
    public static void ClearActiveRun()
    {
        ActiveRunId = null;
        ActiveRunServerBacked = false;
        _activeLevelId = null;
        // Also closes the post-victory window. Returning to the menu from the victory card
        // never routes through ReportAbandonedRun, so without this the id would survive
        // into whatever the player launched next (review 2026-08-08).
        ClearImprovableRun();
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
        if (!OnlineService.IsReady) return;
        // An improvement is meaningless until its finish has landed: the server answers
        // not_finished and the report would be dropped. Wait for the finish to clear.
        if (finish.improve && Queue.items.Exists(p => p.runId == finish.runId && !p.improve)) return;
        if (!_inFlight.Add(Key(finish))) return;

        string rpc = finish.improve ? "improve_run_score" : "finish_run";
        // p_fail_cause exists only on finish_run - improve_run_score would reject the
        // unknown parameter (PostgREST dispatches on the full named-argument set).
        string failCause = finish.improve ? "" :
            ",\"p_fail_cause\":" + (string.IsNullOrEmpty(finish.failCause)
                ? "null" : $"\"{SupabaseHttp.JsonEscape(finish.failCause)}\"");
        string body = $"{{\"p_run_id\":\"{SupabaseHttp.JsonEscape(finish.runId)}\"," +
                      (finish.improve ? "" : $"\"p_won\":{(finish.won ? "true" : "false")},") +
                      $"\"p_score\":{finish.score}," +
                      "\"p_height\":" + finish.height.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                      "\"p_progress\":" + finish.progress.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                      failCause + "}";

        OnlineService.RpcObject<FinishRunDto>(rpc, body,
            dto =>
            {
                _inFlight.Remove(Key(finish));
                // Any server verdict - accepted or rejected - is final; only network
                // failures stay queued.
                RemoveQueued(finish);
                if (dto.accepted)
                {
                    // improve_run_score never touches the meter (the refund happened at the
                    // win), and its reply carries no attempts field - applying the DTO's
                    // default 0 here would wipe the player's attempts to zero.
                    if (!finish.improve) AttemptsSync.ApplyFinishCounts(dto.attempts);
                    XpSystem.ApplyServerTotal(dto.xp_total);
                }
                else if (!finish.improve && finish.won && dto.reason == "already_finished")
                {
                    // The finish DID commit; only its reply was lost, so this retry carries
                    // values the server has never seen. Dropping it would silently bin the
                    // post-victory score - re-send it as what it actually is now.
                    // Gated on finish.won: a LOST run has no second act, and converting one
                    // would fabricate a "won" report the server only answers not_won.
                    //
                    // MERGE, never add: a real improvement may already be queued for this
                    // run (armed while the finish was on the wire). Both share Key(), so a
                    // blind Add would let this weaker copy's verdict delete the real one -
                    // the player's 300 replaced by the victory's 100 (review 2026-08-08).
                    PendingFinish existing = Queue.items.Find(p => p.runId == finish.runId && p.improve);
                    if (existing != null)
                    {
                        existing.score = Mathf.Max(existing.score, finish.score);
                        existing.height = Mathf.Max(existing.height, finish.height);
                        existing.progress = Mathf.Max(existing.progress, finish.progress);
                        SaveQueue();
                        TrySend(existing);
                    }
                    else
                    {
                        PendingFinish retry = new PendingFinish
                        {
                            runId = finish.runId, won = true, score = finish.score,
                            height = finish.height, progress = finish.progress, improve = true,
                        };
                        Queue.items.Add(retry);
                        SaveQueue();
                        TrySend(retry);
                    }
                }
                else Debug.LogWarning($"[Online] {rpc} rejected: {dto.reason}");

                // The finish is gone from the queue; any improvement parked behind it can
                // go now.
                if (!finish.improve) RetryPendingFinishes();
            },
            err => _inFlight.Remove(Key(finish)));
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

    /// <summary>Remove exactly the report that was answered. Matching on run id alone would
    /// delete a queued improvement when its finish resolved, losing the Keep Playing score.</summary>
    private static void RemoveQueued(PendingFinish finish)
    {
        Queue.items.RemoveAll(p => p.runId == finish.runId && p.improve == finish.improve);
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
        _activeLevelId = null;
        // The improvement window is part of the queue file now, so it reloads from disk
        // with it - deliberately: an app kill mid-Keep-Playing must not lose the score,
        // and the level check at report time is what prevents misattribution.
        _queue = null;          // reloaded from disk on demand; pending reports persist
        _inFlight.Clear();
        _grantPending = false;
    }
}
