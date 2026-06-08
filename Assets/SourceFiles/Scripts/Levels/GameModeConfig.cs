using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "GameModeConfig", menuName = "Stacking/Game Mode Config")]
public class GameModeConfig : ScriptableObject
{
    [Header("Round")]
    [Min(0)]
    [SerializeField] private int startingLives = 0;
    [SerializeField] private float initialFallSpeed = 2f;
    [SerializeField] private DifficultyScalingMode difficultyScalingMode = DifficultyScalingMode.PerBlock;
    [SerializeField] private DifficultyAdjustmentMode difficultyAdjustmentMode = DifficultyAdjustmentMode.Additive;
    [SerializeField] private float speedIncreasePerBlock = 0.1f;
    [SerializeField] private float initialGravityScale = 1f;
    [SerializeField] private float gravityIncreasePerBlock = 0.05f;
    [SerializeField] private float speedIncreaseIntervalSeconds = 60f;
    [SerializeField] private float speedIncreasePerInterval = 0.1f;
    [SerializeField] private float gravityIncreasePerInterval = 0.05f;

    [Header("Spawning")]
    [SerializeField] private BlockDefinition[] blockBag;
    [SerializeField] private BlockData[] fallbackBlockDataVariants;
    [SerializeField] private float spawnDelay = 0f;

    [Header("Placement")]
    [SerializeField] private float gridSpacing = 1f;
    [SerializeField] private float minimumLandingNormalY = 0.45f;
    [Tooltip("Extra columns beyond the current floor/tower edge where the active block may still be placed.")]
    [Min(0)]
    [SerializeField] private int horizontalPlacementBufferColumns = 3;

    [Header("Tricky Towers Control (per level)")]
    [Tooltip("How fast a piece slides between columns (units/sec).")]
    [SerializeField] private float maxColumnMoveSpeed = 14f;
    [Tooltip("How hard the piece is driven toward its target column (eases in, never overshoots).")]
    [SerializeField] private float columnApproachSpeed = 25f;
    [Tooltip("Clearance kept when steering into side contacts. Prevents active pieces from pushing the landed tower sideways while still allowing late tucks into openings.")]
    [SerializeField] private float horizontalSteeringContactSkin = 0.02f;
    [Tooltip("How hard the piece rotates toward the requested angle.")]
    [SerializeField] private float rotationApproachSpeed = 20f;
    [Tooltip("Maximum spin speed while rotating (degrees/sec).")]
    [SerializeField] private float maxRotationSpeed = 720f;
    [Tooltip("How close (world units) support must be below a piece before control is handed to physics. Keep small so players can make last-second tuck moves.")]
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
    [Tooltip("A landed piece is 'settled' once its linear speed (units/sec) drops below this. Keep low so unstable pieces get time to tip before maintenance runs.")]
    [SerializeField] private float settleLinearThreshold = 0.08f;
    [Tooltip("...and its spin (degrees/sec) drops below this.")]
    [SerializeField] private float settleAngularThreshold = 8f;
    [Tooltip("How long a landed piece must stay settled before maintenance micro-aligns/sleeps it.")]
    [SerializeField] private float settleTime = 0.35f;
    [Tooltip("Sleep settled dynamic blocks when control finishes. This prevents tiny drift while still allowing future contacts to wake the block.")]
    [SerializeField] private bool sleepSettledBlocksOnLock = true;
    [Tooltip("After a block genuinely settles, correct tiny X/rotation drift back to the placement grid. Large offsets or visibly tilted blocks are left to physics.")]
    [SerializeField] private bool microAlignSettledBlocks = true;
    [Tooltip("Maximum X correction allowed for settled micro-alignment, as a fraction of one grid cell.")]
    [Range(0f, 0.25f)]
    [SerializeField] private float microAlignMaxColumnFraction = 0.08f;
    [Tooltip("Maximum rotation correction allowed for settled micro-alignment, in degrees.")]
    [Range(0f, 15f)]
    [SerializeField] private float microAlignMaxRotationDegrees = 4f;
    [Tooltip("Safety cap: lock a piece after this many seconds even if it never finds a normal landing.")]
    [SerializeField] private float maxControlTime = 12f;

    [Header("Power Ups")]
    [SerializeField] private float powerUpSpawnInterval = 10f;
    [SerializeField] private float powerUpSpawnXRange = 4f;
    [SerializeField] private float slowMotionDuration = 10f;
    [SerializeField] private float slowMotionScale = 0.5f;

