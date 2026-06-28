using UnityEngine;

/// <summary>
/// Ability-specific effect guards. Shared world-impact juice (the hit punch, the every-cell burst,
/// the standard block-shatter destroy) used by both abilities AND block variants now lives in the
/// neutral <see cref="ImpactFx"/> (Core); this file holds only what is genuinely ability business.
///
/// Plain static methods, not an effect-asset graph: abilities call these from Apply/Activate/handlers
/// with whatever parameters their definitions carry.
/// </summary>
public static class AbilityEffects
{
    /// <summary>
    /// Core "can we transform the active piece THIS turn" guard, shared by every transform
    /// consumable (shape-swaps via <see cref="CanTransmuteActivePiece"/> AND variant re-skins via
    /// ApplyVariantConsumable): there must be a spawner, a piece in the air and not mid-lock, and it
    /// must not have fallen past the loss line (the cull sweep owns it then). Kept in one place so
    /// the subtle loss-line invariant can't drift between the abilities that share it. Callers add
    /// their own "already this shape/variant" and prefab checks.
    /// </summary>
    public static bool ActivePieceCanTransform(AbilityContext context)
    {
        if (context == null || context.Spawner == null) return false;

        BlockController active = BlockController.ActiveControlled;
        if (active == null || active.HasLanded) return false;

        Camera camera = Camera.main;
        if (camera != null && camera.orthographic && active.transform.position.y < LossZone.CullY(camera)) return false;

        return true;
    }

    /// <summary>
    /// DEFUSE a piece in place: strip any special-variant LOOK (overlay skins) and reset it to its
    /// shape's plain DefaultData - same GameObject, so rotation/position/fall progress are kept.
    /// Shared by Sanitize (tap) and Ward (auto, on a hazard spawn). Returns false WITHOUT touching the
    /// piece if it can't be reset (no spawner / no identity / no DefaultData), so callers only spend a
    /// charge or play FX on a real neutralise. ApplyVariantToNextBlock re-syncs identity + the
    /// active-piece accounting cache (BLOCKS.md); behaviour is added on lock and trait flags read live
    /// from the applied data, so the reset piece is fully harmless.
    /// </summary>
    public static bool NeutralizeToPlain(AbilityContext context, BlockController block)
    {
        if (context == null || context.Spawner == null || block == null) return false;
        if (!block.TryGetComponent(out BlockIdentity identity)) return false;
        if (identity.Definition == null || identity.Definition.DefaultData == null) return false;

        block.StripVariantSkins();
        context.Spawner.ApplyVariantToNextBlock(identity.Definition.DefaultData);
        return true;
    }

    /// <summary>
    /// Shared CanActivate guard for SHAPE-swap consumables (Shrink, Transmute): the active piece may
    /// be replaced by <paramref name="target"/> only if it can transform this turn (<see
    /// cref="ActivePieceCanTransform"/>), the target shape is wired, and the piece isn't already it.
    /// </summary>
    public static bool CanTransmuteActivePiece(AbilityContext context, BlockDefinition target)
    {
        if (target == null || target.Prefab == null) return false;
        if (target.Prefab.GetComponent<BlockController>() == null) return false;
        if (!ActivePieceCanTransform(context)) return false;

        BlockController active = BlockController.ActiveControlled;
        if (active.TryGetComponent(out BlockIdentity identity) && identity.Definition == target) return false;

        return true;
    }
}
