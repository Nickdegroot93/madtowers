using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// Kinematic descent (I5): grid-authored X/rotation with cast-driven Y, the static-pocket
// vertical tuck, camera clamping, and the snap/position/rotation primitives that write them.
public partial class BlockController
{
    // ---- Grid-first active control -----------------------------------------------------------
    // While a piece is active it behaves like Tetris: kinematic, grid-aligned, and deterministic.
    // Physics only takes over after the placement is locked, which prevents tiny contact-solver
    // drift from deciding whether the next block can fit.
    private void HandleDynamicControl()
    {
        InitDynamicControlBody();
        _controlElapsed += Time.fixedDeltaTime;

        if (!_hasTouchedDown)
        {
            SteerWhileFalling();
        }

        // A piece steered into a gap/off the floor edge has nothing to land on, and the
        // kinematic body never triggers the loss zone - hand it to physics immediately
        // instead of stalling out the maxControlTime safety timer.
        if (!_hasTouchedDown && GameManager.Instance != null &&
            transform.position.y < GameManager.Instance.floorOriginY - 3f)
        {
            LockBlock();
            return;
        }

        if (_controlElapsed >= maxControlTime)
        {
            LockBlock();
        }
    }

    private void InitDynamicControlBody()
    {
        if (_dynamicControlReady) return;
        _dynamicControlReady = true;

        // Active fall is controlled manually (kinematic); landed blocks normalize gravity to a
        // constant so old tower sections are not under ever-rising load as difficulty scales.
        _rb.bodyType = RigidbodyType2D.Kinematic;
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        _rb.gravityScale = 0f;
        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;

        // Pivot rotation around the reference cell (not the centre of mass) while steering, so
        // rotating spins the piece in place instead of swinging it sideways out of its column.
        // The real centre of mass is restored on landing so toppling physics stay correct.
        // It is COMPUTED from the cell layout, never read back from the body here: the body is
        // already Kinematic at this point and kinematic bodies report a zeroed centre of mass,
        // which pinned every landed piece's weight to its grip cell (the body origin) - edge
        // overhangs balanced on one side of the floor and toppled hard on the other.
        _originalCenterOfMass = ComputeUniformCellCenterOfMassLocal();
        Vector2 primaryWorld = _cellGeometry.GetPrimaryWorldCenter(_rb.position);
        _rb.centerOfMass = transform.InverseTransformPoint(primaryWorld);
    }

    // All cells are identical 1x1 boxes of equal density, so the true centre of mass is
    // exactly the average of the cell centres, expressed in body-local space.
    private Vector2 ComputeUniformCellCenterOfMassLocal()
    {
        _cellGeometry.Refresh();
        var centers = _cellGeometry.CellCenters;
        if (centers == null || centers.Count == 0) return Vector2.zero;

        Vector2 sum = Vector2.zero;
        for (int i = 0; i < centers.Count; i++) sum += centers[i];
        return transform.InverseTransformPoint(sum / centers.Count);
    }

    private void SteerWhileFalling()
    {
        // Horizontal: move to the target column as a grid step. This avoids half-column landings,
        // which were the source of many millimetre gaps and "still falling" states.
        float currentColumnX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float columnDelta = _targetColumnX - currentColumnX;
        float velocityX = 0f;
        if (Mathf.Abs(columnDelta) > 0.001f)
        {
            velocityX = columnDelta / Time.fixedDeltaTime;
        }

        // Rotation is also grid-first while active. Letting active pieces sit at small in-between
        // angles made contact classification unpredictable.
        SetRotationZPreservingGridPivot(_targetAngleZ);
        _rb.angularVelocity = 0f;

        Vector3 preStepPosition = transform.position;
        ApplyControlledHorizontalMovement(velocityX * Time.fixedDeltaTime);

        // Transforms are synced manually (auto-sync is off), so the down-cast below sees the
        // horizontal/rotation moves made this step instead of last step's collider poses.
        Physics2D.SyncTransforms();

        if (Mathf.Abs(columnDelta) > 0.001f) TuckIntoStaticPocket(preStepPosition);

        _lastControlledFallSpeed = GetActiveFallSpeed();
        float fallDistance = _lastControlledFallSpeed * Time.fixedDeltaTime;
        if (TryGetDownContact(fallDistance + groundedCheckDistance, out float contactDistance))
        {
            if (contactDistance > 0f)
            {
                Vector3 position = transform.position;
                position.y -= contactDistance;
                SetPosition(position);
            }

            BeginPhysicsLanding();
            return;
        }

        ApplyControlledVerticalMovement(fallDistance);
        ClearControlledLinearVelocity();
        ClampHorizontalToCameraBounds();
    }

