using UnityEngine;

public partial class BlockController
{
    // Read-only planning for Magma's split. Actual fragments still use the ordinary controlled
    // descent and landing path. Match its collision filter, normal gate and minimum support width.
    internal float MeasureMagmaCellDrop(BoxCollider2D cell)
    {
        int count = cell.Cast(Vector2.down, _contactFilter, _castResults, 10000f);
        float nearest = float.PositiveInfinity;
        Bounds bounds = cell.bounds;
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = _castResults[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == _rb) continue;
            float gate = LandableSlope.Covers(hit.collider) ? .3f : landingSupportNormalY;
            if (hit.normal.y < gate) continue;
            Bounds support = hit.collider.bounds;
            float overlap = Mathf.Min(bounds.max.x, support.max.x) - Mathf.Max(bounds.min.x, support.min.x);
            if (overlap < GetMinimumLandingSupportWidth()) continue;
            nearest = Mathf.Min(nearest, Mathf.Max(0f, hit.distance));
        }
        return nearest;
    }
}
