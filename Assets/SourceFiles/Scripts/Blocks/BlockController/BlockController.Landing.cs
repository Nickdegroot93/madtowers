using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// First contact to lock (I5): column snap, settling flush onto the real support, incoming
// overlap resolution, the dynamic handoff with capped impact, and lock bookkeeping.
public partial class BlockController
{
    private void BeginPhysicsLanding()
    {
        if (_hasTouchedDown) return;

        // First contact: X/rotation stay grid-authored, but Y comes from the actual cast contact.
        // From here, gravity and balance decide what happens.
        _hasTouchedDown = true;
        SnapToColumnGrid();
        SetRotationZPreservingGridPivot(_targetAngleZ);
        SettleOntoContact();
        ResolveIncomingOverlaps();

        _rb.bodyType = RigidbodyType2D.Dynamic;
        _rb.constraints = RigidbodyConstraints2D.None;
        _rb.gravityScale = ResolveLandedGravityScale();
        _rb.centerOfMass = _originalCenterOfMass;
        _rb.angularVelocity = 0f;

        _rb.linearVelocity = new Vector2(0f, -GetLandingImpactSpeed());

        // Flick-dropped pieces thump: camera shake + impact sound. Both purely cosmetic
        // (physics never reads the camera, and the landing velocity above is already capped).
        if (_autoDrop)
        {
            TowerCameraController.Impact();
            SfxPlayer.PlayVariant("impact_heavy", 2, 0.6f, 0.07f);
        }

        LockBlock();
    }

    // Slides the piece straight down until its colliders are flush against the support beneath it,
    // closing the small gap the grounded-check leaves (which grows with fall speed). Without this a
    // fast drop would fall through that gap under gravity and regain impact speed before contact.
    private void SettleOntoContact()
    {
        // The column snap just moved the body; sync so the cast measures from the final X.
        Physics2D.SyncTransforms();

        int count = _rb.Cast(Vector2.down, _contactFilter, _castResults, gridSpacing);
        float minDistance = Mathf.Infinity;
        for (int i = 0; i < count; i++)
        {
            if (IsValidLandingSupport(_castResults[i]) && _castResults[i].distance < minDistance)
            {
                minDistance = _castResults[i].distance;
            }
        }

        if (minDistance != Mathf.Infinity && minDistance > 0f)
        {
            Vector3 position = transform.position;
            position.y -= minDistance;
            SetPosition(position);
        }
    }

    private float GetLandingImpactSpeed()
    {
        float controlledSpeed = _lastControlledFallSpeed > 0f ? _lastControlledFallSpeed : GetActiveFallSpeed();
        float cap = Mathf.Max(0f, maxLandingImpactSpeed);
        return cap > 0f ? Mathf.Min(controlledSpeed, cap) : controlledSpeed;
    }

    private bool IsValidLandingSupport(RaycastHit2D hit)
    {
        if (hit.collider == null || hit.normal.y < landingSupportNormalY) return false;
        return GetHorizontalSupportOverlapAtHit(hit) >= GetMinimumLandingSupportWidth();
    }

    private float GetHorizontalSupportOverlapAtHit(RaycastHit2D hit)
    {
        if (!_cellGeometry.TryGetWorldBounds(out Bounds activeBounds) || hit.collider == null) return 0f;

        activeBounds.center += Vector3.down * Mathf.Max(0f, hit.distance);
        Bounds supportBounds = hit.collider.bounds;
        return Mathf.Min(activeBounds.max.x, supportBounds.max.x) -
               Mathf.Max(activeBounds.min.x, supportBounds.min.x);
    }

    private float GetMinimumLandingSupportWidth()
    {
        return Mathf.Max(0f, landingMinSupportWidthFraction) * gridSpacing;
    }

    private void ResolveIncomingOverlaps()
    {
        Collider2D[] ownColliders = GetComponentsInChildren<Collider2D>();
        int iterations = 0;

        while (iterations < 3)
        {
            iterations++;
            Physics2D.SyncTransforms();
            Vector2 totalCorrection = Vector2.zero;

            for (int ownIndex = 0; ownIndex < ownColliders.Length; ownIndex++)
            {
                Collider2D ownCollider = ownColliders[ownIndex];
                if (ownCollider == null || ownCollider.isTrigger) continue;

                ContactFilter2D filter = _contactFilter;
                int count = ownCollider.Overlap(filter, _overlapResults);
                for (int hitIndex = 0; hitIndex < count; hitIndex++)
                {
                    Collider2D otherCollider = _overlapResults[hitIndex];
                    if (otherCollider == null || otherCollider.isTrigger) continue;
                    if (otherCollider.GetComponentInParent<BlockController>() == this) continue;

                    ColliderDistance2D distance = Physics2D.Distance(ownCollider, otherCollider);
                    if (!distance.isOverlapped) continue;

                    totalCorrection += distance.normal * distance.distance;
                }
            }

            if (totalCorrection.sqrMagnitude <= 0.000001f) return;

            Vector3 position = transform.position;
            position.x += totalCorrection.x;
            position.y += totalCorrection.y;
            SetPosition(position);
        }

        Physics2D.SyncTransforms();
    }

    private void LockBlock()
    {
        if (!_isControlEnabled) return;
        _isControlEnabled = false;
        if (ActiveControlled == this) ActiveControlled = null;
        HasLanded = true;
        DestroyPlacementBeam();

        FinalizeDynamicControl();
        _appliedData?.OnLocked(this);

        if (_inputs != null) _inputs.Gameplay.Disable();

        ReportLockedToGameManager();

        OnBlockLocked?.Invoke();
    }

    // Handoff ends player control but does not declare the block settled. The landed maintenance
    // path waits for sustained low motion before micro-aligning/sleeping the body.
    private void FinalizeDynamicControl()
    {
        _rb.bodyType = RigidbodyType2D.Dynamic;
        _rb.constraints = RigidbodyConstraints2D.None;
        // Continuous detection only matters for the fast controlled descent (which is cast-driven
        // anyway). On resting bodies it just adds speculative-contact noise across the stack.
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
        if (_dynamicControlReady)
        {
            _rb.centerOfMass = _originalCenterOfMass;
        }
        _rb.gravityScale = ResolveLandedGravityScale();
        _landedMaintenanceSettleTimer = 0f;
        _stillnessAnchorPosition = _rb.position;
        _stillnessAnchorRotation = _rb.rotation;
        _stillnessTimer = 0f;
    }

    private float ResolveLandedGravityScale()
    {
        return Mathf.Max(0.01f, LandedGravityScale * _gravityScaleMultiplier);
    }

    private void ReportLockedToGameManager()
    {
        if (GameManager.Instance == null) return;

        GameManager.Instance.AddScore();
        GameManager.Instance.UpdateMaxHeight(GetHighestCellY());
    }

    private float GetHighestCellY()
    {
        _cellGeometry.Refresh();
        float highestY = transform.position.y;
        for (int i = 0; i < _cellGeometry.CellCenters.Count; i++)
        {
            if (_cellGeometry.CellCenters[i].y > highestY)
            {
                highestY = _cellGeometry.CellCenters[i].y;
            }
        }

        return highestY;
    }

}
