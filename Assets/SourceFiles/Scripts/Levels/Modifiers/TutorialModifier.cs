using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// First-run gesture tutorial (full design in TUTORIAL.md). A standalone modifier: attach it to
/// one level and it teaches the controls once, then never again. On a save where the tutorial is
/// already done it is a complete no-op, so the level plays normally - which is what makes it
/// "attachable to any level".
///
/// Per teaching piece the flow is: spawn -> descend briskly to a working height with ALL input
/// gated (so the lesson never plays behind the HUD strip and the player can't fiddle early) ->
/// hover the piece + show the step -> wait for the real gesture -> success beat -> next step.
/// Gating is FORCED-ORDER but CUMULATIVE: each step allows every gesture already taught plus the
/// one being taught (research: disabling a gesture you just rewarded breaks the learning
/// contract). A learned drop used "early" just lands the piece - the current step re-arms on the
/// next spawn, so nothing can soft-lock.
///
/// Curriculum (two pieces, both forced to visibly-rotatable shapes from the level's own bag):
///   Piece 1: Rotate (tap) -> Move (drag) -> Soft drop (drag down + hold; rides to the floor)
///   Piece 2: Nudge (corner pills lit) -> Hard drop (flick; instant - completes the tutorial)
/// Then a short "You're ready" coda shows the level goal and fades; the rest of the level is the
/// free-play quick win. Skipping plays a shorter coda that still shows the goal (the runtime's
/// own banner is suppressed while the tutorial owns the intro messaging).
/// </summary>
[CreateAssetMenu(fileName = "Tutorial", menuName = "Stacking/Levels/Modifiers/Tutorial")]
public class TutorialModifier : LevelModifier
{
    private enum Phase { Inactive, PreRoll, Armed, Beat, AwaitPiece, Coda }

    private struct Step
    {
        public PieceGestures Gesture;
        public string Caption;   // <= 8 words (research: text is a caption, the demo teaches)
        public int RequiredReps;
        public bool EndsPiece;   // a drop: it releases the hover and rides the piece down
    }

    // The curriculum. Data here (not serialized) so the asset can never go stale against the
    // code. The per-step gesture gate is the cumulative OR of everything taught so far (see
    // AllowedThrough) - never hand-maintained, so it can't drift from the cumulative rule.
    private static readonly Step[] Steps =
    {
        new Step { Gesture = PieceGestures.Rotate,   Caption = "Tap to rotate",              RequiredReps = 1, EndsPiece = false },
        new Step { Gesture = PieceGestures.Move,     Caption = "Drag left or right to move", RequiredReps = 3, EndsPiece = false },
        new Step { Gesture = PieceGestures.SoftDrop, Caption = "Drag down and hold",         RequiredReps = 1, EndsPiece = true },
        new Step { Gesture = PieceGestures.Nudge,    Caption = "Tap a corner to nudge",      RequiredReps = 1, EndsPiece = false },
        new Step { Gesture = PieceGestures.HardDrop, Caption = "Flick down to slam!",        RequiredReps = 1, EndsPiece = true },
    };

    private static readonly int NudgeStepIndex =
        System.Array.FindIndex(Steps, s => s.Gesture == PieceGestures.Nudge);

    // Preferred teaching shapes, tried in order; every candidate must also pass the
    // visible-rotation test, and unknown names fall back to any shape that passes it.
    private static readonly string[] TeachingShapePreference = { "L", "J", "T", "S", "Z", "I", "Domino" };

    // Pre-roll: the fresh piece descends at this factor of normal speed to the working height,
    // so the lesson starts promptly without reading as a drop. The generous time cap only
    // catches a piece that genuinely cannot reach the line (should never happen in practice).
    private const float PreRollSpeedFactor = 2.2f;
    private const float PreRollTimeoutSeconds = 8f;
    // Once a pre-rolling piece has LANDED before reaching the settle line (a tall tower after
    // many early drops), stop insisting on the low hover: arm at a relaxed line just under the
    // strip, with a short cap so the input lock can never loop.
    private const float ArmWithoutSettleSeconds = 1.2f;

    private const float BeatSeconds = 0.7f;            // success registers before the next ask
    private const float HandIdleReshowSeconds = 2.8f;  // re-show the demo after this much idle
    private const float CodaHoldSeconds = 2.6f;
    private const float SkipCodaHoldSeconds = 1.3f;    // skipped: just long enough to read the goal
    private const float CodaFadeSeconds = 0.9f;
    private const float GroupFadePerSecond = 4f;

    // Nudge-pill spotlight: fully lit while the nudge step teaches, kept faintly lit for the
    // rest of the tutorial (hidden controls decay, never cut), gone with the coda.
    private const float NudgeBoostTeaching = 1f;
    private const float NudgeBoostAfter = 0.45f;

