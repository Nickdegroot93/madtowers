/// <summary>
/// The level's current build ceiling in world Y - the settled height-limit laser line.
/// HeightLimitWavesModifier publishes it; StaticSupportIslandManager caps island
/// generation below it (so islands only ever exist under the line, and a rising line
/// reveals the next band). PositiveInfinity = no limit (endless levels, cleared waves).
/// GameManager.Awake resets it every level load so a limit can never leak between levels.
/// </summary>
public static class TowerHeightLimit
{
    public static float CeilingY { get; private set; } = float.PositiveInfinity;

    public static void Set(float worldY) => CeilingY = worldY;

    public static void Reset() => CeilingY = float.PositiveInfinity;
}
