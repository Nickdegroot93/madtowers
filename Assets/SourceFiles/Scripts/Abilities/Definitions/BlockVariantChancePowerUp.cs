using UnityEngine;

/// <summary>
/// Stackable passive: every future spawn has a chance to be replaced by a specific
/// block variant (e.g. sturdy bricks). Each stack adds the DELTA chance only - the
/// Spawner's variant registry accumulates internally, so re-applying the full value
/// would double-register.
/// </summary>
[CreateAssetMenu(fileName = "BlockVariantChance", menuName = "Stacking/Abilities/Block Variant Chance")]
public class BlockVariantChancePowerUp : PassiveAbility
{
    [SerializeField] private BlockData variant;
    [Range(0f, 1f)]
    [SerializeField] private float chancePerBlock = 0.2f;

    public override void OnAcquired(AbilityContext context, int stacks)
    {
        AddChance(context);
    }

    public override void OnStackAdded(AbilityContext context, int stacks)
    {
        AddChance(context);
    }

    private void AddChance(AbilityContext context)
    {
        if (context.Spawner == null || variant == null) return;

        context.Spawner.AddVariantChance(variant, chancePerBlock);
    }
}
