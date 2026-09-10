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
/// A player-paced welcome accompanies the scenery pan. Movement and rotation are live as
/// gentle arrivals approach an assisted hover; learned controls stay available throughout.
/// The main controls lead into an optional nudge, then a clear handoff into free practice.
/// </summary>
[CreateAssetMenu(fileName = "Tutorial", menuName = "Stacking/Levels/Modifiers/Tutorial")]
public partial class TutorialModifier : LevelModifier
{
    private enum Phase { Inactive, Welcome, WelcomeExit, PreRoll, Armed, Beat, AwaitPiece, Coda }

    private struct Step
    {
        public PieceGestures Gesture;
        public string Caption;   // <= 8 words (research: text is a caption, the demo teaches)
        public string Sub;       // optional second line - the WHY, when the gesture needs one
        public int RequiredReps;
        public bool EndsPiece;   // the committed hard drop ends practice on this piece
    }

    // The curriculum. Data here (not serialized) so the asset can never go stale against the
    // code. The per-step gesture gate is the cumulative OR of everything taught so far (see
    // AllowedThrough) - never hand-maintained, so it can't drift from the cumulative rule.
    // Nudge carries a Sub (Nick 2026-08-11): a bare "tap to nudge" reads as another way to
    // move, when its point is FORCE - a physics shove that darts into gaps and knocks bricks.
    private static readonly Step[] Steps =
    {
        new Step { Gesture = PieceGestures.Move,     Caption = "Drag left or right",
                   Sub = "Slide your brick into position.", RequiredReps = 2, EndsPiece = false },
        new Step { Gesture = PieceGestures.Rotate,   Caption = "Tap to rotate",
                   Sub = "Tap the play area to turn your brick.", RequiredReps = 1, EndsPiece = false },
        new Step { Gesture = PieceGestures.SoftDrop, Caption = "Drag down and hold",
                   Sub = "Hold to fall faster.\nRelease to slow down.", RequiredReps = 1, EndsPiece = false },
        new Step { Gesture = PieceGestures.HardDrop, Caption = "Flick down to slam",
                   Sub = "A quick swipe drops your brick\nstraight down, all the way.", RequiredReps = 1, EndsPiece = true },
        new Step { Gesture = PieceGestures.Nudge,    Caption = "Tap a bottom corner",
                   Sub = "Nudge adds a sideways shove.\nUnlike dragging, it can push other bricks.", RequiredReps = 1, EndsPiece = false },
    };

    private static readonly int NudgeStepIndex =
        System.Array.FindIndex(Steps, s => s.Gesture == PieceGestures.Nudge);

    // Preferred teaching shapes, tried in order; every candidate must also pass the
    // visible-rotation test, and unknown names fall back to any shape that passes it.
    private static readonly string[] TeachingShapePreference = { "L", "J", "T", "S", "Z", "I", "Domino" };

    // Descent never speeds up to reach a lesson. Ease over the last 1.5 world units,
    // then hold above the live tower. No elapsed-time arrival shortcut on tall screens.
    private const float HoverEaseDistance = 1.5f;
    private const float BeatSeconds = 0.55f;
    private const float HandIdleReshowSeconds = 2.8f;  // re-show the demo after this much idle
    private const float CodaHoldSeconds = 4.5f;
    private const float SkipCodaHoldSeconds = 4f;
    private const float CodaFadeSeconds = 0.45f;
    private const float GroupFadePerSecond = 4f;

    // Nudge-pill spotlight: fully lit while the nudge step teaches, kept faintly lit for the
    // rest of the tutorial (hidden controls decay, never cut), gone with the coda.
    private const float NudgeBoostTeaching = 1f;
    private const float NudgeBoostAfter = 0.45f;

    // Instructions always sit on a dark card, independent of the chapter's HUD ink.
    private Color Accent = new Color(1f, 0.98f, 0.94f, 1f);
    private Color Secondary = new Color(0.88f, 0.91f, 0.9f, 1f);
    private Color DotIdle => GameMenuStyle.WithAlpha(Secondary, .32f);

    private Phase _phase = Phase.Inactive;
    private bool _subscribed;
    private int _stepIndex;
    private int _reps;
    private string _goalText;

