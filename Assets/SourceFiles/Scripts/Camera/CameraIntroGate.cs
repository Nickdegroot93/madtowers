/// <summary>
/// Holds the lock->spawn chain during a level's opening camera pan: the camera starts offset to
/// the side and glides to the framing center to reveal the scenery, and the first piece must NOT
/// drop until that pan finishes. TowerCameraController is the sole authority - it sets this at
/// Awake (true if it will pan, false otherwise) and releases it when the pan completes.
/// GameManager turns this into a spawn hold and raises spawn availability when it clears.
/// </summary>
public static class CameraIntroGate
{
    private static readonly object SpawnHoldOwner = new object();

    public static bool IsPlaying { get; private set; }

    public static void Begin()
    {
        IsPlaying = true;
        SyncToGameManager(GameManager.Instance);
    }

    public static void End()
    {
        bool wasPlaying = IsPlaying;
        IsPlaying = false;
        SyncToGameManager(GameManager.Instance);
        if (wasPlaying && GameManager.Instance != null)
        {
            GameManager.Instance.RepublishSpawnAvailability();
        }
    }

    public static void Reset()
    {
        IsPlaying = false;
        SyncToGameManager(GameManager.Instance);
    }

    public static void SyncToGameManager(GameManager gameManager)
    {
        if (gameManager == null) return;

        gameManager.SetSpawnSuspended(SpawnHoldOwner, IsPlaying);
        if (IsPlaying)
        {
            gameManager.SetPhase(GamePhase.Intro);
        }
        else if (gameManager.CurrentPhase == GamePhase.Intro)
        {
            gameManager.SetPhase(GamePhase.Playing);
        }
    }
}