    // A sidestep allowed on snapped-row forgiveness (see ClassifyGridPlacementAtColumn) may
    // seat the piece slightly off the pocket's row, overlapping the static cells above or
    // below it. The piece is kinematic and pre-landing, so Y is still descent-authored -
    // slide it vertically until clear (never sideways: the grid owns X). Corrections come
    // from static geometry only (it has no solver to mediate; a kinematic piece would
    // interpenetrate rock forever), but a tucked step must END fully clear of everything:
    // if the seated position still overlaps rock the budget couldn't clear OR a landed
    // brick the displacement would shove via depenetration, the whole step is reverted.
    private const float TuckMaxTravelFraction = 0.55f;

    private void TuckIntoStaticPocket(Vector3 preStepPosition)
    {
        float maxTravel = TuckMaxTravelFraction * gridSpacing;
        float totalY = 0f;
        bool fits = true;

        for (int iteration = 0; iteration < 3; iteration++)
        {
            fits = TryComputeStaticTuckCorrection(out float correction, out bool anyStaticOverlap);
            if (!fits) break;

            if (Mathf.Abs(correction) <= 0.0005f)
            {
                // The common case: an ordinary open-air sidestep never touched rock at all,
                // and pre-existing row-forgiveness brick proximity stays the solver's business.
                if (!anyStaticOverlap && totalY == 0f) return;
                break; // clear of rock after tucking - still must end clear of bricks
            }

            totalY += correction;
            if (Mathf.Abs(totalY) > maxTravel) { fits = false; break; }

            Vector3 position = transform.position;
            position.y += correction;
            SetPosition(position);
            Physics2D.SyncTransforms();
        }

        if (fits && !HasAnySolidOverlap()) return;

        SetPosition(preStepPosition);
        Physics2D.SyncTransforms();
        _targetColumnX = SnapValue(_cellGeometry.GetPrimaryWorldX(transform.position.x), gridSpacing);
    }

    // Single-axis separation from STATIC geometry. Per-contact pushes are combined as
    // extremes, never summed: two cells overlapping the same island row need the push
    // once, not twice, and opposing pushes (squeezed from above AND below) must read as
    // "does not fit", not cancel to zero and report clear.
    private bool TryComputeStaticTuckCorrection(out float correctionY, out bool anyStaticOverlap)
    {
        correctionY = 0f;
        anyStaticOverlap = false;
        float pushUp = 0f;
        float pushDown = 0f;

        IReadOnlyList<Collider2D> ownColliders = _cellGeometry.SolidColliders;
        for (int ownIndex = 0; ownIndex < ownColliders.Count; ownIndex++)
        {
            Collider2D ownCollider = ownColliders[ownIndex];
            if (ownCollider == null) continue;

            int count = ownCollider.Overlap(_contactFilter, _overlapResults);
            for (int hitIndex = 0; hitIndex < count; hitIndex++)
            {
                Collider2D other = _overlapResults[hitIndex];
                if (other == null || other.isTrigger) continue;
                if (other.attachedRigidbody == _rb) continue;
                if (other.GetComponentInParent<BlockController>() != null) continue;

                ColliderDistance2D distance = Physics2D.Distance(ownCollider, other);
                if (!distance.isOverlapped) continue;

                anyStaticOverlap = true;
                float push = distance.normal.y * distance.distance;
                if (push > pushUp) pushUp = push;
                else if (push < pushDown) pushDown = push;
            }
        }

        if (pushUp > 0f && pushDown < 0f) return false; // squeezed: no vertical move can fit
        correctionY = pushUp > 0f ? pushUp : pushDown;
        return true;
    }

    // Post-tuck validation: the seated position must overlap nothing solid - neither the
    // rock the tuck was clearing nor a landed brick the new Y would interpenetrate.
    private bool HasAnySolidOverlap()
    {
        IReadOnlyList<Collider2D> ownColliders = _cellGeometry.SolidColliders;
        for (int ownIndex = 0; ownIndex < ownColliders.Count; ownIndex++)
        {
            Collider2D ownCollider = ownColliders[ownIndex];
            if (ownCollider == null) continue;

            int count = ownCollider.Overlap(_contactFilter, _overlapResults);
            for (int hitIndex = 0; hitIndex < count; hitIndex++)
            {
                Collider2D other = _overlapResults[hitIndex];
                if (other == null || other.isTrigger) continue;
                if (other.attachedRigidbody == _rb) continue;

                if (Physics2D.Distance(ownCollider, other).isOverlapped) return true;
            }
        }

        return false;
    }

    private void ApplyControlledHorizontalMovement(float deltaX)
    {
        if (Mathf.Abs(deltaX) <= 0.0001f) return;

        Vector3 position = transform.position;
        position.x += deltaX;
        SetPosition(position);
        ClampHorizontalToCameraBounds();
    }

