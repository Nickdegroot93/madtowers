using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class BlockController : MonoBehaviour
{
    private static readonly TowerGrid SharedGrid = new TowerGrid();
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
    [Tooltip("StrictGrid keeps landed blocks aligned. DynamicPhysics allows tilting/tumbling, which can create off-grid edge contacts.")]
    [SerializeField] private BlockLandingMode landingMode = BlockLandingMode.StrictGrid;
    [Tooltip("Small margin for center-of-mass support checks before an overhanging block tips into physics.")]
    [SerializeField] private float stabilityMargin = 0.08f;
    [Tooltip("Allows side contact with existing tower/support cells to stabilize hooked or cornered placements.")]
    [SerializeField] private bool lateralBraceStabilityEnabled = true;
    [SerializeField] private int lateralBraceMinimumContacts = 1;
    [SerializeField] private bool connectedComponentLateralBraceEnabled = true;
    [SerializeField] private int connectedComponentLateralBraceMinimumContacts = 1;
    [SerializeField] private int connectedComponentLateralBraceMaxCells = 4;
    [Tooltip("Extra columns beyond the current floor/tower edge where the active block may still be placed.")]
    [SerializeField] private int horizontalPlacementBufferColumns = 3;

    [Header("Physics Material")]
    [Tooltip("Surface friction applied when the block variant has no PhysicsMaterial2D assigned. Higher grips more (towers slide less); lower is more slippery and tippy. ~0.6-0.8 keeps neat stacks solid like Tricky Towers.")]
    [Range(0f, 1f)]
    [SerializeField] private float defaultBlockFriction = 0.7f;
    [Tooltip("Surface bounciness applied when no PhysicsMaterial2D is assigned. Keep at 0 for stable stacking.")]
    [Range(0f, 1f)]
    [SerializeField] private float defaultBlockBounciness = 0f;
    [Tooltip("Linear drag on a landed block. A little damping makes a placed block settle quickly and resist slow sliding instead of drifting. Too high feels floaty.")]
    [SerializeField] private float restingLinearDamping = 0.25f;
    [Tooltip("Angular drag on a landed block. Damps out wobble so blocks settle and go to sleep instead of jittering.")]
    [SerializeField] private float restingAngularDamping = 0.5f;

    [Tooltip("Shrinks each block collider horizontally by this many world units per side (visual size unchanged). Lets a piece drop into a matching-width gap instead of catching on the corners of its neighbours. Keep small (~0.02). Vertical size is left alone so stacking stays flush.")]
    [Range(0f, 0.2f)]
    [SerializeField] private float horizontalColliderInset = 0.02f;

    [Header("Tricky Towers Control (fallback; GameModeConfig overrides these per level)")]
    [Tooltip("How fast a piece slides between columns, in world units per second. High enough that a one-column hop is quick, low enough that you can still see and interrupt it.")]
    [SerializeField] private float maxColumnMoveSpeed = 14f;
    [Tooltip("How hard the piece is driven toward its target column. Higher reaches the lane faster; it always eases in, so it never overshoots into a half-column.")]
    [SerializeField] private float columnApproachSpeed = 25f;
    [Tooltip("How hard the piece rotates toward the requested angle. Rotation still takes a moment, so a late rotate gets 'caught' mid-turn as it lands.")]
    [SerializeField] private float rotationApproachSpeed = 20f;
    [Tooltip("Maximum spin speed while rotating, in degrees/second. Higher = quicker turns (and wilder spins when caught mid-rotation on landing).")]
    [SerializeField] private float maxRotationSpeed = 720f;
    [Tooltip("How close (world units) support must be below the piece before steering control is handed to physics.")]
    [SerializeField] private float groundedCheckDistance = 0.12f;
    [Tooltip("Caps how fast the piece is moving downward at the moment it lands (units/sec), regardless of how fast it was dropping. Makes a held-down fast drop hit the tower just as softly as a slow drop, so playing fast doesn't shove the blocks below around.")]
    [SerializeField] private float maxLandingImpactSpeed = 1.5f;
    [Tooltip("A landed piece counts as 'settling' once its linear speed (units/sec) drops below this.")]
    [SerializeField] private float settleLinearThreshold = 0.3f;
    [Tooltip("...and its spin (degrees/sec) drops below this.")]
    [SerializeField] private float settleAngularThreshold = 25f;
    [Tooltip("How long the piece must stay settled after landing before the next piece spawns.")]
    [SerializeField] private float settleTime = 0.2f;
    [Tooltip("Safety cap: force the piece to lock after this many seconds even if it never fully settles.")]
    [SerializeField] private float maxControlTime = 12f;

    private static PhysicsMaterial2D _sharedFallbackMaterial;

    private const float RotationStep = 90f;

    private Rigidbody2D _rb;
    private StackingInputs _inputs;
    private bool _isControlEnabled = true;
    private Vector2 _moveInput;
    private bool _isFastDrop;
    private ContactFilter2D _contactFilter;
    private ContactFilter2D _triggerFilter;
    private readonly BlockCellGeometry _cellGeometry = new BlockCellGeometry();
    private IReadOnlyList<FloorSegmentConfig> _floorSegments;
    private Camera _mainCamera;
    private float _physicsGravityScale = 1f;
    private float _gravityScaleMultiplier = 1f;
    private bool _ignoresStabilityFailure;

    private float _dasTimer;
    private int _lastInputDirection = 0;
    private bool _dasActive = false;

    // Tricky Towers dynamic-control state
    private float _targetAngleZ;
    private float _targetColumnX;
    private Vector2 _originalCenterOfMass;
    private bool _dynamicControlReady;
    private bool _hasTouchedDown;
    private float _settleTimer;
    private float _controlElapsed;

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
        SharedGrid.Clear();
        TrackedBlocks.Clear();
        _sharedFallbackMaterial = null;
    }

    public static bool RegisterStaticSupportCells(IReadOnlyList<Vector2> cellCenters, float gridSpacing)
    {
        return SharedGrid.RegisterStaticSupportCells(cellCenters, gridSpacing);
    }

    public static void ConfigureSharedFloor(IReadOnlyList<FloorSegmentConfig> floorSegments, float originY)
    {
        SharedGrid.ConfigureFloorSegments(floorSegments);
        SharedGrid.ConfigureOriginY(originY);
    }

    // Rotation. In dynamic (Tricky Towers) mode this just nudges the target angle by a quarter
    // turn and the body physically rotates toward it; in strict-grid mode it snaps instantly.
    public void RotateLeft()
    {
        if (!_isControlEnabled) return;
        if (landingMode == BlockLandingMode.DynamicPhysics) _targetAngleZ -= 90f;
        else Rotate(-90f);
    }

    public void RotateRight()
    {
        if (!_isControlEnabled) return;
        if (landingMode == BlockLandingMode.DynamicPhysics) _targetAngleZ += 90f;
        else Rotate(90f);
    }

    // Fast Drop
    public void SetFastDrop(bool active) => _isFastDrop = active;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.bodyType = RigidbodyType2D.Kinematic;
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

        // Trigger filter (for collecting power-ups on landing)
        _triggerFilter = new ContactFilter2D();
        _triggerFilter.useTriggers = true;
        _triggerFilter.useLayerMask = false;
        ApplyHorizontalColliderInset();
        _cellGeometry.Cache(gameObject);
        if (!TrackedBlocks.Contains(this))
        {
            TrackedBlocks.Add(this);
        }

        // Ensure initial placement is on the horizontal control grid.
        SnapToColumnGrid();
        SnapRotationToRightAngle();
        _targetAngleZ = transform.eulerAngles.z;
        _targetColumnX = SnapValue(_cellGeometry.GetPrimaryWorldX(transform.position.x), gridSpacing);
    }

    public void ApplyData(BlockData data)
    {
        if (data == null) return;

        if (_rb == null) _rb = GetComponent<Rigidbody2D>();
        _rb.mass = data.Mass;
        _rb.sharedMaterial = ResolveBlockMaterial(data.PhysicsMaterial);
        _gravityScaleMultiplier = data.GravityScaleMultiplier;
        _ignoresStabilityFailure = data.IgnoresStabilityFailure;

        if (data.OverrideLandingMode)
        {
            landingMode = data.LandingModeOverride;
        }

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
        foreach (var sr in renderers)
        {
            sr.color = data.ColorTint;
            if (data.SpriteOverride != null) sr.sprite = data.SpriteOverride;
            if (data.MaterialOverride != null) sr.sharedMaterial = data.MaterialOverride;
        }
    }

    // Pulls each cell collider in horizontally (leaving the sprite untouched) so a piece can
    // drop into a gap of equal column width without snagging on a neighbour's top corner.
    private void ApplyHorizontalColliderInset()
    {
        if (horizontalColliderInset <= 0f) return;

        BoxCollider2D[] colliders = GetComponentsInChildren<BoxCollider2D>();
        foreach (BoxCollider2D box in colliders)
        {
            Vector2 size = box.size;
            size.x = Mathf.Max(0.1f, size.x - horizontalColliderInset * 2f);
            box.size = size;
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
        landingMode = config.LandingMode;
        minimumLandingNormalY = config.MinimumLandingNormalY;
        stabilityMargin = config.StabilityMargin;
        lateralBraceStabilityEnabled = config.LateralBraceStabilityEnabled;
        lateralBraceMinimumContacts = config.LateralBraceMinimumContacts;
        connectedComponentLateralBraceEnabled = config.ConnectedComponentLateralBraceEnabled;
        connectedComponentLateralBraceMinimumContacts = config.ConnectedComponentLateralBraceMinimumContacts;
        connectedComponentLateralBraceMaxCells = config.ConnectedComponentLateralBraceMaxCells;
        horizontalPlacementBufferColumns = config.HorizontalPlacementBufferColumns;

        // Dynamic-mode control feel, configurable per level via the GameModeConfig asset.
        maxColumnMoveSpeed = config.MaxColumnMoveSpeed;
        columnApproachSpeed = config.ColumnApproachSpeed;
        rotationApproachSpeed = config.RotationApproachSpeed;
        maxRotationSpeed = config.MaxRotationSpeed;
        groundedCheckDistance = config.GroundedCheckDistance;
        maxLandingImpactSpeed = config.MaxLandingImpactSpeed;
        settleLinearThreshold = config.SettleLinearThreshold;
        settleAngularThreshold = config.SettleAngularThreshold;
        settleTime = config.SettleTime;
        maxControlTime = config.MaxControlTime;

        _floorSegments = config.FloorSegments;
        SharedGrid.ConfigureFloorSegments(config.FloorSegments);
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
    }

    public bool TryGetWorldBounds(out Bounds bounds)
    {
        return _cellGeometry.TryGetWorldBounds(out bounds);
    }

    private void Update()
    {
        if (!_isControlEnabled) return;

        // Direct read to ensure we have the latest value
        if (_inputs != null)
        {
            _moveInput = _inputs.Gameplay.Move.ReadValue<Vector2>();
            
            // Handle rotation triggers
            if (_inputs.Gameplay.RotateLeft.triggered) RotateLeft();
            if (_inputs.Gameplay.RotateRight.triggered) RotateRight();
            
            // Handle fast drop button
            _isFastDrop = _inputs.Gameplay.FastDrop.IsPressed();
        }

        // Both modes share the same tap/hold (DAS auto-repeat) timing for left/right and differ
        // only in what a single step does: strict grid teleports a column, dynamic mode shifts the
        // target column and physically slides toward it.
        System.Action<int> stepLeftRight = landingMode == BlockLandingMode.DynamicPhysics
            ? ShiftTargetColumn
            : (System.Action<int>)MoveHorizontalDiscrete;
        ProcessHorizontalDas(stepLeftRight);
    }

    private void FixedUpdate()
    {
        if (!_isControlEnabled) return;

        HandleFalling();
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

    public void MoveHorizontalDiscrete(int direction)
    {
        Vector3 originalPos = transform.position;
        Vector3 targetPos = originalPos + new Vector3(direction * gridSpacing, 0, 0);
        targetPos.x = SnapValue(targetPos.x, gridSpacing);

        // Tentatively move, then revert if it overlaps anything solid.
        SetPosition(targetPos);
        SnapToColumnGrid();

        if (!IsPlacementValid())
        {
            SetPosition(originalPos);
        }
    }

    private void HandleFalling()
    {
        if (landingMode == BlockLandingMode.StrictGrid)
        {
            HandleStrictGridFalling();
        }
        else
        {
            HandleDynamicControl();
        }
    }

    private void HandleStrictGridFalling()
    {
        Vector2 fallDelta = Vector2.down * GetActiveFallSpeed() * Time.fixedDeltaTime;

        // Keep the block perfectly on the placement grid while falling.
        SnapToColumnGrid();
        SnapRotationToRightAngle();

        if (TryGetStrictGridMoveDistance(fallDelta, out float moveDistance))
        {
            _rb.MovePosition(_rb.position + fallDelta);
        }
        else
        {
            MoveToContact(fallDelta, moveDistance);
            LockBlock();
        }
    }

    // Current downward speed (units/sec), including the fast-drop boost when S / down is held.
    private float GetActiveFallSpeed()
    {
        bool fastDropRequested = _isFastDrop || (_moveInput.y < -0.5f);
        return fallSpeed * (fastDropRequested ? fastDropMultiplier : 1f);
    }

    // ---- Tricky Towers dynamic control ------------------------------------------------------
    // The piece is a real dynamic body the whole time. While it is still falling we steer it by
    // setting its velocity (horizontal momentum) and spinning it toward a target angle. The
    // instant it has support beneath it we stop steering and hand it fully to physics, carrying
    // over whatever motion it had - so a rotation that was still in progress finishes physically
    // against the tower (the "caught in the middle" feel). It locks once it settles.
    private void HandleDynamicControl()
    {
        InitDynamicControlBody();
        _controlElapsed += Time.fixedDeltaTime;

        bool grounded = _hasTouchedDown || IsGroundedForControl();

        if (!grounded)
        {
            SteerWhileFalling();
        }
        else
        {
            if (!_hasTouchedDown)
            {
                // First contact: hand back the real centre of mass and let gravity take over so
                // the piece settles and topples naturally.
                _hasTouchedDown = true;
                _settleTimer = 0f;
                _rb.gravityScale = ResolvePhysicsGravityScale();
                _rb.centerOfMass = _originalCenterOfMass;

                // Decouple landing impact from fall speed: drop the piece flush against whatever it
                // touched (no gap to re-accelerate over) and cap its downward speed, so a fast drop
                // lands as softly as a slow one and doesn't shove the blocks below around. Sideways
                // and spin momentum are left intact so "caught mid-move/rotation" still works.
                SettleOntoContact();
                Vector2 landingVelocity = _rb.linearVelocity;
                if (landingVelocity.y < -maxLandingImpactSpeed) landingVelocity.y = -maxLandingImpactSpeed;
                _rb.linearVelocity = landingVelocity;
            }

            if (IsSettled())
            {
                _settleTimer += Time.fixedDeltaTime;
                if (_settleTimer >= settleTime)
                {
                    LockBlock();
                    return;
                }
            }
            else
            {
                _settleTimer = 0f;
            }
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

        // Capture the full play gravity (set by the spawner) before we switch it off; we drive the
        // fall ourselves while steering, then restore this gravity the moment the piece lands.
        _physicsGravityScale = ResolvePhysicsGravityScale();

        _rb.bodyType = RigidbodyType2D.Dynamic;
        _rb.constraints = RigidbodyConstraints2D.None;
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
        // Horizontal: drive toward the target COLUMN (not a free analog position) so the piece
        // always comes to rest in a lane and two blocks line up cleanly. The drive eases in -
        // quick but not instant - so a piece can be caught between columns if it lands mid-slide.
        float currentColumnX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float columnError = _targetColumnX - currentColumnX;
        Vector2 velocity = _rb.linearVelocity;
        velocity.x = Mathf.Clamp(columnError * columnApproachSpeed, -maxColumnMoveSpeed, maxColumnMoveSpeed);
        velocity.y = -GetActiveFallSpeed();
        _rb.linearVelocity = velocity;

        // Rotation: spin toward the target angle. The spin scales with how far we still have to
        // turn, so it eases into the target - and if we land before reaching it, the leftover
        // angular velocity carries into the physics hand-off.
        float angleError = Mathf.DeltaAngle(_rb.rotation, _targetAngleZ);
        _rb.angularVelocity = Mathf.Clamp(angleError * rotationApproachSpeed, -maxRotationSpeed, maxRotationSpeed);

        ClampHorizontalToCameraBounds();
    }

    private bool IsGroundedForControl()
    {
        // Look ahead at least one fall-step so a fast drop can't skip past contact between frames.
        float fallStep = GetActiveFallSpeed() * Time.fixedDeltaTime * 1.5f;
        float distance = Mathf.Max(groundedCheckDistance, fallStep);

        RaycastHit2D[] results = new RaycastHit2D[8];
        int count = _rb.Cast(Vector2.down, _contactFilter, results, distance);
        for (int i = 0; i < count; i++)
        {
            if (IsLandingSupport(results[i])) return true;
        }

        return false;
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
        RaycastHit2D[] results = new RaycastHit2D[8];
        int count = _rb.Cast(Vector2.down, _contactFilter, results, gridSpacing);
        float minDistance = Mathf.Infinity;
        for (int i = 0; i < count; i++)
        {
            if (IsLandingSupport(results[i]) && results[i].distance < minDistance)
            {
                minDistance = results[i].distance;
            }
        }

        if (minDistance != Mathf.Infinity && minDistance > 0f)
        {
            Vector3 position = transform.position;
            position.y -= minDistance;
            SetPosition(position);
        }
    }

    // Nudges the target column by one (driven by ProcessHorizontalDas). SteerWhileFalling then
    // slides the piece to that column over a few frames, so it stays in a lane but isn't instant.
    private void ShiftTargetColumn(int direction)
    {
        float candidate = _targetColumnX + direction * gridSpacing;
        if (IsColumnTargetWithinBounds(candidate))
        {
            _targetColumnX = candidate;
        }
    }

    private bool IsColumnTargetWithinBounds(float candidateColumnX)
    {
        if (!TryGetCameraHorizontalBounds(out float cameraMinX, out float cameraMaxX)) return true;
        if (!_cellGeometry.TryGetWorldBounds(out Bounds bounds)) return true;

        float primaryX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float leftReach = primaryX - bounds.min.x;
        float rightReach = bounds.max.x - primaryX;
        const float tolerance = 0.001f;
        return candidateColumnX - leftReach >= cameraMinX - tolerance &&
               candidateColumnX + rightReach <= cameraMaxX + tolerance;
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

    private void Rotate(float angle)
    {
        if (!_isControlEnabled) return;

        Quaternion originalRot = transform.rotation;
        Vector3 originalPos = transform.position;

        SetRotationZ(transform.eulerAngles.z + angle);
        SnapToColumnGrid();

        if (!IsPlacementValid() && !TryWallKick())
        {
            // No valid spot found - revert the rotation entirely.
            transform.rotation = originalRot;
            if (_rb != null) _rb.rotation = originalRot.eulerAngles.z;
            SetPosition(originalPos);
        }
    }

    private bool TryWallKick()
    {
        Vector3 basePos = transform.position;
        float[] offsets =
        {
            gridSpacing,
            -gridSpacing,
            gridSpacing * 2f,
            -gridSpacing * 2f,
            gridSpacing * 3f,
            -gridSpacing * 3f,
            gridSpacing * 4f,
            -gridSpacing * 4f
        };

        foreach (float offset in offsets)
        {
            SetPosition(basePos + new Vector3(offset, 0, 0));
            SnapToColumnGrid();
            if (IsPlacementValid()) return true;
        }

        SetPosition(basePos);
        return false;
    }

    private bool TryGetStrictGridMoveDistance(Vector2 delta, out float moveDistance)
    {
        float distance = delta.magnitude;
        moveDistance = distance;
        if (distance < 0.0001f) return true;

        bool blocked = false;

        _cellGeometry.Refresh();
        if (SharedGrid.HasOriginY)
        {
            blocked = !SharedGrid.TryGetDropDistance(_cellGeometry.CellCenters, gridSpacing, distance, out moveDistance);
        }

        if (!SharedGrid.HasOriginY && TryGetPhysicalSupportMoveDistance(distance, out float floorMoveDistance))
        {
            moveDistance = Mathf.Min(moveDistance, floorMoveDistance);
            blocked = true;
        }

        return !blocked;
    }

    private bool IsLandingSupport(RaycastHit2D hit)
    {
        return hit.collider != null && hit.normal.y >= minimumLandingNormalY;
    }

    private bool TryGetPhysicalSupportMoveDistance(float maxDistance, out float moveDistance)
    {
        moveDistance = 0f;

        RaycastHit2D[] results = new RaycastHit2D[8];
        int count = _rb.Cast(Vector2.down, _contactFilter, results, maxDistance);
        if (count == 0) return false;

        moveDistance = Mathf.Infinity;
        bool foundFloor = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = results[i];
            if (!IsLandingSupport(hit)) continue;

            BlockController hitBlock = GetBlockController(hit.collider);
            if (hitBlock != null && SharedGrid.ContainsBlock(hitBlock)) continue;

            if (hit.distance < moveDistance)
            {
                moveDistance = hit.distance;
                foundFloor = true;
            }
        }

        if (!foundFloor)
        {
            moveDistance = 0f;
            return false;
        }

        return true;
    }

    private BlockController GetBlockController(Collider2D hitCollider)
    {
        if (hitCollider == null) return null;
        Rigidbody2D attachedBody = hitCollider.attachedRigidbody;
        if (attachedBody != null) return attachedBody.GetComponent<BlockController>();
        return hitCollider.GetComponentInParent<BlockController>();
    }

    private bool IsOverlapping()
    {
        Collider2D[] results = new Collider2D[8];
        int count = _rb.Overlap(_contactFilter, results);
        return count > 0;
    }

    private bool IsPlacementValid()
    {
        return !IsOverlapping() && IsWithinHorizontalPlacementBounds();
    }

    private bool IsWithinHorizontalPlacementBounds()
    {
        if (!_cellGeometry.TryGetWorldBounds(out Bounds bounds)) return true;
        if (!TryGetGameplayHorizontalBounds(out float minX, out float maxX)) return true;

        if (TryGetCameraHorizontalBounds(out float cameraMinX, out float cameraMaxX))
        {
            minX = Mathf.Max(minX, cameraMinX);
            maxX = Mathf.Min(maxX, cameraMaxX);
        }

        const float tolerance = 0.001f;
        return bounds.min.x >= minX - tolerance && bounds.max.x <= maxX + tolerance;
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

    private void SnapRotationToRightAngle()
    {
        SetRotationZ(transform.eulerAngles.z);
    }

    private void SetRotationZ(float angle)
    {
        float snappedAngle = SnapValue(angle, RotationStep);
        transform.rotation = Quaternion.Euler(0f, 0f, snappedAngle);
        if (_rb != null) _rb.rotation = snappedAngle;
    }

    private void LockBlock()
    {
        if (!_isControlEnabled) return;
        _isControlEnabled = false;
        HasLanded = true;

        FinalizeLanding();

        if (_inputs != null) _inputs.Gameplay.Disable();

        CollectOverlappingPowerUps();
        ReportLockedToGameManager();

        OnBlockLocked?.Invoke();
        enabled = false;
    }

    private void FinalizeLanding()
    {
        if (landingMode == BlockLandingMode.DynamicPhysics)
        {
            FinalizeDynamicControl();
        }
        else
        {
            LockToGrid();
        }
    }

    // The dynamic piece is already a live, moving rigidbody - we just stop steering it. Crucially
    // we do NOT zero its velocity/spin here, so a piece caught mid-rotation keeps tumbling into
    // place under physics. We only make sure gravity is back on (it is off while we steer).
    private void FinalizeDynamicControl()
    {
        _rb.bodyType = RigidbodyType2D.Dynamic;
        _rb.constraints = RigidbodyConstraints2D.None;
        if (_dynamicControlReady)
        {
            _rb.centerOfMass = _originalCenterOfMass;
        }
        if (_rb.gravityScale <= 0f)
        {
            _rb.gravityScale = ResolvePhysicsGravityScale();
        }
    }

    private void ReleaseIntoPhysics(float instabilityDirection = 0f)
    {
        // Pure Box2D hand-off (the Tricky Towers model). The player steered the piece down on a
        // clean column/right-angle and it has just touched the tower. We clear the carried-over
        // control velocity and hand the body entirely to the physics engine: gravity + friction
        // settle it flush, and it tips over on its own if it is unbalanced. No grid snapping and
        // no freezing - the engine does all the work from here.
        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        _rb.gravityScale = ResolvePhysicsGravityScale();
        _rb.constraints = RigidbodyConstraints2D.None;
        _rb.bodyType = RigidbodyType2D.Dynamic;
    }

    internal void ReleaseFromGridInstability(float instabilityDirection)
    {
        if (_rb == null) _rb = GetComponent<Rigidbody2D>();

        _isControlEnabled = false;
        HasLanded = true;
        if (_inputs != null) _inputs.Gameplay.Disable();

        ReleaseIntoPhysics(instabilityDirection);
        enabled = false;
    }

    private void LockToGrid()
    {
        SnapRotationToRightAngle();
        SnapToColumnGrid();
        _cellGeometry.Refresh();
        SharedGrid.EnsureOriginY(_cellGeometry.CellCenters);
        SnapToRowGrid();
        SnapToColumnGrid();
        _cellGeometry.Refresh();

        if (!_ignoresStabilityFailure && !SharedGrid.IsCenterOfMassSupported(
                _cellGeometry.CellCenters,
                gridSpacing,
                stabilityMargin,
                lateralBraceStabilityEnabled,
                lateralBraceMinimumContacts,
                out float instabilityDirection))
        {
            ReleaseIntoPhysics(instabilityDirection);
            return;
        }

        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        _physicsGravityScale = ResolvePhysicsGravityScale();
        _rb.gravityScale = 0f;
        _rb.constraints = RigidbodyConstraints2D.FreezeAll;
        _rb.bodyType = RigidbodyType2D.Kinematic;

        SharedGrid.RegisterCells(_cellGeometry.CellCenters, gridSpacing, this);
        SharedGrid.ReleaseUnstableComponents(
            gridSpacing,
            stabilityMargin,
            connectedComponentLateralBraceEnabled,
            connectedComponentLateralBraceMinimumContacts,
            connectedComponentLateralBraceMaxCells);
    }

    private float ResolvePhysicsGravityScale()
    {
        if (_rb != null && _rb.gravityScale > 0f)
        {
            _physicsGravityScale = _rb.gravityScale * _gravityScaleMultiplier;
        }
        else if (_physicsGravityScale <= 0f && GameManager.Instance != null)
        {
            _physicsGravityScale = GameManager.Instance.currentGravityScale;
        }

        return Mathf.Max(0.01f, _physicsGravityScale);
    }

    private void SnapToRowGrid()
    {
        if (!SharedGrid.HasOriginY) return;

        float yToSnap = _cellGeometry.GetPrimaryWorldY(transform.position.y);
        float snappedY = SharedGrid.SnapWorldY(yToSnap, gridSpacing);
        float correction = snappedY - yToSnap;

        Vector3 pos = transform.position;
        pos.y += correction;
        SetPosition(pos);
    }

    private void MoveToContact(Vector2 attemptedDelta, float contactDistance)
    {
        if (contactDistance <= 0f) return;

        Vector2 direction = attemptedDelta.normalized;
        SetPosition(transform.position + (Vector3)(direction * contactDistance));
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

    private void CollectOverlappingPowerUps()
    {
        Collider2D[] results = new Collider2D[8];
        int count = _rb.Overlap(_triggerFilter, results);
        for (int i = 0; i < count; i++)
        {
            if (results[i] == null) continue;
            PowerUp pu = results[i].GetComponentInParent<PowerUp>();
            if (pu != null) pu.Collect();
        }
    }
}
