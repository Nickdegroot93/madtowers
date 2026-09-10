/// <summary>Pure startup policy; connection still in flight is never proof of being offline.</summary>
public static class StartupGate
{
    public const float OfflineGraceSeconds = 3f;
    public enum Decision { Waiting, Online, Offline, RetryRequired }

    public static Decision Evaluate(bool enabled, bool connected, bool meterLoaded,
        bool progressLoaded, bool premium, float elapsed)
    {
        if (!enabled || (connected && meterLoaded && progressLoaded)) return Decision.Online;
        if (elapsed < OfflineGraceSeconds) return Decision.Waiting;
        return premium ? Decision.Offline : Decision.RetryRequired;
    }
}
