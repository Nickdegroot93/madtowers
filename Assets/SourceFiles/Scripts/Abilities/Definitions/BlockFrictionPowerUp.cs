using UnityEngine;

/// <summary>
/// Stackable passive that increases the runtime fallback friction used by standard
/// blocks. Variants with explicit physics materials keep their authored behaviour.
/// Front-loaded like the supply passives: the first pickup adds a stronger grip delta,
/// later stacks add smaller top-ups.
/// </summary>
[CreateAssetMenu(fileName = "BlockFriction", menuName = "Stacking/Abilities/Block Friction")]
public class BlockFrictionPowerUp : PassiveAbility
{
    [Tooltip("Added to the standard block friction multiplier on the FIRST pickup. 0.3 = +30% grip.")]
    [Min(0f)]
    [SerializeField] private float firstStackIncrease = 0.3f;

    [Tooltip("Added to the standard block friction multiplier on each ADDITIONAL stack.")]
    [Min(0f)]
    [SerializeField] private float additionalStackIncrease = 0.1f;

    public override void OnAcquired(AbilityContext context, int stacks)
    {
        AddFriction(firstStackIncrease);
    }

    public override void OnStackAdded(AbilityContext context, int stacks)
    {
        AddFriction(additionalStackIncrease);
    }

    private void AddFriction(float multiplierDelta)
    {
        BlockController.AddStandardBlockFrictionMultiplier(multiplierDelta);
    }
}
