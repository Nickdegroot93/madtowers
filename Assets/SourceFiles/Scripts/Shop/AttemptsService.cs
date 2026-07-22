using System;

/// <summary>
/// The attempts meter (SHOP.md §7) - loss-only lives for run STARTS, a completely separate
/// concept from in-run lives (RunState). Free players hold up to 5; starting a campaign run
/// spends one and WINNING refunds it, so only losses drain the meter. Regen is rolling
/// (+1 per 10 minutes); the premium IAP ("MadTowers Unlimited") removes it forever. (An
/// Hour Pass coin purchase existed briefly and was cut by Nick 2026-07-20.) Persisted through ProgressStore as a timestamped
/// last-writer-wins record (DATA.md settings pattern - the count is inherently non-monotonic).
///
/// Soft landing (SHOP.md §7.1): the whole system - meter, shop, supplies - is invisible and
/// inert until the player completes the first campaign chapter (MetaEnabled).
/// </summary>
public static class AttemptsService
{
    public const int MaxAttempts = 5;
    public const int RegenSeconds = 600;       // +1 per 10 min, full 0→5 in 50 min
    public const int AdRefillAmount = 2;

    /// <summary>Rewarded ads are designed (SHOP.md §7) but no ad SDK is integrated; the ad
    /// refill button stays hidden until this flips.</summary>
    public const bool AdsEnabled = false;

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
    /// supplies/shop (SHOP.md §7: premium and free players compete identically).</summary>
    public static bool MeterActive => MetaEnabled && !ProgressStore.IsPremium;

    /// <summary>Current attempts, with regen since the last persisted state applied. A never-
    /// initialized meter (fresh save) reads full.</summary>
    public static int Count
    {
        get
        {
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
            ProgressStore.GetAttemptsState(out int stored, out long updatedAt);
            if (stored < 0 || Count >= MaxAttempts) return TimeSpan.Zero;
            long sinceUpdate = NowUnix() - updatedAt;
            long intoCycle = sinceUpdate % RegenSeconds;
            return TimeSpan.FromSeconds(RegenSeconds - intoCycle);
        }
    }

    /// <summary>May a campaign run start right now? Always true while the meter doesn't
    /// apply (soft landing / premium).</summary>
    public static bool CanStartRun => !MeterActive || Count > 0;

    /// <summary>Charge one attempt for a starting campaign run. No-op when the meter doesn't
    /// apply. Returns whether an attempt was actually spent (the win-refund needs to know).</summary>
    public static bool SpendForRunStart()
    {
        if (!MeterActive) return false;
        int current = Count;
        if (current <= 0) return false;
        Persist(current - 1);
        return true;
    }

    /// <summary>Wins are free (loss-only model): give the spent attempt back.</summary>
    public static void RefundForWin()
    {
        if (!MeterActive) return;
        int current = Count;
        if (current >= MaxAttempts) return;
        Persist(current + 1);
    }

    /// <summary>Rewarded-ad refill (+2, capped). Callable only once AdsEnabled ships.</summary>
    public static void GrantAdRefill()
    {
        if (!AdsEnabled) return;
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
