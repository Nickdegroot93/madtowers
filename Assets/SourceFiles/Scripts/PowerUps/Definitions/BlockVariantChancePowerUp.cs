using UnityEngine;

/// <summary>
/// Gives every future spawn a chance to be replaced by a specific block variant
/// (e.g. sturdy bricks). Picking the same power-up again stacks the chance.
/// </summary>
[CreateAssetMenu(fileName = "BlockVariantChance", menuName = "Stacking/Power Ups/Block Variant Chance")]
public class BlockVariantChancePowerUp : PowerUpDefinition
{
    [SerializeField] private BlockData variant;
    [Range(0f, 1f)]
    [SerializeField] private float chancePerBlock = 0.2f;

    public override void Apply(PowerUpContext context)
    {
        if (context.Spawner == null || variant == null) return;

        context.Spawner.AddVariantChance(variant, chancePerBlock);
    }
}
