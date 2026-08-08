using System;

/// <summary>
/// The attempts meter (SHOP.md §7) - loss-only lives for run STARTS, a completely separate
/// concept from in-run lives (RunState). Free players hold up to 5; starting a campaign run
/// spends one and WINNING refunds it, so only losses drain the meter. Regen is rolling
/// (+1 per 10 minutes); the premium IAP ("MadTowers Unlimited") removes it forever. (An
/// Hour Pass coin purchase existed briefly and was cut by Nick 2026-07-20.)
///
/// ONLINE (BACKEND.md §6): the server owns the meter - start_run charges, finish_run refunds,
/// regen is computed on server time. This class then reads the AttemptsSync display cache and
/// Spend/Refund become no-ops; the legacy wall-clock math below only drives the meter when
/// the whole online layer is disabled (SupabaseConfig.Enabled = false).
///
/// Soft landing (SHOP.md §7.1): the whole system - meter, shop, supplies - is invisible and
/// inert until the player completes the first campaign chapter (MetaEnabled). Online, the
/// charging verdict is the server's (meter_charged); the local rule keeps gating visibility.
/// </summary>
public static class AttemptsService
{
    public const int MaxAttempts = 5;
    public const int RegenSeconds = 600;       // +1 per 10 min, full 0→5 in 50 min
    public const int AdRefillAmount = 2;

    /// <summary>The server has not told us the refill budget yet. Distinct from zero: an
    /// unknown budget must not hide the button (that would blank the affordance for anyone
    /// playing against an older server), whereas a known zero must.</summary>
    public const int GrantsUnknown = -1;

    /// <summary>May the "watch an ad → +2" button show? Requires a showable ad (no provider
    /// installed = no ad SDK yet = hidden), no denied grant this session, and budget left in
    /// the server's rolling 3/day window. The budget arrives on the boot get_profile and is
    /// refreshed by every grant_ad_refill reply, so the CAP hides the button BEFORE a wasted
    /// watch - being denied AFTER sitting through a full video reads as the game taking
    /// something back (SHOP.md §7.3 item 6).</summary>
    public static bool AdRefillAvailable =>
        RewardedAds.Available && !_adRefillDenied && AdGrantsRemaining != 0;

    /// <summary>Rewarded refills left in the server's rolling 24h window, or
    /// <see cref="GrantsUnknown"/> before the first server answer.</summary>
    public static int AdGrantsRemaining { get; private set; } = GrantsUnknown;

    /// <summary>Record the server's remaining-refill count. Ignores the unknown sentinel so
    /// a reply that omits the field never erases a figure we already have.</summary>
    public static void ApplyGrantsRemaining(int remaining)
    {
        if (remaining == GrantsUnknown) return;
        AdGrantsRemaining = Math.Max(0, remaining);
    }

    private static bool _adRefillDenied;

    /// <summary>Online layer enabled but not (yet) connected: campaign runs cannot start
    /// (BACKEND.md §5.1) and the UI should say OFFLINE rather than show meter numbers.</summary>
    public static bool OnlineBlocked => OnlineService.Enabled && !OnlineService.IsReady;

    /// <summary>The server's answer is on hand and is the display source of truth.</summary>
    private static bool UseServer => OnlineService.Enabled && AttemptsSync.HasServerState;

    /// <summary>Are the meta systems (attempts, supplies, shop) live for this player?
    /// Gated on completing the first campaign chapter (§7.1); the dev unlock-all define
    /// also opens it so the shop is testable without replaying chapter 1.</summary>
    public static bool MetaEnabled
    {
        get
        {
            if (Campaign.UnlockAllForTesting) return true;
            ChapterDefinition[] chapters = Campaign.LoadChaptersInOrder();
            return chapters.Length > 0 && Campaign.IsChapterCompleted(chapters[0]);
        }
    }

    /// <summary>Does the attempts METER apply? Premium removes the meter but never the
    /// supplies/shop (SHOP.md §7: premium and free players compete identically). Online the
    /// verdict is the server's; while online is enabled but unanswered the meter stays
    /// hidden (the top bar shows OFFLINE instead of numbers we can't vouch for).</summary>
    public static bool MeterActive
    {
        get
        {
            if (OnlineService.Enabled) return UseServer && AttemptsSync.MeterCharged && !AttemptsSync.Premium;
            return MetaEnabled && !ProgressStore.IsPremium;
        }
    }

    /// <summary>Current attempts. Online: the server count projected forward locally
    /// (AttemptsSync). Offline fallback: regen since the last persisted state. A never-
    /// initialized meter (fresh save) reads full.</summary>
    public static int Count
    {
        get
        {
            if (UseServer) return AttemptsSync.Count;
            ProgressStore.GetAttemptsState(out int stored, out long updatedAt);
            if (stored < 0) return MaxAttempts;
            if (stored >= MaxAttempts) return stored;
            long regenerated = (NowUnix() - updatedAt) / RegenSeconds;
            return (int)Math.Min(MaxAttempts, stored + Math.Max(0, regenerated));
        }
    }

