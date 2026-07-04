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

    // Active-piece control / settle thresholds are a PHYSICS.md CONTRACT: identical in every mode.
    // They live in code (see PhysicsProfile) so they can't drift per-asset; the getters below forward
    // to it. (Previously per-mode [SerializeField]s - every shipped mode asset held these same values.)

    [Header("Power Up Choices")]
    [Tooltip("Every this many placed blocks the game pauses and offers a pick of power-ups. 0 disables choices for this mode.")]
    [Min(0)]
    [SerializeField] private int powerUpChoiceEveryBlocks = 10;
    [Tooltip("Abilities that can appear in choice offers. Rarity weighting comes from each definition; availability conditions filter per level/run.")]
    [SerializeField] private AbilityDefinition[] powerUpChoicePool;
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
    [Tooltip("How far above the tower's peak islands materialize (with the pop reveal). The sky stays clean until the build gets near; keep below the spawn-line offset (~12 above the peak) so revealed islands are immediately landable.")]
    [Min(0f)]
    [SerializeField] private float staticSupportIslandSpawnAheadHeight = 6f;
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
    // Forward to the code-owned physics contract (PhysicsProfile) - identical across every mode.
    public float GroundedCheckDistance => PhysicsProfile.GroundedCheckDistance;
    public float MaxLandingImpactSpeed => PhysicsProfile.MaxLandingImpactSpeed;
    public float SettleLinearThreshold => PhysicsProfile.SettleLinearThreshold;
    public float SettleAngularThreshold => PhysicsProfile.SettleAngularThreshold;
    public float SettleTime => PhysicsProfile.SettleTime;
    public bool SleepSettledBlocksOnLock => PhysicsProfile.SleepSettledBlocksOnLock;
    public bool MicroAlignSettledBlocks => PhysicsProfile.MicroAlignSettledBlocks;
    public float MicroAlignMaxColumnFraction => PhysicsProfile.MicroAlignMaxColumnFraction;
    public float MicroAlignMaxRotationDegrees => PhysicsProfile.MicroAlignMaxRotationDegrees;
    public float MaxControlTime => PhysicsProfile.MaxControlTime;
    public int PowerUpChoiceEveryBlocks => Mathf.Max(0, powerUpChoiceEveryBlocks);
    public IReadOnlyList<AbilityDefinition> PowerUpChoicePool => powerUpChoicePool;
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
    // Runtime-generated floor layout (ProceduralFloorModifier). NonSerialized: it can never dirty
    // the asset. EVERY floor consumer (terrain build, camera framing, reach bounds, island
    // weighting, props) reads FloorSegments, so overriding here keeps them all consistent.
    // The modifier that sets it MUST clear it in OnLevelEnd - the asset instance outlives the
    // scene in the editor, and a stale override would leak into other levels sharing this config.
    [System.NonSerialized] private FloorSegmentConfig[] _runtimeFloorOverride;

    /// <summary>Replace (or clear, with null) the floor layout for this run only. See
    /// FLOORS.md - call PlayAreaController.ApplyConfig() afterwards to rebuild the terrain.</summary>
    public void SetRuntimeFloorOverride(FloorSegmentConfig[] segments) => _runtimeFloorOverride = segments;

    public IReadOnlyList<FloorSegmentConfig> FloorSegments =>
        _runtimeFloorOverride != null && _runtimeFloorOverride.Length > 0 ? _runtimeFloorOverride : floorSegments;
    public float FloorWidth => floorSegments != null && floorSegments.Length > 0
        ? floorSegments[0].GetWidth(gridSpacing)
        : gridSpacing;
    public int FloorColumnCount => floorSegments != null && floorSegments.Length > 0
        ? floorSegments[0].ColumnCount
        : 9;
    public float MinimumCameraY => minimumCameraY;
    public float TowerPeakScreenY => towerPeakScreenY;
    public float SpawnPointScreenY => spawnPointScreenY;
    public float CameraSmoothTime => cameraSmoothTime;
    public float MinimumCameraSize => Mathf.Max(1f, minimumCameraSize);
    public float MaximumCameraSize => Mathf.Max(MinimumCameraSize, maximumCameraSize);
    public float HorizontalCameraPadding => Mathf.Max(0f, horizontalCameraPadding);
    public float HorizontalCameraSafeArea => Mathf.Clamp(horizontalCameraSafeArea, 0.5f, 1f);
    public float CameraZoomSmoothTime => Mathf.Max(0.01f, cameraZoomSmoothTime);

    /// <summary>Runtime only (Custom Game screen): overwrite the curated gameplay knobs on a
    /// CLONED config from the setup screen's settings. Everything not listed here keeps the
    /// preset's value (physics tuning, camera smoothing, settle thresholds, ...). Call on an
    /// Instantiate() copy, never on a project asset.</summary>
    public void ApplyCustomGameOverrides(CustomGameSettings s)
    {
        if (s == null) return;

        startingLives = Mathf.Max(0, s.StartingLives);
        initialFallSpeed = s.InitialFallSpeed;
        maxFallSpeed = s.MaxFallSpeed;
        difficultyScalingMode = s.DifficultyScalingMode;
        difficultyAdjustmentMode = s.DifficultyAdjustmentMode;
        speedIncreasePerBlock = s.SpeedIncreasePerBlock;
        speedIncreaseIntervalSeconds = s.SpeedIncreaseIntervalSeconds;
        speedIncreasePerInterval = s.SpeedIncreasePerInterval;

        floorSegments = BuildCustomFloorSegments(s.FloorShape, Mathf.Max(1, s.FloorColumns));

        spawnDelay = Mathf.Max(0f, s.SpawnDelay);
        powerUpChoiceEveryBlocks = Mathf.Max(0, s.PowerUpChoiceEveryBlocks);
        staticSupportIslandsEnabled = s.StaticIslandsEnabled;
        staticSupportIslandSpawnChance = Mathf.Clamp01(s.StaticIslandSpawnChance);

        blockBag = new List<BlockDefinition>(s.EnabledBlocks).ToArray();
        powerUpChoicePool = new List<AbilityDefinition>(s.EnabledAbilities).ToArray();

        // Variant spawn chances (dev/testing): rebuild the ambient table from the screen's per-variant
        // sliders, keeping only the ones actually dialed up.
        var ambient = new List<AmbientBlockVariantChance>();
        foreach (var kv in s.VariantChances)
            if (kv.Key != null && kv.Value > 0f)
                ambient.Add(new AmbientBlockVariantChance(kv.Key, kv.Value));
        ambientBlockVariantChances = ambient.ToArray();
    }

    /// <summary>Custom Game floor shapes: deterministic terrain presets built from a shape index +
    /// total width, so the whole FloorSegmentConfig terrain space is testable without authoring
    /// assets. Mirrors CustomGameSettings.FloorShapeNames - keep the two lists in step.</summary>
    private static FloorSegmentConfig[] BuildCustomFloorSegments(int shape, int columns)
    {
        switch (shape)
        {
            case 1: // Steps - a staircase rising left to right
            {
                int[] steps = new int[columns];
                for (int i = 0; i < columns; i++) steps[i] = (i * 3) / Mathf.Max(1, columns - 1);
                return new[] { new FloorSegmentConfig(0, columns, 0, steps) };
            }
            case 2: // Valley - raised shoulders, sunken middle to nudge into
            {
                int[] steps = new int[columns];
                for (int i = 0; i < columns; i++)
                {
                    int fromEdge = Mathf.Min(i, columns - 1 - i);
                    steps[i] = fromEdge == 0 ? 3 : (fromEdge == 1 ? 1 : 0);
                }
                return new[] { new FloorSegmentConfig(0, columns, 0, steps) };
            }
            case 3: // Twin pillars - two towers with a void between them
            {
                int pillarWidth = Mathf.Clamp(columns / 3, 2, 5);
                int offset = Mathf.Max(pillarWidth + 1, columns / 3 + 1);
                return new[]
                {
                    new FloorSegmentConfig(-offset, pillarWidth, 4),
                    new FloorSegmentConfig(offset, pillarWidth, 6)
                };
            }
            case 4: // Three pillars - Tricky-Towers-style trio at different heights
            {
                int pillarWidth = Mathf.Clamp(columns / 4, 2, 4);
                int offset = Mathf.Max(pillarWidth + 2, columns / 3 + 2);
                return new[]
                {
                    new FloorSegmentConfig(-offset, pillarWidth, 5),
                    new FloorSegmentConfig(0, pillarWidth, 2),
                    new FloorSegmentConfig(offset, pillarWidth, 7)
                };
            }
            default: // Flat - the classic single strip
                return new[] { new FloorSegmentConfig(0, columns) };
        }
    }
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

    public AmbientBlockVariantChance() { }

    public AmbientBlockVariantChance(BlockData variant, float chancePerBlock)
    {
        this.variant = variant;
        this.chancePerBlock = Mathf.Clamp01(chancePerBlock);
    }
}

