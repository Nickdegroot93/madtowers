using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// The player gestures the active piece accepts, as a run-local gate. Default Everything;
/// the first-run tutorial narrows it to the gestures already taught (plus the one being
/// taught) so a stray flick can't dump the piece mid-lesson (TUTORIAL.md). Every input
/// path - touch, mouse, keyboard - funnels through the gated BlockController entry points,
/// so narrowing this is sufficient to truly disable a control, not just its touch gesture.
/// </summary>
[System.Flags]
public enum PieceGestures
{
    None = 0,
    Rotate = 1 << 0,
    Move = 1 << 1,
    SoftDrop = 1 << 2, // held fast drop (drag down / S key)
    HardDrop = 1 << 3, // flick = latched auto drop
    Nudge = 1 << 4,
    Everything = Rotate | Move | SoftDrop | HardDrop | Nudge,
}

// Player commands: drag steps, the corner-zone nudge dash (with its failure slam and
// rebound lockout), rotation, fast drop, and the DAS auto-repeat that drives held input.
public partial class BlockController
{
    /// <summary>See <see cref="PieceGestures"/>. Owners that narrow it must restore
    /// Everything when done (the tutorial does in its teardown); also reset per run.</summary>
    public static PieceGestures AllowedGestures = PieceGestures.Everything;

    private static bool GestureAllowed(PieceGestures gesture) => (AllowedGestures & gesture) != 0;

    // Why a sideways step was (or wasn't) taken. Drag steps ignore this (a blocked drag
    // stays silent); the nudge dash reads it to decide between wind and a slam.
    private enum ColumnStepResult { Moved, Gated, OutOfBounds, BlockedByBlocks, BlockedByStatic }

    // Vortex bricks mirror left/right. Every horizontal-intent path resolves through these two
    // so the inversion rule lives in one place (a new input path can't forget to apply it).
    private bool InvertSteering => _appliedData != null && _appliedData.InvertHorizontalControls;
    private int ResolveSteerDirection(int rawDirection) => InvertSteering ? -rawDirection : rawDirection;

    // External (touch) horizontal step. Funnels into ShiftTargetColumn, so all grid,
    // placement-buffer and obstacle rules apply exactly as for keyboard movement.
    public void StepColumn(int direction)
    {
        if (!GestureAllowed(PieceGestures.Move)) return;
        // Only the drag/keyboard step path counts as the Move gesture; the nudge dash routes
        // through TryStepColumn separately and reports itself as Nudge.
        if (TryStepColumn(direction) == ColumnStepResult.Moved)
        {
            GameEvents.RaisePieceGesturePerformed(this, PieceGestures.Move);
        }
    }

    private ColumnStepResult TryStepColumn(int direction, bool collectBlockers = false)
    {
        if (!_isControlEnabled || direction == 0) return ColumnStepResult.Gated;
        if (GameManager.Instance != null &&
            (GameManager.Instance.IsGamePaused || GameManager.Instance.isGameOver))
        {
            return ColumnStepResult.Gated;
        }

        direction = ResolveSteerDirection(direction);
        return ShiftTargetColumn(direction > 0 ? 1 : -1, collectBlockers);
    }

    // The nudge dash is a timing skill: mistime it and the piece slams into whatever
    // blocked it - the impact shoves those bricks (loose ones can fall) and the rebound
    // locks further nudges out long enough that spamming can never be the optimal play.
    private const float NudgeFailLockoutSeconds = 0.5f;
    private const float NudgeSlamImpulse = 2f; // per blocking brick; Boulder (mass 4) barely budges

    // Shared across pieces on purpose: a rebound must not reset just because the old
    // piece locked and a fresh one spawned. UIManager dims the corner pills from this.
    private static float _nudgeLockedUntilTime;
    public static float NudgeLockoutRemaining => Mathf.Max(0f, _nudgeLockedUntilTime - Time.time);

