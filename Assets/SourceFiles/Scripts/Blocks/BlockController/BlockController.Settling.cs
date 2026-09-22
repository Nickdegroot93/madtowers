using System.Collections.Generic;
using UnityEngine;

// Maintenance for freely dynamic landed blocks: settle detection, the stillness watchdog,
// knife-edge sleep defer, external jolts, and permanent freezing. Grid-stable blocks never
// enter this path and are never corrected after placement.
public partial class BlockController
{
    private bool IsSettled()
    {
        return _rb.linearVelocity.magnitude <= settleLinearThreshold &&
               Mathf.Abs(_rb.angularVelocity) <= settleAngularThreshold;
    }

    // Sleeping never changes the pose or grid ownership. Dynamic blocks retain the
    // equilibrium physics found, including any tilt. Readiness is tracked per body;
    // SleepGroups coordinates the actual sleep across contacts and block joints.
    private void ResetSettlingTimers()
    {
        _landedMaintenanceSettleTimer = 0f;
        _stillnessTimer = 0f;
        _stillnessAnchorPosition = _rb.position;
        _stillnessAnchorRotation = _rb.rotation;
        _knifeEdgeDeferTime = 0f;
    }

    // --- Dynamic-debris knife-edge sleep guard (see PHYSICS.md I5) -----------------------
    // A quiet block whose centre of mass hangs horizontally outside its supporting contacts
    // is mid-tip; force-sleeping it freezes a coin on its rim. Which side of the floor that
    // happened on was decided by sub-millimetre float noise, so identical-looking edge
    // placements survived on one side and fell on the other. Deferring sleep lets gravity
    // resolve the balance honestly. Strictly bounded: after KnifeEdgeGraceSeconds of
    // staying quiet anyway (leaning, wedged, vine-held) the block becomes eligible for group
    // sleep. Moving neighbours must still finish settling before the group can sleep.
    private const float KnifeEdgeGraceSeconds = 2f;
    private const float SupportSpanEpsilon = 0.01f;
    private static readonly List<ContactPoint2D> SharedContactBuffer = new List<ContactPoint2D>(32);
    private float _knifeEdgeDeferTime;

    private bool ShouldDeferSleepForKnifeEdge()
    {
        if (_knifeEdgeDeferTime >= KnifeEdgeGraceSeconds) return false;

        if (!HasKnifeEdgeSupport())
        {
            _knifeEdgeDeferTime = 0f;
            return false;
        }

        _knifeEdgeDeferTime += Time.fixedDeltaTime;
        return _knifeEdgeDeferTime < KnifeEdgeGraceSeconds;
    }

    // A read-only check is also needed when a neighbour requests group sleep, before
    // this block's own FixedUpdate may have refreshed its timers.
    private bool HasKnifeEdgeSupport()
    {
        int count = _rb.GetContacts(SharedContactBuffer);
        Vector2 centerOfMass = _rb.worldCenterOfMass;
        bool hasSupport = false;
        float supportMinX = float.MaxValue;
        float supportMaxX = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = SharedContactBuffer[i];
            // Supporting contact: below the centre of mass and not a pure side graze.
            // (|normal.y| so the test is robust to contact normal orientation.)
            if (contact.point.y >= centerOfMass.y - 0.05f) continue;
            if (Mathf.Abs(contact.normal.y) < 0.5f) continue;
            hasSupport = true;
            supportMinX = Mathf.Min(supportMinX, contact.point.x);
            supportMaxX = Mathf.Max(supportMaxX, contact.point.x);
        }

