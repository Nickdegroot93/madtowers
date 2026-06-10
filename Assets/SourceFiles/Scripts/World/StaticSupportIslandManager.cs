using UnityEngine;
using System.Collections.Generic;

public class StaticSupportIslandManager : MonoBehaviour
{
    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private GameObject _staticBlockPrefab;
    [SerializeField] private float _spawnInterval = 5f;
    [SerializeField] private float _spawnOffset = 5f;
    [SerializeField] private float _xRange = 4f;
    [Tooltip("Surface friction of support islands. Matches the block friction so pieces grip the island instead of shearing off it.")]
    [Range(0f, 1f)]
    [SerializeField] private float _islandFriction = 0.95f;
    [Tooltip("Rounds island collider corners as a fraction of one cell, matching the falling blocks.")]
    [Range(0f, 0.12f)]
    [SerializeField] private float _islandCornerRadiusFraction = 0.06f;
    [Tooltip("Effective physics footprint of an island cell as a fraction of the visual cell. Must match the blocks' footprint scale so pieces fit cleanly beside and between islands.")]
    [Range(0.85f, 1f)]
    [SerializeField] private float _islandFootprintScale = 0.94f;

    private readonly RuntimeObjectPool _pool = new RuntimeObjectPool();
    private readonly Collider2D[] _overlapResults = new Collider2D[8];
    private PhysicsMaterial2D _islandMaterial;
    private ContactFilter2D _solidFilter;
    private readonly List<int> _validBaseColumns = new List<int>(16);
    private float _nextSpawnRollHeight;
    private GameModeConfig ActiveGameModeConfig => LevelSelectionState.ResolveGameMode(gameModeConfig);

    private void Start()
    {
        _solidFilter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = false
        };