    [Header("Static Support Islands")]
    [SerializeField] private bool staticSupportIslandsEnabled = true;
    [Tooltip("Every N meters of max tower height, the level rolls once for a support island.")]
    [Min(0.1f)]
    [SerializeField] private float staticSupportIslandHeightInterval = 8f;
    [Tooltip("Chance that a support island appears when the interval is reached.")]
    [Range(0f, 1f)]
    [SerializeField] private float staticSupportIslandSpawnChance = 0.35f;
    [Tooltip("Max tower height required before the first support island roll can happen.")]
    [Min(0f)]
    [SerializeField] private float staticSupportIslandFirstHeight = 6f;
    [Tooltip("How far above the current max tower height the island is placed.")]
    [Min(0f)]
    [SerializeField] private float staticSupportIslandSpawnAheadHeight = 8f;
    [SerializeField] private int staticSupportIslandMinColumn = -5;
    [SerializeField] private int staticSupportIslandMaxColumn = 5;
    [Tooltip("How many center columns must stay clear so the default falling lane is never blocked by support islands.")]
    [Min(0)]
    [SerializeField] private int staticSupportIslandCenterClearColumns = 5;
    [SerializeField] private StaticSupportIslandShapeConfig[] staticSupportIslandShapes =
    {
        new StaticSupportIslandShapeConfig("Single", 6, new[] { Vector2Int.zero }),
        new StaticSupportIslandShapeConfig("Two Wide", 3, new[] { Vector2Int.zero, Vector2Int.right }),
        new StaticSupportIslandShapeConfig("Two Tall", 2, new[] { Vector2Int.zero, Vector2Int.up }),
        new StaticSupportIslandShapeConfig("Corner", 1, new[] { Vector2Int.zero, Vector2Int.right, Vector2Int.up })
    };

    [Header("Play Area")]
    [SerializeField] private FloorSegmentConfig[] floorSegments =
    {
        new FloorSegmentConfig()
    };

    [Header("Camera")]
    [Tooltip("Camera Y will never go below this value.")]
    [SerializeField] private float minimumCameraY = 0f;
    [Tooltip("Where the tower peak should sit on screen after the camera catches up. 0 is bottom, 1 is top.")]
    [Range(0.55f, 0.9f)]
    [SerializeField] private float towerPeakScreenY = 0.64f;
    [Tooltip("Where newly spawned blocks should appear on screen. 0 is bottom, 1 is top.")]
    [Range(0.65f, 0.95f)]
    [SerializeField] private float spawnPointScreenY = 0.88f;
    [SerializeField] private float cameraSmoothTime = 0.28f;
    [SerializeField] private float minimumCameraSize = 15f;
    [SerializeField] private float maximumCameraSize = 24f;
    [SerializeField] private float horizontalCameraPadding = 1.5f;
    [Range(0.5f, 1f)]
    [SerializeField] private float horizontalCameraSafeArea = 0.78f;
    [SerializeField] private float cameraZoomSmoothTime = 0.35f;

