using UnityEngine;

/// <summary>
/// Slow Time (Instant): on pick it opens a timed window in which blocks fall slower. It applies a
/// <see cref="StatusEffectDefinition"/> of kind FallSpeedMultiplier, so the slow folds into the
/// per-block NORMAL-descent factor through AbilityRuntime - it never touches Time.timeScale. Fast
/// drops, physics, and the rest of the simulation stay at full speed; only a block's lazy
/// top-to-bottom descent is slowed (same fast-drop-immune rule as Slo-Mo / Air Brake). The status
/// asset owns the duration and the slow factor (15 s @ 0.5x today).
/// </summary>
[CreateAssetMenu(fileName = "SlowMotion", menuName = "Stacking/Abilities/Slow Motion")]
public class SlowMotionPowerUp : InstantAbility
{
    [Tooltip("FallSpeedMultiplier status applied on pick - owns the duration + slow factor. Descent-only.")]
    [SerializeField] private StatusEffectDefinition slowStatus;

    public override void Apply(AbilityContext context)
    {
        if (context == null || context.Status == null || slowStatus == null) return;
        context.Status.Apply(slowStatus);
    }
}
