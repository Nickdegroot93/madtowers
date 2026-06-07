using UnityEngine;
using System.Collections.Generic;

public class StaticSupportIslandManager : MonoBehaviour
{
    [SerializeField] private GameModeConfig gameModeConfig;
    [SerializeField] private GameObject _staticBlockPrefab;
    [SerializeField] private float _spawnInterval = 5f;
    [SerializeField] private float _spawnOffset = 5f;
    [SerializeField] private float _xRange = 4f;

    private readonly RuntimeObjectPool _pool = new RuntimeObjectPool();
    private readonly List<Vector2> _islandCellCenters = new List<Vector2>(4);
    private readonly List<int> _validBaseColumns = new List<int>(16);
    private float _nextSpawnRollHeight;

    private void Start()
    {
        _nextSpawnRollHeight = gameModeConfig != null
            ? gameModeConfig.StaticSupportIslandFirstHeight
            : _spawnInterval;
    }

    private void Update()
    {
        if (GameManager.Instance == null) return;
        if (gameModeConfig != null && !gameModeConfig.StaticSupportIslandsEnabled) return;

        float currentMaxHeight = GameManager.Instance.maxHeight;
        float interval = gameModeConfig != null
            ? gameModeConfig.StaticSupportIslandHeightInterval
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

        float spawnChance = gameModeConfig != null
            ? gameModeConfig.StaticSupportIslandSpawnChance
            : 1f;

        if (Random.value > spawnChance) return;
        if (!TryChooseShape(out StaticSupportIslandShapeConfig shape)) return;

        SpawnSupportIsland(currentMaxHeight, shape);
    }

    private bool TryChooseShape(out StaticSupportIslandShapeConfig shape)
    {
        shape = null;

        IReadOnlyList<StaticSupportIslandShapeConfig> shapes = gameModeConfig != null
            ? gameModeConfig.StaticSupportIslandShapes
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
        float gridSpacing = gameModeConfig != null ? gameModeConfig.GridSpacing : 1f;
        int minColumn = gameModeConfig != null
            ? gameModeConfig.StaticSupportIslandMinColumn
            : Mathf.RoundToInt(-_xRange);
        int maxColumn = gameModeConfig != null
            ? gameModeConfig.StaticSupportIslandMaxColumn
            : Mathf.RoundToInt(_xRange);

        if (!TryBuildValidBaseColumns(shape, minColumn, maxColumn)) return;

        int baseColumn = _validBaseColumns[Random.Range(0, _validBaseColumns.Count)];
        float spawnAhead = gameModeConfig != null
            ? gameModeConfig.StaticSupportIslandSpawnAheadHeight
            : _spawnOffset;
        float baseY = Mathf.Round((currentMaxHeight + spawnAhead) / gridSpacing) * gridSpacing;

        _islandCellCenters.Clear();
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

            _islandCellCenters.Add(new Vector2(cellPosition.x, cellPosition.y));
            _pool.Get(_staticBlockPrefab, cellPosition, Quaternion.identity, islandRoot.transform);
        }

        if (!BlockController.RegisterStaticSupportCells(_islandCellCenters, gridSpacing))
        {
            Destroy(islandRoot);
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
        int clearColumns = gameModeConfig != null
            ? gameModeConfig.StaticSupportIslandCenterClearColumns
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
