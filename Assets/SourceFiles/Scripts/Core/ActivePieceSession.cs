using UnityEngine;

/// <summary>
/// Shared registry of "active-piece sessions" - the runtime drivers (Fission, Overdraw, Zap,
/// Magma melt, ...) that seize the active falling piece and feed their own sequence into the
/// controlled-piece loop. While ANY of them owns the field, another active-piece consumable or a
/// Pocket Cache hold fired into it would corrupt that sequence, so those actions are gated off.
///
/// Each session calls <see cref="Enter"/> when it begins and <see cref="Exit"/> when it finishes,
/// symmetric with its own static <c>IsActive</c> flag. The single <see cref="AnyActive"/> query
/// replaces the hand-maintained list every gate used to enumerate (the old
/// <c>!FissionSession.IsActive &amp;&amp; !OverdrawSession.IsActive &amp;&amp; ...</c> chain):
/// adding a new session no longer means editing AbilityRuntime / HoldCache. A session keeps its
/// own <c>IsActive</c> only for its own re-entrancy guard and any session-specific checks.
/// </summary>
public static class ActivePieceSession
{
    private static int _active;

    /// <summary>True while one or more active-piece sessions own the falling piece.</summary>
    public static bool AnyActive => _active > 0;

    /// <summary>Register a session as it starts (pair with exactly one <see cref="Exit"/>).</summary>
    public static void Enter() => _active++;

    /// <summary>Deregister a session as it finishes. Idempotent-safe at zero.</summary>
    public static void Exit() { if (_active > 0) _active--; }

    // Statics survive scene loads in a player; reset so a session torn down by a reload (rather
    // than a normal Finish) can never leave the gate stuck closed in the next run. Mirrors each
    // session's own IsActive reset.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState() => _active = 0;
}
