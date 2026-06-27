using UnityEngine;

/// <summary>
/// Epic passive that makes future blocks HEAVY and grippy - a planted, knock-resistant tower that
/// shrugs off Tremor and shoves. Not strictly-better: heavier pieces land with more force (a sloppy
/// drop disturbs the stack more) and mass doesn't stop a top-heavy tower from tipping - the grip is
/// the reliable stability win, the mass is the resist-being-shoved character. Reuses the friction
/// knob and the new block-mass knob; both affect future pieces only (like Air Brake / High Friction).
/// </summary>
[CreateAssetMenu(fileName = "Titan", menuName = "Stacking/Abilities/Titan")]
public class TitanPowerUp : PassiveAbility
{
    [Tooltip("Added to the standard block friction multiplier (grip) - the reliable stability gain.")]
    [Min(0f)]
    [SerializeField] private float frictionIncrease = 0.6f;
    [Tooltip("How heavy future blocks become, as a multiple of normal mass. 1.6 = 60% heavier.")]
    [Min(1f)]
    [SerializeField] private float massMultiplier = 1.6f;

    public override void OnAcquired(AbilityContext context, int stacks)
    {
        BlockController.AddStandardBlockFrictionMultiplier(frictionIncrease);
        BlockController.AddStandardBlockMassMultiplier(massMultiplier - 1f); // additive delta over the 1.0 baseline
    }
}
