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

    /// <summary>Rewarded ads are designed (SHOP.md §7) but no ad SDK is integrated; the ad
    /// refill button stays hidden until this flips.</summary>
    public const bool AdsEnabled = false;

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

    /// <summary>Rewarded-ad refill (+2, capped). Callable only once AdsEnabled ships. Online
    /// this must become the server's grant_ad_refill / SSV path (BACKEND.md §6.4) - the
    /// client never grants itself attempts.</summary>
    public static void GrantAdRefill()
    {
        if (!AdsEnabled) return;
        if (OnlineService.Enabled) return; // server-granted only (BACKEND.md §6.4)
        Persist(Math.Min(MaxAttempts, Count + AdRefillAmount));
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
}
