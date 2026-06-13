using UnityEngine;

/// <summary>
/// Stackable passive that scales the spawn-time fall speed down by a flat fraction per
/// stack. It composes as a multiplier in GameManager's fall-speed getter (the contract
/// way - never mutates the difficulty ramp, which would be unrecoverable once the ramp
/// writes again), so it eases the WHOLE speed curve: a given speed is reached at a later
/// block and the effective top speed comes down too. Pull-style, so it vanishes cleanly
/// with the ability.
/// </summary>
[CreateAssetMenu(fileName = "FallSpeedReduction", menuName = "Stacking/Abilities/Fall Speed Reduction")]
public class FallSpeedReductionPowerUp : PassiveAbility
{
    [Tooltip("Fraction the fall speed is reduced per stack. 0.08 = 8% slower per stack.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float reductionPerStack = 0.08f;

    public override float GetFallSpeedFactor(AbilityContext context, int stacks)
    {
        return Mathf.Max(0.1f, 1f - reductionPerStack * stacks);
    }
}
