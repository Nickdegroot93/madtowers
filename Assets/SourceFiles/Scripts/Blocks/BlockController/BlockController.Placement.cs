using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// Grid legality for sideways steps: snapped-row checks against landed blocks, the
// static-obstacle probes with half-cell row forgiveness, and horizontal placement bounds.
public partial class BlockController
{
    // Nudges the target column by one (driven by ProcessHorizontalDas). SteerWhileFalling then
    // slides the piece to that column over a few frames, so it stays in a lane but isn't instant.
    private ColumnStepResult ShiftTargetColumn(int direction, bool collectBlockers = false)
    {
        // A flick-drop is a committed plunge: the column is chosen at flick time and can
        // never change mid-fall (hard-drop convention; swipe drift during the plunge was
        // steering pieces off their intended column). This is the one chokepoint every
        // horizontal step funnels through, so touch drags, the nudge dash and keyboard
        // DAS are all locked out together. Gated = silent: nothing physical was hit.
        if (_autoDrop) return ColumnStepResult.Gated;

        float candidate = _targetColumnX + direction * gridSpacing;
        if (!IsColumnTargetWithinBounds(candidate)) return ColumnStepResult.OutOfBounds;

        ColumnStepResult result = ClassifyGridPlacementAtColumn(candidate, collectBlockers);
        if (result == ColumnStepResult.Moved) _targetColumnX = candidate;
        return result;
    }

    // The landed bricks that refused the last sidestep - a failed nudge slams exactly these.
    private readonly List<BlockController> _stepBlockers = new List<BlockController>(4);

    // collectBlockers: only the nudge needs to know WHO refused the step (to slam them);
    // drag/DAS steps run at auto-repeat rate and take the early-out the moment anything
    // blocks, exactly like the pre-classification code did.
    private ColumnStepResult ClassifyGridPlacementAtColumn(float candidatePrimaryX, bool collectBlockers)
    {
        _stepBlockers.Clear();
        bool staticBlocked = false;

        _cellGeometry.Refresh();
        float currentPrimaryX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float deltaX = candidatePrimaryX - currentPrimaryX;
        float rowTolerance = gridSpacing * 0.8f;

        for (int i = 0; i < _cellGeometry.CellCenters.Count; i++)
        {
            Vector2 activeCell = _cellGeometry.CellCenters[i];

            // Static geometry gets the same half-cell row forgiveness landed blocks get from
            // the snapped-row check below: a destination cell is blocked only if BOTH its
            // current (descent) Y and its snapped row are obstructed. With only the continuous
            // probe, a one-cell pocket between island cells demanded near-perfect vertical
            // alignment (~0.13 of a cell) and was effectively impossible to enter; tower
            // pockets with identical geometry have always allowed half a cell of slack. The
            // off-row seating this permits is resolved by the vertical tuck in SteerWhileFalling.
            Vector2 destination = new Vector2(activeCell.x + deltaX, activeCell.y);
            Vector2 destinationOnRow = new Vector2(destination.x, SnapValue(activeCell.y, gridSpacing));
            if (IsCellBlockedByStaticObstacle(destination) && IsCellBlockedByStaticObstacle(destinationOnRow))
            {
                if (!collectBlockers) return ColumnStepResult.BlockedByStatic;
                staticBlocked = true;
                continue; // rock decides this cell - a brick behind it never took the hit
            }

            float activeColumn = SnapValue(activeCell.x + deltaX, gridSpacing);
            float activeRow = SnapValue(activeCell.y, gridSpacing);

            for (int blockIndex = 0; blockIndex < TrackedBlocks.Count; blockIndex++)
            {
                BlockController block = TrackedBlocks[blockIndex];
                if (block == null || block == this || !block.HasLanded) continue;

                block._cellGeometry.Refresh();
                for (int cellIndex = 0; cellIndex < block._cellGeometry.CellCenters.Count; cellIndex++)
                {
                    Vector2 placedCell = block._cellGeometry.CellCenters[cellIndex];
                    float placedColumn = SnapValue(placedCell.x, gridSpacing);
                    float placedRow = SnapValue(placedCell.y, gridSpacing);
                    if (Mathf.Abs(placedColumn - activeColumn) <= GridMatchTolerance &&
                        Mathf.Abs(placedRow - activeRow) < rowTolerance)
                    {
                        if (!collectBlockers) return ColumnStepResult.BlockedByBlocks;
                        if (!_stepBlockers.Contains(block)) _stepBlockers.Add(block);
                        break;
                    }
                }
            }
        }

        if (_stepBlockers.Count > 0) return ColumnStepResult.BlockedByBlocks;
        return staticBlocked ? ColumnStepResult.BlockedByStatic : ColumnStepResult.Moved;
    }

