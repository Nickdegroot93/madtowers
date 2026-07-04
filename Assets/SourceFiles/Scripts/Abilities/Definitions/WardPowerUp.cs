using UnityEngine;

/// <summary>
/// One-shot passive (charges = 1): the NEXT hazard brick that spawns is silently neutralised into a
/// plain brick of the same shape, and the charge is spent. Fires on spawn - before the piece is ever
/// seen as a hazard - so it just looks like a normal brick dropped. Hazards are identified by the data
/// flag <see cref="BlockData.IsHazard"/>, so a new hostile brick is warded automatically; non-hazard
/// specials (Anchor, Vine, Magma) are ignored, so the charge is never wasted on a helpful brick.
/// Reuses the Sanitize path (strip the variant look in place, reset to the shape's plain DefaultData).
/// </summary>
[CreateAssetMenu(fileName = "Ward", menuName = "Stacking/Abilities/Ward")]
public class WardPowerUp : PassiveAbility
{
    public override bool OnBlockSpawned(AbilityContext context, BlockController block, BlockData data)
    {
        if (data == null || !data.IsHazard) return false;                 // only act on a real hazard
        if (!AbilityEffects.NeutralizeToPlain(context, block)) return false; // and only spend if it actually defused

        SfxPlayer.Play("ward_absorb", 0.75f, 0.04f); // the ward audibly absorbs the hazard
        return true; // consume the one charge
    }

    // Only worth offering where hazards can actually drop.
    public override bool IsAvailable(AbilityContext context, int ownedStacks)
        => base.IsAvailable(context, ownedStacks) && context.LevelHazardVariantCount() >= 1;
}
