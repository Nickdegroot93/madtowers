using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// Landed maintenance (I1-I3): settle detection, the stillness watchdog, the knife-edge
// sleep defer, velocity-only grid pull, sleeping, external jolts, and freezing in place.
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
    // a metronomic, infinite twitch. Grid registration comes from honest sources instead: pieces
    // land exactly on-grid, and the awake-time velocity pull eases flat blocks toward column.
    private void SleepSettledBody()
    {
        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        _rb.Sleep();
    }

    // --- Knife-edge guard (bounded I3 refinement, see PHYSICS.md) -------------------------
    // A quiet block whose centre of mass hangs horizontally outside its supporting contacts
    // is mid-tip; force-sleeping it freezes a coin on its rim. Which side of the floor that
    // happened on was decided by sub-millimetre float noise, so identical-looking edge
    // placements survived on one side and fell on the other. Deferring sleep lets gravity
    // resolve the balance honestly. Strictly bounded: after KnifeEdgeGraceSeconds of
    // staying quiet anyway (leaning, wedged, vine-held) the block sleeps normally - I3's
    // no-twitch guarantee is delayed for marginal blocks, never lost.
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
        if ((!microAlignSettledBlocks && !sleepSettledBlocksOnLock) || _rb == null) return;
        if (_rb.bodyType != RigidbodyType2D.Dynamic || _rb.IsSleeping()) return;

        bool deferSleep = ShouldDeferSleepForKnifeEdge();

        UpdateStillnessWatchdog(deferSleep);
        if (_rb.IsSleeping()) return;

        // While deferred, the block stays fully live - no grid pull, no soft damping, no
        // settle timer - so nothing slows the tip that resolves the knife edge.
        if (IsSettled() && !deferSleep)
        {
            PullQuietBlockTowardGrid();
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
        // grace expires the watchdog acts immediately - the I3 guarantee is delayed for
        // marginal blocks, never lost.
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

    private void PullQuietBlockTowardGrid()
    {
        if (!microAlignSettledBlocks) return;

        // Tolerance contract: only ease blocks that are already essentially in place. A piece
        // that tipped, tilted, or slid beyond the caps can never reach its snapped X, so pulling
        // it every frame would turn it into a permanent agitator for the whole tower.
        float snappedRotation = SnapValue(_rb.rotation, RotationStep);
        if (Mathf.Abs(Mathf.DeltaAngle(_rb.rotation, snappedRotation)) > QuietPullMaxTiltDegrees) return;

        _cellGeometry.Refresh();
        float primaryX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float correction = SnapValue(primaryX, gridSpacing) - primaryX;
        if (Mathf.Abs(correction) <= 0.001f * gridSpacing) return;
        if (Mathf.Abs(correction) > Mathf.Max(0f, microAlignMaxColumnFraction) * gridSpacing) return;

        // Correct via a small velocity bias, never by writing the transform. Position writes
        // fought the contact solver (each step created fresh penetration that popped the
        // neighbours awake) and broke rigidbody interpolation, which made whole towers shimmer.
        // A sub-settle-threshold velocity keeps the solver in charge and never resets the
        // settle timer; whatever drift remains is closed by the bounded snap at sleep time.
        float maxPullSpeed = Mathf.Max(0f, quietGridPullMaxSpeedFraction) * gridSpacing;
        float pullSpeed = Mathf.Clamp(
            correction * Mathf.Clamp01(quietGridPullFactor) / Time.fixedDeltaTime,
            -maxPullSpeed, maxPullSpeed);

        Vector2 velocity = _rb.linearVelocity;
        velocity.x = pullSpeed;
        _rb.linearVelocity = velocity;
    }

    // External disturbance (earthquakes, wind, ...) as a velocity impulse - the only legal way
    // for outside systems to push a landed block (PHYSICS.md I1: never positions). Anchored
    // (Static) blocks ignore jolts by nature of their body type.
    public void ApplyJolt(Vector2 velocityChange)
    {
        if (_rb == null || _rb.bodyType != RigidbodyType2D.Dynamic) return;

        _rb.WakeUp();
        _rb.linearVelocity += velocityChange;
    }

    // Freezes this block permanently exactly where it currently is - used by anchor brick
    // variants and the cement-tower power-up. A Static body costs nothing in the solver and
    // acts as a player-made platform; it can never drift, wake, or be knocked over.
    public void FreezeInPlace()
    {
        if (_rb == null || _rb.bodyType == RigidbodyType2D.Static) return;

        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        _rb.bodyType = RigidbodyType2D.Static;
    }
}
