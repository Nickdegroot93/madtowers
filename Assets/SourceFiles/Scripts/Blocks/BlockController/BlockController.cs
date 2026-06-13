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
    [Tooltip("Effective physics footprint of each cell as a fraction of the visual cell. Slightly undersized shapes give perfect placements real clearance - a piece can slide into a gap exactly its own size - and stop side-by-side blocks from transmitting every landing through the whole row.")]
    [Range(0.85f, 1f)]
    [SerializeField] private float colliderFootprintScale = 0.94f;

    [Header("Placement Beam")]
    [SerializeField] private bool showPlacementBeam = true;

    // Behind the ground skin (-50) and all bricks (0), in front of the background (-100):
    // the beam reads as part of the backdrop and visually stops at whatever it passes
    // behind. Code-owned (not serialized) so it can't go stale in prefab import caches.
    private const int PlacementBeamSortingOrder = -60;
    private const int VectorGuideGhostSortingOrder = -5;
    private static bool _vectorGuideEnabled;

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

    private const float RotationStep = 90f;
    private const float GridMatchTolerance = 0.05f;
    // The quiet grid pull only runs on blocks that seated flat. Nudging a tilted block sideways
    // engages/releases its lean contact each frame, which can feed a rocking limit cycle.
    private const float QuietPullMaxTiltDegrees = 1f;
    private const float LandedGravityScale = 1f;
    private const int CastResultCapacity = 32;

    private readonly RaycastHit2D[] _castResults = new RaycastHit2D[CastResultCapacity];
    private readonly Collider2D[] _overlapResults = new Collider2D[CastResultCapacity];

    private Rigidbody2D _rb;
    private StackingInputs _inputs;
    private bool _isControlEnabled = true;
    private Vector2 _moveInput;
    private bool _isFastDrop;
    private ContactFilter2D _contactFilter;
    private BlockData _appliedData;
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

    public bool HasLanded { get; private set; }
    public static IReadOnlyList<BlockController> AllBlocks => TrackedBlocks;

    public event System.Action OnBlockLocked;

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
        _nudgeLockedUntilTime = 0f;
        _vectorGuideEnabled = false;
    }

    public static void AddStandardBlockFrictionMultiplier(float multiplierDelta)
    {
        if (multiplierDelta <= 0f) return;

        _standardBlockFrictionMultiplier += multiplierDelta;
        RefreshStandardBlockFriction();
    }

    public static void SetVectorGuideEnabled(bool enabled)
    {
        _vectorGuideEnabled = enabled;
    }

    // Rotation nudges the target angle by a quarter turn. Active pieces snap to that target while
    // The piece currently under player control (null between lock and next spawn).
    // Touch gestures use this to address their commands.
    public static BlockController ActiveControlled { get; private set; }

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
        TrackedBlocks.Remove(this);
        DestroyPlacementBeam();
        _inputs?.Dispose();
    }

    public bool TryGetWorldBounds(out Bounds bounds)
    {
        return _cellGeometry.TryGetWorldBounds(out bounds);
    }

    // ---- Off-screen loss (driven by LossZone's camera-relative cull) -----------------------

    // "Falling" for the cull test. An unlocked piece can't be judged by velocity (steering
    // zeroes the kinematic body's velocity every step) - but an unlocked piece below the
    // line is descending by definition. A landed block must be dynamic, awake and genuinely
    // moving down: resting/sleeping/frozen tower blocks below the camera are the NORMAL
    // state at altitude and must never count as lost.
    private const float LostFallingSpeed = -1f;

    public bool IsLostBelow(float cullY)
    {
        if (!TryGetWorldBounds(out Bounds bounds) || bounds.max.y >= cullY) return false;
        if (!HasLanded) return true;
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
            if (_appliedData != null && _appliedData.InvertHorizontalControls)
            {
                _moveInput.x = -_moveInput.x;
            }

            // Handle rotation triggers
            if (_inputs.Gameplay.RotateLeft.triggered) RotateLeft();
            if (_inputs.Gameplay.RotateRight.triggered) RotateRight();
            
            // Handle fast drop button (keyboard OR touch gesture)
            _isFastDrop = _inputs.Gameplay.FastDrop.IsPressed() || _externalFastDrop;
        }

        // cached: a fresh delegate every Update is a per-frame allocation
        _dasStep ??= direction => ShiftTargetColumn(direction); // DAS ignores the step result
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