    private BlockController _piece;
    private float _preRollTime;
    private float _feedbackTime;
    private bool _explainedHover;
    private bool _beatArmsSamePiece;
    private bool _softDropPracticing;
    private bool _learnedSoftDropActive;
    private bool _pieceCommitted;
    private float _animTime;
    private float _beatTime;
    private float _codaTime;
    private float _codaEntranceTime;
    private float _codaStartAlpha;
    private float _idleTime; // seconds since the last touch; the demo shows at/after the reshow threshold
    private Vector2 _beatBurstAt;

    // Overlay. Everything except Skip lives under _group for the entrance and goal handoff.
    private GameObject _overlayRoot;
    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _stripRect;
    private TextMeshProUGUI _caption;
    private TextMeshProUGUI _subline;
    private TextMeshProUGUI _tag;
    private TextMeshProUGUI _skipLabel;
    private TextMeshProUGUI _hoverHint;
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
    private const float StripHeight = 248f;
    private const float StripSideMargin = 40f;
    private const float StripPadding = 28f;

    // Micro-animation state (the "juice"): pop timers rest above their window when idle.
    private const float PopSettleSeconds = 0.22f;
    private const float DotPopSeconds = 0.28f;
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

        _phase = Phase.Welcome;
        _stepIndex = 0;
        _reps = 0;
        _feedbackTime = 0f;
        _explainedHover = false;
        _goalText = context != null && context.Level != null && context.Level.IsIntroduction
            ? $"Build a tower of {Mathf.CeilToInt(context.Level.TargetValue)} standing bricks\nto get the hang of things."
            : context?.Level?.Instruction;
        _welcomeGameManager = context?.GameManager ?? GameManager.Instance;
        _welcomeGameManager?.SetSpawnSuspended(this, true);
        SetInputGate(PieceGestures.None);
        TouchGestureInput.Suspended = true;
        ForceTeachingShapes(context);
        Subscribe();

        BuildOverlay();
        BuildWelcome();
        UIManager.Instance?.SetTutorialPresentation(welcoming: true, teaching: true);

