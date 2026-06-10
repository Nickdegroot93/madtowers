using UnityEngine;

/// <summary>
/// One-shot: the next brick becomes the given variant (e.g. a single Anchor brick).
/// Unlike BlockVariantChancePowerUp this does not persist - one brick, then back to normal.
/// </summary>
[CreateAssetMenu(fileName = "NextBlockVariant", menuName = "Stacking/Power Ups/Next Block Variant")]
public class NextBlockVariantPowerUp : PowerUpDefinition
{
    [SerializeField] private BlockData variant;

    public override void Apply(PowerUpContext context)
    {
        if (context.Spawner == null || variant == null) return;

        context.Spawner.ApplyVariantToNextBlock(variant);
    }
}
