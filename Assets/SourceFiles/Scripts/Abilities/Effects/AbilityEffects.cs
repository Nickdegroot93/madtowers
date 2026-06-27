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
