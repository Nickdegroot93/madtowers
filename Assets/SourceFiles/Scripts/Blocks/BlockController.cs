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
    [Tooltip("Small clearance kept between the actively steered piece and side contacts. This lets the falling piece tuck into openings, but stops left/right input from shoving the landed tower sideways.")]
    [SerializeField] private float horizontalSteeringContactSkin = 0.02f;
    [Tooltip("How hard the piece rotates toward the requested angle. Rotation still takes a moment, so a late rotate gets 'caught' mid-turn as it lands.")]
    [SerializeField] private float rotationApproachSpeed = 20f;
    [Tooltip("Maximum spin speed while rotating, in degrees/second. Higher = quicker turns (and wilder spins when caught mid-rotation on landing).")]
    [SerializeField] private float maxRotationSpeed = 720f;
    [Tooltip("How close (world units) support must be below the piece before steering control is handed to physics. Keep small so players can make last-second tuck moves.")]
    [SerializeField] private float groundedCheckDistance = 0.03f;
    [Tooltip("If support is detected while the piece is still this far from its target column, keep sliding horizontally instead of landing on a corner.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float landingColumnToleranceFraction = 0.08f;
    [Tooltip("Upward contacts this close to a cell's left/right edge are ignored as landing support, so pieces do not stand on tiny corners instead of entering gaps.")]
    [Range(0f, 0.25f)]
    [SerializeField] private float landingCornerInsetFraction = 0.08f;
    [Tooltip("How quickly a controlled falling piece slides sideways off an invalid corner contact.")]
    [SerializeField] private float cornerSlideSpeed = 4f;
    [Tooltip("How long an unresolved invalid corner contact may stay controlled before it is released to physics.")]
    [SerializeField] private float invalidContactReleaseTime = 0.25f;
    [Tooltip("Maximum downward velocity kept when control hands off to physics. Keep at 0 to prevent falling impact from shoving the tower; gravity/weight still applies after landing.")]
    [SerializeField] private float maxLandingImpactSpeed = 0f;
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

    private static PhysicsMaterial2D _sharedFallbackMaterial;

    private const float RotationStep = 90f;
    private const float ControlledContactSkin = 0.001f;

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
    private float _invalidContactTimer;
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
        TrackedBlocks.Clear();
        _sharedFallbackMaterial = null;
    }

    // Rotation nudges the target angle by a quarter turn. The body physically rotates toward it
    // while falling; if it lands mid-turn, the remaining spin can carry into physics.
    public void RotateLeft()
    {
        if (!_isControlEnabled) return;
        _targetAngleZ -= RotationStep;
    }

    public void RotateRight()
    {
        if (!_isControlEnabled) return;
        _targetAngleZ += RotationStep;
    }

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

        ResetControlTargets();
    }

    public void ApplyData(BlockData data)
    {
        if (data == null) return;

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
        minimumLandingNormalY = config.MinimumLandingNormalY;
        horizontalPlacementBufferColumns = config.HorizontalPlacementBufferColumns;

        // Dynamic-mode control feel, configurable per level via the GameModeConfig asset.
        maxColumnMoveSpeed = config.MaxColumnMoveSpeed;
        columnApproachSpeed = config.ColumnApproachSpeed;
        horizontalSteeringContactSkin = config.HorizontalSteeringContactSkin;
        rotationApproachSpeed = config.RotationApproachSpeed;
        maxRotationSpeed = config.MaxRotationSpeed;
        groundedCheckDistance = config.GroundedCheckDistance;
        landingColumnToleranceFraction = config.LandingColumnToleranceFraction;
        landingCornerInsetFraction = config.LandingCornerInsetFraction;
        cornerSlideSpeed = config.CornerSlideSpeed;
        invalidContactReleaseTime = config.InvalidContactReleaseTime;
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

    // ---- Tricky Towers dynamic control ------------------------------------------------------
    // The piece is a real dynamic body the whole time, but while it is still falling we move it by
    // explicit cast-and-position steps with zero linear velocity. That keeps the placement grid
    // clean and prevents fall momentum from shoving the tower. The instant it has support beneath
    // it, control locks, the next block can spawn, and gravity/physics take over. Any remaining
    // spin can still settle under physics.
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
        float desiredVelocityX = Mathf.Clamp(columnError * columnApproachSpeed, -maxColumnMoveSpeed, maxColumnMoveSpeed);
        float velocityX = LimitHorizontalSteeringVelocity(desiredVelocityX);

        // Rotation: spin toward the target angle. The spin scales with how far we still have to
        // turn, so it eases into the target - and if we land before reaching it, the leftover
        // angular velocity carries into the physics hand-off.
        float angleError = Mathf.DeltaAngle(_rb.rotation, _targetAngleZ);
        _rb.angularVelocity = Mathf.Clamp(angleError * rotationApproachSpeed, -maxRotationSpeed, maxRotationSpeed);

        ApplyControlledHorizontalMovement(velocityX * Time.fixedDeltaTime);

        float fallDistance = GetActiveFallSpeed() * Time.fixedDeltaTime;
        if (TryGetControlledDownContact(
                fallDistance + groundedCheckDistance,
                out float contactDistance,
                out bool isLandingSupport,
                out Vector2 contactPoint))
        {
            bool delayForHorizontalTuck = ShouldDelayLandingForHorizontalTuck(velocityX);
            if (!isLandingSupport)
            {
                ApplyControlledVerticalMovementBeforeContact(fallDistance, contactDistance);

                bool slidOffCorner = delayForHorizontalTuck && TrySlideOffInvalidCorner(contactPoint.x, velocityX);
                if (slidOffCorner)
                {
                    _invalidContactTimer = 0f;
                    return;
                }

                _invalidContactTimer += Time.fixedDeltaTime;
                if (_invalidContactTimer < invalidContactReleaseTime)
                {
                    return;
                }

                _invalidContactTimer = 0f;
                BeginPhysicsLanding();
                return;
            }

            if (delayForHorizontalTuck)
            {
                ApplyControlledVerticalMovementBeforeContact(fallDistance, contactDistance);
                _invalidContactTimer = 0f;
                return;
            }

            if (contactDistance > 0f)
            {
                Vector3 position = transform.position;
                position.y -= contactDistance;
                SetPosition(position);
            }

            ClearControlledLinearVelocity();
            _invalidContactTimer = 0f;
            BeginPhysicsLanding();
            return;
        }

        ApplyControlledVerticalMovement(fallDistance);
        ClearControlledLinearVelocity();
        _invalidContactTimer = 0f;
        ClampHorizontalToCameraBounds();
    }

    private bool ShouldDelayLandingForHorizontalTuck(float appliedVelocityX)
    {
        if (Mathf.Abs(appliedVelocityX) <= 0.001f) return false;

        float currentColumnX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float remainingColumnError = Mathf.Abs(_targetColumnX - currentColumnX);
        float tolerance = Mathf.Max(0f, landingColumnToleranceFraction) * gridSpacing;
        return remainingColumnError > tolerance;
    }

    private bool TrySlideOffInvalidCorner(float contactPointX, float requestedVelocityX)
    {
        int requestedDirection = Mathf.Abs(requestedVelocityX) > 0.001f
            ? (int)Mathf.Sign(requestedVelocityX)
            : _cellGeometry.GetSlideDirectionAwayFromNearestHorizontalEdge(contactPointX);
        int geometryDirection = _cellGeometry.GetSlideDirectionAwayFromNearestHorizontalEdge(contactPointX);

        return TrySlideOffInvalidCornerInDirection(requestedDirection) ||
               (geometryDirection != requestedDirection && TrySlideOffInvalidCornerInDirection(geometryDirection));
    }

    private bool TrySlideOffInvalidCornerInDirection(int slideDirection)
    {
        if (slideDirection == 0) return false;

        float desiredSpeed = Mathf.Max(0f, cornerSlideSpeed) * slideDirection;
        float allowedSpeed = LimitHorizontalSteeringVelocity(desiredSpeed);
        if (Mathf.Sign(allowedSpeed) != slideDirection || Mathf.Abs(allowedSpeed) <= 0.001f) return false;

        ApplyControlledHorizontalMovement(allowedSpeed * Time.fixedDeltaTime);
        return true;
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

    private void ApplyControlledVerticalMovementBeforeContact(float fallDistance, float contactDistance)
    {
        float safeMoveDistance = Mathf.Min(fallDistance, Mathf.Max(0f, contactDistance - ControlledContactSkin));
        ApplyControlledVerticalMovement(safeMoveDistance);
        ClearControlledLinearVelocity();
    }

    private void ClearControlledLinearVelocity()
    {
        _rb.linearVelocity = Vector2.zero;
    }

    private void BeginPhysicsLanding()
    {
        if (_hasTouchedDown) return;

        // First contact: place the piece flush against support before the physics step can turn
        // fall speed into an impact impulse. From here, gravity and balance decide what happens.
        _hasTouchedDown = true;
        _rb.gravityScale = ResolvePhysicsGravityScale();
        _rb.centerOfMass = _originalCenterOfMass;

        float targetAngleError = Mathf.Abs(Mathf.DeltaAngle(_rb.rotation, _targetAngleZ));
        if (targetAngleError <= microAlignMaxRotationDegrees)
        {
            SetRotationZ(_targetAngleZ);
            _rb.angularVelocity = 0f;
        }

        SettleOntoContact();

        Vector2 landingVelocity = _rb.linearVelocity;
        landingVelocity.x = 0f;
        landingVelocity.y = Mathf.Max(landingVelocity.y, -Mathf.Max(0f, maxLandingImpactSpeed));
        if (landingVelocity.y > 0f) landingVelocity.y = 0f;
        _rb.linearVelocity = landingVelocity;

        LockBlock();
    }

    private float LimitHorizontalSteeringVelocity(float desiredVelocityX)
    {
        if (Mathf.Abs(desiredVelocityX) < 0.001f) return 0f;

        Vector2 direction = desiredVelocityX > 0f ? Vector2.right : Vector2.left;
        float requestedDistance = Mathf.Abs(desiredVelocityX) * Time.fixedDeltaTime;
        float skin = Mathf.Max(0f, horizontalSteeringContactSkin);
        float castDistance = requestedDistance + skin;

        RaycastHit2D[] results = new RaycastHit2D[8];
        int count = _rb.Cast(direction, _contactFilter, results, castDistance);
        float closestDistance = Mathf.Infinity;

        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = results[i];
            if (hit.collider == null) continue;

            // Only side-facing contacts should block horizontal steering. Upward floor/tower
            // contacts are handled by the landing handoff.
            if (Vector2.Dot(hit.normal, direction) >= -0.35f) continue;
            if (hit.distance < closestDistance) closestDistance = hit.distance;
        }

        if (closestDistance == Mathf.Infinity) return desiredVelocityX;

        float allowedDistance = Mathf.Max(0f, closestDistance - skin);
        if (allowedDistance <= 0.001f) return 0f;

        float allowedSpeed = allowedDistance / Time.fixedDeltaTime;
        return Mathf.Sign(desiredVelocityX) * Mathf.Min(Mathf.Abs(desiredVelocityX), allowedSpeed);
    }

    private bool TryGetControlledDownContact(
        float maxDistance,
        out float moveDistance,
        out bool isLandingSupport,
        out Vector2 contactPoint)
    {
        moveDistance = 0f;
        isLandingSupport = false;
        contactPoint = default;
        float distance = Mathf.Max(0.001f, maxDistance);

        RaycastHit2D[] results = new RaycastHit2D[8];
        int count = _rb.Cast(Vector2.down, _contactFilter, results, distance);
        float closestContactDistance = Mathf.Infinity;
        bool closestContactIsLandingSupport = false;
        Vector2 closestContactPoint = default;
        const float contactTieTolerance = 0.001f;

        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = results[i];
            if (hit.collider == null) continue;

            bool hitIsLandingSupport = IsLandingSupport(hit);
            if (hit.distance < closestContactDistance - contactTieTolerance)
            {
                closestContactDistance = hit.distance;
                closestContactIsLandingSupport = hitIsLandingSupport;
                closestContactPoint = hit.point;
            }
            else if (Mathf.Abs(hit.distance - closestContactDistance) <= contactTieTolerance && hitIsLandingSupport)
            {
                closestContactIsLandingSupport = true;
                closestContactPoint = hit.point;
            }
        }

        if (closestContactDistance == Mathf.Infinity) return false;

        moveDistance = Mathf.Max(0f, closestContactDistance);
        isLandingSupport = closestContactIsLandingSupport;
        contactPoint = closestContactPoint;
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

    private bool IsLandingSupport(RaycastHit2D hit)
    {
        if (hit.collider == null || hit.normal.y < minimumLandingNormalY) return false;

        float inset = Mathf.Max(0f, landingCornerInsetFraction) * gridSpacing;
        return _cellGeometry.IsWorldXInsideAnyCellInterior(hit.point.x, inset);
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

        FinalizeDynamicControl();

        if (_inputs != null) _inputs.Gameplay.Disable();

        CollectOverlappingPowerUps();
        ReportLockedToGameManager();

        OnBlockLocked?.Invoke();
    }

    // The dynamic piece is already a live rigidbody. Controlled linear velocity has already been
    // cleared on landing, while spin may continue so a piece caught mid-rotation can still tumble
    // into place. If the body is already settled at handoff, we can immediately micro-align/sleep;
    // otherwise landed maintenance will do that later.
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

        bool canFinalizeAsSettled = _hasTouchedDown && IsSettled();
        if (canFinalizeAsSettled)
        {
            TryMicroAlignSettledBlock();
        }

        if (sleepSettledBlocksOnLock && canFinalizeAsSettled)
        {
            SleepSettledBody();
        }
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

    private void SleepSettledBody()
    {
        _rb.linearVelocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        _rb.Sleep();
    }

    private void HandleLandedMaintenance()
    {
        if ((!microAlignSettledBlocks && !sleepSettledBlocksOnLock) || _rb == null || _rb.IsSleeping()) return;

        if (IsSettled())
        {
            _landedMaintenanceSettleTimer += Time.fixedDeltaTime;
            if (_landedMaintenanceSettleTimer >= settleTime)
            {
                TryMicroAlignSettledBlock();
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
