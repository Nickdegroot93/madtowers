using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared horizontal-extent math so the placement reach bounds (BlockController) and the camera
/// framing (TowerCameraController) agree on ONE floor-edge convention: a cell at column C spans
/// [(C - 0.5), (C + 0.5)] * gridSpacing. These two used to carry private copies of this logic; if
/// they ever drifted apart, a piece could be steered to a column that sits off-screen or against a
/// visible wall — the "walled off at the edge" bug class this whole subsystem exists to kill. Keep
/// every consumer on this single definition.
/// </summary>
public static class HorizontalBounds
{
    /// <summary>Grow a running [minX, maxX] union by one candidate span. `hasBounds` starts false
    /// and is set once the first span is folded in (so an empty union is detectable).</summary>
    public static void Encapsulate(float candidateMinX, float candidateMaxX,
        ref float minX, ref float maxX, ref bool hasBounds)
    {
        minX = hasBounds ? Mathf.Min(minX, candidateMinX) : candidateMinX;
        maxX = hasBounds ? Mathf.Max(maxX, candidateMaxX) : candidateMaxX;
        hasBounds = true;
    }

    /// <summary>Fold every floor segment's world X span into the running union.</summary>
    public static void AddFloorSegments(IReadOnlyList<FloorSegmentConfig> segments, float gridSpacing,
        ref float minX, ref float maxX, ref bool hasBounds)
    {
        if (segments == null) return;
        for (int i = 0; i < segments.Count; i++)
        {
            FloorSegmentConfig segment = segments[i];
            if (segment == null) continue;
            Encapsulate((segment.LeftColumn - 0.5f) * gridSpacing, (segment.RightColumn + 0.5f) * gridSpacing,
                ref minX, ref maxX, ref hasBounds);
        }
    }
}