        GameModeConfig activeConfig = ActiveGameModeConfig;
        _nextSpawnRollHeight = activeConfig != null
            ? activeConfig.StaticSupportIslandFirstHeight
            : _spawnInterval;
    }

    private void Update()
    {
        if (GameManager.Instance == null) return;
        GameModeConfig activeConfig = ActiveGameModeConfig;
        if (activeConfig != null && !activeConfig.StaticSupportIslandsEnabled) return;

        float currentMaxHeight = GameManager.Instance.maxHeight;
        float interval = activeConfig != null
            ? activeConfig.StaticSupportIslandHeightInterval
            : _spawnInterval;

        while (currentMaxHeight >= _nextSpawnRollHeight)
        {
            RollSupportIsland(currentMaxHeight);
            _nextSpawnRollHeight += interval;
        }
    }

    private void RollSupportIsland(float currentMaxHeight)
    {
        if (_staticBlockPrefab == null) return;

        GameModeConfig activeConfig = ActiveGameModeConfig;
        float spawnChance = activeConfig != null
            ? activeConfig.StaticSupportIslandSpawnChance
            : 1f;

        if (Random.value > spawnChance) return;
        if (!TryChooseShape(out StaticSupportIslandShapeConfig shape)) return;

        SpawnSupportIsland(currentMaxHeight, shape);
    }

    private bool TryChooseShape(out StaticSupportIslandShapeConfig shape)
    {
        shape = null;

        GameModeConfig activeConfig = ActiveGameModeConfig;
        IReadOnlyList<StaticSupportIslandShapeConfig> shapes = activeConfig != null
            ? activeConfig.StaticSupportIslandShapes
            : null;

        if (shapes == null || shapes.Count == 0) return false;

        int totalWeight = 0;
        for (int i = 0; i < shapes.Count; i++)
        {
            StaticSupportIslandShapeConfig candidate = shapes[i];
            if (candidate == null || !candidate.HasCells) continue;
            totalWeight += candidate.Weight;
        }

        if (totalWeight <= 0) return false;

        int roll = Random.Range(0, totalWeight);
        for (int i = 0; i < shapes.Count; i++)
        {
            StaticSupportIslandShapeConfig candidate = shapes[i];
            if (candidate == null || !candidate.HasCells || candidate.Weight <= 0) continue;

            if (roll < candidate.Weight)
            {
                shape = candidate;
                return true;
            }

            roll -= candidate.Weight;
        }

        return false;
    }

    private void SpawnSupportIsland(float currentMaxHeight, StaticSupportIslandShapeConfig shape)
    {
        GameModeConfig activeConfig = ActiveGameModeConfig;
        float gridSpacing = activeConfig != null ? activeConfig.GridSpacing : 1f;
        int minColumn = activeConfig != null
            ? activeConfig.StaticSupportIslandMinColumn
            : Mathf.RoundToInt(-_xRange);
        int maxColumn = activeConfig != null
            ? activeConfig.StaticSupportIslandMaxColumn
            : Mathf.RoundToInt(_xRange);

        if (!TryBuildValidBaseColumns(shape, minColumn, maxColumn)) return;

        float spawnAhead = activeConfig != null
            ? activeConfig.StaticSupportIslandSpawnAheadHeight
            : _spawnOffset;
        float baseY = Mathf.Round((currentMaxHeight + spawnAhead) / gridSpacing) * gridSpacing;

        // Never materialize a platform inside the falling piece, the tower, or another island —
        // a piece overlapping a platform can't land on it and ghosts straight through.
        int baseColumn = 0;
        bool foundClearColumn = false;
        while (_validBaseColumns.Count > 0)
        {
            int candidateIndex = Random.Range(0, _validBaseColumns.Count);
            int candidate = _validBaseColumns[candidateIndex];
            if (IsIslandAreaClear(shape, candidate, baseY, gridSpacing))
            {
                baseColumn = candidate;
                foundClearColumn = true;
                break;
            }

            _validBaseColumns.RemoveAt(candidateIndex);
        }

        if (!foundClearColumn) return;

        GameObject islandRoot = new GameObject($"Static Support Island - {shape.DisplayName}");
        islandRoot.transform.SetParent(transform);
        islandRoot.transform.position = Vector3.zero;

        IReadOnlyList<Vector2Int> offsets = shape.CellOffsets;
        for (int i = 0; i < offsets.Count; i++)
        {
            Vector2Int offset = offsets[i];
            Vector3 cellPosition = new Vector3(
                (baseColumn + offset.x) * gridSpacing,
                baseY + offset.y * gridSpacing,
                0f);

            GameObject cell = _pool.Get(_staticBlockPrefab, cellPosition, Quaternion.identity, islandRoot.transform);
            ConfigureIslandCellPhysics(cell, gridSpacing);
        }
    }

    private bool IsIslandAreaClear(StaticSupportIslandShapeConfig shape, int baseColumn, float baseY, float gridSpacing)
    {
        IReadOnlyList<Vector2Int> offsets = shape.CellOffsets;
        Vector2 probeSize = Vector2.one * (gridSpacing * 0.95f);

        for (int i = 0; i < offsets.Count; i++)
        {
            Vector2 cellCenter = new Vector2(
                (baseColumn + offsets[i].x) * gridSpacing,
                baseY + offsets[i].y * gridSpacing);

            if (Physics2D.OverlapBox(cellCenter, probeSize, 0f, _solidFilter, _overlapResults) > 0)
            {
                return false;
            }
        }

        return true;
    }

    // Islands are the tower's sturdy anchors, so their surfaces must behave like block surfaces:
    // real friction (the prefab has none, which leaves engine-default 0.4 and lets pieces shear
    // off) and rounded corners with an exact cell-sized footprint, matching the falling blocks.
    private void ConfigureIslandCellPhysics(GameObject cell, float gridSpacing)
    {
        if (cell == null) return;

        if (_islandMaterial == null)
        {
            _islandMaterial = new PhysicsMaterial2D("SupportIslandMaterial")
            {
                friction = _islandFriction,
                bounciness = 0f
            };
        }

        BoxCollider2D[] boxes = cell.GetComponentsInChildren<BoxCollider2D>();
        for (int i = 0; i < boxes.Length; i++)
        {
            BoxCollider2D box = boxes[i];
            box.sharedMaterial = _islandMaterial;

            // Pooled cells come back already shrunk; only resize once.
            if (box.edgeRadius > 0f) continue;

            Vector2 targetSize = box.size * Mathf.Clamp(_islandFootprintScale, 0.85f, 1f);
            float requestedRadius = Mathf.Max(0f, _islandCornerRadiusFraction) * gridSpacing;
            float radius = Mathf.Min(requestedRadius, Mathf.Min(targetSize.x, targetSize.y) * 0.45f);
            if (radius <= 0f) continue;

            box.size = new Vector2(
                Mathf.Max(0.05f, targetSize.x - 2f * radius),
                Mathf.Max(0.05f, targetSize.y - 2f * radius));
            box.edgeRadius = radius;
        }
    }

    private bool TryBuildValidBaseColumns(
        StaticSupportIslandShapeConfig shape,
        int allowedMinColumn,
        int allowedMaxColumn)
    {
        _validBaseColumns.Clear();

        IReadOnlyList<Vector2Int> offsets = shape.CellOffsets;
        if (offsets == null || offsets.Count == 0) return false;

        int minOffsetX = int.MaxValue;
        int maxOffsetX = int.MinValue;

        for (int i = 0; i < offsets.Count; i++)
        {
            minOffsetX = Mathf.Min(minOffsetX, offsets[i].x);
            maxOffsetX = Mathf.Max(maxOffsetX, offsets[i].x);
        }

        int minBaseColumn = allowedMinColumn - minOffsetX;
        int maxBaseColumn = allowedMaxColumn - maxOffsetX;
        if (minBaseColumn > maxBaseColumn) return false;

        for (int baseColumn = minBaseColumn; baseColumn <= maxBaseColumn; baseColumn++)
        {
            if (IsShapeInsideCenterClearLane(shape, baseColumn)) continue;
            _validBaseColumns.Add(baseColumn);
        }

        return _validBaseColumns.Count > 0;
    }

    private bool IsShapeInsideCenterClearLane(StaticSupportIslandShapeConfig shape, int baseColumn)
    {
        GameModeConfig activeConfig = ActiveGameModeConfig;
        int clearColumns = activeConfig != null
            ? activeConfig.StaticSupportIslandCenterClearColumns
            : 0;

        if (clearColumns <= 0) return false;

        int clearMinColumn = -(clearColumns / 2);
        int clearMaxColumn = clearMinColumn + clearColumns - 1;
        IReadOnlyList<Vector2Int> offsets = shape.CellOffsets;

        for (int i = 0; i < offsets.Count; i++)
        {
            int cellColumn = baseColumn + offsets[i].x;
            if (cellColumn >= clearMinColumn && cellColumn <= clearMaxColumn)
            {
                return true;
            }
        }

        return false;
    }
}
