using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds everything a block is touching, including visually-adjacent neighbours across the
/// small collider clearance gap. Shared by variants that react to their contacts (Bomb, Vine).
/// </summary>
public static class BlockTouchScanner
{
    /// <summary>
    /// Collects all non-trigger colliders within touchRange of the root's own colliders
    /// (excluding the root's). Results are added to the provided set.
    /// </summary>
    public static void CollectTouchingColliders(
        GameObject root, float touchRange, HashSet<Collider2D> results, Collider2D[] buffer)
    {
        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = false
        };

        Collider2D[] ownColliders = root.GetComponentsInChildren<Collider2D>();
        for (int colliderIndex = 0; colliderIndex < ownColliders.Length; colliderIndex++)
        {
            Collider2D own = ownColliders[colliderIndex];
            if (own == null || own.isTrigger) continue;

            Bounds bounds = own.bounds;
            Vector2 probeSize = (Vector2)bounds.size + Vector2.one * (2f * touchRange);
            int count = Physics2D.OverlapBox(bounds.center, probeSize, 0f, filter, buffer);
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = buffer[i];
                if (hit == null || hit.transform.IsChildOf(root.transform)) continue;

                results.Add(hit);
            }
        }
    }
}
