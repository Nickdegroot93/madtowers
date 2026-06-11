using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "GameModeConfig", menuName = "Stacking/Game Mode Config")]
public class GameModeConfig : ScriptableObject
{
    [Header("Round")]
    [Min(0)]
    [SerializeField] private int startingLives = 0;
    [SerializeField] private float initialFallSpeed = 2f;
    [Tooltip("Hard ceiling for the controlled fall speed no matter how long the round runs. Keeps long games playable; raise per level for harder modes.")]
    [SerializeField] private float maxFallSpeed = 5f;
    [SerializeField] private DifficultyScalingMode difficultyScalingMode = DifficultyScalingMode.PerBlock;
    [SerializeField] private DifficultyAdjustmentMode difficultyAdjustmentMode = DifficultyAdjustmentMode.Additive;
    [SerializeField] private float speedIncreasePerBlock = 0.1f;
    [SerializeField] private float speedIncreaseIntervalSeconds = 60f;
    [SerializeField] private float speedIncreasePerInterval = 0.1f;

    [Header("Spawning")]
    [SerializeField] private BlockDefinition[] blockBag;
    [SerializeField] private BlockData[] fallbackBlockDataVariants;
    [Tooltip("Level-flavour variant rolls: each spawn has these chances to be replaced by the given variant (e.g. 3% giant bricks on a hard level). Power-ups can stack more chances on top at runtime.")]
    [SerializeField] private AmbientBlockVariantChance[] ambientBlockVariantChances;
    [SerializeField] private float spawnDelay = 0f;

    [Header("Placement")]
    [SerializeField] private float gridSpacing = 1f;
    [Tooltip("Extra columns beyond the current floor/tower edge where the active block may still be placed.")]
    [Min(0)]
    [SerializeField] private int horizontalPlacementBufferColumns = 3;

    [Header("Active Piece Control")]
    [Tooltip("How close (world units) support must be below a piece before control is handed to physics. Keep small so players can make last-second tuck moves.")]
    [SerializeField] private float groundedCheckDistance = 0.03f;
    [Tooltip("Maximum downward velocity kept when control hands off to physics. 0 means use the current controlled fall speed.")]
    [SerializeField] private float maxLandingImpactSpeed = 2f;
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

    [Header("Power Up Choices")]
    [Tooltip("Every this many placed blocks the game pauses and offers a pick of power-ups. 0 disables choices for this mode.")]
    [Min(0)]
    [SerializeField] private int powerUpChoiceEveryBlocks = 10;
    [Tooltip("Power-ups that can appear in choice offers. Rarity weighting comes from each definition.")]
    [SerializeField] private PowerUpDefinition[] powerUpChoicePool;
    [SerializeField] private float slowMotionScale = 0.5f;