    // The corner-zone nudge: a one-tap precision dash of EXACTLY one column - same grid
    // rules as StepColumn (so it can never do anything a drag couldn't). A dash that
    // moves gets sold with wind + a swoosh; a dash into bricks or rock is a failed
    // nudge: thud, shoved bricks, and a rebound lockout. Steps refused for non-physical
    // reasons (no control, paused, off the play area) stay silent - there is nothing
    // there to hit.
    public void Nudge(int direction)
    {
        if (!GestureAllowed(PieceGestures.Nudge)) return;
        if (NudgeLockoutRemaining > 0f) return; // still rebounding from a failed nudge

        int attempted = AttemptedStepDirection(direction);
        ColumnStepResult result = TryStepColumn(direction, collectBlockers: true);
        switch (result)
        {
            case ColumnStepResult.Moved:
                if (TryGetWorldBounds(out Bounds bounds)) DashWindFx.Spawn(bounds, attempted);
                SfxPlayer.Play("nudge", 0.6f, 0.05f);
                break;

            case ColumnStepResult.BlockedByBlocks:
            case ColumnStepResult.BlockedByStatic:
                FailNudge(result == ColumnStepResult.BlockedByBlocks, attempted);
                break;
        }

        // Every deliberate corner tap on a controllable piece counts as the gesture - including
        // an out-of-bounds dash that stays silent in gameplay terms. Only non-attempts (paused,
        // control already ended) are excluded: pressing the button IS the lesson.
        if (result != ColumnStepResult.Gated)
        {
            GameEvents.RaisePieceGesturePerformed(this, PieceGestures.Nudge);
        }
    }

    // The direction the piece actually dashed toward (Vortex inverts the input) - the
    // slam impulse and impact FX must match the physical hit, not the button pressed.
    private int AttemptedStepDirection(int inputDirection)
    {
        return ResolveSteerDirection(inputDirection > 0 ? 1 : -1);
    }

    private void FailNudge(bool hitBricks, int direction)
    {
        _nudgeLockedUntilTime = Time.time + NudgeFailLockoutSeconds;

        if (hitBricks) SlamBlockingBricks(direction);

        if (TryGetWorldBounds(out Bounds bounds)) NudgeImpactFx.Spawn(bounds, direction);
        TowerCameraController.Impact(0.08f, 0.15f);
        SfxPlayer.Play("nudge_thud_01", 0.6f, 0.07f);
    }

    // The failed dash hits the blocking bricks with real force. Invariant I1: landed
    // bodies are only ever influenced through velocity - an impulse via the solver is
    // the sanctioned mechanism. Well-supported bricks absorb it through friction; a
    // loose or overhanging brick can genuinely be knocked off.
    private void SlamBlockingBricks(int direction)
    {
        Vector2 impulse = new Vector2(direction * NudgeSlamImpulse, 0f);
        for (int i = 0; i < _stepBlockers.Count; i++)
        {
            BlockController block = _stepBlockers[i];
            if (block == null || block._rb == null) continue;
            if (block._rb.bodyType != RigidbodyType2D.Dynamic) continue; // anchored/frozen stay rock

            block._rb.WakeUp();
            block._rb.AddForce(impulse, ForceMode2D.Impulse);
        }
    }

    // falling, so they stay on the same grid rules as horizontal movement.
    public void RotateLeft()
    {
        if (!_isControlEnabled || !GestureAllowed(PieceGestures.Rotate)) return;
        if (!CanRotateVariant) { _appliedData?.OnRotationDenied(this, -1); return; }
        _targetAngleZ -= RotationStep;
        SfxPlayer.Play("rotate-swoosh", 0.5f, 0.06f);
        GameEvents.RaisePieceGesturePerformed(this, PieceGestures.Rotate);
    }

    public void RotateRight()
    {
        if (!_isControlEnabled || !GestureAllowed(PieceGestures.Rotate)) return;
        if (!CanRotateVariant) { _appliedData?.OnRotationDenied(this, 1); return; }
        _targetAngleZ += RotationStep;
        SfxPlayer.Play("rotate-swoosh", 0.5f, 0.06f);
        GameEvents.RaisePieceGesturePerformed(this, PieceGestures.Rotate);
    }

    /// <summary>Whether the applied variant permits rotation at all (Locked-style bricks
    /// refuse it). Public so the tutorial can ask the piece itself instead of re-deriving
    /// the rule from BlockIdentity - the piece's applied data is the truth.</summary>
    public bool CanRotateVariant => _appliedData == null || _appliedData.CanRotate;

