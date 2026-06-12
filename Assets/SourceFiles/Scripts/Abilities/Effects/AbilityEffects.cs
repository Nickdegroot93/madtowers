using UnityEngine;

/// <summary>
/// Shared, reusable effect helpers - the home for behaviour used by MORE THAN ONE
/// ability kind (the Safety-Net-shared-by-a-one-shot-and-a-combo case). Plain static
/// methods, not an effect-asset graph: abilities call these from Apply/Activate/
/// handlers with whatever parameters their definitions carry.
///
/// Rules for effects that touch the world (from PHYSICS.md - binding):
/// - NEVER write position/rotation on landed blocks; velocity only (ApplyJolt) or
///   lifecycle (FreezeInPlace, Destroy).
/// - Spawned static geometry must match the world contract (friction 0.95, footprint
///   0.94, corner radius 0.06) and never materialize intersecting the falling piece.
/// </summary>
public static class AbilityEffects
{
    /// <summary>Destroy a block with the standard shatter presentation. The caller owns
    /// the decision; this owns the consistent look/sound.</summary>
    public static void DestroyBlockWithShatter(BlockController block, Color tint)
    {
        if (block == null) return;

        if (block.TryGetWorldBounds(out Bounds bounds))
        {
            BlockShatterFx.Spawn(bounds, tint);
        }
        SfxPlayer.Play("impact_soft_01", 0.7f, 0.06f);
        Object.Destroy(block.gameObject);
    }
}
