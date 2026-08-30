using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
// Identity, tuning fields, runtime state, and the Unity lifecycle. The class is split into
// focused partials (the sibling BlockController.*.cs files); PHYSICS.md at the repo root is
// the binding contract for all of them.
public partial class BlockController : MonoBehaviour
{
    private static readonly List<BlockController> TrackedBlocks = new List<BlockController>();

    [Header("Movement Settings")]
    [SerializeField] public float fallSpeed = 2.0f;
    [SerializeField] public float fastDropMultiplier = 10.0f;
    // Ability fall-speed multiplier (Air Brake, recovery / slo-mo windows), stamped at spawn from
    // GameManager.AbilityFallSpeedFactor AND re-stamped live whenever that factor changes, so an
    // ability used mid-fall is felt by the piece already in the air instead of only by the next one.
    // Applies to NORMAL descent only - fast drop / flick use the un-factored base speed (the player
    // chose to go fast; abilities don't fight that).
    private float _normalFallSpeedFactor = 1f;
    private bool _fallSpeedPinned;
    public void SetNormalFallSpeedFactor(float factor)
    {
        _fallSpeedPinned = false;
        _normalFallSpeedFactor = Mathf.Clamp(factor, 0.05f, 3f);
    }

    /// <summary>Pin this piece's normal descent to a scripted factor (the tutorial's pre-roll
    /// ride-in). Live ability re-stamps skip a pinned piece, so a recompute mid-lesson can't yank
    /// the speed of a piece a script is driving; SetNormalFallSpeedFactor releases the pin (which
    /// is exactly what the script's restore call does).</summary>
    public void PinNormalFallSpeedFactor(float factor)
    {
        _normalFallSpeedFactor = Mathf.Clamp(factor, 0.05f, 3f);
        _fallSpeedPinned = true;
    }

    public bool NormalFallSpeedPinned => _fallSpeedPinned;

    // Per-piece "initial slow" window (Slowburn): this piece falls at _initialSlowFactor of its
    // normal speed until _initialSlowEndTime, then resumes full ramped speed. Time.time is scaled,
    // so a pause freezes the window. Default factor 1 / end 0 = no effect for an unslowed piece.
    private float _initialSlowFactor = 1f;
    private float _initialSlowEndTime;
    /// <summary>Slow this piece's NORMAL descent to <paramref name="factor"/> for the next
    /// <paramref name="seconds"/> (a per-piece thinking beat). Fast drops are unaffected.</summary>
    public void BeginInitialSlow(float seconds, float factor)
    {
        _initialSlowFactor = Mathf.Clamp(factor, 0.05f, 1f);
        _initialSlowEndTime = Time.time + Mathf.Max(0f, seconds);
    }
    private float CurrentInitialSlowFactor() => Time.time < _initialSlowEndTime ? _initialSlowFactor : 1f;
    [SerializeField] private LayerMask collisionLayers = 1;

    [Header("Grid Movement Settings")]
    [Tooltip("Width of one placement column in world units.")]
    [SerializeField] private float gridSpacing = 1.0f;
    [SerializeField] private float dasDelay = 0.2f;
    [SerializeField] private float dasRate = 0.05f;
    [Tooltip("Extra columns beyond the current floor/tower edge where the active block may still be placed.")]
    [SerializeField] private int horizontalPlacementBufferColumns = 3;

    [Header("Physics Material")]
    [Tooltip("Surface friction applied when the block variant has no PhysicsMaterial2D assigned. Higher grips more so tall dynamic towers shear less.")]
    [Range(0f, 1f)]
    [SerializeField] private float defaultBlockFriction = 0.95f;
    [Tooltip("Surface bounciness applied when no PhysicsMaterial2D is assigned. Keep at 0 for stable stacking.")]
    [Range(0f, 1f)]
    [SerializeField] private float defaultBlockBounciness = 0f;
    [Tooltip("Linear drag on a landed block. A little damping makes a placed block settle quickly and resist slow sliding instead of drifting. Too high feels floaty.")]
    [SerializeField] private float restingLinearDamping = 0.5f;
    [Tooltip("Angular drag on a landed block. Damps out wobble so blocks settle and go to sleep instead of jittering.")]
    [SerializeField] private float restingAngularDamping = 3f;