    [Header("Static Support Islands")]
    [SerializeField] private bool staticSupportIslandsEnabled = true;
    [Tooltip("Vertical spacing in meters between island spawn rows (snapped to the grid; one roll per row per side band).")]
    [Min(0.1f)]
    [SerializeField] private float staticSupportIslandHeightInterval = 1f;
    [Tooltip("Chance that a cluster spawns on a given row, rolled independently PER SIDE band (then weighted by floor distance). Canonical 0.25 ≈ a few stones per screen, almost all on the flanks - playtested between 0.05 (felt empty) and 0.4 (cluttered the phone screen).")]
    [Range(0f, 1f)]
    [SerializeField] private float staticSupportIslandSpawnChance = 0.25f;
    [Tooltip("Meters above the floor where island generation starts. Canonical 9: the first screens of building stay completely clean.")]
    [Min(0f)]
    [SerializeField] private float staticSupportIslandFirstHeight = 9f;
    [Tooltip("How far above the camera's top edge generation stays ahead, so islands always exist before they scroll into view.")]
    [Min(0f)]
    [SerializeField] private float staticSupportIslandSpawnAheadHeight = 2f;
    [SerializeField] private int staticSupportIslandMinColumn = -6;
    [SerializeField] private int staticSupportIslandMaxColumn = 6;
    [Tooltip("How many center columns must stay clear so the default falling lane is never blocked by support islands. The columns between this lane and min/max column form the two side bands.")]
    [Min(0)]
    [SerializeField] private int staticSupportIslandCenterClearColumns = 3;
    // Tricky-Towers distribution: overwhelmingly singles, occasional pairs, rare corner.
    [SerializeField] private StaticSupportIslandShapeConfig[] staticSupportIslandShapes =
    {
        new StaticSupportIslandShapeConfig("Single", 12, new[] { Vector2Int.zero }),
        new StaticSupportIslandShapeConfig("Two Wide", 2, new[] { Vector2Int.zero, Vector2Int.right }),
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
    [Tooltip("Where the tower peak should sit on screen after the camera catches up. 0 is bottom, 1 is top. Lower = more room (and reaction time) between the tower and the spawn point; raise for harder levels.")]
    [Range(0.35f, 0.9f)]
    [SerializeField] private float towerPeakScreenY = 0.5f;
    [Tooltip("Where newly spawned blocks should appear on screen. 0 is bottom, 1 is top.")]
    [Range(0.5f, 0.98f)]
    [SerializeField] private float spawnPointScreenY = 0.9f;
    [SerializeField] private float cameraSmoothTime = 0.28f;
    [SerializeField] private float minimumCameraSize = 15f;
    [SerializeField] private float maximumCameraSize = 24f;
    [SerializeField] private float horizontalCameraPadding = 1.5f;
    [Range(0.5f, 1f)]
    [SerializeField] private float horizontalCameraSafeArea = 0.78f;
    [SerializeField] private float cameraZoomSmoothTime = 0.35f;

    public int StartingLives => startingLives;
    public float InitialFallSpeed => initialFallSpeed;
    public float MaxFallSpeed => Mathf.Max(0.1f, maxFallSpeed);
    public DifficultyScalingMode DifficultyScalingMode => difficultyScalingMode;
    public DifficultyAdjustmentMode DifficultyAdjustmentMode => difficultyAdjustmentMode;
    public float SpeedIncreasePerBlock => speedIncreasePerBlock;
    public float SpeedIncreaseIntervalSeconds => Mathf.Max(1f, speedIncreaseIntervalSeconds);
    public float SpeedIncreasePerInterval => speedIncreasePerInterval;
    public IReadOnlyList<BlockDefinition> BlockBag => blockBag;
    public IReadOnlyList<BlockData> FallbackBlockDataVariants => fallbackBlockDataVariants;
    public IReadOnlyList<AmbientBlockVariantChance> AmbientBlockVariantChances => ambientBlockVariantChances;
    public float SpawnDelay => spawnDelay;
    public float GridSpacing => gridSpacing;
    public int HorizontalPlacementBufferColumns => Mathf.Max(0, horizontalPlacementBufferColumns);
    public float GroundedCheckDistance => groundedCheckDistance;
    public float MaxLandingImpactSpeed => maxLandingImpactSpeed;
    public float SettleLinearThreshold => settleLinearThreshold;
    public float SettleAngularThreshold => settleAngularThreshold;
    public float SettleTime => settleTime;
    public bool SleepSettledBlocksOnLock => sleepSettledBlocksOnLock;
    public bool MicroAlignSettledBlocks => microAlignSettledBlocks;
    public float MicroAlignMaxColumnFraction => Mathf.Clamp(microAlignMaxColumnFraction, 0f, 0.25f);
    public float MicroAlignMaxRotationDegrees => Mathf.Clamp(microAlignMaxRotationDegrees, 0f, 15f);
    public float MaxControlTime => maxControlTime;
    public int PowerUpChoiceEveryBlocks => Mathf.Max(0, powerUpChoiceEveryBlocks);
    public IReadOnlyList<PowerUpDefinition> PowerUpChoicePool => powerUpChoicePool;
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
public sealed class AmbientBlockVariantChance
{
    [SerializeField] private BlockData variant;
    [Range(0f, 1f)]
    [SerializeField] private float chancePerBlock = 0.03f;

    public BlockData Variant => variant;
    public float ChancePerBlock => Mathf.Clamp01(chancePerBlock);
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
