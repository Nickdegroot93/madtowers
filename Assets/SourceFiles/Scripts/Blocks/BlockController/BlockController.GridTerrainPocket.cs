using System.Collections.Generic;
using UnityEngine;

// The one narrow terrain exception to the support graph. A brick fitted into a one-cell-high
// static socket is physically form-locked: the floor and ceiling can react as a force couple, so
// releasing it to Dynamic would manufacture the shallow lean the socket geometry prevents.
public partial class BlockController
{
    // Detects one cell of this piece captured between exact, parallel STATIC boundaries one grid
    // row apart. The reaction intervals must overlap in the direction needed to oppose the
    // attempted rotation: for a rightward/clockwise escape, some ceiling contact must sit left of
    // some floor contact (mirrored for a leftward escape). The floor pocket can therefore hold a
    // long bar without granting the same privilege to a merely overhanging bar or a tower sandwich.
    private bool HasStaticPocketBrace(int escapeDirection)
    {
        escapeDirection = escapeDirection < 0 ? -1 : 1;

        _cellGeometry.Refresh();
        IReadOnlyList<Vector2> cells = _cellGeometry.CellCenters;
        for (int i = 0; i < cells.Count; i++)
        {
            Vector2 cell = cells[i];
            if (!TryGetStaticGridBoundaryInterval(
                    cell, -1, out float floorMinX, out float floorMaxX)) continue;
            if (!TryGetStaticGridBoundaryInterval(
                    cell, 1, out float ceilingMinX, out float ceilingMaxX)) continue;

            float minimumLeverArm = GridBalanceToleranceFraction *
                                    Mathf.Max(0.01f, gridSpacing);
            if (escapeDirection > 0 && ceilingMinX < floorMaxX - minimumLeverArm) return true;
            if (escapeDirection < 0 && ceilingMaxX > floorMinX + minimumLeverArm) return true;
        }

        return false;
    }

    // Queries only authored world geometry. Frozen/grid blocks are intentionally excluded even
    // though they can be ordinary supports: this special case represents an unyielding terrain
    // socket, not adhesive contact between pieces. Direction is -1 for a floor and +1 for a
    // ceiling; matching the expected interface Y proves the opening is exactly one cell high.
    private bool TryGetStaticGridBoundaryInterval(
        Vector2 cell,
        int verticalDirection,
        out float boundaryMinX,
        out float boundaryMaxX)
    {
        boundaryMinX = float.MaxValue;
        boundaryMaxX = float.MinValue;
        verticalDirection = verticalDirection < 0 ? -1 : 1;

        float grid = Mathf.Max(0.01f, gridSpacing);
        float interfaceY = cell.y + verticalDirection * grid * 0.5f;
        float verticalTolerance = GridSupportVerticalToleranceFraction * grid;
        Vector2 probeSize = new Vector2(grid * 0.75f, grid * GridSupportProbeHeightFraction);
        Vector2 probeCenter = new Vector2(
            cell.x,
            interfaceY + verticalDirection * probeSize.y * 0.25f);
        int count = Physics2D.OverlapBox(probeCenter, probeSize, 0f, _contactFilter, _overlapResults);

        bool found = false;
        float cellMinX = cell.x - grid * 0.5f;
        float cellMaxX = cell.x + grid * 0.5f;
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = _overlapResults[i];
            if (hit == null || hit.isTrigger || hit.attachedRigidbody == _rb) continue;
            if (LandableSlope.Covers(hit)) continue;
            if (hit.GetComponentInParent<BlockController>() != null) continue;

            Rigidbody2D body = hit.attachedRigidbody;
            if (body != null && body.bodyType != RigidbodyType2D.Static) continue;

            Bounds bounds = hit.bounds;
            float boundaryY = verticalDirection < 0 ? bounds.max.y : bounds.min.y;
            if (Mathf.Abs(boundaryY - interfaceY) > verticalTolerance) continue;

            float minX = Mathf.Max(cellMinX, bounds.min.x);
            float maxX = Mathf.Min(cellMaxX, bounds.max.x);
            if (maxX - minX < GetMinimumLandingSupportWidth()) continue;

            boundaryMinX = Mathf.Min(boundaryMinX, minX);
            boundaryMaxX = Mathf.Max(boundaryMaxX, maxX);
            found = true;
        }

        return found;
    }
}