    // Placed tetrominoes are handled by the grid-snapped check above (the grid stays the sole X
    // authority there), but support islands and other static geometry have no BlockController, so
    // a sideways step would otherwise teleport the kinematic piece straight into them.
    private bool IsCellBlockedByStaticObstacle(Vector2 candidateCellCenter)
    {
        Vector2 probeSize = Vector2.one * (gridSpacing * 0.8f);
        int count = Physics2D.OverlapBox(candidateCellCenter, probeSize, 0f, _contactFilter, _overlapResults);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = _overlapResults[i];
            if (hit == null || hit.isTrigger) continue;
            if (hit.attachedRigidbody == _rb) continue;
            if (hit.GetComponentInParent<BlockController>() != null) continue;
            return true;
        }

        return false;
    }

    private bool IsColumnTargetWithinBounds(float candidateColumnX)
    {
        if (!_cellGeometry.TryGetWorldBounds(out Bounds bounds)) return true;

        float primaryX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float leftReach = primaryX - bounds.min.x;
        float rightReach = bounds.max.x - primaryX;
        float minX = float.NegativeInfinity;
        float maxX = float.PositiveInfinity;

        if (TryGetGameplayHorizontalBounds(out float gameplayMinX, out float gameplayMaxX))
        {
            minX = gameplayMinX;
            maxX = gameplayMaxX;
        }

        if (TryGetCameraHorizontalBounds(out float cameraMinX, out float cameraMaxX))
        {
            minX = Mathf.Max(minX, cameraMinX);
            maxX = Mathf.Min(maxX, cameraMaxX);
        }

        const float tolerance = 0.001f;
        return candidateColumnX - leftReach >= minX - tolerance &&
               candidateColumnX + rightReach <= maxX + tolerance;
    }

    private bool TryGetGameplayHorizontalBounds(out float minX, out float maxX)
    {
        minX = float.PositiveInfinity;
        maxX = float.NegativeInfinity;
        bool hasBounds = false;

        AddFloorHorizontalBounds(ref minX, ref maxX, ref hasBounds);
        AddPlacedBlockHorizontalBounds(ref minX, ref maxX, ref hasBounds);

        if (!hasBounds) return false;

        float buffer = Mathf.Max(0, horizontalPlacementBufferColumns) * gridSpacing;
        minX -= buffer;
        maxX += buffer;
        return true;
    }

    private void AddFloorHorizontalBounds(ref float minX, ref float maxX, ref bool hasBounds)
    {
        if (_floorSegments == null) return;

        for (int i = 0; i < _floorSegments.Count; i++)
        {
            FloorSegmentConfig segment = _floorSegments[i];
            if (segment == null) continue;

            float segmentMinX = (segment.LeftColumn - 0.5f) * gridSpacing;
            float segmentMaxX = (segment.RightColumn + 0.5f) * gridSpacing;
            ExpandHorizontalBounds(segmentMinX, segmentMaxX, ref minX, ref maxX, ref hasBounds);
        }
    }

    private void AddPlacedBlockHorizontalBounds(ref float minX, ref float maxX, ref bool hasBounds)
    {
        for (int i = 0; i < TrackedBlocks.Count; i++)
        {
            BlockController block = TrackedBlocks[i];
            if (block == null || block == this || !block.HasLanded) continue;
            if (!block.TryGetWorldBounds(out Bounds blockBounds)) continue;

            ExpandHorizontalBounds(blockBounds.min.x, blockBounds.max.x, ref minX, ref maxX, ref hasBounds);
        }
    }

    private void ExpandHorizontalBounds(float candidateMinX, float candidateMaxX, ref float minX, ref float maxX, ref bool hasBounds)
    {
        minX = hasBounds ? Mathf.Min(minX, candidateMinX) : candidateMinX;
        maxX = hasBounds ? Mathf.Max(maxX, candidateMaxX) : candidateMaxX;
        hasBounds = true;
    }

    private bool TryGetCameraHorizontalBounds(out float minX, out float maxX)
    {
        minX = 0f;
        maxX = 0f;

        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
        }

        if (_mainCamera == null || !_mainCamera.orthographic) return false;

        float halfWidth = _mainCamera.orthographicSize * _mainCamera.aspect;
        minX = _mainCamera.transform.position.x - halfWidth;
        maxX = _mainCamera.transform.position.x + halfWidth;
        return true;
    }

}
