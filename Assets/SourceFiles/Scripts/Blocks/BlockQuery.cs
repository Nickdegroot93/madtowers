using UnityEngine;

/// <summary>
/// Shared spatial queries over the live block field - the physics-contract-sensitive
/// bits (which body types count, the 0.94 collider width, landed-vs-falling) that
/// abilities must not each re-derive. Read-only; never moves or mutates blocks.
/// </summary>
public static class BlockQuery
{
    private const float DefaultProbeDepth = 0.35f;
    private static readonly RaycastHit2D[] Hits = new RaycastHit2D[16];
    private static readonly ContactFilter2D SolidFilter = new ContactFilter2D { useTriggers = false };

    /// <summary>
    /// The nearest non-frozen landed block directly beneath <paramref name="from"/>
    /// (a downward box-cast of its own footprint). Static colliders - the floor and
    /// support islands - and frozen blocks (Static bodies) are excluded, so a caller
    /// can treat "null" as "nothing destructible under me". Returns null if
    /// <paramref name="from"/> has no bounds.
    /// </summary>
    public static BlockController SupportBlockBelow(BlockController from, float probeDepth = DefaultProbeDepth)
    {
        if (from == null || !from.TryGetWorldBounds(out Bounds bounds)) return null;

        Vector2 size = new Vector2(bounds.size.x * 0.9f, Mathf.Max(0.05f, bounds.size.y * 0.5f));
        int count = Physics2D.BoxCast(bounds.center, size, 0f, Vector2.down,
            SolidFilter, Hits, bounds.extents.y + probeDepth);

        BlockController best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = Hits[i];
            if (hit.collider == null) continue;

            BlockController block = hit.collider.GetComponentInParent<BlockController>();
            if (block == null || block == from || !block.HasLanded || block.IsFrozenInPlace) continue;

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                best = block;
            }
        }
        return best;
    }

    /// <summary>Whether the block is at least partly within the camera viewport. A non-orthographic
    /// or absent camera counts as visible; a block with no bounds counts as not. Shared by the
    /// targeting abilities so the viewport test can't drift between them.</summary>
    public static bool IsOnScreen(BlockController block, Camera camera = null)
    {
        return block != null && block.TryGetWorldBounds(out Bounds bounds) && IsOnScreen(bounds, camera);
    }

    /// <summary>Whether the world bounds are at least partly within the camera viewport.</summary>
    public static bool IsOnScreen(Bounds bounds, Camera camera = null)
    {
        if (camera == null) camera = Camera.main;
        if (camera == null || !camera.orthographic) return true;

        Vector3 min = camera.WorldToViewportPoint(bounds.min);
        Vector3 max = camera.WorldToViewportPoint(bounds.max);
        return max.x >= 0f && min.x <= 1f && max.y >= 0f && min.y <= 1f;
    }
}
