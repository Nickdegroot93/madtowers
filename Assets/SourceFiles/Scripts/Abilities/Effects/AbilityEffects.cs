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
    /// Shared CanActivate guard for transform consumables (Shrink, future
    /// shape-swaps): the active piece may be replaced by <paramref name="target"/> only if
    /// everything Spawner.ReplaceActivePiece needs is present BEFORE the slot is consumed -
    /// target + prefab + BlockController wired; a piece in the air and not mid-lock; not
    /// already that shape; and not fallen past the loss line (the cull sweep owns it then).
    /// Kept in one place so the subtle loss-line/already-this-shape invariants can't drift
    /// between the abilities that share them.
    /// </summary>
    public static bool CanTransmuteActivePiece(AbilityContext context, BlockDefinition target)
    {
        if (context == null || context.Spawner == null || target == null || target.Prefab == null) return false;
        if (target.Prefab.GetComponent<BlockController>() == null) return false;

        BlockController active = BlockController.ActiveControlled;
        if (active == null || active.HasLanded) return false;
        if (active.TryGetComponent(out BlockIdentity identity) && identity.Definition == target) return false;

        Camera camera = Camera.main;
        if (camera != null && camera.orthographic && active.transform.position.y < LossZone.CullY(camera)) return false;

        return true;
    }
}