    private static readonly Color Accent = new Color(0.42f, 0.78f, 1f, 1f);
    private static readonly Color DotIdle = new Color(1f, 1f, 1f, 0.22f);
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.24f);

    private Phase _phase = Phase.Inactive;
    private bool _subscribed;
    private int _stepIndex;
    private int _reps;
    private string _goalText;

    private BlockController _piece;
    private float _preRollTime;
    private bool _armWithoutSettle;
    private bool _beatArmsSamePiece;
    private float _animTime;
    private float _beatTime;
    private float _codaTime;
    private float _idleTime; // seconds since the last touch; the demo shows at/after the reshow threshold
    private Vector2 _beatBurstAt;

    // Overlay. Everything except Skip lives under _group so pre-roll/steps can crossfade as one.
    private GameObject _overlayRoot;
    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _stripRect;
    private TextMeshProUGUI _caption;
    private TextMeshProUGUI _subline;
    private GameObject _skipRoot;
    private Image[] _dots;
    private RectTransform _hand;
    private Image _handImage;
    private RectTransform _arrow;
    private Image _arrowImage;
    private RectTransform _ring;
    private Image _ringImage;

    // Screen geometry. Derived from the live HUD/camera/canvas and re-checked every update -
    // scaleFactor is not trustworthy on the overlay's build frame, and RESPONSIVE.md requires
    // hand-positioned UI to re-apply on screen changes (rotation, foldables, safe area).
    private float _stripTopVp;    // viewport Y of the instruction strip's top edge
    private float _stripBottomVp; // viewport Y of its bottom edge (the relaxed arm line hangs off it)
    private float _settleVp;      // viewport Y a teaching piece descends to before its lesson
    private float _skipBaseX;     // skip pill offset, incl. the safe-area right inset
    private const float StripHeight = 210f; // canvas units

    // Micro-animation state (the "juice"): pop timers rest above their window when idle.
    private const float PopSettleSeconds = 0.8f;
    private const float DotPopSeconds = 0.45f;
    private float _liveTime;
    private float _captionPop = 10f;
    private float _sublinePop = 10f;
    private float _dotPopTime = 10f;
    private float _appliedSlideAlpha = -1f;

    /// <summary>While teaching, the tutorial owns the intro messaging: it shows the goal itself
    /// in its coda (earned or skipped), so the runtime's banner must not talk over the lessons.</summary>
    public override bool SuppressesGoalBanner => _phase != Phase.Inactive;

    public override void OnLevelStart(LevelModifierContext context)
    {
        // Standalone gate: a completed tutorial makes this modifier inert, so the level is normal.
        if (ProgressStore.IsTutorialCompleted()) return;

        _phase = Phase.AwaitPiece; // the camera-intro gate holds the first spawn; we wait for it
        _stepIndex = 0;
        _reps = 0;
        _armWithoutSettle = false;
        _goalText = context != null && context.Level != null ? context.Level.Instruction : null;
        BlockController.AllowedGestures = PieceGestures.None;
        ForceTeachingShapes(context);
        Subscribe();

        // Built now, invisible (alpha 0): the intro camera pan hides the construction cost -
        // including the ghost hand's procedural texture - instead of a gameplay frame paying it.
        BuildOverlay();

        // Handle the (unexpected) case of a piece existing before we started.
        if (BlockController.ActiveControlled != null) BeginPreRoll(BlockController.ActiveControlled);
    }

    public override void OnUpdate(LevelModifierContext context, float deltaTime)
    {
        if (_phase == Phase.Inactive) return;

        RefreshScreenGeometry();
        UpdateGroupFade(deltaTime);
        UpdateStripAnimation(deltaTime);

        switch (_phase)
        {
            case Phase.PreRoll: UpdatePreRoll(deltaTime); break;
            case Phase.Armed:   UpdateArmed(deltaTime); break;
            case Phase.Beat:    UpdateBeat(deltaTime); break;
            case Phase.Coda:    UpdateCoda(deltaTime); break;
        }
    }

    public override void OnLevelEnd(LevelModifierContext context) => Teardown();

    // ---- Teaching shapes -------------------------------------------------------------------

    // Front-load the spawn queue with two visibly-rotatable shapes and pin their variants to
    // the shape default, so the rotate lesson can't land on a square (or roll an ambient
    // variant that refuses to rotate). The NEXT preview follows automatically - the queue IS
    // the preview.
    private void ForceTeachingShapes(LevelModifierContext context)
    {
        Spawner spawner = context != null ? context.Spawner : null;
        if (spawner == null) return;

        BlockDefinition first = PickTeachingShape(spawner, exclude: null);
        if (first == null) return; // bag has nothing usable; the level's own rolls play
        BlockDefinition second = PickTeachingShape(spawner, exclude: first) ?? first;

        // Insert-at-front order: the piece requeued LAST spawns FIRST.
        spawner.RequeueDefinition(second);
        spawner.RequeueDefinition(first);
        if (first.DefaultData != null)
        {
            spawner.QueueVariantOverride(first.DefaultData, 1);
            // The override queue is positional (consumed front-first by bag spawns): the second
            // slot may only be pinned when the first is, or its pin lands on the first piece.
            if (second.DefaultData != null) spawner.QueueVariantOverride(second.DefaultData, 1);
        }
    }

    private static BlockDefinition PickTeachingShape(Spawner spawner, BlockDefinition exclude)
    {
        IReadOnlyList<BlockDefinition> bag = spawner.ConfiguredBlockBag;
        if (bag == null) return null;

        for (int p = 0; p < TeachingShapePreference.Length; p++)
        {
            for (int i = 0; i < bag.Count; i++)
            {
                BlockDefinition candidate = bag[i];
                if (candidate == null || candidate.Prefab == null || candidate == exclude) continue;
                if (candidate.DisplayName == TeachingShapePreference[p] && IsVisiblyRotatable(candidate))
                {
                    return candidate;
                }
            }
        }

        // Unknown names (renamed or themed content): any shape whose quarter-turn visibly
        // changes it still qualifies - the lesson cares about geometry, not naming.
        for (int i = 0; i < bag.Count; i++)
        {
            BlockDefinition candidate = bag[i];
            if (candidate == null || candidate.Prefab == null || candidate == exclude) continue;
            if (IsVisiblyRotatable(candidate)) return candidate;
        }
        return null;
    }

    // A shape can teach rotation only if (a) its default variant allows rotating at all and
    // (b) a 90-degree turn visibly changes it - the 2x2 square and the single Pip map onto
    // themselves under a quarter turn, so their rotation is invisible. Cell centres come from
    // BlockCellGeometry (the canonical cell-geometry source), read off the prefab asset.
    private static bool IsVisiblyRotatable(BlockDefinition definition)
    {
        if (definition.DefaultData != null && !definition.DefaultData.CanRotate) return false;

        var geometry = new BlockCellGeometry();
        geometry.Cache(definition.Prefab);
        geometry.Refresh();
        IReadOnlyList<Vector2> centers = geometry.CellCenters;
        if (centers.Count == 0) return false;

        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < centers.Count; i++) centroid += centers[i];
        centroid /= centers.Count;

        var layout = new HashSet<Vector2Int>();
        for (int i = 0; i < centers.Count; i++) layout.Add(Quantize(centers[i] - centroid));
        for (int i = 0; i < centers.Count; i++)
        {
            Vector2 d = centers[i] - centroid;
            if (!layout.Contains(Quantize(new Vector2(-d.y, d.x)))) return true;
        }
        return false; // 4-fold symmetric: rotating it looks like nothing happened
    }

    // Cell centers sit on multiples of half the grid; x4 rounding compares them robustly.
    private static Vector2Int Quantize(Vector2 v) =>
        new Vector2Int(Mathf.RoundToInt(v.x * 4f), Mathf.RoundToInt(v.y * 4f));

    // The cumulative gesture gate: everything taught so far plus the step being taught.
    private static PieceGestures AllowedThrough(int stepIndex)
    {
        PieceGestures mask = PieceGestures.None;
        for (int i = 0; i <= stepIndex && i < Steps.Length; i++) mask |= Steps[i].Gesture;
        return mask;
    }

    // ---- Per-piece pre-roll ------------------------------------------------------------------

    private void BeginPreRoll(BlockController piece)
    {
        _piece = piece;
        _preRollTime = 0f;
        _phase = Phase.PreRoll;
        _animTime = 0f;

        // Full lock while the piece rides in: touch is swallowed, and the gesture gate covers
        // the keyboard. It descends briskly (not a drop) so the lesson starts promptly.
        TouchGestureInput.Suspended = true;
        BlockController.AllowedGestures = PieceGestures.None;
        if (piece != null)
        {
            piece.SetDescentSuspended(false);
            piece.SetNormalFallSpeedFactor(PreRollSpeedFactor);
        }

        // The whole lesson is visible from the first pre-roll frame - caption, ghost hand AND
        // (for the nudge step) the lit corner pills - so the ask is never ambiguous while the
        // piece rides in. The demo anchors to the live piece, so it simply tracks the descent.
        ApplyStepVisuals();
        _groupVisible = true;
    }

    private void UpdatePreRoll(float deltaTime)
    {
        if (_piece == null) { EnterAwaitPiece(); return; } // piece lost; wait for the next
        if (_piece.HasLanded)
        {
            // The tower outgrew the settle line (many early drops): hovering failed. Release
            // the lock and arm the NEXT piece promptly wherever it is - never loop the lock.
            _armWithoutSettle = true;
            EnterAwaitPiece();
            return;
        }

        _preRollTime += deltaTime;
        _animTime += deltaTime;
        UpdateHandAnimation(); // the demo already plays while the piece rides in

        // Degraded mode (a previous pre-roll landed before settling: the tower is tall) uses a
        // relaxed line - just below the strip - so the lesson is still fully visible, plus a
        // short cap so the input lock can never loop. Reaching EITHER line restores normal
        // mode, so the strict settle height comes back once the tower allows it again.
        float settleLine = _armWithoutSettle ? _stripBottomVp - 0.04f : _settleVp;
        if (HasReached(_piece, settleLine))
        {
            _armWithoutSettle = false;
            ArmStep();
        }
        else if (_preRollTime >= (_armWithoutSettle ? ArmWithoutSettleSeconds : PreRollTimeoutSeconds))
        {
            ArmStep();
        }
    }

    private bool HasReached(BlockController piece, float viewportY)
    {
        Camera cam = TowerCameraController.Camera;
        if (cam == null) return _preRollTime >= 1.5f; // no camera to measure with - just start
        return cam.WorldToViewportPoint(piece.transform.position).y <= viewportY;
    }

    // The between-pieces idle: input unlocked at the current lesson's gate, demo hidden.
    private void EnterAwaitPiece()
    {
        _phase = Phase.AwaitPiece;
        SetInputGate(AllowedThrough(_stepIndex));
        HideDemo();
    }

    // The one invariant the tutorial must never get wrong - handing input back - lives in
    // one place: release the hard lock and open the gesture gate to the given width.
    private static void SetInputGate(PieceGestures gate)
    {
        TouchGestureInput.Suspended = false;
        BlockController.AllowedGestures = gate;
    }

    // ---- Step machine --------------------------------------------------------------------------

    private void ArmStep()
    {
        if (_stepIndex >= Steps.Length) return;
        if (_piece == null) { EnterAwaitPiece(); return; }

        _piece.SetDescentSuspended(true); // hover for the lesson
        RestoreNormalSpeed(_piece);
        SetInputGate(AllowedThrough(_stepIndex));

        // A piece that cannot rotate (a Locked-style variant on an unpinned level) could never
        // raise the Rotate gesture - skip that lesson rather than strand it behind its gate.
        // Input is already unlocked above, so the success beat plays with live controls.
        if (Steps[_stepIndex].Gesture == PieceGestures.Rotate && !_piece.CanRotateVariant)
        {
            CompleteStep();
            return;
        }

        // No _animTime reset: the demo has been looping since the pre-roll began and must not
        // stutter at the moment input unlocks. Seeding _idleTime at the threshold keeps the
        // demo visible from the first armed frame without registering as a reshow crossing.
        _phase = Phase.Armed;
        _idleTime = HandIdleReshowSeconds;
        ApplyStepVisuals();
    }

    private void UpdateArmed(float deltaTime)
    {
        if (_piece == null) { EnterAwaitPiece(); return; } // piece lost; re-arm on the next spawn

        // The demo hides the instant a finger is down (the player is trying - don't talk over
        // them) and returns after a beat of inactivity if the step still isn't done.
        if (IsPointerDown())
        {
            _idleTime = 0f;
            HideDemo(); // color setters early-out on equal values - free while held
            return;
        }

        float previousIdle = _idleTime;
        _idleTime += deltaTime;
        if (_idleTime < HandIdleReshowSeconds) return;              // still resting after a touch
        if (previousIdle < HandIdleReshowSeconds) _animTime = 0f;   // just crossed: restart the loop

        _animTime += deltaTime;
        UpdateHandAnimation();
    }

    // A gesture counts while its step is armed, during the previous step's success beat (the
    // gate is already open and the caption already asks for it - a fast player must get
    // credit), and on the still-falling previous piece between lessons.
    private void HandlePieceGesture(BlockController block, PieceGestures gesture)
    {
        if (block == null || block != _piece) return;
        if (_phase != Phase.Armed && _phase != Phase.Beat && _phase != Phase.AwaitPiece) return;
        if (_stepIndex >= Steps.Length) return;

        Step step = Steps[_stepIndex];
        if (step.Gesture != gesture) return;

        _reps++;
        if (_reps < step.RequiredReps)
        {
            if (_subline != null)
            {
                _subline.text = $"{_reps} / {step.RequiredReps}";
                _sublinePop = 0f;
            }
            return;
        }

        CompleteStep();
    }

    private void CompleteStep()
    {
        SfxPlayer.Play("pop_01", 0.75f, 0.04f);
        _beatBurstAt = _piece != null ? OverlayPointFromWorld(_piece.transform.position) : Vector2.zero;
        _dotPopTime = 0f; // the just-earned dot (index _stepIndex - 1 after the increment) pops

        // The beat may only hand back to the SAME piece when it is actually still hovering
        // for a lesson (Armed, or a chained completion during a beat). A step credited on the
        // still-falling previous piece (AwaitPiece) must never re-suspend a committed descent.
        _beatArmsSamePiece = !Steps[_stepIndex].EndsPiece &&
                             (_phase == Phase.Armed || _phase == Phase.Beat);

        _reps = 0;
        _stepIndex++;
        HideDemo();

        if (_stepIndex >= Steps.Length)
        {
            BeginCoda(earned: true);
            return;
        }

        // Success beat: the win registers before the next ask. The dots/caption/gesture gate
        // already flip to the next step so a fast player is never held back by the pause -
        // the gate is cumulative, so opening it early can't un-teach anything.
        _phase = Phase.Beat;
        _beatTime = 0f;
        BlockController.AllowedGestures = AllowedThrough(_stepIndex);
        ApplyStepVisuals();
    }

    private void UpdateBeat(float deltaTime)
    {
        _beatTime += deltaTime;

        // Expanding ring burst where the piece was - the multi-sensory "got it".
        float t = Mathf.Clamp01(_beatTime / BeatSeconds);
        SetRing(_beatBurstAt, Mathf.Lerp(0.7f, 2.2f, t), Mathf.Lerp(0.65f, 0f, t));

        if (_beatTime < BeatSeconds) return;
        SetRing(default, 0f, 0f);

        // A non-drop step completed on a still-hovering piece arms the next lesson right here;
        // anything else (a drop, a credit earned on a falling piece, a piece that ended in the
        // meantime) waits for the next spawn.
        if (_beatArmsSamePiece && _piece != null && _piece == BlockController.ActiveControlled)
        {
            ArmStep();
        }
        else
        {
            EnterAwaitPiece();
        }
    }

    // ---- Completion ----------------------------------------------------------------------------

    // Shared exit into the coda. Earned: the last gesture just fired (the piece is mid-plunge),
    // celebrate and show the goal. Skipped: hand control back and still show the goal briefly -
    // the runtime's own banner was suppressed, and even a player who knows the controls needs
    // the objective. Marked done IMMEDIATELY either way: quitting during the coda or the free
    // build must never re-show the tutorial.
    private void BeginCoda(bool earned)
    {
        ProgressStore.MarkTutorialCompleted();
        SetInputGate(PieceGestures.Everything);
        if (!earned && _piece != null)
        {
            _piece.SetDescentSuspended(false);
            RestoreNormalSpeed(_piece);
        }

        _phase = Phase.Coda;
        _codaTime = earned ? 0f : CodaHoldSeconds - SkipCodaHoldSeconds;
        HideDemo();
        if (_skipRoot != null) _skipRoot.SetActive(false);
        if (earned) SfxPlayer.Play("ui-start-game", 0.6f);

        if (_caption != null)
        {
            _caption.text = earned ? "You're ready!"
                : string.IsNullOrWhiteSpace(_goalText) ? "Good luck!" : _goalText;
            _caption.color = earned ? Accent : RuntimeUiKit.TitleColor;
            _captionPop = 0f;
        }
        if (_subline != null)
        {
            _subline.text = earned && !string.IsNullOrWhiteSpace(_goalText) ? _goalText : "";
            _sublinePop = 0f;
        }
        if (_dots != null)
        {
            for (int i = 0; i < _dots.Length; i++)
            {
                if (_dots[i] == null) continue;
                if (earned) _dots[i].color = Accent;
                _dots[i].rectTransform.localScale = Vector3.one; // freeze the breathing cleanly
            }
        }
    }

    private void UpdateCoda(float deltaTime)
    {
        _codaTime += deltaTime;

        float fade = Mathf.Clamp01((_codaTime - CodaHoldSeconds) / CodaFadeSeconds);
        if (_group != null) _group.alpha = 1f - fade; // direct: the fade IS the animation
        // _stepIndex is frozen throughout the coda, so the boost tier is derivable live.
        UIManager.SetNudgeGuideBoost(NudgeBoostFor(_stepIndex) * (1f - fade));

        if (_codaTime >= CodaHoldSeconds + CodaFadeSeconds) Teardown();
    }

    private static void RestoreNormalSpeed(BlockController piece)
    {
        if (piece == null) return;
        piece.SetNormalFallSpeedFactor(
            GameManager.Instance != null ? GameManager.Instance.AbilityFallSpeedFactor : 1f);
    }

    // ---- Event wiring --------------------------------------------------------------------------

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        GameEvents.BlockSpawned += HandleBlockSpawned;
        GameEvents.PieceGesturePerformed += HandlePieceGesture;
        GameEvents.GameOver += HandleGameOver;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        GameEvents.BlockSpawned -= HandleBlockSpawned;
        GameEvents.PieceGesturePerformed -= HandlePieceGesture;
        GameEvents.GameOver -= HandleGameOver;
    }

    // Every fresh piece pre-rolls into the current lesson - including one spawned early because
    // the player used an already-learned drop mid-step (allowed; the step simply re-arms).
    private void HandleBlockSpawned(BlockController block, BlockData variant)
    {
        if (_phase == Phase.Inactive || _phase == Phase.Coda) return;
        BeginPreRoll(block);
    }

    // The run died mid-lesson: modifier updates stop on game over, so tear down NOW - the
    // overlay must not sit frozen over the game-over flow, and the input lock must not
    // outlive the run that owned it. (The tutorial itself stays unfinished for next time.)
    private void HandleGameOver(int score, float maxHeight) => Teardown();

    // ---- Overlay -------------------------------------------------------------------------------

    private void BuildOverlay()
    {
        RuntimeUiKit.EnsureEventSystem();
        _overlayRoot = RuntimeUiKit.CreateOverlayCanvas("Tutorial", 3400);
        _canvas = _overlayRoot.GetComponent<Canvas>();

        // Everything but Skip fades as one block (pre-roll shows it early, the coda fades it out).
        GameObject content = new GameObject("Content", typeof(RectTransform));
        RectTransform contentRect = (RectTransform)content.transform;
        contentRect.SetParent(_overlayRoot.transform, false);
        RuntimeUiKit.Stretch(contentRect);
        _group = content.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.alpha = 0f;

        // A gentle dim to focus attention without hiding the board (gestures pass through -
        // TouchGestureInput reads devices directly, and the group never blocks raycasts).
        RuntimeUiKit.CreateBackdrop(content.transform, DimColor);

        BuildDemo(content.transform); // under the strip so text always reads over the hand
        BuildStrip(content.transform);
        BuildSkip();
        RefreshScreenGeometry();
    }

    // The strip sits directly under the real HUD (queried, not guessed: notches and the NEXT
    // card move its bottom edge), and the teaching piece hovers well below the strip - the two
    // can never overlap, on any aspect. Cheap enough to re-derive every update, which also
    // rides out the build-frame scaleFactor=1 window and any later screen change.
    private void RefreshScreenGeometry()
    {
        if (_canvas == null) return;

        float previousStripTop = _stripTopVp;
        float previousSkipX = _skipBaseX;

        Camera cam = TowerCameraController.Camera;
        float hudBottomVp = 0.865f;
        if (cam != null && UIManager.Instance != null &&
            UIManager.Instance.TryGetTopHudBottomWorldY(cam, out float hudWorldY))
        {
            hudBottomVp = Mathf.Clamp(cam.WorldToViewportPoint(new Vector3(0f, hudWorldY, 0f)).y, 0.7f, 0.95f);
        }
        _stripTopVp = hudBottomVp - 0.006f;

        float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
        float stripHeightVp = StripHeight * scale / Mathf.Max(1f, Screen.height);
        _stripBottomVp = _stripTopVp - stripHeightVp;
        _settleVp = Mathf.Clamp(_stripBottomVp - 0.22f, 0.42f, 0.68f);
        _skipBaseX = -22f - RuntimeUiKit.SafeAreaRightInset(_canvas);

        // Re-anchor only on a real change: the world<->viewport round-trip carries float noise
        // well below half a pixel, which must not re-dirty the anchors every frame.
        if (Mathf.Abs(previousStripTop - _stripTopVp) > 0.0005f ||
            Mathf.Abs(previousSkipX - _skipBaseX) > 0.25f)
        {
            ApplyOverlayAnchors();
        }
    }

    private void ApplyOverlayAnchors()
    {
        if (_stripRect != null)
        {
            _stripRect.anchorMin = new Vector2(0f, _stripTopVp);
            _stripRect.anchorMax = new Vector2(1f, _stripTopVp);
        }
        if (_skipRoot != null)
        {
            RectTransform skipRect = (RectTransform)_skipRoot.transform;
            skipRect.anchorMin = skipRect.anchorMax = new Vector2(1f, _stripTopVp);
        }
        _appliedSlideAlpha = -1f; // force the slide writer to reposition against the new anchors
    }

    private void BuildStrip(Transform parent)
    {
        Image background = RuntimeUiKit.CreateImage(parent, "Instruction", null,
            new Color(0.02f, 0.05f, 0.09f, 0.8f));
        _stripRect = background.rectTransform;
        _stripRect.pivot = new Vector2(0.5f, 1f);
        _stripRect.sizeDelta = new Vector2(0f, StripHeight);

        // Accent hairline along the bottom edge: the one-glance signal that this band is a
        // special mode, not another dialog.
        Image edge = RuntimeUiKit.CreateImage(_stripRect, "Edge", null,
            new Color(Accent.r, Accent.g, Accent.b, 0.55f));
        RectTransform edgeRect = edge.rectTransform;
        edgeRect.anchorMin = new Vector2(0f, 0f);
        edgeRect.anchorMax = new Vector2(1f, 0f);
        edgeRect.pivot = new Vector2(0.5f, 0f);
        edgeRect.sizeDelta = new Vector2(0f, 3f);
        edgeRect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI tag = RuntimeUiKit.CreateTmp(_stripRect, "Tag", "TUTORIAL", 20,
            new Color(Accent.r, Accent.g, Accent.b, 0.85f), TextAnchor.MiddleCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(0f, -18f), new Vector2(400f, 26f), new Vector2(0.5f, 1f));
        tag.characterSpacing = 12f;

        // Caption band sits at the optical centre of the strip (tag above, dots below).
        _caption = RuntimeUiKit.CreateTmp(_stripRect, "Caption", "", 46, RuntimeUiKit.TitleColor,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        RectTransform captionRect = _caption.rectTransform;
        captionRect.anchorMin = new Vector2(0f, 1f);
        captionRect.anchorMax = new Vector2(1f, 1f);
        captionRect.offsetMin = new Vector2(150f, -142f);
        captionRect.offsetMax = new Vector2(-150f, -46f);
        RuntimeUiKit.AutoSize(_caption, 26f, 46f);

        // Second line: rep progress during a step, the level goal during the coda.
        _subline = RuntimeUiKit.CreateTmp(_stripRect, "Subline", "", 24, RuntimeUiKit.BodyTextColor,
            TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.TitleFont,
            Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        RectTransform sublineRect = _subline.rectTransform;
        sublineRect.anchorMin = new Vector2(0f, 1f);
        sublineRect.anchorMax = new Vector2(1f, 1f);
        sublineRect.offsetMin = new Vector2(60f, -176f);
        sublineRect.offsetMax = new Vector2(-60f, -144f);
        RuntimeUiKit.AutoSize(_subline, 17f, 24f);

        BuildStepDots(_stripRect);
    }

    private void BuildStepDots(Transform strip)
    {
        _dots = new Image[Steps.Length];
        const float spacing = 42f, size = 15f;
        float startX = -(Steps.Length - 1) * spacing * 0.5f;
        for (int i = 0; i < Steps.Length; i++)
        {
            Image dot = RuntimeUiKit.CreateImage(strip, $"Dot{i}", RuntimeSprites.Bubble(), DotIdle);
            RectTransform rect = dot.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(startX + i * spacing, 18f);
            _dots[i] = dot;
        }
    }

    private void BuildDemo(Transform parent)
    {
        _ringImage = RuntimeUiKit.CreateImage(parent, "TapRing", RuntimeSprites.Bubble(), Accent);
        _ring = _ringImage.rectTransform;
        _ring.anchorMin = _ring.anchorMax = new Vector2(0.5f, 0.5f);
        _ring.sizeDelta = new Vector2(170f, 170f);

        _arrowImage = RuntimeUiKit.CreateImage(parent, "Arrow", RuntimeSprites.Chevron(), Color.white);
        _arrow = _arrowImage.rectTransform;
        _arrow.anchorMin = _arrow.anchorMax = new Vector2(0.5f, 0.5f);
        _arrow.sizeDelta = new Vector2(84f, 84f);

        _handImage = RuntimeUiKit.CreateImage(parent, "Hand", RuntimeSprites.Hand(),
            new Color(1f, 0.97f, 0.92f, 1f));
        _hand = _handImage.rectTransform;
        _hand.anchorMin = _hand.anchorMax = new Vector2(0.5f, 0.5f);
        _hand.pivot = new Vector2(0.5f, 0.5f);
        _hand.sizeDelta = new Vector2(140f, 163f);
        HideDemo();
    }

    // Skip lives OUTSIDE the fading group (a player who already knows the game must always be
    // able to leave, even mid-pre-roll) - a quiet ghost pill riding the strip's right edge,
    // vertically centred on it, kept clear of the device's safe area.
    private void BuildSkip()
    {
        _skipRoot = new GameObject("Skip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)_skipRoot.transform;
        rect.SetParent(_overlayRoot.transform, false);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(148f, 62f);

        Image hit = _skipRoot.GetComponent<Image>();
        hit.sprite = RuntimeSprites.RoundedPanel();
        hit.type = Image.Type.Sliced;
        hit.color = new Color(1f, 1f, 1f, 0.035f);
        RuntimeUiKit.AddOutline(rect, new Color(1f, 1f, 1f, 0.16f));

        TextMeshProUGUI label = RuntimeUiKit.CreateTmp(_skipRoot.transform, "Label", "SKIP", 23,
            new Color(0.92f, 0.97f, 1f, 0.62f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        label.characterSpacing = 8f;

        Button button = _skipRoot.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => BeginCoda(earned: false));

        // Touches must not double as gameplay taps (Skip sits inside the tap-to-rotate zone) -
        // the same publish-your-rect contract the ability slots use.
        GameObject skipObject = _skipRoot;
        _skipExclusion = () => skipObject != null && skipObject.activeSelf
            ? ScreenRectOf((RectTransform)skipObject.transform)
            : default;
        TouchGestureInput.RegisterUiExclusionRect(_skipExclusion);
    }

    private System.Func<Rect> _skipExclusion;
    private static readonly Vector3[] CornerBuffer = new Vector3[4];

    // ScreenSpaceOverlay: world corners ARE screen pixels.
    private static Rect ScreenRectOf(RectTransform rect)
    {
        if (rect == null) return default;
        rect.GetWorldCorners(CornerBuffer);
        return new Rect(CornerBuffer[0].x, CornerBuffer[0].y,
            CornerBuffer[2].x - CornerBuffer[0].x, CornerBuffer[2].y - CornerBuffer[0].y);
    }

    private void ApplyStepVisuals()
    {
        if (_stepIndex >= Steps.Length) return;
        UIManager.SetNudgeGuideBoost(NudgeBoostFor(_stepIndex));
        if (_caption != null)
        {
            string caption = Steps[_stepIndex].Caption;
            if (_caption.text != caption)
            {
                _caption.text = caption;
                _captionPop = 0f; // pop only on a real change - re-arms must not re-bounce it
            }
            _caption.color = RuntimeUiKit.TitleColor;
        }
        if (_subline != null) _subline.text = "";
        if (_dots != null)
        {
            for (int i = 0; i < _dots.Length; i++)
            {
                if (_dots[i] == null) continue;
                _dots[i].color = i < _stepIndex ? new Color(Accent.r, Accent.g, Accent.b, 0.55f)
                    : i == _stepIndex ? Accent
                    : DotIdle;
            }
        }
    }

    // Fully lit while nudge is the current lesson, faintly lit for the lessons after it
    // (hidden controls decay, never cut), dark before it is introduced.
    private static float NudgeBoostFor(int stepIndex)
    {
        if (NudgeStepIndex < 0) return 0f;
        if (stepIndex == NudgeStepIndex) return NudgeBoostTeaching;
        return stepIndex > NudgeStepIndex ? NudgeBoostAfter : 0f;
    }

    private bool _groupVisible;

    private void UpdateGroupFade(float deltaTime)
    {
        if (_group == null) return;
        if (_phase != Phase.Coda) // the coda drives alpha itself
        {
            _group.alpha = Mathf.MoveTowards(_group.alpha, _groupVisible ? 1f : 0f, GroupFadePerSecond * deltaTime);
        }

        // Entrance/exit motion: the strip rides its own fade - slides down out of the HUD as it
        // appears, retreats back up as the coda fades. Skip sits outside the fading group (it
        // must stay tappable), so it follows the same motion explicitly. Written only while the
        // alpha is actually changing.
        if (Mathf.Approximately(_group.alpha, _appliedSlideAlpha)) return;
        _appliedSlideAlpha = _group.alpha;

        float slide = (1f - _group.alpha) * 36f;
        if (_stripRect != null)
        {
            _stripRect.anchoredPosition = new Vector2(0f, slide);
        }
        if (_skipRoot != null)
        {
            ((RectTransform)_skipRoot.transform).anchoredPosition =
                new Vector2(_skipBaseX, -StripHeight * 0.5f + slide);
        }
    }

    // The micro-animations that keep the band feeling alive: new text pops in on the game's
    // shared elastic curve (FxKit), the current step dot breathes, and a just-earned dot lands
    // with a bigger pop. Idle timers rest above their window, so settled elements stop writing.
    private void UpdateStripAnimation(float deltaTime)
    {
        _liveTime += deltaTime;
        _captionPop += deltaTime;
        _sublinePop += deltaTime;
        _dotPopTime += deltaTime;

        if (_caption != null && _captionPop <= PopSettleSeconds)
        {
            _caption.rectTransform.localScale = Vector3.one * FxKit.Elastic(_captionPop, -0.16f, 9f, 13f);
        }
        if (_subline != null && _sublinePop <= PopSettleSeconds)
        {
            _subline.rectTransform.localScale = Vector3.one * FxKit.Elastic(_sublinePop, -0.16f, 9f, 13f);
        }
        if (_dots != null && _phase != Phase.Coda)
        {
            for (int i = 0; i < _dots.Length; i++)
            {
                if (_dots[i] == null) continue;
                if (i == _stepIndex)
                {
                    float breathe = 1f + 0.25f * (0.5f + 0.5f * Mathf.Sin(_liveTime * 5f));
                    _dots[i].rectTransform.localScale = Vector3.one * breathe;
                }
                else if (i == _stepIndex - 1 && _dotPopTime <= DotPopSeconds)
                {
                    _dots[i].rectTransform.localScale = Vector3.one * FxKit.Elastic(_dotPopTime, 1f, 8f, 9f);
                }
            }
        }
    }

    private static bool IsPointerDown()
    {
        if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0) return true;
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
    }

    // ---- Ghost-hand animation ------------------------------------------------------------------
    // All demo positions are derived live from the actual targets - the piece and the real
    // corner nudge zones - converted into overlay canvas units, so the demo always plays where
    // the gesture must physically happen, on any screen. Each frame starts hidden; the active
    // animation shows only the elements it uses.

    private void UpdateHandAnimation()
    {
        if (_hand == null || _stepIndex >= Steps.Length || _piece == null) return;

        HideDemo();
        switch (Steps[_stepIndex].Gesture)
        {
            case PieceGestures.Rotate:
            {
                Vector2 p = PieceDemoAnchor();
                AnimateTap(p + new Vector2(-270f, -40f), p + new Vector2(270f, -40f));
                break;
            }
            case PieceGestures.Move:
            {
                Vector2 p = PieceDemoAnchor();
                AnimateSwipe(p + new Vector2(-250f, -150f), p + new Vector2(250f, -150f), 1.8f, ArrowDir.Right, easeIn: false);
                break;
            }
            case PieceGestures.SoftDrop:
            {
                Vector2 p = PieceDemoAnchor();
                AnimateHold(p + new Vector2(0f, -40f), p + new Vector2(0f, -430f));
                break;
            }
            case PieceGestures.Nudge:
                AnimateTap(NudgeZoneOverlayCenter(-1), NudgeZoneOverlayCenter(1));
                break;
            case PieceGestures.HardDrop:
            {
                Vector2 p = PieceDemoAnchor();
                AnimateSwipe(p + new Vector2(0f, -20f), p + new Vector2(0f, -540f), 1.05f, ArrowDir.Down, easeIn: true);
                break;
            }
        }
    }

    private Vector2 PieceDemoAnchor() => ClampToPlayArea(OverlayPointFromWorld(_piece.transform.position));

    private enum ArrowDir { Right, Down }

    // Alternating two-point tap (rotate: either side of the piece; nudge: both corner pills),
    // with a ripple out from the fingertip on the press.
    private void AnimateTap(Vector2 a, Vector2 b)
    {
        const float period = 1.3f;
        float p = Mathf.Repeat(_animTime, period) / period;
        int cycle = Mathf.FloorToInt(_animTime / period);
        Vector2 pos = (cycle % 2 == 0) ? a : b;

        float press = Mathf.Exp(-Mathf.Pow((p - 0.35f) / 0.12f, 2f));
        float alpha = FadeInOut(p);
        SetHand(pos, Mathf.Lerp(1f, 0.82f, press), alpha);

        Vector2 tip = pos + new Vector2(0f, 62f); // fingertip is at the top of the hand sprite
        float rp = Mathf.Clamp01((p - 0.35f) / 0.5f);
        float ringAlpha = p > 0.35f ? Mathf.Lerp(0.55f, 0f, rp) : 0f;
        SetRing(tip, Mathf.Lerp(0.5f, 1.6f, rp), ringAlpha);
    }

    private void AnimateSwipe(Vector2 from, Vector2 to, float period, ArrowDir dir, bool easeIn)
    {
        float p = Mathf.Repeat(_animTime, period) / period;
        float travel = Mathf.InverseLerp(0.12f, 0.72f, p);
        float e = easeIn ? travel * travel : Mathf.SmoothStep(0f, 1f, travel);
        Vector2 pos = Vector2.Lerp(from, to, Mathf.Clamp01(e));

        float alpha = FadeInOut(p);
        SetHand(pos, 1f, alpha);

        Vector2 lead = dir == ArrowDir.Right ? new Vector2(95f, 0f) : new Vector2(0f, -95f);
        SetArrow(pos + lead, dir, alpha);
    }

    private void AnimateHold(Vector2 from, Vector2 to)
    {
        const float period = 2.2f;
        float p = Mathf.Repeat(_animTime, period) / period;
        float e = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.45f, p));
        Vector2 pos = Vector2.Lerp(from, to, e);

        float alpha = p < 0.9f ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.1f, p))
                               : Mathf.Lerp(1f, 0f, Mathf.InverseLerp(0.9f, 1f, p));
        float throb = p > 0.45f && p < 0.9f ? 1f + 0.05f * Mathf.Sin(_animTime * 8f) : 1f;
        SetHand(pos, throb * 0.9f, alpha);
        SetArrow(pos + new Vector2(0f, -95f), ArrowDir.Down, alpha * 0.85f);
    }

    private static float FadeInOut(float p)
    {
        if (p < 0.12f) return Mathf.SmoothStep(0f, 1f, p / 0.12f);
        if (p > 0.85f) return Mathf.SmoothStep(1f, 0f, (p - 0.85f) / 0.15f);
        return 1f;
    }

    // ---- Coordinate plumbing -------------------------------------------------------------------

    private Vector2 OverlayPointFromScreen(Vector2 screenPx)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)_overlayRoot.transform, screenPx, null, out Vector2 local);
        return local;
    }

    private Vector2 OverlayPointFromWorld(Vector3 world)
    {
        Camera cam = TowerCameraController.Camera;
        if (cam == null) return Vector2.zero;
        return OverlayPointFromScreen(cam.WorldToScreenPoint(world));
    }

    // Centre of the real corner nudge zone (the same fractions the hitbox and the pills use).
    private Vector2 NudgeZoneOverlayCenter(int side)
    {
        float x = side < 0
            ? Screen.width * TouchGestureInput.NudgeZoneWidthFraction * 0.5f
            : Screen.width * (1f - TouchGestureInput.NudgeZoneWidthFraction * 0.5f);
        float y = Screen.height * TouchGestureInput.NudgeZoneHeightFraction * 0.5f;
        return OverlayPointFromScreen(new Vector2(x, y));
    }

    // Keep the demo on screen and out from under the strip when the piece sits near an edge.
    private Vector2 ClampToPlayArea(Vector2 point)
    {
        Rect root = ((RectTransform)_overlayRoot.transform).rect;
        float halfW = root.width * 0.5f;
        float halfH = root.height * 0.5f;
        float stripBottom = (_stripTopVp - 0.5f) * root.height - StripHeight;
        return new Vector2(
            Mathf.Clamp(point.x, -halfW + 300f, halfW - 300f),
            Mathf.Clamp(point.y, -halfH + 320f, stripBottom - 120f));
    }

    private void SetHand(Vector2 pos, float scale, float alpha)
    {
        _hand.anchoredPosition = pos;
        _hand.localScale = new Vector3(scale, scale, 1f);
        SetImageAlpha(_handImage, alpha);
    }

    private void SetArrow(Vector2 pos, ArrowDir dir, float alpha)
    {
        if (_arrow == null) return;
        _arrow.anchoredPosition = pos;
        // The chevron sprite points LEFT: 180 flips it right, +90 turns it down.
        _arrow.localRotation = Quaternion.Euler(0f, 0f, dir == ArrowDir.Right ? 180f : 90f);
        SetImageAlpha(_arrowImage, alpha);
    }

    private void SetRing(Vector2 pos, float scale, float alpha)
    {
        if (_ring == null) return;
        _ring.anchoredPosition = pos;
        _ring.localScale = new Vector3(scale, scale, 1f);
        SetImageAlpha(_ringImage, alpha);
    }

    private void HideDemo()
    {
        SetImageAlpha(_handImage, 0f);
        SetImageAlpha(_arrowImage, 0f);
        SetImageAlpha(_ringImage, 0f);
    }

    private static void SetImageAlpha(Image image, float alpha)
    {
        if (image == null) return;
        Color c = image.color; c.a = alpha; image.color = c;
    }

    // ---- Teardown ------------------------------------------------------------------------------

    // Shared exit for finish/skip/game-over/scene-unload. Restores every global this modifier
    // narrows (input lock, gesture gate, nudge spotlight) - the tutorial must never outlive its
    // run. GameManager.Awake re-clears the same globals per run as a final safety net.
    private void Teardown()
    {
        _phase = Phase.Inactive;
        SetInputGate(PieceGestures.Everything);
        UIManager.SetNudgeGuideBoost(0f);
        // Also release the piece itself: a lesson hover left suspended would hang mid-air
        // forever (hover time doesn't count toward the force-lock), e.g. behind a game-over
        // screen. Harmless when the piece is already falling, landed, or being destroyed.
        if (_piece != null)
        {
            _piece.SetDescentSuspended(false);
            RestoreNormalSpeed(_piece);
        }
        Unsubscribe();
        if (_skipExclusion != null)
        {
            TouchGestureInput.UnregisterUiExclusionRect(_skipExclusion);
            _skipExclusion = null;
        }
        if (_overlayRoot != null)
        {
            Destroy(_overlayRoot);
            _overlayRoot = null;
        }
        _canvas = null;
        _group = null;
        _stripRect = null;
        _caption = null;
        _subline = null;
        _skipRoot = null;
        _dots = null;
        _hand = null; _handImage = null;
        _arrow = null; _arrowImage = null;
        _ring = null; _ringImage = null;
        _piece = null;
    }
}