    [Tooltip("Rounds block collider corners as a fraction of one cell.")]
    [Range(0f, 0.12f)]
    [SerializeField] private float colliderCornerRadiusFraction = 0.06f;
    [Tooltip("Effective world-horizontal physics footprint of each cell as a fraction of the visual cell. Slightly undersized width gives perfect placements real side clearance while the full-height collider preserves grid-true support height.")]
    [Range(0.85f, 1f)]
    [SerializeField] private float colliderFootprintScale = 0.94f;

    [Header("Placement Beam")]
    [SerializeField] private bool showPlacementBeam = true;

    // Behind the ground skin (-50) and all bricks (0), in front of EVERY backdrop layer:
    // imported layers stack up from -89 and are capped at ~36 per preset (max -53), so -52
    // clears them all - the beam only stops at the floor and the tower, never at scenery.
    // Code-owned (not serialized) so it can't go stale in prefab import caches.
    private const int PlacementBeamSortingOrder = -52;
    private const int VectorGuideGhostSortingOrder = -5;
    // Run-local ability toggles (Vector Guide, ...). One bitfield instead of a named
    // static per ability - see BlockFeature. Reset with the rest of the static state below.
    private static BlockFeature _features;

    // Bumped whenever the placed geometry behind the reach bounds changes - a block lands or leaves
    // tracking, or an island spawns. Active pieces cache their reach bounds against this stamp so
    // the per-FixedUpdate steering clamp and per-input legality check don't rescan every tracked
    // block + island on a tall tower. The placement-occupancy cache below is invalidated separately
    // when an awake landed block moves far enough to affect snapped cell legality.
    private static int _reachGeometryVersion;
    private static int _placementOccupancyVersion;
    private static int _placementOccupancyStamp = -1;
    private static readonly Dictionary<Vector2Int, List<BlockController>> LandedCellOccupancy =
        new Dictionary<Vector2Int, List<BlockController>>();

    public static void InvalidateReachGeometry()
    {
        _reachGeometryVersion++;
        _placementOccupancyVersion++;
    }

