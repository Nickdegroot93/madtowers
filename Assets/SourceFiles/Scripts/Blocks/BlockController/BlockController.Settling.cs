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

    // Going to sleep must never move the body. A block that physics holds slightly off-grid or
    // tilted has an off-grid equilibrium: snapping it at sleep time teleports it away from that
    // equilibrium, the solver wakes it and pushes it back, and the next sleep snaps it again -
    // a metronomic, infinite twitch. Grid registration is decided once before physics handoff;
    // rejected or released Dynamic bodies are never registered later.
    private void SleepSettledBody()
    {
        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        _rb.Sleep();
        _fallingAway = false; // re-earned sleep = provably stable again, not falling debris
    }

    // --- Dynamic-debris knife-edge sleep guard (see PHYSICS.md I5) -----------------------
    // A quiet block whose centre of mass hangs horizontally outside its supporting contacts
    // is mid-tip; force-sleeping it freezes a coin on its rim. Which side of the floor that
    // happened on was decided by sub-millimetre float noise, so identical-looking edge
    // placements survived on one side and fell on the other. Deferring sleep lets gravity
    // resolve the balance honestly. Strictly bounded: after KnifeEdgeGraceSeconds of
    // staying quiet anyway (leaning, wedged, vine-held) the block sleeps normally - the
    // no-twitch guarantee is delayed for marginal debris, never lost.
    private const float KnifeEdgeGraceSeconds = 2f;
    private const float SupportSpanEpsilon = 0.01f;
    private static readonly ContactPoint2D[] SharedContactBuffer = new ContactPoint2D[16];
    private float _knifeEdgeDeferTime;

    private bool ShouldDeferSleepForKnifeEdge()
    {
        if (_knifeEdgeDeferTime >= KnifeEdgeGraceSeconds) return false;

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

        bool knifeEdged = hasSupport &&
            (centerOfMass.x < supportMinX - SupportSpanEpsilon ||
             centerOfMass.x > supportMaxX + SupportSpanEpsilon);
        if (!knifeEdged)
        {
            _knifeEdgeDeferTime = 0f;
            return false;
        }

        _knifeEdgeDeferTime += Time.fixedDeltaTime;
        return _knifeEdgeDeferTime < KnifeEdgeGraceSeconds;
    }

    private void HandleLandedMaintenance()
    {
        if (_rb == null) return;
        if (_rb.bodyType != RigidbodyType2D.Dynamic || _rb.IsSleeping()) return;

        if (!_fallingAway && _rb.linearVelocity.y < FallingAwaySpeed) _fallingAway = true;

        InvalidatePlacementOccupancyIfMoved();
        if (!sleepSettledBlocksOnLock) return;

        bool deferSleep = ShouldDeferSleepForKnifeEdge();

        UpdateStillnessWatchdog(deferSleep);
        if (_rb.IsSleeping()) return;

        // While deferred, the block stays fully live - no grid pull, no soft damping, no
        // settle timer - so nothing slows the tip that resolves the knife edge.
        if (IsSettled() && !deferSleep)
        {
            SoftDampSettledBody();
            _landedMaintenanceSettleTimer += Time.fixedDeltaTime;
            if (_landedMaintenanceSettleTimer >= settleTime)
            {
                // Sleep freezes the block exactly where physics left it (see SleepSettledBody).
                if (sleepSettledBlocksOnLock)
                {
                    SleepSettledBody();
                }
                _landedMaintenanceSettleTimer = 0f;
            }
        }
        else
        {
            _landedMaintenanceSettleTimer = 0f;
        }
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

    // The velocity-based settle check above can be defeated by a marginal contact configuration:
    // a block pivoting on a corner alternates between two contact states and the solver kicks it
    // every cycle, so its instantaneous velocity never stays quiet. But such a limit cycle has
    // zero NET movement, which is what this watchdog measures. Anything that is not actually
    // going anywhere is put to sleep, making persistent twitching structurally impossible.
    private void UpdateStillnessWatchdog(bool deferSleep)
    {
        if (!sleepSettledBlocksOnLock) return;

        float positionDrift = Vector2.Distance(_rb.position, _stillnessAnchorPosition);
        float rotationDrift = Mathf.Abs(Mathf.DeltaAngle(_rb.rotation, _stillnessAnchorRotation));
        if (positionDrift > stillnessPositionTolerance || rotationDrift > stillnessRotationToleranceDegrees)
        {
            _stillnessAnchorPosition = _rb.position;
            _stillnessAnchorRotation = _rb.rotation;
            _stillnessTimer = 0f;
            return;
        }

        _stillnessTimer += Time.fixedDeltaTime;
        // The timer keeps accruing while a knife-edge defers sleep, so the moment the
        // grace expires the watchdog acts immediately - the bounded Dynamic-debris stillness
        // guarantee is delayed for marginal blocks, never lost.
        if (_stillnessTimer >= stillnessTime && !deferSleep)
        {
            SleepSettledBody();
        }
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
