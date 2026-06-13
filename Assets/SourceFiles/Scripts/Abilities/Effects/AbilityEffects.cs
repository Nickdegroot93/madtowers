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
        // A destroyed placed block is one fewer block on the board.
        if (GameManager.Instance != null) GameManager.Instance.RemovePlacedBlock(block);
        Object.Destroy(block.gameObject);
    }

    /// <summary>
    /// Play an authored effect prefab bursting from EVERY cell of a block, so it reads
    /// as the whole body breaking (a 1x4 I-piece erupts from four origins, a square
    /// from one) instead of a single point in the middle. Each burst is sized to one
    /// cell (CFXR effects read at ~1 cell at scale 1); <paramref name="scaleMultiplier"/>
    /// scales all of them (1 = cell-sized). Reads cell colliders, so call it while the
    /// block still exists (before Destroy - which is frame-deferred anyway).
    /// </summary>
    public static void BurstFromEveryCell(BlockController block, GameObject prefab, float scaleMultiplier = 1f)
    {
        if (block == null || prefab == null) return;

        BoxCollider2D[] cells = block.GetComponentsInChildren<BoxCollider2D>();
        if (cells.Length == 0)
        {
            // No cell colliders - fall back to one burst at the block centre.
            if (block.TryGetWorldBounds(out Bounds whole)) Vfx.Spawn(prefab, whole.center, CellScale(whole, scaleMultiplier));
            return;
        }

        foreach (BoxCollider2D cell in cells)
        {
            Bounds b = cell.bounds;
            Vfx.Spawn(prefab, b.center, CellScale(b, scaleMultiplier));
        }
    }

    private static float CellScale(Bounds cell, float multiplier)
    {
        return Mathf.Max(0.1f, multiplier * Mathf.Max(cell.size.x, cell.size.y));
    }

    /// <summary>
    /// The shared "this hit had weight" punch: a micro hit-stop (pause-safe time freeze)
    /// plus a camera kick. The juice standard's physical-feedback half - layer it with an
    /// authored VFX and a sound for a full impact (see ABILITIES.md §13).
    /// </summary>
    public static void ImpactPunch(float stopSeconds = 0.06f, float shakeAmplitude = 0.1f, float shakeDuration = 0.16f)
    {
        HitStop.Trigger(stopSeconds);
        TowerCameraController.Impact(shakeAmplitude, shakeDuration);
    }
}
