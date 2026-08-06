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

    /// <summary>
    /// The HUD pills' one settle-pop (CoinHud, WaveHud): a small elastic scale pulse on the
    /// target that runs itself to completion. `age` is the caller's state - set it to 0 to
    /// trigger a pop; this advances it and parks it at +infinity when the pop has settled,
    /// after which calls are free no-ops.
    /// </summary>
    public static void TickSettlePop(Transform target, ref float age, float deltaTime)
    {
        if (float.IsPositiveInfinity(age)) return;

        age += deltaTime;
        if (age > 0.5f)
        {
            target.localScale = Vector3.one;
            age = float.PositiveInfinity;
            return;
        }
        target.localScale = Vector3.one * Elastic(age, 0.1f, 9f, 24f);
    }
}
