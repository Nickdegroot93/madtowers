using UnityEngine;

/// <summary>
/// Shared game-feel helpers. The game's single "elastic" curve lives here so every
/// punch/pop (HUD slot tap today; future ability UI) settles with the same feel.
/// </summary>
public static class FxKit
{
    /// <summary>Damped spring settle: starts at 1+amplitude, oscillates to 1.</summary>
    public static float Elastic(float t, float amplitude, float damping, float frequency)
    {
        return 1f + amplitude * Mathf.Exp(-damping * t) * Mathf.Cos(frequency * t);
    }
}
