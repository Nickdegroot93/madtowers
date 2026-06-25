using UnityEngine;

/// <summary>
/// Stackable passive that SUPPRESSES an annoying brick: each stack multiplicatively reduces that
/// variant's ambient spawn chance for the rest of the run. Relative (not a flat subtraction), so
/// it scales with whatever the level's base rate is - a small ambient % isn't instantly zeroed,
/// and stacking shrinks the brick asymptotically toward (but never fully to) zero, so it stays a
/// rare threat rather than vanishing.
///
/// Pair it on the asset with <c>requiresVariantsInLevel = [the same variant]</c> so it is only
/// ever offered on levels that can actually spawn that brick. The mirror of
/// <see cref="BlockVariantChancePowerUp"/> (which boosts); both feed the Spawner's one
/// accumulating per-variant chance, so a stack adds its effect once (clone-per-run safe).
/// </summary>
[CreateAssetMenu(fileName = "BlockVariantChanceReduction", menuName = "Stacking/Abilities/Block Variant Chance Reduction")]
public class BlockVariantChanceReductionPowerUp : PassiveAbility
{
    [SerializeField] private BlockData variant;
    [Tooltip("Fraction of the variant's CURRENT spawn chance removed per stack (0.5 = halve it). " +
             "Relative, so it scales with the level's base rate; stacks multiply (two 0.5 stacks => 0.25x).")]
    [Range(0f, 1f)]
    [SerializeField] private float reductionPerStack = 0.5f;

    public override void OnAcquired(AbilityContext context, int stacks)
    {
        ApplyReduction(context);
    }

    public override void OnStackAdded(AbilityContext context, int stacks)
    {
        ApplyReduction(context);
    }

    private void ApplyReduction(AbilityContext context)
    {
        if (context.Spawner == null || variant == null) return;
        context.Spawner.ReduceVariantChance(variant, reductionPerStack);
    }
}
