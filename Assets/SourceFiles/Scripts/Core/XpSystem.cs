using System;

/// <summary>
/// The account XP / level system (XP.md). PRESENTATION ONLY today (Nick 2026-08-01): the
/// level says "how much you've played" and nothing reads it for gameplay - it exists so
/// accounts carry a correct level when online play arrives.
///
/// ONLINE the award is SERVER-AUTHORITATIVE: finish_run computes it (constants mirrored in
/// supabase/migrations/20260801000003_xp.sql - keep in sync, including the boost-weekend
/// multiplier that only exists server-side) and returns the lifetime total, which lands in
/// the local save as a display cache. With the whole online layer disabled the same formula
/// accrues locally at 1x so offline editor work still levels. Premium-offline runs are
/// unranked and deliberately earn nothing - same rule as leaderboards, and it avoids a local
/// total the next server answer would visibly rewind.
/// </summary>
public static class XpSystem
{
    // ---- award per finished campaign run (loss, quit and win all report) ----
    public const int ParticipationXp = 10;  // any run whose goal progress moved at all
    public const int ProgressXpMax = 40;    // linear in progress toward the goal, capped at the target
    public const int OvershootXpMax = 10;   // progress past the goal, capped at 2x target. Only runs
                                            // that END past it pay this (replays, post-goal losses) -
                                            // a first win reports at verification, progress 1.0 (XP.md §1)
    public const int WinXp = 25;

    // ---- level curve: Need(L) = NeedBase + NeedSlope*(L-1), everyone starts at 1, no cap.
    // Linear increments (quadratic total) on purpose: exponential thresholds explode without
    // a level cap. 1->2 is one good run; 99->100 is ~30.
    public const int NeedBase = 60;
    public const int NeedSlope = 15;

    /// <summary>Fires when the lifetime total moves (local award or server verdict).</summary>
    public static event Action Changed;

    public static long TotalXp => ProgressStore.XpEarned;

    public static int Level => LevelForXp(TotalXp);

    /// <summary>Fill of the XP bar: progress through the current level, 0..1.</summary>
    public static float Fraction01
    {
        get
        {
            long total = TotalXp;
            int level = LevelForXp(total);
            long floor = XpToReachLevel(level);
            long need = XpToNextLevel(level);
            return need <= 0 ? 0f : UnityEngine.Mathf.Clamp01((total - floor) / (float)need);
        }
    }

    /// <summary>XP to go from <paramref name="level"/> to the next one.</summary>
    public static long XpToNextLevel(int level) => NeedBase + (long)NeedSlope * (Math.Max(1, level) - 1);

    /// <summary>Total lifetime XP at which <paramref name="level"/> begins (level 1 = 0).</summary>
    public static long XpToReachLevel(int level)
    {
        long n = Math.Max(1, level) - 1;
        return n * NeedBase + NeedSlope * n * (n - 1) / 2;
    }

    public static int LevelForXp(long xp)
    {
        if (xp <= 0) return 1;
        int level = 1;
        long remaining = xp;
        while (remaining >= XpToNextLevel(level))
        {
            remaining -= XpToNextLevel(level);
            level++;
        }
        return level;
    }

    /// <summary>The per-run award. <paramref name="progressRaw"/> is unclamped goal progress
    /// (1 = at the target); clamped to [0, 2] here AND server-side. Mirrors finish_run.</summary>
    public static int ComputeRunXp(float progressRaw, bool won)
    {
        float progress = UnityEngine.Mathf.Clamp(progressRaw, 0f, 2f);
        int xp = 0;
        if (progress > 0f)
        {
            xp = ParticipationXp
               + RoundHalfUp(ProgressXpMax * Math.Min(1f, progress))
               + RoundHalfUp(OvershootXpMax * Math.Max(0f, progress - 1f));
        }
        if (won) xp += WinXp;
        return xp;
    }

    // Postgres round(numeric) is half-away-from-zero; Mathf.RoundToInt is half-to-even.
    // The values are always >= 0 here, so floor(x + 0.5) keeps the mirror exact.
    private static int RoundHalfUp(float value) => (int)Math.Floor(value + 0.5f);

    /// <summary>Local accrual for online-layer-disabled play (no multiplier - boosts are an
    /// online event). Online builds never call this; the server pays via finish_run.</summary>
    public static void ReportLocalRun(float progressRaw, bool won)
    {
        int gained = ComputeRunXp(progressRaw, won);
        if (gained <= 0) return;
        ProgressStore.AddXp(gained);
        Changed?.Invoke();
    }

    /// <summary>A server XP verdict arrived (finish_run reply or the boot get_profile):
    /// cache it into the save so the top bar and offline sessions read the same number.</summary>
    public static void ApplyServerTotal(long total)
    {
        if (ProgressStore.SetXpFromServer(total)) Changed?.Invoke();
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode() => Changed = null;
}