    [Header("Active Piece Control (fallback; GameModeConfig overrides these per level)")]
    [Tooltip("How close (world units) support must be below the piece before steering control is handed to physics. Keep small so players can make last-second tuck moves.")]
    [SerializeField] private float groundedCheckDistance = 0.03f;
    [Tooltip("Maximum downward velocity kept when control hands off to physics. 0 means use the current controlled fall speed.")]
    [SerializeField] private float maxLandingImpactSpeed = 2f;
    [Tooltip("A landed piece counts as 'settled' once its linear speed (units/sec) drops below this. Keep low so unstable pieces get time to tip before maintenance runs.")]
    [SerializeField] private float settleLinearThreshold = 0.08f;
    [Tooltip("...and its spin (degrees/sec) drops below this.")]
    [SerializeField] private float settleAngularThreshold = 8f;
    [Tooltip("How long a landed piece must stay settled before maintenance micro-aligns/sleeps it.")]
    [SerializeField] private float settleTime = 0.35f;
    [Tooltip("Sleep a settled dynamic block when control finishes. This prevents tiny post-settle drift without freezing the body; future contacts can wake it again.")]
    [SerializeField] private bool sleepSettledBlocksOnLock = true;
    [Tooltip("After a block genuinely settles, correct tiny X/rotation drift back to the placement grid. Large offsets or visibly tilted blocks are left to physics.")]
    [SerializeField] private bool microAlignSettledBlocks = true;
    [Tooltip("Maximum X correction allowed for settled micro-alignment, as a fraction of one grid cell.")]
    [Range(0f, 0.25f)]
    [SerializeField] private float microAlignMaxColumnFraction = 0.08f;
    [Tooltip("Maximum rotation correction allowed for settled micro-alignment, in degrees.")]
    [Range(0f, 15f)]
    [SerializeField] private float microAlignMaxRotationDegrees = 4f;
    [Tooltip("Safety cap: force the piece to lock after this many seconds even if it never finds a normal landing.")]
    [SerializeField] private float maxControlTime = 12f;
    [Tooltip("Velocity damping applied each FixedUpdate while a landed block is below the settle thresholds but still awake.")]
    [Range(0f, 1f)]
    [SerializeField] private float softSettleDampingFactor = 0.8f;
    [Tooltip("Minimum upward normal for a contact to count as real landing support.")]
    [Range(0f, 1f)]
    [SerializeField] private float landingSupportNormalY = 0.7f;
    [Tooltip("Minimum horizontal support overlap required for landing, as a fraction of one grid cell.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float landingMinSupportWidthFraction = 0.15f;
    [Tooltip("A landed block that has not NET-moved beyond these tolerances for the stillness window is force-slept, even if the solver keeps twitching it in place. This is what guarantees oscillation can never persist: twitching has zero net movement by definition.")]
    [SerializeField] private float stillnessPositionTolerance = 0.005f;
    [Tooltip("Net rotation tolerance (degrees) for the stillness watchdog.")]
    [SerializeField] private float stillnessRotationToleranceDegrees = 0.5f;
    [Tooltip("How long a block must stay within the stillness tolerances before it is force-slept.")]
    [SerializeField] private float stillnessTime = 0.75f;
    [Tooltip("How strongly quiet landed blocks are eased back toward their grid X while still awake.")]
    [Range(0f, 1f)]
    [SerializeField] private float quietGridPullFactor = 0.15f;
    [Tooltip("Maximum corrective speed toward the grid in cells/sec. Must stay well below the settle threshold so the correction itself can never keep a block awake.")]
    [Range(0f, 0.05f)]
    [SerializeField] private float quietGridPullMaxSpeedFraction = 0.02f;

    private static PhysicsMaterial2D _sharedFallbackMaterial;
    private static float _sharedFallbackBaseFriction = 0.95f;
    private static float _standardBlockFrictionMultiplier = 1f;
    // Ability-driven block-mass multiplier (Titan). Applied to each piece's mass at ApplyData, so
    // it affects FUTURE pieces only - like the friction knob. Run-local (reset below).
    private static float _standardBlockMassMultiplier = 1f;

    private const float RotationStep = 90f;
    // The widest piece is the horizontal 1x4 (I-piece). The reachable area beside any obstacle
    // (tower block OR sky island) must always leave at least this many clear columns on the
    // outer side, so even a horizontal 1x4 can slip down past it and fall off. This is a
    // correctness floor tied to block geometry, NOT the designer's aesthetic placement buffer -
    // consumed by the placement bounds, the camera zoom, and the island spawn confinement.
    public const int WidestBlockColumns = 4;
    // The quiet grid pull only runs on blocks that seated flat. Nudging a tilted block sideways
    // engages/releases its lean contact each frame, which can feed a rocking limit cycle.
    private const float QuietPullMaxTiltDegrees = 1f;
    private const float LandedGravityScale = 1f;
    private const int CastResultCapacity = 32;

    private readonly RaycastHit2D[] _castResults = new RaycastHit2D[CastResultCapacity];
    private readonly Collider2D[] _overlapResults = new Collider2D[CastResultCapacity];
    private BoxCollider2D[] _forgivenColliders;
    private Vector2[] _forgivenColliderBaseSizes;

    private Rigidbody2D _rb;
    private StackingInputs _inputs;
    private bool _isControlEnabled = true;
    private Vector2 _moveInput;
    private bool _isFastDrop;
    // Fission: while true the controlled piece hovers (steering still works) and does not
    // advance downward or run the landing cast - the start of descent is deferred until the
    // player commits a drop. The body stays kinematic and never-landed throughout, so I1/I5
    // hold (first contact is merely postponed, never a transform write on a landed block).
    private bool _descentSuspended;
    private ContactFilter2D _contactFilter;
    private BlockData _appliedData;
    // Datas a mid-air re-apply REPLACED (ApplyVariantConsumable: Tremor -> Vine). The new data
    // owns flags/steering, but the old variant's landing behaviour is still owed - a Tremor
    // turned Vine must still quake, now dragging its welded cluster along (Nick 2026-08-30).
    // OnLocked fires for these too; a defuse (NeutralizeToPlain) clears them - defused is GONE.
    private readonly List<BlockData> _replacedDatas = new List<BlockData>();
    private readonly BlockCellGeometry _cellGeometry = new BlockCellGeometry();
    private IReadOnlyList<FloorSegmentConfig> _floorSegments;
    private Camera _mainCamera;
    private SpriteRenderer _placementBeamRenderer;
    private Transform _vectorGuideGhostRoot;
    private readonly List<SpriteRenderer> _vectorGuideFillRenderers = new List<SpriteRenderer>(4);
    private readonly List<SpriteRenderer> _vectorGuideLineRenderers = new List<SpriteRenderer>(16);
    private SpriteRenderer _vectorGuideSourceRenderer;
    private float _gravityScaleMultiplier = 1f;

    private float _dasTimer;
    private int _lastInputDirection = 0;
    private bool _dasActive = false;
    private System.Action<int> _dasStep;

    // Tricky Towers dynamic-control state
    private float _targetAngleZ;
    private float _targetColumnX;
    private Vector2 _originalCenterOfMass;
    private bool _dynamicControlReady;
    private bool _hasTouchedDown;
    private float _landedMaintenanceSettleTimer;
    private float _controlElapsed;
    private float _lastControlledFallSpeed;
    private Vector2 _stillnessAnchorPosition;
    private float _stillnessAnchorRotation;
    private float _stillnessTimer;
    private Vector2 _lastPlacementOccupancyPosition;
    private float _lastPlacementOccupancyRotation;

    // Per-piece cache of the gameplay reach bounds, refreshed only when _reachGeometryVersion moves.
    private int _reachBoundsStamp = -1;
    private float _reachBoundsMinX;
    private float _reachBoundsMaxX;
    private bool _reachBoundsValid;

    public bool HasLanded { get; private set; }
    public bool IsFrozenInPlace => _rb != null && _rb.bodyType == RigidbodyType2D.Static;
    public static IReadOnlyList<BlockController> AllBlocks => TrackedBlocks;

    /// <summary>A removed support changes contact topology even for sleeping bodies. Wake dynamic landed
    /// blocks so the next physics step can let unsupported tower sections fall instead of hovering.</summary>
    public static void WakeDynamicLandedBlocks(BlockController except = null)
    {
        for (int i = 0; i < TrackedBlocks.Count; i++)
        {
            BlockController block = TrackedBlocks[i];
            if (block == null || block == except || !block.HasLanded || block._rb == null) continue;
            if (block._rb.bodyType != RigidbodyType2D.Dynamic) continue;
            block._rb.WakeUp();
        }
    }

    /// <summary>Stop counting this block as a live tower member right now, before it is
    /// destroyed - used while a rescue animation (Rebound) plays it out. Removes it from
    /// AllBlocks so height/camera/ability sweeps that filter on HasLanded stop seeing a block
    /// that has already left the board. OnDestroy's own removal then becomes a no-op.</summary>
    public void DetachFromTracking()
    {
        TrackedBlocks.Remove(this);
        if (HasLanded) InvalidateReachGeometry(); // a landed block leaving the tower changes the reach bounds
    }

    public event System.Action<BlockController> OnBlockLocked;

    /// <summary>Raised on this piece at the very start of its lock, BEFORE it counts as landed and
    /// before its variant data activates (BlockData.OnLocked) - the last moment anything can change
    /// what this brick becomes. Ward's pending strike defuses a hazard here when a fast drop beats
    /// its timer; the piece is still in-air state, so the normal in-place variant swap applies.</summary>
    public event System.Action<BlockController> BeforeLock;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetGridState()
    {
        ResetRuntimeState();
    }

    public static void ResetRuntimeState()
    {
        TrackedBlocks.Clear();
        _sharedFallbackMaterial = null;
        _sharedFallbackBaseFriction = 0.95f;
        _standardBlockFrictionMultiplier = 1f;
        _standardBlockMassMultiplier = 1f;
        _nudgeLockedUntilTime = 0f;
        _features = BlockFeature.None;
        AllowedGestures = PieceGestures.Everything;
        _reachGeometryVersion = 0;
        _placementOccupancyVersion = 0;
        _placementOccupancyStamp = -1;
        LandedCellOccupancy.Clear();
    }

    public static void AddStandardBlockFrictionMultiplier(float multiplierDelta)
    {
        if (multiplierDelta <= 0f) return;

        _standardBlockFrictionMultiplier += multiplierDelta;
        RefreshStandardBlockFriction();
    }

    // Titan: heavier blocks (applied to future pieces at ApplyData). Additive delta over the 1.0
    // baseline, mirroring the friction knob - shared static, run-local.
    public static void AddStandardBlockMassMultiplier(float multiplierDelta)
    {
        if (multiplierDelta <= 0f) return;
        _standardBlockMassMultiplier += multiplierDelta;
    }

    /// <summary>Turn a run-local block feature on or off (abilities call this in OnAcquired/OnRemoved).</summary>
    public static void SetFeature(BlockFeature feature, bool enabled)
    {
        if (enabled) _features |= feature;
        else _features &= ~feature;
    }

    /// <summary>True while the given run-local block feature is enabled.</summary>
    public static bool HasFeature(BlockFeature feature) => (_features & feature) != 0;

    // Rotation nudges the target angle by a quarter turn. Active pieces snap to that target while
    // The piece currently under player control (null between lock and next spawn).
    // Touch gestures use this to address their commands.
    public static BlockController ActiveControlled { get; private set; }

    /// <summary>The brick the player is actually steering right now: ActiveControlled, but only
    /// while it is a real in-air piece - not landed/locked, and not already past the loss cull
    /// (Destroy is deferred, so a doomed piece can still BE ActiveControlled for a frame). Null when
    /// there is nothing to act on. Every "apply this to the brick that's currently falling" path
    /// goes through here so they can't drift apart.</summary>
    public static BlockController LiveActivePiece
    {
        get
        {
            BlockController active = ActiveControlled;
            if (active == null || active.HasLanded) return null;
            return LossZone.IsBelowCull(active.transform.position) ? null : active;
        }
    }

    /// <summary>Width of one placement column in world units (for gesture distance mapping).</summary>
    public float GridSpacing => gridSpacing;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.bodyType = RigidbodyType2D.Kinematic;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        _rb.gravityScale = 0f;
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        _rb.sharedMaterial = ResolveBlockMaterial(_rb.sharedMaterial);
        _rb.linearDamping = restingLinearDamping;
        _rb.angularDamping = restingAngularDamping;
        _mainCamera = Camera.main;
        _inputs = new StackingInputs();

        // Solid-collision filter (for casting/overlap against other blocks & floor)
        _contactFilter = new ContactFilter2D();
        _contactFilter.useTriggers = false;
        _contactFilter.SetLayerMask(collisionLayers);
        _contactFilter.useLayerMask = true;

        ApplyColliderForgiveness();
        _cellGeometry.Cache(gameObject);
        if (!TrackedBlocks.Contains(this))
        {
            TrackedBlocks.Add(this);
        }

        ResetControlTargets();
        CreatePlacementBeam();
        ApplyBlockSkin();

        ActiveControlled = this; // newly spawned piece starts under player control
    }
    private void OnEnable()
    {
        if (_inputs != null)
        {
            _inputs.Gameplay.Enable();
        }
    }

    private void OnDisable()
    {
        if (_inputs != null)
        {
            _inputs.Gameplay.Disable();
        }
    }

    private void OnDestroy()
    {
        if (ActiveControlled == this) ActiveControlled = null; // e.g. destroyed by the loss zone mid-fall
        bool wasTracked = TrackedBlocks.Remove(this);
        if (wasTracked && HasLanded) InvalidateReachGeometry(); // a landed block destroyed changes the reach bounds
        DestroyPlacementBeam();
        _inputs?.Dispose();
    }

    public bool TryGetWorldBounds(out Bounds bounds)
    {
        return _cellGeometry.TryGetWorldBounds(out bounds);
    }

    /// <summary>Copies the current world-space cell centres into <paramref name="results"/>
    /// (cleared first). Fresh each call - PlacementScout reads SETTLED positions, not where
    /// the cells were at lock.</summary>
    public void GetWorldCellCenters(List<Vector2> results)
    {
        results.Clear();
        _cellGeometry.Refresh();
        for (int i = 0; i < _cellGeometry.CellCenters.Count; i++)
        {
            results.Add(_cellGeometry.CellCenters[i]);
        }
    }

    /// <summary>The block's solid (non-trigger) colliders - the live shapes for geometric
    /// coverage tests. Cell CENTERS snapped to the grid lie once a tower tilts; these don't.</summary>
    public IReadOnlyList<Collider2D> SolidColliders => _cellGeometry.SolidColliders;

    // ---- Off-screen loss (driven by LossZone's camera-relative cull) -----------------------

    // "Falling" for the cull test. An unlocked piece can't be judged by velocity (steering
    // zeroes the kinematic body's velocity every step) - but an unlocked piece below the
    // line is descending by definition. A landed block must be dynamic, awake and genuinely
    // moving down: resting/sleeping/frozen tower blocks below the camera are the NORMAL
    // state at altitude and must never count as lost.
    private const float LostFallingSpeed = -1f;

    // Sticky "clearly falling away" marker for the camera: once a landed block free-falls
    // faster than any settle or landing transient can reach (the landing handoff velocity is
    // capped at 2), it is debris - the camera stops framing it (genre convention: knocked-off
    // bricks tumble off-screen untracked; the view holds on the stable tower) and the loss
    // line charges it moments later. Latched only while the body is Dynamic: a block converted
    // to Static (Freeze, Hardline's platform conversion - which sets bodyType directly, not via
    // FreezeInPlace) is stationary terrain again and must be framed. SleepSettledBody clears
    // the latch too: a knocked-loose block that came to rest and re-earned sleep is provably
    // stable, not debris.
    private const float FallingAwaySpeed = -2.5f;
    private bool _fallingAway;
    public bool IsFallingAway =>
        _fallingAway && _rb != null && _rb.bodyType == RigidbodyType2D.Dynamic;

    // World Y where the piece stood when it locked (set once in LockBlock, never re-anchored -
    // unlike the stillness anchor, which follows the body). The falling-clear test below
    // measures displacement against this.
    private float _landedAnchorY;

    /// <summary>The stricter debris test for HEIGHT accounting - the live-tower-top walk that
    /// feeds the camera, the HUD counter, hold-steady win checks and the flood's swallow rule.
    /// <see cref="IsFallingAway"/> alone is the WRONG predicate there: it is a sticky latch
    /// that stays set on a block that got jolted past the trip speed but re-seated in place,
    /// until sleep is re-earned - and treating that whole window as "not part of the tower"
    /// dropped the live top a full block during routine recoverable transients (wrongful flood
    /// deaths, aborted win countdowns, camera dips - review 2026-08-11). A block only counts
    /// as LEAVING the tower while all three hold: the fast-fall latch tripped, it is still
    /// descending NOW, and it has fallen clearly further than one cell below where it locked.
    /// So a jolt-and-reseat never qualifies (no displacement), a one-cell support-loss reseat
    /// never qualifies (displacement stops at one cell), and a genuinely knocked-off block
    /// qualifies within a few tenths of a second - then counts again, at its new honest
    /// height, the moment it comes to rest anywhere.</summary>
    private const float FallingClearDropDistance = 1.25f;  // world units ≈ cells (GridSpacing 1); > 1 so a one-cell reseat can't qualify
    private const float FallingClearDescentSpeed = -0.5f;  // must be going down NOW - debris at rest counts again immediately
    public bool IsFallingClearOfTower =>
        IsFallingAway &&
        _rb.linearVelocity.y < FallingClearDescentSpeed &&
        _rb.position.y < _landedAnchorY - FallingClearDropDistance;

    public bool IsLostBelow(float cullY)
    {
        if (!TryGetWorldBounds(out Bounds bounds)) return false;

        // The ACTIVE piece is judged by its TOP edge: while the player is steering, a low cell
        // dipping under the line can be a legitimate deep-pocket entry (pockets reach ~3 cells
        // below the datum, the line sits at datum - 4 - one overhanging cell of an L crosses it
        // during a valid tuck). Only a piece ENTIRELY below the line is beyond saving - nothing
        // landable exists down there, so no still-placeable piece is ever charged.
        if (!HasLanded) return bounds.max.y < cullY;

        // Landed blocks use the CENTRE, not the top: the death line should read as what destroys
        // the block, so it's charged as the line passes through its middle - not after the whole
        // block has dropped a full height below the line (which looked like it fell THROUGH the
        // beam before dying).
        if (bounds.center.y >= cullY) return false;
        return _rb != null && _rb.bodyType == RigidbodyType2D.Dynamic &&
               !_rb.IsSleeping() && _rb.linearVelocity.y < LostFallingSpeed;
    }

    // A block that left the screen at the bottom: the life is charged the moment it leaves
    // view - whether it would have wedged into the tower 100 m further down must not matter.
    // GameOver runs BEFORE the control handoff so a final-life loss reaches the spawner's
    // game-over gate first (no replacement piece spawns into a dead game), and AddScore's
    // own gate keeps the lost piece from scoring posthumously.
    public void HandleLostBelowScreen()
    {
        // Water deaths splash at the waterline; everything else keeps the fog mist. The
        // flood surface is -infinity when no flood runs, so this never fires elsewhere.
        if (transform.position.y < RisingFloodModifier.FloodSurfaceY)
            FloodSplashFx.Play(transform.position.x);
        else
            LifeLossFx.Play(transform.position); // the mist swallows the block - visible at the screen edge
        if (GameManager.Instance != null) GameManager.Instance.GameOver();
        if (!HasLanded) LockBlock(); // end control cleanly so (lives permitting) the next piece spawns
        Destroy(gameObject);
    }

    private void Update()
    {
        if (!_isControlEnabled) return;
        if (GameManager.Instance != null && GameManager.Instance.IsGamePaused) return;

        // Direct read to ensure we have the latest value
        if (_inputs != null)
        {
            _moveInput = _inputs.Gameplay.Move.ReadValue<Vector2>();
            if (InvertSteering) _moveInput.x = -_moveInput.x;

            // The keyboard steering axis obeys the same gesture gate as the touch entry points
            // (see PieceGestures): a gated-off axis reads as unpressed. Down-intent folds into
            // _isFastDrop below, which is the single source of soft-drop truth.
            if (!GestureAllowed(PieceGestures.Move)) _moveInput.x = 0f;

            // Handle rotation triggers
            if (_inputs.Gameplay.RotateLeft.triggered) RotateLeft();
            if (_inputs.Gameplay.RotateRight.triggered) RotateRight();

            // _isFastDrop is the SINGLE source of down intent: the OR of every soft-drop source
            // (keyboard FastDrop key, held down-axis, touch pull via SetFastDrop), gesture-gated
            // once. Fall speed and the hover release read only this flag - no raw down-axis
            // checks elsewhere. The gesture event is edge-triggered here, on the combined value,
            // so keyboard and touch soft drops report identically.
            bool fastDrop = ((_inputs.Gameplay.FastDrop.IsPressed() || _moveInput.y < -0.5f)
                             && GestureAllowed(PieceGestures.SoftDrop))
                            || _externalFastDrop;
            if (fastDrop != _isFastDrop)
            {
                _isFastDrop = fastDrop;
                if (fastDrop) GameEvents.RaisePieceGesturePerformed(this, PieceGestures.SoftDrop);
            }
        }

        // cached: a fresh delegate every Update is a per-frame allocation. The keyboard/DAS
        // step is a Move gesture like any drag step, so it reports through the same event.
        _dasStep ??= direction =>
        {
            if (ShiftTargetColumn(direction) == ColumnStepResult.Moved)
            {
                GameEvents.RaisePieceGesturePerformed(this, PieceGestures.Move);
            }
        };
        ProcessHorizontalDas(_dasStep);
    }

    private void FixedUpdate()
    {
        if (HasLanded)
        {
            HandleLandedMaintenance();
            return;
        }

        if (!_isControlEnabled) return;

        HandleDynamicControl();
    }

    private void LateUpdate()
    {
        UpdatePlacementBeam();
    }

}