    private void ApplyControlledVerticalMovement(float distance)
    {
        if (distance <= 0.0001f) return;

        Vector3 position = transform.position;
        position.y -= distance;
        SetPosition(position);
    }

    private void ClearControlledLinearVelocity()
    {
        _rb.linearVelocity = Vector2.zero;
    }

    private bool TryGetDownContact(float maxDistance, out float moveDistance)
    {
        moveDistance = 0f;
        float distance = Mathf.Max(0.001f, maxDistance);
        int count = _rb.Cast(Vector2.down, _contactFilter, _castResults, distance);
        float closestContactDistance = Mathf.Infinity;

        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = _castResults[i];
            if (!IsValidLandingSupport(hit)) continue;

            if (hit.distance < closestContactDistance)
            {
                closestContactDistance = hit.distance;
            }
        }

        if (closestContactDistance == Mathf.Infinity) return false;

        moveDistance = Mathf.Max(0f, closestContactDistance);
        return true;
    }

    private void ClampHorizontalToCameraBounds()
    {
        if (!TryGetCameraHorizontalBounds(out float cameraMinX, out float cameraMaxX)) return;
        if (!_cellGeometry.TryGetWorldBounds(out Bounds bounds)) return;

        float leftReach = transform.position.x - bounds.min.x;
        float rightReach = bounds.max.x - transform.position.x;
        float minCenterX = cameraMinX + leftReach;
        float maxCenterX = cameraMaxX - rightReach;
        if (minCenterX > maxCenterX) return;

        Vector3 position = transform.position;
        Vector2 velocity = _rb.linearVelocity;

        if (position.x < minCenterX)
        {
            position.x = minCenterX;
            if (velocity.x < 0f) velocity.x = 0f;
            SetPosition(position);
            _rb.linearVelocity = velocity;
        }
        else if (position.x > maxCenterX)
        {
            position.x = maxCenterX;
            if (velocity.x > 0f) velocity.x = 0f;
            SetPosition(position);
            _rb.linearVelocity = velocity;
        }
    }

    // Snaps the actual tetromino cells onto the column grid, leaving Y untouched.
    private void SnapToColumnGrid()
    {
        float xToSnap = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float snappedX = SnapValue(xToSnap, gridSpacing);
        float correction = snappedX - xToSnap;

        Vector3 pos = transform.position;
        pos.x += correction;
        SetPosition(pos);
    }

    private float SnapValue(float value, float step)
    {
        if (step <= 0f) return value;
        return Mathf.Round(value / step) * step;
    }

    private void SetPosition(Vector3 pos)
    {
        pos.z = transform.position.z;
        transform.position = pos;
        if (_rb != null) _rb.position = pos;
    }

    private void SetRotationZ(float angle)
    {
        float snappedAngle = SnapValue(angle, RotationStep);
        transform.rotation = Quaternion.Euler(0f, 0f, snappedAngle);
        if (_rb != null) _rb.rotation = snappedAngle;
    }

    private void SetRotationZPreservingGridPivot(float angle)
    {
        float snappedAngle = SnapValue(angle, RotationStep);
        float currentAngle = _rb != null ? _rb.rotation : transform.eulerAngles.z;
        if (Mathf.Abs(Mathf.DeltaAngle(currentAngle, snappedAngle)) <= 0.001f) return;

        if (!TryGetRotationPivot(out Vector2 pivotBefore))
        {
            SetRotationZ(snappedAngle);
            return;
        }

        SetRotationZ(snappedAngle);
        Physics2D.SyncTransforms();

        if (!TryGetRotationPivot(out Vector2 pivotAfter))
        {
            return;
        }

        Vector3 position = transform.position;
        Vector2 correction = pivotBefore - pivotAfter;
        position.x += correction.x;
        position.y += correction.y;
        SetPosition(position);
        Physics2D.SyncTransforms();

        if (!_hasTouchedDown && _isControlEnabled)
        {
            _targetColumnX = SnapValue(_cellGeometry.GetPrimaryWorldX(transform.position.x), gridSpacing);
        }
    }

    private bool TryGetRotationPivot(out Vector2 pivot)
    {
        pivot = default;
        if (!_cellGeometry.TryGetWorldBounds(out Bounds bounds)) return false;

        Vector3 center = bounds.center;
        pivot = new Vector2(
            SnapValue(center.x, gridSpacing),
            SnapValue(center.y, gridSpacing));
        return true;
    }

    private void ResetControlTargets()
    {
        if (_dynamicControlReady || HasLanded) return;

        SnapToColumnGrid();
        SetRotationZ(transform.eulerAngles.z);
        _targetAngleZ = transform.eulerAngles.z;
        _targetColumnX = SnapValue(_cellGeometry.GetPrimaryWorldX(transform.position.x), gridSpacing);
    }

}
