/// <summary>
/// Holds the lock->spawn chain across a puzzle-mode wave transition: when a wave clears the
/// laser line glides up and the next band of support islands pops into existence, and the next
/// piece must NOT drop until that reveal has finished (otherwise a fast-dropped piece can hit
/// an island that materializes mid-fall). HeightLimitWavesModifier raises the hold the moment a
/// wave clears and releases it once the line has settled and every revealed island has popped;
/// Spawner.SpawnNextBlock honours it exactly like the win-verification hold.
/// GameManager.Awake resets it every level load so a hold can never leak between levels.
/// </summary>
public static class WaveRevealGate
{
    public static bool IsHoldingSpawn { get; private set; }

    public static void Hold() => IsHoldingSpawn = true;

    public static void Release() => IsHoldingSpawn = false;

    public static void Reset() => IsHoldingSpawn = false;
}