    public int StartingLives => startingLives;
    public float InitialFallSpeed => initialFallSpeed;
    public DifficultyScalingMode DifficultyScalingMode => difficultyScalingMode;
    public DifficultyAdjustmentMode DifficultyAdjustmentMode => difficultyAdjustmentMode;
    public float SpeedIncreasePerBlock => speedIncreasePerBlock;
    public float InitialGravityScale => initialGravityScale;
    public float GravityIncreasePerBlock => gravityIncreasePerBlock;
    public float SpeedIncreaseIntervalSeconds => Mathf.Max(1f, speedIncreaseIntervalSeconds);
    public float SpeedIncreasePerInterval => speedIncreasePerInterval;
    public float GravityIncreasePerInterval => gravityIncreasePerInterval;
    public IReadOnlyList<BlockDefinition> BlockBag => blockBag;
    public IReadOnlyList<BlockData> FallbackBlockDataVariants => fallbackBlockDataVariants;
    public float SpawnDelay => spawnDelay;
    public float GridSpacing => gridSpacing;
    public float MinimumLandingNormalY => minimumLandingNormalY;
    public int HorizontalPlacementBufferColumns => Mathf.Max(0, horizontalPlacementBufferColumns);
    public float MaxColumnMoveSpeed => maxColumnMoveSpeed;
    public float ColumnApproachSpeed => columnApproachSpeed;
    public float HorizontalSteeringContactSkin => Mathf.Max(0f, horizontalSteeringContactSkin);
    public float RotationApproachSpeed => rotationApproachSpeed;
    public float MaxRotationSpeed => maxRotationSpeed;
    public float GroundedCheckDistance => groundedCheckDistance;
    public float LandingColumnToleranceFraction => Mathf.Clamp(landingColumnToleranceFraction, 0f, 0.5f);
    public float LandingCornerInsetFraction => Mathf.Clamp(landingCornerInsetFraction, 0f, 0.25f);
    public float CornerSlideSpeed => Mathf.Max(0f, cornerSlideSpeed);
    public float InvalidContactReleaseTime => Mathf.Max(0f, invalidContactReleaseTime);
    public float MaxLandingImpactSpeed => maxLandingImpactSpeed;
    public float SettleLinearThreshold => settleLinearThreshold;
    public float SettleAngularThreshold => settleAngularThreshold;
    public float SettleTime => settleTime;
    public bool SleepSettledBlocksOnLock => sleepSettledBlocksOnLock;
    public bool MicroAlignSettledBlocks => microAlignSettledBlocks;
    public float MicroAlignMaxColumnFraction => Mathf.Clamp(microAlignMaxColumnFraction, 0f, 0.25f);
    public float MicroAlignMaxRotationDegrees => Mathf.Clamp(microAlignMaxRotationDegrees, 0f, 15f);
    public float MaxControlTime => maxControlTime;
    public float PowerUpSpawnInterval => powerUpSpawnInterval;
    public float PowerUpSpawnXRange => powerUpSpawnXRange;
    public float SlowMotionDuration => slowMotionDuration;
    public float SlowMotionScale => slowMotionScale;
    public bool StaticSupportIslandsEnabled => staticSupportIslandsEnabled;
    public float StaticSupportIslandHeightInterval => Mathf.Max(0.1f, staticSupportIslandHeightInterval);
    public float StaticSupportIslandSpawnChance => Mathf.Clamp01(staticSupportIslandSpawnChance);
    public float StaticSupportIslandFirstHeight => Mathf.Max(0f, staticSupportIslandFirstHeight);
    public float StaticSupportIslandSpawnAheadHeight => Mathf.Max(0f, staticSupportIslandSpawnAheadHeight);
    public int StaticSupportIslandMinColumn => Mathf.Min(staticSupportIslandMinColumn, staticSupportIslandMaxColumn);
    public int StaticSupportIslandMaxColumn => Mathf.Max(staticSupportIslandMinColumn, staticSupportIslandMaxColumn);
    public int StaticSupportIslandCenterClearColumns => Mathf.Max(0, staticSupportIslandCenterClearColumns);
    public IReadOnlyList<StaticSupportIslandShapeConfig> StaticSupportIslandShapes => staticSupportIslandShapes;
    public IReadOnlyList<FloorSegmentConfig> FloorSegments => floorSegments;
    public float FloorWidth => floorSegments != null && floorSegments.Length > 0
        ? floorSegments[0].GetWidth(gridSpacing)
        : gridSpacing;
    public float MinimumCameraY => minimumCameraY;
    public float TowerPeakScreenY => towerPeakScreenY;
    public float SpawnPointScreenY => spawnPointScreenY;
    public float CameraSmoothTime => cameraSmoothTime;
    public float MinimumCameraSize => Mathf.Max(1f, minimumCameraSize);
    public float MaximumCameraSize => Mathf.Max(MinimumCameraSize, maximumCameraSize);
    public float HorizontalCameraPadding => Mathf.Max(0f, horizontalCameraPadding);
    public float HorizontalCameraSafeArea => Mathf.Clamp(horizontalCameraSafeArea, 0.5f, 1f);
    public float CameraZoomSmoothTime => Mathf.Max(0.01f, cameraZoomSmoothTime);
}

[System.Serializable]
public sealed class StaticSupportIslandShapeConfig
{
    [SerializeField] private string displayName = "Support Island";
    [Min(0)]
    [SerializeField] private int weight = 1;
    [SerializeField] private Vector2Int[] cellOffsets =
    {
        Vector2Int.zero
    };

    public StaticSupportIslandShapeConfig()
    {
    }

    public StaticSupportIslandShapeConfig(string displayName, int weight, Vector2Int[] cellOffsets)
    {
        this.displayName = displayName;
        this.weight = weight;
        this.cellOffsets = cellOffsets;
    }

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Support Island" : displayName;
    public int Weight => Mathf.Max(0, weight);
    public IReadOnlyList<Vector2Int> CellOffsets => cellOffsets;
    public bool HasCells => cellOffsets != null && cellOffsets.Length > 0;
}

[System.Serializable]
public sealed class FloorSegmentConfig
{
    [SerializeField] private int centerColumn = 0;
    [Min(1)]
    [SerializeField] private int columnCount = 9;

    public int CenterColumn => centerColumn;
    public int ColumnCount => Mathf.Max(1, columnCount);
    public int LeftColumn => centerColumn - ColumnCount / 2;
    public int RightColumn => LeftColumn + ColumnCount - 1;

    public float GetCenterX(float gridSpacing)
    {
        return (LeftColumn + RightColumn) * 0.5f * gridSpacing;
    }

    public float GetWidth(float gridSpacing)
    {
        return ColumnCount * gridSpacing;
    }
}
