using UnityEngine;

/// <summary>
/// One-shot: the next brick becomes the given variant (e.g. a single Anchor brick).
/// Unlike BlockVariantChancePowerUp this does not persist - one brick, then back to normal.
/// </summary>
[CreateAssetMenu(fileName = "NextBlockVariant", menuName = "Stacking/Abilities/Next Block Variant")]
public class NextBlockVariantPowerUp : InstantAbility
{
    [SerializeField] private BlockData variant;

    public override void Apply(AbilityContext context)
    {
        if (context.Spawner == null || variant == null) return;

        context.Spawner.ApplyVariantToNextBlock(variant);
    }
}