    /// <summary>Time until the next +1, or zero when full/uninitialized.</summary>
    public static TimeSpan NextRegenIn
    {
        get
        {
            if (UseServer) return AttemptsSync.NextRegenIn;
            ProgressStore.GetAttemptsState(out int stored, out long updatedAt);
            if (stored < 0 || Count >= MaxAttempts) return TimeSpan.Zero;
            long sinceUpdate = NowUnix() - updatedAt;
            long intoCycle = sinceUpdate % RegenSeconds;
            return TimeSpan.FromSeconds(RegenSeconds - intoCycle);
        }
    }

    /// <summary>May a campaign run start right now, as far as the METER knows? Always true
    /// while the meter doesn't apply (soft landing / premium). Online this is advisory - the
    /// real gate is the start_run grant (RunGate.BeginRun); OnlineBlocked is checked
    /// separately by the UI because it blocks for a different reason with different copy.</summary>
    public static bool CanStartRun => !MeterActive || Count > 0;

    /// <summary>Charge one attempt for a starting campaign run. Online: no-op - the server
    /// charged at start_run (RunGate). Offline fallback: local spend. Returns whether an
    /// attempt was actually spent (the local win-refund needs to know).</summary>
    public static bool SpendForRunStart()
    {
        if (OnlineService.Enabled) return false;
        if (!MeterActive) return false;
        int current = Count;
        if (current <= 0) return false;
        Persist(current - 1);
        return true;
    }

    /// <summary>Wins are free (loss-only model): give the spent attempt back. Online the
    /// refund happens server-side in finish_run.</summary>
    public static void RefundForWin()
    {
        if (OnlineService.Enabled) return;
        if (!MeterActive) return;
        int current = Count;
        if (current >= MaxAttempts) return;
        Persist(current + 1);
    }

    /// <summary>Rewarded-ad refill (+2, capped), called AFTER the ad reports watched-to-end.
    /// Online the grant is the server's (grant_ad_refill RPC - the client never grants itself
    /// attempts; SSV replaces this claim path before launch, BACKEND.md §6.4); offline it is
    /// the local wall-clock meter. onDone(true) = the meter moved (AttemptsSync.Changed /
    /// the local count carries the new value).</summary>
    public static void RequestAdRefill(Action<bool> onDone)
    {
        if (OnlineService.Enabled)
        {
            OnlineService.RpcObject<AdRefillDto>("grant_ad_refill", "{}",
                dto =>
                {
                    // Every reply carries the budget, success or not - the success path
                    // reports it AFTER the ledger insert, so spending the last grant hides
                    // the button immediately rather than one watch late.
                    ApplyGrantsRemaining(dto.grants_remaining);
                    if (dto.ok)
                    {
                        AttemptsSync.ApplyServer(dto.attempts, dto.seconds_until_next,
                            dto.premium, AttemptsSync.MeterCharged);
                    }
                    else
                    {
                        // rate_limited / premium don't heal within this session - stop
                        // offering. attempts_full DOES heal (the next spent attempt makes
                        // room): the top-bar "+" can race regen to a full meter during the
                        // ad itself (tap at 4/5, regen ticks mid-video - review 2026-08-01),
                        // and that near-miss must not kill the refill for the whole session.
                        if (dto.reason != "attempts_full") _adRefillDenied = true;
                        UnityEngine.Debug.Log($"[Ads] grant_ad_refill denied: {dto.reason}");
                    }
                    onDone?.Invoke(dto.ok);
                },
                err =>
                {
                    UnityEngine.Debug.LogWarning($"[Ads] grant_ad_refill failed: {err}");
                    onDone?.Invoke(false);
                });
            return;
        }

        if (!MeterActive)
        {
            onDone?.Invoke(false);
            return;
        }
        Persist(Math.Min(MaxAttempts, Count + AdRefillAmount));
        onDone?.Invoke(true);
    }

    /// <summary>grant_ad_refill reply (JSON key names are the server contract - never rename).</summary>
    [Serializable]
    private class AdRefillDto
    {
        public bool ok;
        public string reason;
        public int attempts;
        public bool premium;
        public int seconds_until_next;
        // Sentinel default: absent field must not read as "budget exhausted".
        public int grants_remaining = GrantsUnknown;
    }

    // Persist "count as of now" - regen derives from this timestamp. A naive NowUnix() stamp
    // would RESET the 10-minute cycle on every spend/refund, starving an actively-losing
    // player of regen entirely; carry the partial cycle over whenever one was in progress
    // (the old state was initialized and below max - a full meter accumulates nothing).
    private static void Persist(int count)
    {
        ProgressStore.GetAttemptsState(out int oldCount, out long oldUpdatedAt);
        long stamp = NowUnix();
        if (oldCount >= 0 && oldCount < MaxAttempts && count < MaxAttempts
            && oldUpdatedAt > 0 && stamp > oldUpdatedAt)
        {
            stamp -= (stamp - oldUpdatedAt) % RegenSeconds;
        }
        ProgressStore.SetAttemptsState(count, stamp);
    }

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        _adRefillDenied = false;
        AdGrantsRemaining = GrantsUnknown;   // re-learned from the next get_profile
    }
}
