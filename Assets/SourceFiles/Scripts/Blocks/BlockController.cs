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
        _nudgeLockedUntilTime = 0f;
    }

    // Rotation nudges the target angle by a quarter turn. Active pieces snap to that target while
    // The piece currently under player control (null between lock and next spawn).
    // Touch gestures use this to address their commands.
    public static BlockController ActiveControlled { get; private set; }

    /// <summary>Width of one placement column in world units (for gesture distance mapping).</summary>
    public float GridSpacing => gridSpacing;

    // Why a sideways step was (or wasn't) taken. Drag steps ignore this (a blocked drag
    // stays silent); the nudge dash reads it to decide between wind and a slam.
    private enum ColumnStepResult { Moved, Gated, OutOfBounds, BlockedByBlocks, BlockedByStatic }

    // External (touch) horizontal step. Funnels into ShiftTargetColumn, so all grid,
    // placement-buffer and obstacle rules apply exactly as for keyboard movement.
    public void StepColumn(int direction)
    {
        TryStepColumn(direction);
    }

    private ColumnStepResult TryStepColumn(int direction, bool collectBlockers = false)
    {
        if (!_isControlEnabled || direction == 0) return ColumnStepResult.Gated;
        if (GameManager.Instance != null &&
            (GameManager.Instance.IsGamePaused || GameManager.Instance.isGameOver))
        {
            return ColumnStepResult.Gated;
        }

        if (_appliedData != null && _appliedData.InvertHorizontalControls) direction = -direction;
        return ShiftTargetColumn(direction > 0 ? 1 : -1, collectBlockers);
    }

    // The nudge dash is a timing skill: mistime it and the piece slams into whatever
    // blocked it - the impact shoves those bricks (loose ones can fall) and the rebound
    // locks further nudges out long enough that spamming can never be the optimal play.
    private const float NudgeFailLockoutSeconds = 0.5f;
    private const float NudgeSlamImpulse = 2f; // per blocking brick; Heavy (mass 3) barely budges

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
        if (NudgeLockoutRemaining > 0f) return; // still rebounding from a failed nudge

        int attempted = AttemptedStepDirection(direction);
        ColumnStepResult result = TryStepColumn(direction, collectBlockers: true);
        switch (result)
        {
            case ColumnStepResult.Moved:
                if (TryGetWorldBounds(out Bounds bounds)) DashWindFx.Spawn(bounds, attempted);
                SfxPlayer.Play("swoosh_01", 0.45f, 0.08f);
                break;

            case ColumnStepResult.BlockedByBlocks:
            case ColumnStepResult.BlockedByStatic:
                FailNudge(result == ColumnStepResult.BlockedByBlocks, attempted);
                break;
        }
    }

    // The direction the piece actually dashed toward (Dizzy inverts the input) - the
    // slam impulse and impact FX must match the physical hit, not the button pressed.
    private int AttemptedStepDirection(int inputDirection)
    {
        int direction = inputDirection > 0 ? 1 : -1;
        if (_appliedData != null && _appliedData.InvertHorizontalControls) direction = -direction;
        return direction;
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
            if (block._rb.bodyType != RigidbodyType2D.Dynamic) continue; // anchored/cemented stay rock

            block._rb.WakeUp();
            block._rb.AddForce(impulse, ForceMode2D.Impulse);
        }
    }

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
    // External (touch) fast-drop request; OR-ed with the keyboard each frame in Update.
    private bool _externalFastDrop;
    public void SetFastDrop(bool active) => _externalFastDrop = active;

    // Latched full-speed descent (triggered by a quick downward flick): stays on until the
    // piece lands, no held finger required. Steering stays available for last-second tucks.
    private bool _autoDrop;
    public void StartAutoDrop()
    {
        if (_isControlEnabled) _autoDrop = true;
    }

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

    // One sprite covers the whole tetromino (piece_T.png etc.), so every piece of a shape
    // looks identical. Cell renderers are hidden; their colliders (the entire physics
    // footprint) are untouched, and the skin child has no collider, so BlockCellGeometry
    // and all casts never see it. Sprites are generated by Tools/generate_piece_sprites.py.
    private void ApplyBlockSkin()
    {
        string shape = ThemeSkins.ExtractShapeToken(name);
        if (string.IsNullOrEmpty(shape)) return;

        Sprite pieceSprite = ThemeSkins.LoadPiece(shape);
        if (pieceSprite == null)
        {
            Debug.LogWarning(
                $"[BlockSkin] No sprite at Resources/{ThemeSkins.Folder}/piece_{shape} " +
                $"for '{name}' - piece renders as plain cells. Check the Skins folder imported correctly.",
                this);
            return;
        }

        SpriteRenderer[] cellRenderers = GetComponentsInChildren<SpriteRenderer>();
        if (cellRenderers.Length == 0) return;

        Vector2 min = cellRenderers[0].transform.localPosition;
        Vector2 max = min;
        foreach (var sr in cellRenderers)
        {
            Vector2 p = sr.transform.localPosition;
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
            sr.enabled = false;
        }

        GameObject skinGo = new GameObject("PieceSkin");
        skinGo.transform.SetParent(transform, false);
        skinGo.transform.localPosition = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0f);

        SpriteRenderer skinRenderer = skinGo.AddComponent<SpriteRenderer>();
        skinRenderer.sprite = pieceSprite;
        skinRenderer.sortingLayerID = cellRenderers[0].sortingLayerID;
        skinRenderer.sortingOrder = cellRenderers[0].sortingOrder;
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
        if (sourceRenderer == null) return;

        GameObject beam = new GameObject($"{name}_PlacementBeam");
        _placementBeamRenderer = beam.AddComponent<SpriteRenderer>();
        _placementBeamRenderer.sprite = RuntimeSprites.PlacementBeam();
        _placementBeamRenderer.drawMode = SpriteDrawMode.Sliced;
        _placementBeamRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        _placementBeamRenderer.sortingOrder = PlacementBeamSortingOrder;
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

        Transform beamTransform = _placementBeamRenderer.transform;
        beamTransform.position = new Vector3(centerX, centerY, transform.position.z);
        beamTransform.rotation = Quaternion.identity;
        beamTransform.localScale = Vector3.one;
        _placementBeamRenderer.size = new Vector2(width, height);
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
        float multiplier = 1f;
        if (_autoDrop) multiplier = fastDropMultiplier * AutoDropBoost;
        else if (_isFastDrop || _moveInput.y < -0.5f) multiplier = fastDropMultiplier;
        return fallSpeed * multiplier;
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

        // A piece steered into a gap/off the floor edge has nothing to land on, and the
        // kinematic body never triggers the loss zone - hand it to physics immediately
        // instead of stalling out the maxControlTime safety timer.
        if (!_hasTouchedDown && GameManager.Instance != null &&
            transform.position.y < GameManager.Instance.floorOriginY - 3f)
        {
            LockBlock();
            return;
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
        // It is COMPUTED from the cell layout, never read back from the body here: the body is
        // already Kinematic at this point and kinematic bodies report a zeroed centre of mass,
        // which pinned every landed piece's weight to its grip cell (the body origin) - edge
        // overhangs balanced on one side of the floor and toppled hard on the other.
        _originalCenterOfMass = ComputeUniformCellCenterOfMassLocal();
        Vector2 primaryWorld = _cellGeometry.GetPrimaryWorldCenter(_rb.position);
        _rb.centerOfMass = transform.InverseTransformPoint(primaryWorld);
    }

    // All cells are identical 1x1 boxes of equal density, so the true centre of mass is
    // exactly the average of the cell centres, expressed in body-local space.
    private Vector2 ComputeUniformCellCenterOfMassLocal()
    {
        _cellGeometry.Refresh();
        var centers = _cellGeometry.CellCenters;
        if (centers == null || centers.Count == 0) return Vector2.zero;

        Vector2 sum = Vector2.zero;
        for (int i = 0; i < centers.Count; i++) sum += centers[i];
        return transform.InverseTransformPoint(sum / centers.Count);
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

        Vector3 preStepPosition = transform.position;
        ApplyControlledHorizontalMovement(velocityX * Time.fixedDeltaTime);

        // Transforms are synced manually (auto-sync is off), so the down-cast below sees the
        // horizontal/rotation moves made this step instead of last step's collider poses.
        Physics2D.SyncTransforms();

        if (Mathf.Abs(columnDelta) > 0.001f) TuckIntoStaticPocket(preStepPosition);

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

        ApplyControlledVerticalMovement(fallDistance);
        ClearControlledLinearVelocity();
        ClampHorizontalToCameraBounds();
    }

    // A sidestep allowed on snapped-row forgiveness (see ClassifyGridPlacementAtColumn) may
    // seat the piece slightly off the pocket's row, overlapping the static cells above or
    // below it. The piece is kinematic and pre-landing, so Y is still descent-authored -
    // slide it vertically until clear (never sideways: the grid owns X). Corrections come
    // from static geometry only (it has no solver to mediate; a kinematic piece would
    // interpenetrate rock forever), but a tucked step must END fully clear of everything:
    // if the seated position still overlaps rock the budget couldn't clear OR a landed
    // brick the displacement would shove via depenetration, the whole step is reverted.
    private const float TuckMaxTravelFraction = 0.55f;

    private void TuckIntoStaticPocket(Vector3 preStepPosition)
    {
        float maxTravel = TuckMaxTravelFraction * gridSpacing;
        float totalY = 0f;
        bool fits = true;

        for (int iteration = 0; iteration < 3; iteration++)
        {
            fits = TryComputeStaticTuckCorrection(out float correction, out bool anyStaticOverlap);
            if (!fits) break;

            if (Mathf.Abs(correction) <= 0.0005f)
            {
                // The common case: an ordinary open-air sidestep never touched rock at all,
                // and pre-existing row-forgiveness brick proximity stays the solver's business.
                if (!anyStaticOverlap && totalY == 0f) return;
                break; // clear of rock after tucking - still must end clear of bricks
            }

            totalY += correction;
            if (Mathf.Abs(totalY) > maxTravel) { fits = false; break; }

            Vector3 position = transform.position;
            position.y += correction;
            SetPosition(position);
            Physics2D.SyncTransforms();
        }

        if (fits && !HasAnySolidOverlap()) return;

        SetPosition(preStepPosition);
        Physics2D.SyncTransforms();
        _targetColumnX = SnapValue(_cellGeometry.GetPrimaryWorldX(transform.position.x), gridSpacing);
    }

    // Single-axis separation from STATIC geometry. Per-contact pushes are combined as
    // extremes, never summed: two cells overlapping the same island row need the push
    // once, not twice, and opposing pushes (squeezed from above AND below) must read as
    // "does not fit", not cancel to zero and report clear.
    private bool TryComputeStaticTuckCorrection(out float correctionY, out bool anyStaticOverlap)
    {
        correctionY = 0f;
        anyStaticOverlap = false;
        float pushUp = 0f;
        float pushDown = 0f;

        IReadOnlyList<Collider2D> ownColliders = _cellGeometry.SolidColliders;
        for (int ownIndex = 0; ownIndex < ownColliders.Count; ownIndex++)
        {
            Collider2D ownCollider = ownColliders[ownIndex];
            if (ownCollider == null) continue;

            int count = ownCollider.Overlap(_contactFilter, _overlapResults);
            for (int hitIndex = 0; hitIndex < count; hitIndex++)
            {
                Collider2D other = _overlapResults[hitIndex];
                if (other == null || other.isTrigger) continue;
                if (other.attachedRigidbody == _rb) continue;
                if (other.GetComponentInParent<BlockController>() != null) continue;

                ColliderDistance2D distance = Physics2D.Distance(ownCollider, other);
                if (!distance.isOverlapped) continue;

                anyStaticOverlap = true;
                float push = distance.normal.y * distance.distance;
                if (push > pushUp) pushUp = push;
                else if (push < pushDown) pushDown = push;
            }
        }

        if (pushUp > 0f && pushDown < 0f) return false; // squeezed: no vertical move can fit
        correctionY = pushUp > 0f ? pushUp : pushDown;
        return true;
    }

    // Post-tuck validation: the seated position must overlap nothing solid - neither the
    // rock the tuck was clearing nor a landed brick the new Y would interpenetrate.
    private bool HasAnySolidOverlap()
    {
        IReadOnlyList<Collider2D> ownColliders = _cellGeometry.SolidColliders;
        for (int ownIndex = 0; ownIndex < ownColliders.Count; ownIndex++)
        {
            Collider2D ownCollider = ownColliders[ownIndex];
            if (ownCollider == null) continue;

            int count = ownCollider.Overlap(_contactFilter, _overlapResults);
            for (int hitIndex = 0; hitIndex < count; hitIndex++)
            {
                Collider2D other = _overlapResults[hitIndex];
                if (other == null || other.isTrigger) continue;
                if (other.attachedRigidbody == _rb) continue;

                if (Physics2D.Distance(ownCollider, other).isOverlapped) return true;
            }
        }

        return false;
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

        // Flick-dropped pieces thump: camera shake + impact sound. Both purely cosmetic
        // (physics never reads the camera, and the landing velocity above is already capped).
        if (_autoDrop)
        {
            TowerCameraController.Impact();
            SfxPlayer.PlayVariant("impact_heavy", 2, 0.6f, 0.07f);
        }

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
        if (hit.collider == null || hit.normal.y < landingSupportNormalY) return false;
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
    private ColumnStepResult ShiftTargetColumn(int direction, bool collectBlockers = false)
    {
        float candidate = _targetColumnX + direction * gridSpacing;
        if (!IsColumnTargetWithinBounds(candidate)) return ColumnStepResult.OutOfBounds;

        ColumnStepResult result = ClassifyGridPlacementAtColumn(candidate, collectBlockers);
        if (result == ColumnStepResult.Moved) _targetColumnX = candidate;
        return result;
    }

    // The landed bricks that refused the last sidestep - a failed nudge slams exactly these.
    private readonly List<BlockController> _stepBlockers = new List<BlockController>(4);

    // collectBlockers: only the nudge needs to know WHO refused the step (to slam them);
    // drag/DAS steps run at auto-repeat rate and take the early-out the moment anything
    // blocks, exactly like the pre-classification code did.
    private ColumnStepResult ClassifyGridPlacementAtColumn(float candidatePrimaryX, bool collectBlockers)
    {
        _stepBlockers.Clear();
        bool staticBlocked = false;

        _cellGeometry.Refresh();
        float currentPrimaryX = _cellGeometry.GetPrimaryWorldX(transform.position.x);
        float deltaX = candidatePrimaryX - currentPrimaryX;
        float rowTolerance = gridSpacing * 0.8f;

        for (int i = 0; i < _cellGeometry.CellCenters.Count; i++)
        {
            Vector2 activeCell = _cellGeometry.CellCenters[i];

            // Static geometry gets the same half-cell row forgiveness landed blocks get from
            // the snapped-row check below: a destination cell is blocked only if BOTH its
            // current (descent) Y and its snapped row are obstructed. With only the continuous
            // probe, a one-cell pocket between island cells demanded near-perfect vertical
            // alignment (~0.13 of a cell) and was effectively impossible to enter; tower
            // pockets with identical geometry have always allowed half a cell of slack. The
            // off-row seating this permits is resolved by the vertical tuck in SteerWhileFalling.
            Vector2 destination = new Vector2(activeCell.x + deltaX, activeCell.y);
            Vector2 destinationOnRow = new Vector2(destination.x, SnapValue(activeCell.y, gridSpacing));
            if (IsCellBlockedByStaticObstacle(destination) && IsCellBlockedByStaticObstacle(destinationOnRow))
            {
                if (!collectBlockers) return ColumnStepResult.BlockedByStatic;
                staticBlocked = true;
                continue; // rock decides this cell - a brick behind it never took the hit
            }

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
                        if (!collectBlockers) return ColumnStepResult.BlockedByBlocks;
                        if (!_stepBlockers.Contains(block)) _stepBlockers.Add(block);
                        break;
                    }
                }
            }
        }

        if (_stepBlockers.Count > 0) return ColumnStepResult.BlockedByBlocks;
        return staticBlocked ? ColumnStepResult.BlockedByStatic : ColumnStepResult.Moved;
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
        if (ActiveControlled == this) ActiveControlled = null;
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
