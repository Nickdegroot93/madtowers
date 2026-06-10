using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class BlockController : MonoBehaviour
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
    [Tooltip("Minimum upward normal required for a collision to count as a landing surface.")]
    [SerializeField] private float minimumLandingNormalY = 0.45f;
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
    [SerializeField] private Color placementBeamColor = new Color(1f, 1f, 1f, 0.12f);
    [SerializeField] private int placementBeamSortingOrder = -10;

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
    [Tooltip("Small horizontal correction allowed while falling past a rejected corner graze, as a fraction of one grid cell.")]
    [Range(0f, 0.3f)]
    [SerializeField] private float lateralAssistMaxOverlapFraction = 0f;
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
    private float _gravityScaleMultiplier = 1f;

    private float _dasTimer;
    private int _lastInputDirection = 0;
    private bool _dasActive = false;

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
    }

    // Rotation nudges the target angle by a quarter turn. Active pieces snap to that target while
    // falling, so they stay on the same grid rules as horizontal movement.
    public void RotateLeft()
    {
        if (!_isControlEnabled || !CanRotateVariant) return;
        _targetAngleZ -= RotationStep;
    }

    public void RotateRight()
    {
        if (!_isControlEnabled || !CanRotateVariant) return;
        _targetAngleZ += RotationStep;
    }

    private bool CanRotateVariant => _appliedData == null || _appliedData.CanRotate;

    // Fast Drop
    public void SetFastDrop(bool active) => _isFastDrop = active;

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
    }

    public void ApplyData(BlockData data)
    {
        if (data == null) return;

        _appliedData = data;
        if (_rb == null) _rb = GetComponent<Rigidbody2D>();
        _rb.mass = data.Mass;
        _rb.sharedMaterial = ResolveBlockMaterial(data.PhysicsMaterial);
        _gravityScaleMultiplier = data.GravityScaleMultiplier;

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
        foreach (var sr in renderers)
        {
            sr.color = data.ColorTint;
            if (data.SpriteOverride != null) sr.sprite = data.SpriteOverride;
            if (data.MaterialOverride != null) sr.sharedMaterial = data.MaterialOverride;
        }

        ResetControlTargets();
        data.OnApplied(this);
    }

    private void CreatePlacementBeam()
    {
        if (!showPlacementBeam || _placementBeamRenderer != null) return;

        SpriteRenderer sourceRenderer = GetComponentInChildren<SpriteRenderer>();
        if (sourceRenderer == null || sourceRenderer.sprite == null) return;

        GameObject beam = new GameObject($"{name}_PlacementBeam");
        _placementBeamRenderer = beam.AddComponent<SpriteRenderer>();
        _placementBeamRenderer.sprite = sourceRenderer.sprite;
        _placementBeamRenderer.color = placementBeamColor;
        _placementBeamRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        _placementBeamRenderer.sortingOrder = placementBeamSortingOrder;
        _placementBeamRenderer.enabled = false;
    }

    private void DestroyPlacementBeam()
    {
        if (_placementBeamRenderer == null) return;

        Destroy(_placementBeamRenderer.gameObject);
        _placementBeamRenderer = null;
    }

    private void UpdatePlacementBeam()
    {
        if (_placementBeamRenderer == null) return;

        bool shouldShow = showPlacementBeam && _isControlEnabled && !HasLanded && !_hasTouchedDown;
        if (!shouldShow || !TryGetPlacementBeamFootprint(out float centerX, out float width) ||
            !TryGetPlacementBeamVerticalSpan(out float centerY, out float height))
        {
            _placementBeamRenderer.enabled = false;
            return;
        }

        Bounds spriteBounds = _placementBeamRenderer.sprite.bounds;
        float spriteWidth = Mathf.Max(0.001f, spriteBounds.size.x);
        float spriteHeight = Mathf.Max(0.001f, spriteBounds.size.y);

        Transform beamTransform = _placementBeamRenderer.transform;
        beamTransform.position = new Vector3(centerX, centerY, transform.position.z);
        beamTransform.rotation = Quaternion.identity;
        beamTransform.localScale = new Vector3(width / spriteWidth, height / spriteHeight, 1f);

        _placementBeamRenderer.color = placementBeamColor;
        _placementBeamRenderer.enabled = true;
    }

    private bool TryGetPlacementBeamFootprint(out float centerX, out float width)
    {
        centerX = transform.position.x;
        width = gridSpacing;

        _cellGeometry.Refresh();
        if (_cellGeometry.CellCenters.Count == 0) return false;

        float halfCell = gridSpacing * 0.5f;
        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        for (int i = 0; i < _cellGeometry.CellCenters.Count; i++)
        {
            Vector2 cellCenter = _cellGeometry.CellCenters[i];
            minX = Mathf.Min(minX, cellCenter.x - halfCell);
            maxX = Mathf.Max(maxX, cellCenter.x + halfCell);
        }

        if (minX == float.PositiveInfinity || maxX == float.NegativeInfinity) return false;

        centerX = (minX + maxX) * 0.5f;
        width = Mathf.Max(gridSpacing, maxX - minX);
        return true;
    }

    private bool TryGetPlacementBeamVerticalSpan(out float centerY, out float height)
    {
        centerY = transform.position.y;
        height = 0f;

        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
        }

        if (_mainCamera == null || !_mainCamera.orthographic) return false;

        height = _mainCamera.orthographicSize * 2f;
        centerY = _mainCamera.transform.position.y;
        return height > 0f;
    }

    // Physics shapes are slightly smaller than the visual cells (Tricky-Towers style). With an
    // exactly cell-sized footprint a piece can never enter a gap that is exactly its own size:
    // any sub-pixel drift of a neighbour pinches the slot, the piece wedges on the corners, and
    // the depenetration shoves the walls apart. The sprite stays full size; only collision
    // shrinks. The inset is uniform so it survives 90-degree rotations, and
    // BoxCollider2D.edgeRadius expands outward, so the box is shrunk by 2r on top of the inset.
    private void ApplyColliderForgiveness()
    {
        BoxCollider2D[] colliders = GetComponentsInChildren<BoxCollider2D>();
        if (colliders.Length == 0) return;

        float footprintScale = Mathf.Clamp(colliderFootprintScale, 0.85f, 1f);
        for (int i = 0; i < colliders.Length; i++)
        {
            BoxCollider2D box = colliders[i];
            Vector2 targetSize = box.size * footprintScale;
            float requestedRadius = Mathf.Max(0f, colliderCornerRadiusFraction) * gridSpacing;
            float radius = Mathf.Min(requestedRadius, Mathf.Min(targetSize.x, targetSize.y) * 0.45f);
            box.size = new Vector2(
                Mathf.Max(0.05f, targetSize.x - 2f * radius),
                Mathf.Max(0.05f, targetSize.y - 2f * radius));
            box.edgeRadius = radius;
        }
    }

    // Returns the variant's own PhysicsMaterial2D if one is assigned; otherwise a single
    // shared fallback material so blocks always have real friction (a null material would
    // leave them on engine defaults and make tall towers slide apart).
    private PhysicsMaterial2D ResolveBlockMaterial(PhysicsMaterial2D explicitMaterial)
    {
        if (explicitMaterial != null) return explicitMaterial;

        if (_sharedFallbackMaterial == null)
        {
            _sharedFallbackMaterial = new PhysicsMaterial2D("BlockFallbackMaterial")
            {
                friction = defaultBlockFriction,
                bounciness = defaultBlockBounciness
            };
        }

        return _sharedFallbackMaterial;
    }

    public void ApplyConfig(GameModeConfig config)
    {
        if (config == null) return;

        gridSpacing = config.GridSpacing;
        minimumLandingNormalY = config.MinimumLandingNormalY;
        horizontalPlacementBufferColumns = config.HorizontalPlacementBufferColumns;

        groundedCheckDistance = config.GroundedCheckDistance;
        maxLandingImpactSpeed = config.MaxLandingImpactSpeed;
        settleLinearThreshold = config.SettleLinearThreshold;
        settleAngularThreshold = config.SettleAngularThreshold;
        settleTime = config.SettleTime;
        sleepSettledBlocksOnLock = config.SleepSettledBlocksOnLock;
        microAlignSettledBlocks = config.MicroAlignSettledBlocks;
        microAlignMaxColumnFraction = config.MicroAlignMaxColumnFraction;
        microAlignMaxRotationDegrees = config.MicroAlignMaxRotationDegrees;
        maxControlTime = config.MaxControlTime;

        _floorSegments = config.FloorSegments;
        ResetControlTargets();
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
        TrackedBlocks.Remove(this);
        DestroyPlacementBeam();
    }

    public bool TryGetWorldBounds(out Bounds bounds)
    {
        return _cellGeometry.TryGetWorldBounds(out bounds);
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
            
            // Handle fast drop button
            _isFastDrop = _inputs.Gameplay.FastDrop.IsPressed();
        }

        ProcessHorizontalDas(ShiftTargetColumn);
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

    // Current downward speed (units/sec), including the fast-drop boost when S / down is held.
    private float GetActiveFallSpeed()
    {
        bool fastDropRequested = _isFastDrop || (_moveInput.y < -0.5f);
        return fallSpeed * (fastDropRequested ? fastDropMultiplier : 1f);
    }

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
        _originalCenterOfMass = _rb.centerOfMass;
        Vector2 primaryWorld = _cellGeometry.GetPrimaryWorldCenter(_rb.position);
        _rb.centerOfMass = transform.InverseTransformPoint(primaryWorld);
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

        ApplyControlledHorizontalMovement(velocityX * Time.fixedDeltaTime);

        // Transforms are synced manually (auto-sync is off), so the down-cast below sees the
        // horizontal/rotation moves made this step instead of last step's collider poses.
        Physics2D.SyncTransforms();

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

        ApplyLateralPlacementAssist(fallDistance + groundedCheckDistance);
        ApplyControlledVerticalMovement(fallDistance);
        ClearControlledLinearVelocity();
        ClampHorizontalToCameraBounds();
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

        LockBlock();
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

    private bool IsSettled()
    {
        return _rb.linearVelocity.magnitude <= settleLinearThreshold &&
               Mathf.Abs(_rb.angularVelocity) <= settleAngularThreshold;
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
        if (hit.collider == null || hit.normal.y < Mathf.Max(minimumLandingNormalY, landingSupportNormalY)) return false;
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

    private void ApplyLateralPlacementAssist(float maxDistance)
    {
        if (lateralAssistMaxOverlapFraction <= 0f) return;

        float assist = CalculateLateralPlacementAssist(maxDistance);
        if (Mathf.Abs(assist) <= 0.0001f) return;

        Vector3 position = transform.position;
        position.x += assist;
        SetPosition(position);
        Physics2D.SyncTransforms();
    }

    private float CalculateLateralPlacementAssist(float maxDistance)
    {
        float distance = Mathf.Max(0.001f, maxDistance);
        int count = _rb.Cast(Vector2.down, _contactFilter, _castResults, distance);
        float maxAssist = Mathf.Max(0f, lateralAssistMaxOverlapFraction) * gridSpacing;
        float bestAssist = 0f;

        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = _castResults[i];
            if (hit.collider == null || IsValidLandingSupport(hit)) continue;

            float sideSign = Mathf.Sign(hit.normal.x);
            if (Mathf.Approximately(sideSign, 0f)) continue;

            float overlap = GetHorizontalSupportOverlapAtHit(hit);
            if (overlap <= 0f || overlap > maxAssist) continue;

            float candidate = sideSign * overlap;
            if (Mathf.Abs(candidate) > Mathf.Abs(bestAssist))
            {
                bestAssist = candidate;
            }
        }

        return bestAssist;
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

    // Nudges the target column by one (driven by ProcessHorizontalDas). SteerWhileFalling then
    // slides the piece to that column over a few frames, so it stays in a lane but isn't instant.
    private void ShiftTargetColumn(int direction)
    {
        float candidate = _targetColumnX + direction * gridSpacing;
        if (IsColumnTargetWithinBounds(candidate) && IsGridPlacementFreeAtColumn(candidate))
        {
            _targetColumnX = candidate;
        }
    }

    private bool IsGridPlacementFreeAtColumn(float candidatePrimaryX)
    {
        _cellGeometry.Refresh();
        float currentPrimaryX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float deltaX = candidatePrimaryX - currentPrimaryX;
        float rowTolerance = gridSpacing * 0.8f;

        for (int i = 0; i < _cellGeometry.CellCenters.Count; i++)
        {
            Vector2 activeCell = _cellGeometry.CellCenters[i];
            if (IsCellBlockedByStaticObstacle(new Vector2(activeCell.x + deltaX, activeCell.y))) return false;

            float activeColumn = SnapValue(activeCell.x + deltaX, gridSpacing);
            float activeRow = SnapValue(activeCell.y, gridSpacing);

            for (int blockIndex = 0; blockIndex < TrackedBlocks.Count; blockIndex++)
            {
                BlockController block = TrackedBlocks[blockIndex];
                if (block == null || block == this || !block.HasLanded) continue;

                block._cellGeometry.Refresh();
                for (int cellIndex = 0; cellIndex < block._cellGeometry.CellCenters.Count; cellIndex++)
                {
                    Vector2 placedCell = block._cellGeometry.CellCenters[cellIndex];
                    float placedColumn = SnapValue(placedCell.x, gridSpacing);
                    float placedRow = SnapValue(placedCell.y, gridSpacing);
                    if (Mathf.Abs(placedColumn - activeColumn) <= GridMatchTolerance &&
                        Mathf.Abs(placedRow - activeRow) < rowTolerance)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    // Placed tetrominoes are handled by the grid-snapped check above (the grid stays the sole X
    // authority there), but support islands and other static geometry have no BlockController, so
    // a sideways step would otherwise teleport the kinematic piece straight into them.
    private bool IsCellBlockedByStaticObstacle(Vector2 candidateCellCenter)
    {
        Vector2 probeSize = Vector2.one * (gridSpacing * 0.8f);
        int count = Physics2D.OverlapBox(candidateCellCenter, probeSize, 0f, _contactFilter, _overlapResults);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = _overlapResults[i];
            if (hit == null || hit.isTrigger) continue;
            if (hit.attachedRigidbody == _rb) continue;
            if (hit.GetComponentInParent<BlockController>() != null) continue;
            return true;
        }

        return false;
    }

    private bool IsColumnTargetWithinBounds(float candidateColumnX)
    {
        if (!_cellGeometry.TryGetWorldBounds(out Bounds bounds)) return true;

        float primaryX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float leftReach = primaryX - bounds.min.x;
        float rightReach = bounds.max.x - primaryX;
        float minX = float.NegativeInfinity;
        float maxX = float.PositiveInfinity;

        if (TryGetGameplayHorizontalBounds(out float gameplayMinX, out float gameplayMaxX))
        {
            minX = gameplayMinX;
            maxX = gameplayMaxX;
        }

        if (TryGetCameraHorizontalBounds(out float cameraMinX, out float cameraMaxX))
        {
            minX = Mathf.Max(minX, cameraMinX);
            maxX = Mathf.Min(maxX, cameraMaxX);
        }

        const float tolerance = 0.001f;
        return candidateColumnX - leftReach >= minX - tolerance &&
               candidateColumnX + rightReach <= maxX + tolerance;
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

    private bool TryGetGameplayHorizontalBounds(out float minX, out float maxX)
    {
        minX = float.PositiveInfinity;
        maxX = float.NegativeInfinity;
        bool hasBounds = false;

        AddFloorHorizontalBounds(ref minX, ref maxX, ref hasBounds);
        AddPlacedBlockHorizontalBounds(ref minX, ref maxX, ref hasBounds);

        if (!hasBounds) return false;

        float buffer = Mathf.Max(0, horizontalPlacementBufferColumns) * gridSpacing;
        minX -= buffer;
        maxX += buffer;
        return true;
    }

    private void AddFloorHorizontalBounds(ref float minX, ref float maxX, ref bool hasBounds)
    {
        if (_floorSegments == null) return;

        for (int i = 0; i < _floorSegments.Count; i++)
        {
            FloorSegmentConfig segment = _floorSegments[i];
            if (segment == null) continue;

            float segmentMinX = (segment.LeftColumn - 0.5f) * gridSpacing;
            float segmentMaxX = (segment.RightColumn + 0.5f) * gridSpacing;
            ExpandHorizontalBounds(segmentMinX, segmentMaxX, ref minX, ref maxX, ref hasBounds);
        }
    }

    private void AddPlacedBlockHorizontalBounds(ref float minX, ref float maxX, ref bool hasBounds)
    {
        for (int i = 0; i < TrackedBlocks.Count; i++)
        {
            BlockController block = TrackedBlocks[i];
            if (block == null || block == this || !block.HasLanded) continue;
            if (!block.TryGetWorldBounds(out Bounds blockBounds)) continue;

            ExpandHorizontalBounds(blockBounds.min.x, blockBounds.max.x, ref minX, ref maxX, ref hasBounds);
        }
    }

    private void ExpandHorizontalBounds(float candidateMinX, float candidateMaxX, ref float minX, ref float maxX, ref bool hasBounds)
    {
        minX = hasBounds ? Mathf.Min(minX, candidateMinX) : candidateMinX;
        maxX = hasBounds ? Mathf.Max(maxX, candidateMaxX) : candidateMaxX;
        hasBounds = true;
    }

    private bool TryGetCameraHorizontalBounds(out float minX, out float maxX)
    {
        minX = 0f;
        maxX = 0f;

        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
        }

        if (_mainCamera == null || !_mainCamera.orthographic) return false;

        float halfWidth = _mainCamera.orthographicSize * _mainCamera.aspect;
        minX = _mainCamera.transform.position.x - halfWidth;
        maxX = _mainCamera.transform.position.x + halfWidth;
        return true;
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

    private void LockBlock()
    {
        if (!_isControlEnabled) return;
        _isControlEnabled = false;
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

    private void TryMicroAlignSettledBlock()
    {
        if (!microAlignSettledBlocks) return;

        Vector3 originalPosition = transform.position;
        float originalRotation = _rb.rotation;
        float snappedRotation = SnapValue(_rb.rotation, RotationStep);
        float rotationCorrection = Mathf.Abs(Mathf.DeltaAngle(_rb.rotation, snappedRotation));
        if (rotationCorrection > microAlignMaxRotationDegrees) return;

        transform.rotation = Quaternion.Euler(0f, 0f, snappedRotation);
        _rb.rotation = snappedRotation;
        Physics2D.SyncTransforms();

        _cellGeometry.Refresh();
        float primaryX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float snappedPrimaryX = SnapValue(primaryX, gridSpacing);
        float maxColumnCorrection = Mathf.Max(0f, microAlignMaxColumnFraction) * gridSpacing;
        float columnCorrection = snappedPrimaryX - primaryX;
        if (Mathf.Abs(columnCorrection) > maxColumnCorrection)
        {
            SetPosition(originalPosition);
            transform.rotation = Quaternion.Euler(0f, 0f, originalRotation);
            _rb.rotation = originalRotation;
            Physics2D.SyncTransforms();
            return;
        }

        Vector3 position = transform.position;
        position.x += columnCorrection;
        SetPosition(position);
        Physics2D.SyncTransforms();
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

    private void HandleLandedMaintenance()
    {
        if ((!microAlignSettledBlocks && !sleepSettledBlocksOnLock) || _rb == null) return;
        if (_rb.bodyType != RigidbodyType2D.Dynamic || _rb.IsSleeping()) return;

        UpdateStillnessWatchdog();
        if (_rb.IsSleeping()) return;

        if (IsSettled())
        {
            PullQuietBlockTowardGrid();
            SoftDampSettledBody();
            _landedMaintenanceSettleTimer += Time.fixedDeltaTime;
            if (_landedMaintenanceSettleTimer >= settleTime)
            {
                // Sleep freezes the block exactly where physics left it (see SleepSettledBody).
                // The teleporting micro-align only runs in the stay-awake configuration.
                if (sleepSettledBlocksOnLock)
                {
                    SleepSettledBody();
                }
                else
                {
                    TryMicroAlignSettledBlock();
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
    private void UpdateStillnessWatchdog()
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
        if (_stillnessTimer >= stillnessTime)
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