        // A no-pan scene may already have delivered its first piece before modifiers start.
        // Retain it under our welcome hold without repositioning it or finishing the camera.
        if (BlockController.ActiveControlled != null)
        {
            _piece = BlockController.ActiveControlled;
            _piece.SetTutorialDescentHeld(true);
        }
    }

    public override void OnUpdate(LevelModifierContext context, float deltaTime)
    {
        if (_phase == Phase.Inactive) return;

        RefreshScreenGeometry();
        if (_phase == Phase.Welcome || _phase == Phase.WelcomeExit)
        {
            UpdateWelcome(deltaTime);
            return;
        }
        if (_feedbackTime > 0f)
        {
            _feedbackTime = Mathf.Max(0f, _feedbackTime - deltaTime);
            if (_feedbackTime == 0f && _phase != Phase.Coda && _phase != Phase.AwaitPiece) ApplyStepVisuals();
        }
        UpdateGroupFade(deltaTime);
        UpdateStripAnimation(deltaTime);

        if (_softDropPracticing)
        {
            // Observe the real combined touch/keyboard release. The player sees the
            // speed change before the brick pauses for the final flick on this same piece.
            if (_piece == null || _piece.HasLanded || !_piece.IsFastDropping)
                CompleteStep();
            else
            {
                HideDemo();
                return;
            }
        }

        if (_learnedSoftDropActive && (_piece == null || _piece.HasLanded || !_piece.IsFastDropping))
        {
            _learnedSoftDropActive = false;
            if (_piece != null && !_piece.HasLanded && !_pieceCommitted)
                _phase = Phase.PreRoll; // regain the assisted hold after a learned soft drop
        }

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
        PieceGestures mask = PieceGestures.Move | PieceGestures.Rotate;
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
        _idleTime = HandIdleReshowSeconds;
        _pieceCommitted = false;
        _learnedSoftDropActive = false;

        SetInputGate(AllowedThrough(_stepIndex));
        if (piece != null)
        {
            piece.SetTutorialDescentHeld(false);
            RestoreNormalSpeed(piece);
            RefreshScreenGeometry();
        }
        _groupVisible = true;
        _skipRoot.SetActive(true);
        ApplyStepVisuals();
    }

    private void UpdatePreRoll(float deltaTime)
    {
        if (_piece == null || _piece.HasLanded) { EnterAwaitPiece(); return; }
        _preRollTime += deltaTime;
        UpdateArmed(deltaTime);
        EaseIntoPracticeHover();
    }

    private void EaseIntoPracticeHover()
    {
        if (_piece == null || _piece.HasLanded || _pieceCommitted || _softDropPracticing) return;
        Camera cam = TowerCameraController.Camera;
        if (cam == null)
        {
            if (_preRollTime >= 2f) ArmStep();
            return;
        }
        float hoverY = cam.ViewportToWorldPoint(new Vector3(.5f, _settleVp, cam.nearClipPlane)).y;
        // Use the piece's real lower edge, including its rotation, to preserve clearance.
        if (GameManager.Instance != null)
        {
            float lowerExtent = _piece.TryGetWorldBounds(out Bounds bounds)
                ? Mathf.Max(0f, _piece.transform.position.y - bounds.min.y) : 2f;
            hoverY = Mathf.Max(hoverY, GameManager.Instance.LiveTowerTopWorldY + lowerExtent + 2f);
        }
        float remaining = _piece.transform.position.y - hoverY;
        if (remaining <= .08f) { ArmStep(); return; }
        float normalFactor = GameManager.Instance != null ? GameManager.Instance.AbilityFallSpeedFactor : 1f;
        float ease = Mathf.SmoothStep(.18f, 1f, Mathf.Clamp01(remaining / HoverEaseDistance));
        _piece.PinNormalFallSpeedFactor(Mathf.Min(1f, normalFactor) * ease);
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
        if (_piece == null || _piece.HasLanded || _pieceCommitted) { EnterAwaitPiece(); return; }

        _piece.SetTutorialDescentHeld(true);
        if (!_explainedHover && _hoverHint != null)
        {
            _explainedHover = true;
            _hoverHint.text = "Take your time — we’ll hold your brick.";
        }
        RestoreNormalSpeed(_piece);
        SetInputGate(AllowedThrough(_stepIndex));
        _groupVisible = true;

        if (_skipRoot != null) _skipRoot.SetActive(true);

        // A piece that cannot rotate (a Locked-style variant on an unpinned level) could never
        // raise the Rotate gesture - skip that lesson rather than strand it behind its gate.
        // Input is already unlocked above, so the success beat plays with live controls.
        if (Steps[_stepIndex].Gesture == PieceGestures.Rotate && !_piece.CanRotateVariant)
        {
            CompleteStep();
            return;
        }

        // Continue the same prompt at the working height, with the brick held for practice.
        _animTime = 0f;
        _phase = Phase.Armed;
        _idleTime = HandIdleReshowSeconds;
        ApplyStepVisuals();
    }

    private void UpdateArmed(float deltaTime)
    {
        if (_piece == null || _piece.HasLanded) { EnterAwaitPiece(); return; }
        if (_feedbackTime > 0f) { HideDemo(); return; }

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

    // A gesture counts during arrival, hover and the previous step's success beat.
    // Learned inputs stay live while the next instruction waits for the feedback to settle.
    private void HandlePieceGesture(BlockController block, PieceGestures gesture)
    {
        if (block == null || block != _piece || _pieceCommitted) return;
        if (_phase != Phase.Armed && _phase != Phase.Beat && _phase != Phase.AwaitPiece &&
            !(_phase == Phase.PreRoll && _groupVisible)) return;
        if (_stepIndex >= Steps.Length) return;

        Step step = Steps[_stepIndex];
        if (gesture == PieceGestures.HardDrop)
        {
            _pieceCommitted = true;
            _piece.SetTutorialDescentHeld(false);
            RestoreNormalSpeed(_piece);
            if (step.Gesture != gesture)
            {
                EnterAwaitPiece(); // a learned slam during nudge resumes on the next brick
                return;
            }
        }
        if (gesture == PieceGestures.SoftDrop && step.Gesture != gesture)
        {
            // Learned held drops take ownership from assisted arrival. On release, regain
            // the safety hover without resuming any scripted speed multiplier.
            _piece.SetTutorialDescentHeld(false);
            RestoreNormalSpeed(_piece);
            _phase = Phase.Armed;
            _learnedSoftDropActive = true;
            HideDemo();
            return;
        }
        if (step.Gesture != gesture) return;

        if (gesture == PieceGestures.SoftDrop)
        {
            _feedbackTime = 0f;
            _softDropPracticing = true;
            _phase = Phase.Armed;
            _piece.SetTutorialDescentHeld(false);
            RestoreNormalSpeed(_piece);
            if (_subline != null) _subline.text = "Release to slow down again.";
            HideDemo();
            return;
        }

        if (_hoverHint != null) _hoverHint.text = "";
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
        bool arriving = _phase == Phase.PreRoll;
        bool endsPiece = Steps[_stepIndex].EndsPiece;
        _softDropPracticing = false;
        SfxPlayer.Play("pop_01", 0.6f, 0.04f);
        Haptics.Light();
        _feedbackTime = BeatSeconds;
        if (_subline != null) _subline.text = "Nicely done.";
        if (_hoverHint != null) _hoverHint.text = "";
        _beatBurstAt = _piece != null ? OverlayPointFromWorld(_piece.transform.position) : Vector2.zero;
        _dotPopTime = 0f; // the just-earned dot (index _stepIndex - 1 after the increment) pops

        // The beat may only hand back to the SAME piece when it is actually still hovering
        // for a lesson (Armed, or a chained completion during a beat). A step credited on the
        // still-falling previous piece (AwaitPiece) must never re-suspend a committed descent.
        _beatArmsSamePiece = !endsPiece && !_pieceCommitted &&
                             _piece != null && !_piece.HasLanded &&
                             (_phase == Phase.Armed || _phase == Phase.Beat);

        _reps = 0;
        _stepIndex++;
        HideDemo();

        if (_stepIndex >= Steps.Length)
        {
            BeginCoda(earned: true);
            return;
        }

        if (endsPiece)
        {
            // Let the visible slam finish. Introduce nudge when its new, controllable brick
            // arrives, without asking for a corner tap on an already committed drop.
            _phase = Phase.AwaitPiece;
            return;
        }

        // Input advances immediately; the caption waits for the brief success beat.
        // A fast player can already earn the next action without waiting on animation.
        _phase = arriving ? Phase.PreRoll : Phase.Beat;
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
        if (_beatArmsSamePiece && !_pieceCommitted && _piece != null && !_piece.HasLanded &&
            _piece == BlockController.ActiveControlled)
        {
            ArmStep();
        }
        else
        {
            EnterAwaitPiece();
        }
    }

    // ---- Completion ----------------------------------------------------------------------------

    // Shared exit into the coda. Earned: the optional nudge was just tried,
    // celebrate and show the goal. Skipped: hand control back and still show the goal briefly -
    // the runtime's own banner was suppressed, and even a player who knows the controls needs
    // the objective. Marked done IMMEDIATELY either way: quitting during the coda or the free
    // build must never re-show the tutorial.
    private void BeginCoda(bool earned)
    {
        ProgressStore.MarkTutorialCompleted();
        _feedbackTime = 0f;
        UIManager.Instance?.SetTutorialPresentation(welcoming: false, teaching: false);
        SetInputGate(PieceGestures.Everything);
        if (_piece != null)
        {
            _piece.SetTutorialDescentHeld(false);
            RestoreNormalSpeed(_piece);
        }

        _phase = Phase.Coda;
        _softDropPracticing = false;
        _learnedSoftDropActive = false;
        if (_hoverHint != null) _hoverHint.text = "";
        _codaTime = earned ? 0f : CodaHoldSeconds - SkipCodaHoldSeconds;
        _codaEntranceTime = 0f;
        _codaStartAlpha = _group != null ? _group.alpha : 1f;
        HideDemo();
        if (_skipRoot != null) _skipRoot.SetActive(false);
        if (earned) SfxPlayer.Play("ui-star-earned", 0.75f);

        if (_caption != null)
        {
            _caption.text = earned ? "You’ve got the basics!" : "Let’s get stacking";
            _caption.color = Accent;
            _captionPop = 0f;
        }
        if (_subline != null)
        {
            _subline.text = !string.IsNullOrWhiteSpace(_goalText)
                ? _goalText : "Bottom corners nudge even when hidden.";
            _sublinePop = 0f;
        }
        if (_tag != null) _tag.text = "PRACTICE";
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
        _codaEntranceTime += deltaTime;

        float fade = Mathf.Clamp01((_codaTime - CodaHoldSeconds) / CodaFadeSeconds);
        if (_group != null) _group.alpha = Mathf.Lerp(_codaStartAlpha, 1f,
            Mathf.Clamp01(_codaEntranceTime * GroupFadePerSecond)) * (1f - fade);
        // _stepIndex is frozen throughout the coda, so the boost tier is derivable live.
        UIManager.SetNudgeGuideBoost(NudgeBoostFor(_stepIndex) * (1f - fade));

        if (_codaTime >= CodaHoldSeconds + CodaFadeSeconds)
        {
            Teardown();
        }
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
        if (_phase == Phase.Welcome || _phase == Phase.WelcomeExit)
        {
            _piece = block;
            block.SetTutorialDescentHeld(true);
            return;
        }
        if (_softDropPracticing) CompleteStep(); // a held soft drop landed before release
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

        // The instruction card and demo fade together. Skip stays independently tappable.
        GameObject content = new GameObject("Content", typeof(RectTransform));
        RectTransform contentRect = (RectTransform)content.transform;
        contentRect.SetParent(_overlayRoot.transform, false);
        RuntimeUiKit.Stretch(contentRect);
        _group = content.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.alpha = 0f;

        BuildDemo(content.transform); // under the strip so text always reads over the hand
        BuildStrip(content.transform);
        BuildSkip();
        Canvas.ForceUpdateCanvases();
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
        // 0.016, not the original 0.006: the strip's top edge nearly kissed the NEXT card
        // (Nick 2026-08-30) - give the HUD a visible breath of backdrop.
        _stripTopVp = hudBottomVp - 0.016f;

        float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
        float stripHeightVp = StripHeight * scale / Mathf.Max(1f, Screen.height);
        _stripBottomVp = _stripTopVp - stripHeightVp;
        _settleVp = Mathf.Clamp(_stripBottomVp - 0.22f, 0.42f, 0.68f);
        _skipBaseX = -StripSideMargin - 12f - RuntimeUiKit.SafeAreaRightInset(_canvas);
        if (_stripRect != null)
        {
            _stripRect.offsetMin = new Vector2(StripSideMargin + RuntimeUiKit.SafeAreaLeftInset(_canvas), _stripRect.offsetMin.y);
            _stripRect.offsetMax = new Vector2(-StripSideMargin - RuntimeUiKit.SafeAreaRightInset(_canvas), _stripRect.offsetMax.y);
        }

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
        _stripRect = RuntimeUiKit.CreateRect(parent, "Instruction", new Vector2(0f, 1f),
            Vector2.one, new Vector2(.5f, 1f), Vector2.zero, new Vector2(0f, StripHeight));

        var backing = _stripRect.gameObject.AddComponent<Image>();
        backing.sprite = RuntimeSprites.RoundedPanel();
        backing.type = Image.Type.Sliced;
        backing.color = new Color(0.035f, 0.05f, 0.06f, 0.92f);
        backing.raycastTarget = false;

        _tag = InstructionText("Tag", "TUTORIAL", 24f, Secondary, 16f, 40f, true);
        _tag.characterSpacing = 3f;
        _tag.alignment = TextAlignmentOptions.MidlineLeft;
        _tag.rectTransform.offsetMax = new Vector2(-216f, _tag.rectTransform.offsetMax.y);
        _caption = InstructionText("Caption", "", 50f, Accent, 57f, 72f, true);
        RuntimeUiKit.AutoSize(_caption, 46f, 50f);
        _subline = InstructionText("Subline", "", 32f, Secondary, 132f, 88f, false);
        _subline.textWrappingMode = TextWrappingModes.Normal;
        BuildStepDots(_stripRect);
        _hoverHint = InstructionText("HoverHint", "", 26f, Accent, StripHeight + 12f, 44f, false);
        var hintBacking = _hoverHint.gameObject.AddComponent<Shadow>();
        hintBacking.effectColor = new Color(0f, 0f, 0f, .85f);
        hintBacking.effectDistance = new Vector2(0f, -2f);
    }

    private TextMeshProUGUI InstructionText(string name, string value, float size, Color color,
        float top, float height, bool strong)
    {
        var rect = RuntimeUiKit.CreateRect(_stripRect, name, new Vector2(0f, 1f), Vector2.one,
            new Vector2(.5f, 1f), Vector2.zero, Vector2.zero);
        rect.offsetMin = new Vector2(StripPadding, -top - height);
        rect.offsetMax = new Vector2(-StripPadding, -top);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        HudVisualStyle.Text(text, strong);
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Midline;
        return text;
    }

    private void BuildStepDots(Transform strip)
    {
        _dots = new Image[NudgeStepIndex];
        const float spacing = 40f;
        float startX = -(_dots.Length - 1) * spacing * .5f;
        for (int i = 0; i < _dots.Length; i++)
        {
            Image dot = RuntimeUiKit.CreateImage(strip, $"Progress{i}", null, DotIdle);
            var rect = dot.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
            rect.sizeDelta = new Vector2(28f, 3f);
            rect.anchoredPosition = new Vector2(startX + i * spacing, 16f);
            _dots[i] = dot;
        }
    }

    private void BuildDemo(Transform parent)
    {
        _ringImage = RuntimeUiKit.CreateImage(parent, "TapRing", RuntimeSprites.Bubble(), Accent);
        _ring = _ringImage.rectTransform;
        _ring.anchorMin = _ring.anchorMax = new Vector2(0.5f, 0.5f);
        _ring.sizeDelta = new Vector2(170f, 170f);

        _arrowImage = RuntimeUiKit.CreateImage(parent, "Arrow", RuntimeSprites.Chevron(), Accent);
        _arrow = _arrowImage.rectTransform;
        _arrow.anchorMin = _arrow.anchorMax = new Vector2(0.5f, 0.5f);
        _arrow.sizeDelta = new Vector2(84f, 84f);

        // Nick's tap-hand art (Resources/Menu/tap_hand, 2026-08-30) replaces the procedural
        // ghost hand. The rect PIVOTS on the FINGERTIP (measured in the art's alpha), so
        // SetHand places the tip at the gesture point, the press-scale shrinks toward the
        // touch, and the tap ripple blooms exactly on the finger.
        Sprite handArt = Resources.Load<Sprite>("Menu/tap_hand");
        _handImage = RuntimeUiKit.CreateImage(parent, "Hand",
            handArt != null ? handArt : RuntimeSprites.Hand(),
            handArt != null ? Color.white : new Color(1f, 0.97f, 0.92f, 1f));
        _hand = _handImage.rectTransform;
        _hand.anchorMin = _hand.anchorMax = new Vector2(0.5f, 0.5f);
        _hand.pivot = handArt != null
            ? new Vector2(0.248f, 0.906f)   // the art's index fingertip
            : new Vector2(0.469f, 0.875f);  // the procedural fallback's fingertip
        _hand.sizeDelta = handArt != null ? new Vector2(156f, 156f) : new Vector2(120f, 140f);
        HideDemo();
    }

    // Skip lives OUTSIDE the fading group (a player who already knows the game must always be
    // able to leave on replacement arrivals) - open text beside the TUTORIAL tag,
    // with a 72-unit hit area kept clear of the device's safe area.
    private void BuildSkip()
    {
        _skipRoot = new GameObject("Skip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)_skipRoot.transform;
        rect.SetParent(_overlayRoot.transform, false);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(184f, 72f);

        Image hit = _skipRoot.GetComponent<Image>();
        hit.color = Color.clear;

        TextMeshProUGUI label = RuntimeUiKit.CreateTmp(_skipRoot.transform, "Label", "SKIP", 23,
            Secondary, TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        _skipLabel = label;
        HudVisualStyle.Text(label, true);
        label.color = Secondary;
        label.characterSpacing = 3f;

        Button button = _skipRoot.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => BeginCoda(earned: _stepIndex == NudgeStepIndex));

        // Touches must not double as gameplay taps (Skip sits inside the tap-to-rotate zone) -
        // the same publish-your-rect contract the ability slots use.
        GameObject skipObject = _skipRoot;
        _skipExclusion = () => skipObject != null && skipObject.activeSelf
            ? ScreenRectOf((RectTransform)skipObject.transform)
            : default;
        TouchGestureInput.RegisterUiExclusionRect(_skipExclusion);

        // Reveal together with the first instruction after the welcome has closed.
        _skipRoot.SetActive(false);
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
        if (_skipLabel != null) _skipLabel.text = _stepIndex == NudgeStepIndex ? "TRY LATER" : "SKIP";
        if (_feedbackTime > 0f) return; // let success register before replacing the instruction
        UIManager.SetNudgeGuideBoost(NudgeBoostFor(_stepIndex));
        if (_tag != null)
            _tag.text = _stepIndex == NudgeStepIndex ? "TUTORIAL · OPTIONAL" : $"TUTORIAL · {_stepIndex + 1} / {NudgeStepIndex}";
        if (_caption != null)
        {
            string caption = Steps[_stepIndex].Caption;
            if (_caption.text != caption)
            {
                _caption.text = caption;
                _captionPop = 0f; // pop only on a real change - re-arms must not re-bounce it
            }
            _caption.color = Accent;
        }
        if (_subline != null)
        {
            // Keep the helper until a multi-rep action reports its progress.
            string sub = _reps > 0 ? $"{_reps} / {Steps[_stepIndex].RequiredReps}" : Steps[_stepIndex].Sub ?? "";
            if (_subline.text != sub)
            {
                _subline.text = sub;
                if (sub.Length > 0) _sublinePop = 0f;
            }
        }
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

        float slide = (1f - _group.alpha) * 12f;
        if (_stripRect != null)
        {
            _stripRect.anchoredPosition = new Vector2(_stripRect.anchoredPosition.x, slide);
        }
        if (_skipRoot != null)
        {
            ((RectTransform)_skipRoot.transform).anchoredPosition =
                new Vector2(_skipBaseX, -36f + slide);
        }
    }

    // Quiet text settles and short progress-line pulses match the open gameplay HUD.
    private void UpdateStripAnimation(float deltaTime)
    {
        _captionPop += deltaTime;
        _sublinePop += deltaTime;
        _dotPopTime += deltaTime;

        if (_caption != null && _captionPop <= PopSettleSeconds)
        {
            _caption.rectTransform.localScale = Vector3.one * Mathf.Lerp(.98f, 1f,
                Mathf.SmoothStep(0f, 1f, _captionPop / PopSettleSeconds));
        }
        if (_subline != null && _sublinePop <= PopSettleSeconds)
        {
            _subline.rectTransform.localScale = Vector3.one;
        }
        if (_dots != null && _phase != Phase.Coda)
        {
            for (int i = 0; i < _dots.Length; i++)
            {
                if (_dots[i] == null) continue;
                if (i == _stepIndex)
                {
                    _dots[i].rectTransform.localScale = Vector3.one;
                }
                else if (i == _stepIndex - 1 && _dotPopTime <= DotPopSeconds)
                {
                    _dots[i].rectTransform.localScale = new Vector3(
                        Mathf.Lerp(1.2f, 1f, Mathf.Clamp01(_dotPopTime / DotPopSeconds)), 1f, 1f);
                }
                else _dots[i].rectTransform.localScale = Vector3.one;
            }
        }
    }

    private static bool IsPointerDown()
    {
        if (UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled &&
            UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0) return true;
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
        _hand.localRotation = Quaternion.identity;
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
                // Point down into the corner so the palm stays on screen above the
                // shorter touch zone, rather than being cut off by the phone edge.
                _hand.localRotation = Quaternion.Euler(0f, 0f, 180f);
                AnimateTap(NudgeZoneOverlayCenter(-1), NudgeZoneOverlayCenter(1));
                _hand.localScale *= .85f;
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

        // The hand rect pivots on the fingertip (BuildDemo), so `pos` IS the tip - the
        // ripple blooms exactly where the finger touches.
        float rp = Mathf.Clamp01((p - 0.35f) / 0.5f);
        float ringAlpha = p > 0.35f ? Mathf.Lerp(0.55f, 0f, rp) : 0f;
        SetRing(pos, Mathf.Lerp(0.5f, 1.6f, rp), ringAlpha);
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
        // Close event ownership before releasing a hold, which can spawn synchronously.
        Unsubscribe();
        ReleaseWelcomeHold();
        UIManager.Instance?.SetTutorialPresentation(welcoming: false, teaching: false);
        SetInputGate(PieceGestures.Everything);
        UIManager.SetNudgeGuideBoost(0f);
        // Also release the piece itself: a lesson hover left suspended would hang mid-air
        // forever (hover time doesn't count toward the force-lock), e.g. behind a game-over
        // screen. Harmless when the piece is already falling, landed, or being destroyed.
        if (_piece != null)
        {
            _piece.SetTutorialDescentHeld(false);
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
        _tag = null;
        _skipLabel = null;
        _hoverHint = null;
        _welcome = null;
        _welcomePanel = null;
        _welcomeHud = null;
        _softDropPracticing = false;
        _skipRoot = null;
        _dots = null;
        _hand = null; _handImage = null;
        _arrow = null; _arrowImage = null;
        _ring = null; _ringImage = null;
        _piece = null;
    }
}