[System.Serializable]
public sealed class FloorSegmentConfig
{
    [SerializeField] private int centerColumn = 0;
    [Min(1)]
    [SerializeField] private int columnCount = 9;
    [Tooltip("Raises this whole segment's top surface this many cells above the floor datum (0 = classic flat floor). Pillars = several raised segments with gaps between their column spans.")]
    [Min(0)]
    [SerializeField] private int baseHeightCells = 0;
    [Tooltip("Optional per-column EXTRA cells on top of Base Height, left to right; missing/short entries mean 0. Steps, ridges and valleys: e.g. [3,0,0,0,3] digs a valley between two shoulders. Heights are always ABOVE the datum - the datum stays the lowest landable surface.")]
    [SerializeField] private int[] columnHeightSteps = null;
    [Tooltip("Carve 1x1 nudge-in POCKETS into the ground body: each entry names a column (0-based from this segment's left edge) and a depth in cells below that column's top surface (1 = directly under the top). Put them on columns whose side face is exposed (outer edges, or beside a height step) so a brick can be nudged in sideways and stick out of the wall.")]
    [SerializeField] private FloorPocketConfig[] pockets = null;

    public FloorSegmentConfig()
    {
    }

    public FloorSegmentConfig(int centerColumn, int columnCount)
    {
        this.centerColumn = centerColumn;
        this.columnCount = Mathf.Max(1, columnCount);
    }

