using UnityEngine;

/// <summary>
/// Rare passive: while on your LAST life (lives == 0 - the next lost block ends the run),
/// normal block descent is slowed by a FLAT offset - reductionFraction of the level's
/// INITIAL speed, NOT a multiplier. The gap stays a constant amount as the difficulty ramp
/// climbs (so it does not slow acceleration; its relative help fades as the game speeds up).
/// Expressed as the multiplier that achieves that flat offset at the current speed, which
/// also makes it normal-descent-only (fast drops stay full speed, like the other slows).
/// Turns off the moment a life is regained. Permanent (charges = 0), unique.
/// </summary>
[CreateAssetMenu(fileName = "LastStand", menuName = "Stacking/Abilities/Last Stand")]
public class LastStandAbility : PassiveAbility
{
    [Tooltip("Flat speed cut while on the last life, as a fraction of the level's INITIAL speed. 0.2 = the slowdown is always 20% of the starting speed (100% -> 80%, 200% -> 180%).")]
    [Range(0f, 0.9f)]
    [SerializeField] private float reductionFraction = 0.2f;

    public override float GetFallSpeedFactor(AbilityContext context, int stacks)
    {
        GameManager gm = context.GameManager;
        if (gm == null || gm.lives != 0) return 1f; // only on the last life

        float baseSpeed = gm.BaseFallSpeed;
        if (baseSpeed <= 0.01f) return 1f;

        // Flat offset = reductionFraction of the INITIAL speed, expressed as the multiplier
        // that yields it at the current ramped speed. Recomputed per block, so the gap stays
        // constant (and shrinks in relative terms) as the ramp climbs.
        float initial = context.Config != null ? context.Config.InitialFallSpeed : baseSpeed;
        float offset = reductionFraction * initial;
        return Mathf.Clamp(1f - offset / baseSpeed, 0.05f, 1f);
    }
}