    // Fast Drop
    // External (touch) fast-drop request; OR-ed with the keyboard each frame in Update, which
    // owns the SoftDrop gesture-event edge (so touch and keyboard report identically).
    private bool _externalFastDrop;
    public void SetFastDrop(bool active)
    {
        if (active && !GestureAllowed(PieceGestures.SoftDrop)) return; // releasing always allowed
        _externalFastDrop = active;
    }

    // Latched full-speed descent (triggered by a quick downward flick): stays on until the
    // piece lands, no held finger required. The plunge is COMMITTED - all horizontal steps
    // are gated off in ShiftTargetColumn for its duration (hard-drop convention); rotation
    // stays available. Flicking means deciding the column first.
    private bool _autoDrop;
    public void StartAutoDrop()
    {
        if (!_isControlEnabled || !GestureAllowed(PieceGestures.HardDrop)) return;
        _autoDrop = true;
        GameEvents.RaisePieceGesturePerformed(this, PieceGestures.HardDrop);
    }

    /// <summary>The system-initiated plunge (magma melt cells): same committed descent as a
    /// player flick, but it bypasses the gesture gate and never reads as a player gesture -
    /// a machine drop must neither be blocked by a tutorial step nor complete one.</summary>
    public void ForceAutoDrop()
    {
        if (_isControlEnabled) _autoDrop = true;
    }

    // Fission hover: hold the piece at the spawn line (steering stays live) until the player
    // commits a drop. Any descent intent in SteerWhileFalling auto-clears this, so the normal
    // flick / fast-drop gesture is the commit with no extra input plumbing.
    public void SetDescentSuspended(bool suspended)
    {
        if (suspended && !_descentSuspended) _suspendedElapsed = 0f; // fresh watchdog window per hover
        _descentSuspended = suspended;
    }

    // Shared left/right auto-repeat (DAS) timing. `step` is invoked once on initial press, then
    // repeatedly at `dasRate` after the initial `dasDelay` while the direction is held.
    private void ProcessHorizontalDas(System.Action<int> step)
    {
        int inputDir = 0;
        if (_moveInput.x > 0.5f) inputDir = 1;
        else if (_moveInput.x < -0.5f) inputDir = -1;

        if (inputDir != 0)
        {
            if (inputDir != _lastInputDirection)
            {
                step(inputDir);
                _dasTimer = dasDelay;
                _lastInputDirection = inputDir;
                _dasActive = true;
            }
            else if (_dasActive)
            {
                _dasTimer -= Time.deltaTime;
                if (_dasTimer <= 0f)
                {
                    step(inputDir);
                    _dasTimer = dasRate;
                }
            }
        }
        else
        {
            _lastInputDirection = 0;
            _dasActive = false;
        }
    }

    // A flick means "I'm done with this piece" - it plunges much faster than held fast
    // drop. Safe at any speed: the descent cast sweeps the full per-step distance (no
    // tunneling) and landing velocity is capped by maxLandingImpactSpeed regardless,
    // so fast play hits exactly as softly as slow play.
    private const float AutoDropBoost = 2.5f;

    // Current downward speed (units/sec), including the fast-drop boost when S / down is
    // held or a flick latched the auto-drop.
    private float GetActiveFallSpeed()
    {
        // Fast paths (flick / held fast drop / down) use the BASE speed: the player chose to
        // go fast, so ability slows (Air Brake, recovery / slo-mo) never apply. Only normal
        // descent gets the ability factor. _isFastDrop already folds in every soft-drop
        // source, including the held down-axis (computed each Update).
        if (_autoDrop) return fallSpeed * fastDropMultiplier * AutoDropBoost;
        if (_isFastDrop) return fallSpeed * fastDropMultiplier;
        // Slowburn's per-piece initial-slow folds in here (normal descent only); it lapses to 1 after
        // its window, so the piece resumes full ramped speed. A flick/fast-drop above bypasses it.
        return fallSpeed * _normalFallSpeedFactor * CurrentInitialSlowFactor();
    }

}
