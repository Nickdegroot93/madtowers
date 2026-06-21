/// <summary>
/// Holds the lock->spawn chain during a level's opening camera pan: the camera starts offset to
/// the side and glides to the framing center to reveal the scenery, and the first piece must NOT
/// drop until that pan finishes. TowerCameraController is the sole authority - it sets this at
/// Awake (true if it will pan, false otherwise) and releases it when the pan completes, then kicks
/// the spawn (the chain is event-driven and never retries a gated spawn on its own).
/// Spawner.SpawnNextBlock honours it exactly like the win-verification and wave-reveal holds.
/// </summary>
public static class CameraIntroGate
{
    public static bool IsPlaying { get; private set; }

    public static void Begin() => IsPlaying = true;

    public static void End() => IsPlaying = false;

    public static void Reset() => IsPlaying = false;
}
