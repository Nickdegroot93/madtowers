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
    [SerializeField] private float spawnDelay = 0.5f;

    [Header("Placement")]
    [SerializeField] private float gridSpacing = 1f;
    [SerializeField] private BlockLandingMode landingMode = BlockLandingMode.StrictGrid;
    [SerializeField] private float minimumLandingNormalY = 0.45f;
    [Tooltip("Required inset from the support edge before a grid-locked block or tower component counts as stable.")]
    [SerializeField] private float stabilityMargin = 0.08f;
    [Tooltip("Allows side contact with existing tower/support cells to stabilize hooked or cornered placements.")]
    [SerializeField] private bool lateralBraceStabilityEnabled = true;
    [Tooltip("How many side contacts are needed before a bottom-supported overhang counts as braced.")]
    [Min(1)]
    [SerializeField] private int lateralBraceMinimumContacts = 1;
    [Tooltip("If enabled, small connected chunks can use lateral brace forgiveness.")]
    [SerializeField] private bool connectedComponentLateralBraceEnabled = true;
    [Tooltip("How many side contacts are needed before a connected tower chunk counts as braced.")]
    [Min(1)]
    [SerializeField] private int connectedComponentLateralBraceMinimumContacts = 1;
    [Tooltip("Maximum connected chunk size that can use lateral brace forgiveness. Larger chunks must satisfy center-of-mass support.")]
    [Min(1)]
    [SerializeField] private int connectedComponentLateralBraceMaxCells = 4;
    [Tooltip("Extra columns beyond the current floor/tower edge where the active block may still be placed.")]
    [Min(0)]
    [SerializeField] private int horizontalPlacementBufferColumns = 3;

    [Header("Tricky Towers Control (per level)")]
    [Tooltip("How fast a piece slides between columns (units/sec).")]
    [SerializeField] private float maxColumnMoveSpeed = 14f;
    [Tooltip("How hard the piece is driven toward its target column (eases in, never overshoots).")]
    [SerializeField] private float columnApproachSpeed = 25f;
    [Tooltip("How hard the piece rotates toward the requested angle.")]
    [SerializeField] private float rotationApproachSpeed = 20f;
    [Tooltip("Maximum spin speed while rotating (degrees/sec).")]
    [SerializeField] private float maxRotationSpeed = 720f;
    [Tooltip("How close (world units) support must be below a piece before control is handed to physics.")]
    [SerializeField] private float groundedCheckDistance = 0.12f;
    [Tooltip("Caps downward speed at the moment of landing (units/sec) so a fast drop lands as softly as a slow one.")]
    [SerializeField] private float maxLandingImpactSpeed = 1.5f;
    [Tooltip("A landed piece is 'settling' once its linear speed (units/sec) drops below this.")]
    [SerializeField] private float settleLinearThreshold = 0.3f;
    [Tooltip("...and its spin (degrees/sec) drops below this.")]
    [SerializeField] private float settleAngularThreshold = 25f;
    [Tooltip("How long a piece must stay settled after landing before the next piece spawns.")]
    [SerializeField] private float settleTime = 0.2f;
    [Tooltip("Safety cap: lock a piece after this many seconds even if it never fully settles.")]
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
    public BlockLandingMode LandingMode => landingMode;
    public float MinimumLandingNormalY => minimumLandingNormalY;
    public float StabilityMargin => stabilityMargin;
    public bool LateralBraceStabilityEnabled => lateralBraceStabilityEnabled;
    public int LateralBraceMinimumContacts => Mathf.Max(1, lateralBraceMinimumContacts);
    public bool ConnectedComponentLateralBraceEnabled => connectedComponentLateralBraceEnabled;
    public int ConnectedComponentLateralBraceMinimumContacts => Mathf.Max(1, connectedComponentLateralBraceMinimumContacts);
    public int ConnectedComponentLateralBraceMaxCells => Mathf.Max(1, connectedComponentLateralBraceMaxCells);
    public int HorizontalPlacementBufferColumns => Mathf.Max(0, horizontalPlacementBufferColumns);
    public float MaxColumnMoveSpeed => maxColumnMoveSpeed;
    public float ColumnApproachSpeed => columnApproachSpeed;
    public float RotationApproachSpeed => rotationApproachSpeed;
    public float MaxRotationSpeed => maxRotationSpeed;
    public float GroundedCheckDistance => groundedCheckDistance;
    public float MaxLandingImpactSpeed => maxLandingImpactSpeed;
    public float SettleLinearThreshold => settleLinearThreshold;
    public float SettleAngularThreshold => settleAngularThreshold;
    public float SettleTime => settleTime;
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