        return hasSupport &&
            (centerOfMass.x < supportMinX - SupportSpanEpsilon ||
             centerOfMass.x > supportMaxX + SupportSpanEpsilon);
    }

    private void HandleLandedMaintenance()
    {
        if (_rb == null) return;
        if (_rb.bodyType != RigidbodyType2D.Dynamic || _rb.IsSleeping()) return;

        if (!_fallingAway && _rb.linearVelocity.y < FallingAwaySpeed) _fallingAway = true;

        InvalidatePlacementOccupancyIfMoved();
        if (!sleepSettledBlocksOnLock) return;

        bool deferSleep = ShouldDeferSleepForKnifeEdge();

        UpdateStillnessWatchdog();

        // While deferred, the block stays fully live - no grid pull, no soft damping, no
        // settle timer - so nothing slows the tip that resolves the knife edge.
        if (IsSettled() && !deferSleep)
        {
            SoftDampSettledBody();
            _landedMaintenanceSettleTimer += Time.fixedDeltaTime;
        }
        else
        {
            _landedMaintenanceSettleTimer = 0f;
        }

        if (!deferSleep && IsReadyForGroupSleep()) TrySleepSettledGroup();
    }

    private bool IsReadyForGroupSleep()
    {
        if (!HasLanded || _isControlEnabled || !sleepSettledBlocksOnLock) return false;
        if (_rb.IsSleeping()) return true;
        if (_knifeEdgeDeferTime < KnifeEdgeGraceSeconds && HasKnifeEdgeSupport()) return false;

        // Recheck current motion: a neighbour can ask before this body's maintenance
        // runs, and its timers may describe the preceding physics step.
        return (_landedMaintenanceSettleTimer >= settleTime && IsSettled()) ||
               (_stillnessTimer >= stillnessTime && IsWithinStillnessWindow());
    }

    private bool IsWithinStillnessWindow()
    {
        return Vector2.Distance(_rb.position, _stillnessAnchorPosition) <= stillnessPositionTolerance &&
               Mathf.Abs(Mathf.DeltaAngle(_rb.rotation, _stillnessAnchorRotation)) <= stillnessRotationToleranceDegrees;
    }

    private void InvalidatePlacementOccupancyIfMoved()
    {
        float positionTolerance = Mathf.Max(0.005f, gridSpacing * 0.05f);
        float rotationTolerance = 2f;
        if (Vector2.Distance(_rb.position, _lastPlacementOccupancyPosition) <= positionTolerance &&
            Mathf.Abs(Mathf.DeltaAngle(_rb.rotation, _lastPlacementOccupancyRotation)) <= rotationTolerance)
        {
            return;
        }

        _lastPlacementOccupancyPosition = _rb.position;
        _lastPlacementOccupancyRotation = _rb.rotation;
        _placementOccupancyVersion++;
    }

    // Contact jitter can keep instantaneous speed above the settle threshold while the
    // pose stays within a small window. The watchdog makes that body eligible for sleep;
    // it cannot sleep independently of neighbours that are still moving.
    private void UpdateStillnessWatchdog()
    {
        if (!IsWithinStillnessWindow())
        {
            _stillnessAnchorPosition = _rb.position;
            _stillnessAnchorRotation = _rb.rotation;
            _stillnessTimer = 0f;
            return;
        }

        // Accrue while knife-edge sleep is deferred, preserving the existing grace period.
        _stillnessTimer += Time.fixedDeltaTime;
    }

    private void SoftDampSettledBody()
    {
        float damping = Mathf.Clamp01(softSettleDampingFactor);
        _rb.linearVelocity *= damping;
        _rb.angularVelocity *= damping;
    }

    // External disturbances release a grid-stable connected structure exactly once, then act on
    // ordinary dynamic bodies. Permanently frozen blocks remain terrain and ignore jolts.
    public void ApplyJolt(Vector2 velocityChange)
    {
        if (_rb == null || IsFrozenInPlace) return;

        ReleaseGridStructureForForce();
        if (_rb.bodyType != RigidbodyType2D.Dynamic) return;
        ResetSettlingTimers();
        _rb.WakeUp();
        _rb.linearVelocity += velocityChange;
    }

    /// <summary>Current body speed (u/s); 0 for non-dynamic (anchored/frozen) bodies. A
    /// read-only physics peek for steadiness checks - the hold-steady countdown's motion
    /// abort (LevelRuntimeController.TowerInMotion). Never writes body state.</summary>
    public float CurrentSpeed => _rb != null && _rb.bodyType == RigidbodyType2D.Dynamic
        ? _rb.linearVelocity.magnitude : 0f;

    // Freezes this block permanently exactly where it currently is - used by anchor brick
    // variants and the Freeze power-up. A Static body costs nothing in the solver and
    // acts as a player-made platform; it can never drift, wake, or be knocked over.
    public void FreezeInPlace()
    {
        if (_rb == null || _rb.bodyType == RigidbodyType2D.Static) return;

        _isGridStable = false;
        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        _rb.bodyType = RigidbodyType2D.Static;
        _fallingAway = false; // frozen = stationary terrain again; the camera may frame it
    }

    // The Freeze power-up's entry point: kick off the crawling-ice overlay NOW, but delay the
    // actual physics lock by physicsDelaySeconds so a settling/teetering block keeps moving for
    // a beat and then locks as the ice grabs it (it reads as the freeze stopping the motion).
    // FreezeInPlace is idempotent, so a block already frozen just no-ops.
    public void Freeze(float visualSeconds, float physicsDelaySeconds)
    {
        if (_rb == null) return;

        FreezeFrost.Apply(this, visualSeconds); // the crawling-ice look lives in Abilities/Effects
        if (_rb.bodyType == RigidbodyType2D.Static) return;

        if (physicsDelaySeconds <= 0f) FreezeInPlace();
        else Invoke(nameof(FreezeInPlace), physicsDelaySeconds);
    }
}
