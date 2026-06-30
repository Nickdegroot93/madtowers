using UnityEngine;

/// <summary>
/// Applies the user's graphics settings to global engine state. The frame-rate cap is true global
/// state (Application.targetFrameRate), so it's set here at startup and whenever settings change.
/// The other graphics toggles are enforced at their render chokepoints instead (see GRAPHICS.md):
/// post-processing in PostFxController, screen shake in TowerCameraController.Impact, decorative
/// prefab VFX in Vfx.Spawn — so current and future effects respect them without per-site checks.
/// (Named *Applier to avoid colliding with UnityEngine.Rendering.GraphicsSettings.)
/// </summary>
public static class GraphicsSettingsApplier
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // Idempotent: this static survives fast-enter-playmode, so re-bind rather than stack.
        SettingsService.Changed -= ApplyFrameRate;
        SettingsService.Changed += ApplyFrameRate;
        ApplyFrameRate();
    }

    private static void ApplyFrameRate()
    {
        // vSync must be off or targetFrameRate is ignored (the cap becomes the display refresh).
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = SettingsService.TargetFrameRate;
    }
}
