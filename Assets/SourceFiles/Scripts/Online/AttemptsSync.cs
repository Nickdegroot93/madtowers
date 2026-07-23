using System;
using UnityEngine;

/// <summary>
/// Display cache of the SERVER-owned attempts meter (BACKEND.md §6.3). The server is the
/// only authority - this holds its last answer and projects regen forward locally so the
/// top-bar chip and modal countdown stay live between exchanges. Updated by RunGate results
/// and Refresh(); it never writes anything.
/// </summary>
public static class AttemptsSync
{
    // Mirrors the server's values for display projection only (the server recomputes its own).
    private const int MaxAttempts = 5;
    private const int RegenSeconds = 600;
    private const float RefreshDebounceSeconds = 2f;

    private static int _count;
    private static int _secondsUntilNext;
    private static bool _premium;
    private static bool _meterCharged;
    private static float _fetchedAtRealtime;
    private static float _lastRefreshAt = float.NegativeInfinity;
    private static bool _refreshInFlight;

    [Serializable]
    private class AttemptsDto
    {
        public int count;
        public int seconds_until_next;
        public bool premium;
        public bool meter_charged;
    }

    /// <summary>False until the first server answer of this session.</summary>
    public static bool HasServerState { get; private set; }

    /// <summary>Server count projected forward by locally elapsed regen, capped.</summary>
    public static int Count
    {
        get
        {
            Project(out int count, out _);
            return count;
        }
    }

    public static TimeSpan NextRegenIn
    {
        get
        {
            Project(out _, out double remaining);
            return TimeSpan.FromSeconds(remaining);
        }
    }

    /// <summary>The server's soft-landing verdict: this user is past the chapter-1 exemption
    /// and runs charge the meter (BACKEND.md §6.1 / SHOP.md §7.1).</summary>
    public static bool MeterCharged => _meterCharged;

    public static bool Premium => _premium;

    public static event Action Changed;

    /// <summary>Ask the server for fresh meter state (rpc get_attempts), debounced.</summary>
    public static void Refresh()
    {
        if (!OnlineService.IsReady || _refreshInFlight) return;
        if (Time.realtimeSinceStartup - _lastRefreshAt < RefreshDebounceSeconds) return;
        _lastRefreshAt = Time.realtimeSinceStartup;
        _refreshInFlight = true;

        OnlineService.RpcObject<AttemptsDto>("get_attempts", "{}",
            dto =>
            {
                _refreshInFlight = false;
                ApplyServer(dto.count, dto.seconds_until_next, dto.premium, dto.meter_charged);
            },
            err => _refreshInFlight = false);
    }

    public static void ApplyServer(int count, int secondsUntilNext, bool premium, bool meterCharged)
    {
        _count = Mathf.Clamp(count, 0, MaxAttempts);
        _secondsUntilNext = Mathf.Max(0, secondsUntilNext);
        _premium = premium;
        _meterCharged = meterCharged;
        _fetchedAtRealtime = Time.realtimeSinceStartup;
        HasServerState = true;
        Changed?.Invoke();
    }

    /// <summary>Count-only update from a finish_run reply (it doesn't restate premium or the
    /// soft-landing verdict; a refund also never moves the regen deadline). Before the first
    /// full server answer there is no deadline to preserve - backfilling one from an empty
    /// projection hands out phantom regen - so take the count pessimistically and fetch the
    /// real verdict instead.</summary>
    internal static void ApplyFinishCounts(int count)
    {
        bool hadState = HasServerState;
        double remaining = RegenSeconds;
        if (hadState) Project(out _, out remaining);

        _count = Mathf.Clamp(count, 0, MaxAttempts);
        _secondsUntilNext = (int)remaining;
        _fetchedAtRealtime = Time.realtimeSinceStartup;
        HasServerState = true;
        Changed?.Invoke();

        if (!hadState)
        {
            _lastRefreshAt = float.NegativeInfinity; // bypass the debounce; we need the verdict
            Refresh();
        }
    }

    private static void Project(out int count, out double remaining)
    {
        if (!HasServerState)
        {
            count = 0;
            remaining = 0;
            return;
        }
        count = _count;
        if (count >= MaxAttempts)
        {
            remaining = 0;
            return;
        }
        double elapsed = Time.realtimeSinceStartup - _fetchedAtRealtime;
        remaining = _secondsUntilNext - elapsed;
        while (remaining <= 0 && count < MaxAttempts)
        {
            count++;
            remaining += RegenSeconds;
        }
        if (count >= MaxAttempts) remaining = 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        _count = 0;
        _secondsUntilNext = 0;
        _premium = false;
        _meterCharged = false;
        _fetchedAtRealtime = 0f;
        _lastRefreshAt = float.NegativeInfinity;
        _refreshInFlight = false;
        HasServerState = false;
        Changed = null;
    }
}
