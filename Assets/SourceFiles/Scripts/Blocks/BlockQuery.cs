using UnityEngine;

/// <summary>
/// Shared spatial queries over the live block field - the physics-contract-sensitive
/// bits (which body types count, the 0.94 collider footprint, landed-vs-falling) that
/// abilities must not each re-derive. Read-only; never moves or mutates blocks.
/// </summary>
public static class BlockQuery
{
    private const float DefaultProbeDepth = 0.35f;
    private static readonly RaycastHit2D[] Hits = new RaycastHit2D[16];
    private static readonly ContactFilter2D SolidFilter = new ContactFilter2D { useTriggers = false };

    /// <summary>
    /// The nearest DYNAMIC, landed block directly beneath <paramref name="from"/>
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

            Rigidbody2D body = hit.collider.attachedRigidbody;
            if (body == null || body.bodyType != RigidbodyType2D.Dynamic) continue;

            BlockController block = hit.collider.GetComponentInParent<BlockController>();
            if (block == null || block == from || !block.HasLanded) continue;

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                best = block;
            }
        }
        return best;
    }
}