    public FloorSegmentConfig(int centerColumn, int columnCount, int baseHeightCells, int[] columnHeightSteps = null,
        FloorPocketConfig[] pockets = null)
        : this(centerColumn, columnCount)
    {
        this.baseHeightCells = Mathf.Max(0, baseHeightCells);
        this.columnHeightSteps = columnHeightSteps;
        this.pockets = pockets;
    }

    public IReadOnlyList<FloorPocketConfig> Pockets => pockets;

    public int CenterColumn => centerColumn;
    public int ColumnCount => Mathf.Max(1, columnCount);
    public int LeftColumn => centerColumn - ColumnCount / 2;
    public int RightColumn => LeftColumn + ColumnCount - 1;
    public int BaseHeightCells => Mathf.Max(0, baseHeightCells);

    /// <summary>Top height of the i-th column (0 = LeftColumn) in cells above the floor datum.</summary>
    public int GetColumnHeightCells(int columnIndex)
    {
        int extra = columnHeightSteps != null && columnIndex >= 0 && columnIndex < columnHeightSteps.Length
            ? Mathf.Max(0, columnHeightSteps[columnIndex])
            : 0;
        return BaseHeightCells + extra;
    }

    public int MaxColumnHeightCells
    {
        get
        {
            int max = BaseHeightCells;
            if (columnHeightSteps != null)
                for (int i = 0; i < Mathf.Min(columnHeightSteps.Length, ColumnCount); i++)
                    max = Mathf.Max(max, BaseHeightCells + Mathf.Max(0, columnHeightSteps[i]));
            return max;
        }
    }

    public float GetCenterX(float gridSpacing)
    {
        return (LeftColumn + RightColumn) * 0.5f * gridSpacing;
    }

    public float GetWidth(float gridSpacing)
    {
        return ColumnCount * gridSpacing;
    }
}

/// <summary>A 1x1-cell niche carved into a floor segment's ground body (FloorTerrain splits the
/// column's collider around it and draws a dark socket). Column is 0-based from the segment's
/// LEFT edge; depth 1 is the cell directly below that column's top surface. Keep depths <= 3 -
/// the active-piece bailout force-locks a kinematic piece ~3 units below the datum.</summary>
[System.Serializable]
public sealed class FloorPocketConfig
{
    [Min(0)]
    [SerializeField] private int column = 0;
    [Min(1)]
    [SerializeField] private int depthCells = 1;

    public FloorPocketConfig()
    {
    }

    public FloorPocketConfig(int column, int depthCells)
    {
        this.column = Mathf.Max(0, column);
        this.depthCells = Mathf.Max(1, depthCells);
    }

    public int Column => Mathf.Max(0, column);
    public int DepthCells => Mathf.Max(1, depthCells);
}
