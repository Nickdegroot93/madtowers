using UnityEngine;

/// <summary>
/// Consumable that applies a BlockData VARIANT to the active falling piece, keeping its
/// shape (Magma: the current piece becomes a molten version of itself). Unlike
/// TransmuteAbility - which swaps the whole shape via ReplaceActivePiece - this re-skins the
/// piece in place through Spawner.ApplyVariantToNextBlock, which also re-reports the piece's
/// identity and accounting flags so a variant with non-default count/life flags is never
/// scored against the original (BLOCKS.md). Generic: any "tap: current piece becomes
/// variant V" ability is just another asset, no new code.
///
/// <see cref="count"/> extends this to "the next N bricks become variant V": the falling piece
/// transforms now, the remaining N-1 are queued for the upcoming spawns (e.g. Vine = 2).
/// </summary>
[CreateAssetMenu(fileName = "ApplyVariant", menuName = "Stacking/Abilities/Apply Variant")]
public class ApplyVariantConsumable : ConsumableAbility
{
    [Tooltip("The variant data applied to the active falling piece (same shape, new behaviour).")]
    [SerializeField] private BlockData variant;
    [Tooltip("How many upcoming bricks become this variant: the falling piece counts as the first, " +
             "any remainder is queued for the next spawns. 1 = just the current piece (e.g. Anchor); 2 = Vine.")]
    [Min(1)]
    [SerializeField] private int count = 1;

    [Header("Transform FX (swappable)")]
    [Tooltip("Plays on the piece as it transforms (a CFXR transform/poof effect). Null-safe.")]
    [SerializeField] private GameObject transformEffect;
    [Tooltip("Scale for the transform effect - CFXR effects are character-sized, a block usually wants < 1.")]
    [SerializeField] private float transformScale = 0.6f;

    // The slot is consumed BEFORE Activate, so refuse every way the apply could fail. The
    // active-piece/loss-line invariant is shared with the shape-swaps (AbilityEffects); here we
    // add the variant-specific guards: a variant must be wired and the piece not already it.
    public override bool CanActivate(AbilityContext context)
    {
        if (variant == null || !AbilityEffects.ActivePieceCanTransform(context)) return false;

        BlockController active = BlockController.ActiveControlled;
        if (active.TryGetComponent(out BlockIdentity identity) && identity.Variant == variant) return false;

        return true;
    }

    public override void Activate(AbilityContext context)
    {
        BlockController active = BlockController.ActiveControlled;
        if (active == null || active.HasLanded) return;

        // Apply the variant to the in-air piece. ApplyVariantToNextBlock re-assigns the
        // BlockIdentity and GameManager's active-piece cache, so the new count/life flags
        // take effect for this piece's lock and loss. Any extra count is queued for the
        // upcoming spawns (e.g. Vine turns this piece AND the next one).
        context.Spawner.ApplyVariantToNextBlock(variant);
        context.Spawner.QueueVariantOverride(variant, Mathf.Max(1, count) - 1);

        Vfx.Spawn(transformEffect, active.transform.position, transformScale); // null-safe
        ImpactFx.ImpactPunch(0.03f, 0.08f, 0.12f);
        SfxPlayer.Play("transmute", 0.9f);
    }
}
