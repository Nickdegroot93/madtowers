/// <summary>
/// Holds the lock->spawn chain across a puzzle-mode wave transition: when a wave clears the
/// laser line glides up and the next band of support islands pops into existence, and the next
/// piece must NOT drop until that reveal has finished (otherwise a fast-dropped piece can hit
/// an island that materializes mid-fall). HeightLimitWavesModifier raises the hold the moment a
/// wave clears and releases it once the line has settled and every revealed island has popped;
/// GameManager turns this into a spawn hold and raises spawn availability when it clears.
/// GameManager.Awake resets it every level load so a hold can never leak between levels.
/// </summary>
public static class WaveRevealGate
{
    private static readonly object SpawnHoldOwner = new object();

    public static bool IsHoldingSpawn { get; private set; }

    public static void Hold()
    {
        IsHoldingSpawn = true;
        SyncToGameManager(GameManager.Instance);
    }

    public static void Release()
    {
        bool wasHolding = IsHoldingSpawn;
        IsHoldingSpawn = false;
        SyncToGameManager(GameManager.Instance);
        if (wasHolding && GameManager.Instance != null)
        {
            GameManager.Instance.RepublishSpawnAvailability();
        }
    }

    public static void Reset()
    {
        IsHoldingSpawn = false;
        SyncToGameManager(GameManager.Instance);
    }

    public static void SyncToGameManager(GameManager gameManager)
    {
        if (gameManager == null) return;

        gameManager.SetSpawnSuspended(SpawnHoldOwner, IsHoldingSpawn);
    }
}
