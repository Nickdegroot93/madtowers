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
        bool edgePortal = HasFeature(BlockFeature.EdgePortal);
        if (edgePortal && TryWrapColumnTarget(candidate, out float wrappedCandidate))
        {
            candidate = wrappedCandidate;
        }
        else if (!IsColumnTargetWithinBounds(candidate, includeGameplayBounds: !edgePortal))
        {
            return ColumnStepResult.OutOfBounds;
        }

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

            EnsureLandedCellOccupancy();
            if (TryGetLandedCellOccupants(activeColumn, activeRow, out List<BlockController> occupants))
            {
                for (int blockerIndex = 0; blockerIndex < occupants.Count; blockerIndex++)
                {
                    BlockController block = occupants[blockerIndex];
                    if (block == null || block == this || !block.HasLanded) continue;
                    if (!collectBlockers) return ColumnStepResult.BlockedByBlocks;
                    if (!_stepBlockers.Contains(block)) _stepBlockers.Add(block);
                }
            }
        }

        if (_stepBlockers.Count > 0) return ColumnStepResult.BlockedByBlocks;
        return staticBlocked ? ColumnStepResult.BlockedByStatic : ColumnStepResult.Moved;
    }

    private void EnsureLandedCellOccupancy()
    {
        if (_placementOccupancyStamp == _placementOccupancyVersion) return;

        _placementOccupancyStamp = _placementOccupancyVersion;
        LandedCellOccupancy.Clear();

        for (int blockIndex = 0; blockIndex < TrackedBlocks.Count; blockIndex++)
        {
            BlockController block = TrackedBlocks[blockIndex];
            if (block == null || !block.HasLanded) continue;

            block._cellGeometry.Refresh();
            for (int cellIndex = 0; cellIndex < block._cellGeometry.CellCenters.Count; cellIndex++)
            {
                Vector2 placedCell = block._cellGeometry.CellCenters[cellIndex];
                Vector2Int key = ToPlacementGridKey(
                    SnapValue(placedCell.x, gridSpacing),
                    SnapValue(placedCell.y, gridSpacing));
                if (!LandedCellOccupancy.TryGetValue(key, out List<BlockController> occupants))
                {
                    occupants = new List<BlockController>(1);
                    LandedCellOccupancy.Add(key, occupants);
                }
                if (!occupants.Contains(block)) occupants.Add(block);
            }
        }
    }

    private bool TryGetLandedCellOccupants(float snappedColumn, float snappedRow, out List<BlockController> occupants)
    {
        occupants = null;
        if (gridSpacing <= 0f) return false;
        return LandedCellOccupancy.TryGetValue(ToPlacementGridKey(snappedColumn, snappedRow), out occupants);
    }

    private Vector2Int ToPlacementGridKey(float snappedColumn, float snappedRow)
    {
        if (gridSpacing <= 0f) return Vector2Int.zero;
        return new Vector2Int(
            Mathf.RoundToInt(snappedColumn / gridSpacing),
            Mathf.RoundToInt(snappedRow / gridSpacing));
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

    private bool IsColumnTargetWithinBounds(float candidateColumnX, bool includeGameplayBounds = true)
    {
        if (!_cellGeometry.TryGetWorldBounds(out Bounds bounds)) return true;

        float primaryX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float leftReach = primaryX - bounds.min.x;
        float rightReach = bounds.max.x - primaryX;
        float minX = float.NegativeInfinity;
        float maxX = float.PositiveInfinity;

        // Movement is gated by the gameplay REACH bounds (obstacles + the widest-block margin),
        // never by the camera - the camera is now a follow camera that pans/zooms to keep the
        // piece in view, so clamping to it would re-introduce the "walled off at the screen edge"
        // bug this whole change set exists to kill. The only camera-bounded path is Edge Portal,
        // which wraps targets across the *visible* screen edges (gameplay bounds excluded there).
        if (includeGameplayBounds)
        {
            if (TryGetGameplayHorizontalBounds(out float gameplayMinX, out float gameplayMaxX))
            {
                minX = gameplayMinX;
                maxX = gameplayMaxX;
            }
        }
        else if (TryGetCameraHorizontalBounds(out float cameraMinX, out float cameraMaxX))
        {
            minX = cameraMinX;
            maxX = cameraMaxX;
        }

        const float tolerance = 0.001f;
        return candidateColumnX - leftReach >= minX - tolerance &&
               candidateColumnX + rightReach <= maxX + tolerance;
    }

    private bool TryWrapColumnTarget(float candidateColumnX, out float wrappedColumnX)
    {
        wrappedColumnX = candidateColumnX;
        if (!_cellGeometry.TryGetWorldBounds(out Bounds bounds)) return false;
        if (!TryGetCameraHorizontalBounds(out float cameraMinX, out float cameraMaxX)) return false;

        float primaryX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float leftReach = primaryX - bounds.min.x;
        float rightReach = bounds.max.x - primaryX;
        float candidateMinX = candidateColumnX - leftReach;
        float candidateMaxX = candidateColumnX + rightReach;
        float minPrimaryX = cameraMinX + leftReach;
        float maxPrimaryX = cameraMaxX - rightReach;
        if (minPrimaryX > maxPrimaryX) return false;

        const float tolerance = 0.001f;
        if (candidateMaxX < cameraMinX - tolerance)
        {
            return TryGetRightmostVisibleGridColumn(minPrimaryX, maxPrimaryX, out wrappedColumnX);
        }
        if (candidateMinX > cameraMaxX + tolerance)
        {
            return TryGetLeftmostVisibleGridColumn(minPrimaryX, maxPrimaryX, out wrappedColumnX);
        }
        if (candidateMinX < cameraMinX - tolerance)
        {
            return TryGetRightmostVisibleGridColumn(minPrimaryX, maxPrimaryX, out wrappedColumnX);
        }
        if (candidateMaxX > cameraMaxX + tolerance)
        {
            return TryGetLeftmostVisibleGridColumn(minPrimaryX, maxPrimaryX, out wrappedColumnX);
        }

        return false;
    }

    private bool TryGetRightmostVisibleGridColumn(float minPrimaryX, float maxPrimaryX, out float columnX)
    {
        columnX = maxPrimaryX;
        if (gridSpacing <= 0f) return minPrimaryX <= maxPrimaryX;

        const float tolerance = 0.001f;
        columnX = Mathf.Floor((maxPrimaryX + tolerance) / gridSpacing) * gridSpacing;
        return columnX >= minPrimaryX - tolerance;
    }

    private bool TryGetLeftmostVisibleGridColumn(float minPrimaryX, float maxPrimaryX, out float columnX)
    {
        columnX = minPrimaryX;
        if (gridSpacing <= 0f) return minPrimaryX <= maxPrimaryX;

        const float tolerance = 0.001f;
        columnX = Mathf.Ceil((minPrimaryX - tolerance) / gridSpacing) * gridSpacing;
        return columnX <= maxPrimaryX + tolerance;
    }

    // Cached against the placed-geometry version: the reach bounds only change when a block lands
    // or leaves the tower, or an island spawns (all bump _reachGeometryVersion). During a single
    // piece's fall nothing else lands, so every steering-clamp and input-legality call this lifetime
    // reuses one computation instead of rescanning all tracked blocks + islands.
    private bool TryGetGameplayHorizontalBounds(out float minX, out float maxX)
    {
        if (_reachBoundsStamp != _reachGeometryVersion)
        {
            _reachBoundsStamp = _reachGeometryVersion;
            _reachBoundsValid = ComputeGameplayHorizontalBounds(out _reachBoundsMinX, out _reachBoundsMaxX);
        }

        minX = _reachBoundsMinX;
        maxX = _reachBoundsMaxX;
        return _reachBoundsValid;
    }

    private bool ComputeGameplayHorizontalBounds(out float minX, out float maxX)
    {
        minX = float.PositiveInfinity;
        maxX = float.NegativeInfinity;
        bool hasBounds = false;

        AddFloorHorizontalBounds(ref minX, ref maxX, ref hasBounds);
        AddPlacedBlockHorizontalBounds(ref minX, ref maxX, ref hasBounds);
        AddStaticIslandHorizontalBounds(ref minX, ref maxX, ref hasBounds);

        if (!hasBounds) return false;

        // The reachable margin must always fit the widest block (the horizontal 1x4) flush
        // beside the outermost obstacle, so a piece can always reach the drop lane down its
        // outer side. WidestBlockColumns is the correctness floor; the designer buffer may
        // widen it further but never below it.
        int bufferColumns = Mathf.Max(Mathf.Max(0, horizontalPlacementBufferColumns), WidestBlockColumns);
        float buffer = bufferColumns * gridSpacing;
        minX -= buffer;
        maxX += buffer;
        return true;
    }

    private void AddFloorHorizontalBounds(ref float minX, ref float maxX, ref bool hasBounds)
    {
        HorizontalBounds.AddFloorSegments(_floorSegments, gridSpacing, ref minX, ref maxX, ref hasBounds);
    }

    private void AddPlacedBlockHorizontalBounds(ref float minX, ref float maxX, ref bool hasBounds)
    {
        for (int i = 0; i < TrackedBlocks.Count; i++)
        {
            BlockController block = TrackedBlocks[i];
            if (block == null || block == this || !block.HasLanded) continue;
            if (!block.TryGetWorldBounds(out Bounds blockBounds)) continue;

            HorizontalBounds.Encapsulate(blockBounds.min.x, blockBounds.max.x, ref minX, ref maxX, ref hasBounds);
        }
    }

    // Sky islands have no BlockController, so the placed-block sweep above never sees them.
    // Fold their world horizontal extent in so the reachable area opens up beside a platform
    // too (the buffer above then guarantees the widest block fits past it). Islands are
    // confined to the reachable area at spawn time, so this only ever widens, never traps.
    private void AddStaticIslandHorizontalBounds(ref float minX, ref float maxX, ref bool hasBounds)
    {
        if (!StaticSupportIslandManager.TryGetWorldHorizontalExtent(out float islandMinX, out float islandMaxX)) return;
        HorizontalBounds.Encapsulate(islandMinX, islandMaxX, ref minX, ref maxX, ref hasBounds);
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
