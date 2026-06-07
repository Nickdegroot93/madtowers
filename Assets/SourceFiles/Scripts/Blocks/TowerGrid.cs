using System.Collections.Generic;
using UnityEngine;

public sealed class TowerGrid
{
    private static readonly Vector2Int[] NeighborOffsets =
    {
        Vector2Int.right,
        Vector2Int.left,
        Vector2Int.up,
        Vector2Int.down
    };

    private readonly Dictionary<Vector2Int, BlockController> _occupiedCells = new Dictionary<Vector2Int, BlockController>();
    private readonly HashSet<Vector2Int> _visitedCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> _componentCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> _evaluatedCells = new HashSet<Vector2Int>();
    private readonly HashSet<int> _floorColumns = new HashSet<int>();
    private readonly HashSet<Vector2Int> _staticSupportCells = new HashSet<Vector2Int>();
    private readonly Dictionary<BlockController, float> _unstableBlocks = new Dictionary<BlockController, float>();
    private readonly Queue<Vector2Int> _cellsToVisit = new Queue<Vector2Int>();
    private readonly List<Vector2Int> _cellsToRemove = new List<Vector2Int>();
    private float _componentInstabilityDirection;
    private bool _hasOriginY;
    private float _originY;

    public bool HasOriginY => _hasOriginY;

    public void Clear()
    {
        _occupiedCells.Clear();
        _visitedCells.Clear();
        _componentCells.Clear();
        _evaluatedCells.Clear();
        _floorColumns.Clear();
        _staticSupportCells.Clear();
        _unstableBlocks.Clear();
        _cellsToVisit.Clear();
        _cellsToRemove.Clear();
        _hasOriginY = false;
        _originY = 0f;
    }

    public void ConfigureFloorSegments(IReadOnlyList<FloorSegmentConfig> floorSegments)
    {
        _floorColumns.Clear();
        if (floorSegments == null) return;

        for (int i = 0; i < floorSegments.Count; i++)
        {
            FloorSegmentConfig segment = floorSegments[i];
            if (segment == null) continue;

            for (int column = segment.LeftColumn; column <= segment.RightColumn; column++)
            {
                _floorColumns.Add(column);
            }
        }
    }

    public void ConfigureOriginY(float originY)
    {
        _originY = originY;
        _hasOriginY = true;
    }

    public void EnsureOriginY(IReadOnlyList<Vector2> cellCenters)
    {
        if (_hasOriginY || cellCenters == null || cellCenters.Count == 0) return;

        float lowestY = float.PositiveInfinity;
        for (int i = 0; i < cellCenters.Count; i++)
        {
            if (cellCenters[i].y < lowestY) lowestY = cellCenters[i].y;
        }

        if (lowestY == float.PositiveInfinity) return;

        _originY = lowestY;
        _hasOriginY = true;
    }

    public bool TryGetDropDistance(IReadOnlyList<Vector2> cellCenters, float gridSpacing, float distance, out float moveDistance)
    {
        moveDistance = distance;
        if (!_hasOriginY || cellCenters == null || cellCenters.Count == 0) return true;

        bool blocked = false;
        for (int i = 0; i < cellCenters.Count; i++)
        {
            Vector2 center = cellCenters[i];
            int column = WorldToColumn(center.x, gridSpacing);

            foreach (Vector2Int occupiedCell in _occupiedCells.Keys)
            {
                blocked |= TryUpdateDropDistanceFromSupportCell(occupiedCell, column, center.y, gridSpacing, distance, ref moveDistance);
            }

            foreach (Vector2Int supportCell in _staticSupportCells)
            {
                blocked |= TryUpdateDropDistanceFromSupportCell(supportCell, column, center.y, gridSpacing, distance, ref moveDistance);
            }

            if (IsFloorColumn(column, 0))
            {
                blocked |= TryUpdateDropDistanceToSupportedCenterY(
                    GridToWorldY(0, gridSpacing),
                    center.y,
                    distance,
                    ref moveDistance);
            }
        }

        return !blocked;
    }

    public bool RegisterStaticSupportCells(IReadOnlyList<Vector2> cellCenters, float gridSpacing)
    {
        if (!_hasOriginY || cellCenters == null || cellCenters.Count == 0) return false;

        for (int i = 0; i < cellCenters.Count; i++)
        {
            Vector2 center = cellCenters[i];
            _staticSupportCells.Add(new Vector2Int(WorldToColumn(center.x, gridSpacing), WorldToRow(center.y, gridSpacing)));
        }

        return true;
    }

    public bool IsCenterOfMassSupported(IReadOnlyList<Vector2> cellCenters, float gridSpacing, float stabilityMargin)
    {
        return IsCenterOfMassSupported(cellCenters, gridSpacing, stabilityMargin, false, 1, out _);
    }

    public bool IsCenterOfMassSupported(
        IReadOnlyList<Vector2> cellCenters,
        float gridSpacing,
        float stabilityMargin,
        bool allowLateralBrace,
        int minimumLateralBraceContacts,
        out float instabilityDirection)
    {
        instabilityDirection = 0f;
        if (!_hasOriginY || cellCenters == null || cellCenters.Count == 0) return true;

        BuildEvaluatedCellSet(cellCenters, gridSpacing);

        float totalX = 0f;
        bool hasSupport = false;
        float supportMinX = float.PositiveInfinity;
        float supportMaxX = float.NegativeInfinity;

        for (int i = 0; i < cellCenters.Count; i++)
        {
            Vector2 center = cellCenters[i];
            int column = WorldToColumn(center.x, gridSpacing);
            int row = WorldToRow(center.y, gridSpacing);

            totalX += GridToWorldX(column, gridSpacing);

            if (!IsCellSupported(column, row)) continue;

            hasSupport = true;
            float supportCenterX = GridToWorldX(column, gridSpacing);
            float halfCell = gridSpacing * 0.5f;
            supportMinX = Mathf.Min(supportMinX, supportCenterX - halfCell);
            supportMaxX = Mathf.Max(supportMaxX, supportCenterX + halfCell);
        }

        if (!hasSupport)
        {
            float unsupportedCenterX = totalX / cellCenters.Count;
            instabilityDirection = unsupportedCenterX >= 0f ? 1f : -1f;
            return false;
        }

        float centerOfMassX = totalX / cellCenters.Count;
        float supportMidX = (supportMinX + supportMaxX) * 0.5f;
        instabilityDirection = centerOfMassX >= supportMidX ? 1f : -1f;
        if (centerOfMassX > supportMinX + stabilityMargin &&
            centerOfMassX < supportMaxX - stabilityMargin)
        {
            return true;
        }

        return allowLateralBrace &&
               CountLateralBraceContacts(_evaluatedCells) >= Mathf.Max(1, minimumLateralBraceContacts);
    }

    public void RegisterCells(IReadOnlyList<Vector2> cellCenters, float gridSpacing, BlockController owner)
    {
        if (!_hasOriginY || cellCenters == null || owner == null) return;

        RemoveBlock(owner);

        for (int i = 0; i < cellCenters.Count; i++)
        {
            Vector2 center = cellCenters[i];
            _occupiedCells[new Vector2Int(WorldToColumn(center.x, gridSpacing), WorldToRow(center.y, gridSpacing))] = owner;
        }
    }

    public bool ContainsBlock(BlockController owner)
    {
        if (owner == null) return false;

        foreach (BlockController registeredOwner in _occupiedCells.Values)
        {
            if (registeredOwner == owner) return true;
        }

        return false;
    }

    public void RemoveBlock(BlockController owner)
    {
        if (owner == null) return;

        _cellsToRemove.Clear();
        foreach (KeyValuePair<Vector2Int, BlockController> occupiedCell in _occupiedCells)
        {
            if (occupiedCell.Value == owner) _cellsToRemove.Add(occupiedCell.Key);
        }

        for (int i = 0; i < _cellsToRemove.Count; i++)
        {
            _occupiedCells.Remove(_cellsToRemove[i]);
        }
    }

    public void ReleaseUnstableComponents(
        float gridSpacing,
        float stabilityMargin,
        bool allowLateralBrace,
        int minimumLateralBraceContacts,
        int maximumLateralBraceCells)
    {
        _visitedCells.Clear();
        _unstableBlocks.Clear();

        foreach (Vector2Int occupiedCell in _occupiedCells.Keys)
        {
            if (_visitedCells.Contains(occupiedCell)) continue;

            BuildComponent(occupiedCell);
            if (!IsComponentSupported(
                    gridSpacing,
                    stabilityMargin,
                    allowLateralBrace,
                    minimumLateralBraceContacts,
                    maximumLateralBraceCells))
            {
                CollectComponentOwners();
            }
        }

        foreach (KeyValuePair<BlockController, float> unstableBlock in _unstableBlocks)
        {
            RemoveBlock(unstableBlock.Key);
        }

        foreach (KeyValuePair<BlockController, float> unstableBlock in _unstableBlocks)
        {
            if (unstableBlock.Key != null) unstableBlock.Key.ReleaseFromGridInstability(unstableBlock.Value);
        }
    }

    public float SnapWorldY(float worldY, float gridSpacing)
    {
        if (!_hasOriginY) return worldY;
        return GridToWorldY(WorldToRow(worldY, gridSpacing), gridSpacing);
    }

    private bool IsCellSupported(int column, int row)
    {
        if (IsFloorColumn(column, row)) return true;
        Vector2Int supportCell = new Vector2Int(column, row - 1);
        return _occupiedCells.ContainsKey(supportCell) || _staticSupportCells.Contains(supportCell);
    }

    private bool IsFloorColumn(int column, int row)
    {
        if (row > 0) return false;
        return _floorColumns.Count == 0 || _floorColumns.Contains(column);
    }

    private void BuildComponent(Vector2Int startCell)
    {
        _componentCells.Clear();
        _cellsToVisit.Clear();

        _visitedCells.Add(startCell);
        _componentCells.Add(startCell);
        _cellsToVisit.Enqueue(startCell);

        while (_cellsToVisit.Count > 0)
        {
            Vector2Int cell = _cellsToVisit.Dequeue();
            for (int i = 0; i < NeighborOffsets.Length; i++)
            {
                Vector2Int neighbor = cell + NeighborOffsets[i];
                if (_visitedCells.Contains(neighbor)) continue;
                if (!_occupiedCells.ContainsKey(neighbor)) continue;

                _visitedCells.Add(neighbor);
                _componentCells.Add(neighbor);
                _cellsToVisit.Enqueue(neighbor);
            }
        }
    }

    private bool IsComponentSupported(
        float gridSpacing,
        float stabilityMargin,
        bool allowLateralBrace,
        int minimumLateralBraceContacts,
        int maximumLateralBraceCells)
    {
        _componentInstabilityDirection = 0f;
        if (_componentCells.Count == 0) return true;

        float totalX = 0f;
        float supportMinX = float.PositiveInfinity;
        float supportMaxX = float.NegativeInfinity;
        bool hasSupport = false;

        foreach (Vector2Int cell in _componentCells)
        {
            totalX += GridToWorldX(cell.x, gridSpacing);

            Vector2Int below = new Vector2Int(cell.x, cell.y - 1);
            bool supportedByFloor = IsFloorColumn(cell.x, cell.y);
            bool supportedByOutsideBlock = _occupiedCells.ContainsKey(below) && !_componentCells.Contains(below);
            bool supportedByStaticIsland = _staticSupportCells.Contains(below);

            if (!supportedByFloor && !supportedByOutsideBlock && !supportedByStaticIsland) continue;

            hasSupport = true;
            float supportCenterX = GridToWorldX(cell.x, gridSpacing);
            float halfCell = gridSpacing * 0.5f;
            supportMinX = Mathf.Min(supportMinX, supportCenterX - halfCell);
            supportMaxX = Mathf.Max(supportMaxX, supportCenterX + halfCell);
        }

        if (!hasSupport)
        {
            float unsupportedCenterX = totalX / _componentCells.Count;
            _componentInstabilityDirection = unsupportedCenterX >= 0f ? 1f : -1f;
            return false;
        }

        float centerOfMassX = totalX / _componentCells.Count;
        float supportMidX = (supportMinX + supportMaxX) * 0.5f;
        _componentInstabilityDirection = centerOfMassX >= supportMidX ? 1f : -1f;
        if (centerOfMassX > supportMinX + stabilityMargin &&
            centerOfMassX < supportMaxX - stabilityMargin)
        {
            return true;
        }

        return allowLateralBrace &&
               _componentCells.Count <= Mathf.Max(1, maximumLateralBraceCells) &&
               CountLateralBraceContacts(_componentCells) >= Mathf.Max(1, minimumLateralBraceContacts);
    }

    private void CollectComponentOwners()
    {
        foreach (Vector2Int cell in _componentCells)
        {
            if (_occupiedCells.TryGetValue(cell, out BlockController owner) && owner != null)
            {
                _unstableBlocks[owner] = _componentInstabilityDirection;
            }
        }
    }

    private bool TryUpdateDropDistanceFromSupportCell(
        Vector2Int supportCell,
        int fallingColumn,
        float fallingCenterY,
        float gridSpacing,
        float maxDistance,
        ref float moveDistance)
    {
        if (supportCell.x != fallingColumn) return false;

        float supportedCenterY = GridToWorldY(supportCell.y + 1, gridSpacing);
        return TryUpdateDropDistanceToSupportedCenterY(supportedCenterY, fallingCenterY, maxDistance, ref moveDistance);
    }

    private void BuildEvaluatedCellSet(IReadOnlyList<Vector2> cellCenters, float gridSpacing)
    {
        _evaluatedCells.Clear();
        if (cellCenters == null) return;

        for (int i = 0; i < cellCenters.Count; i++)
        {
            Vector2 center = cellCenters[i];
            _evaluatedCells.Add(new Vector2Int(WorldToColumn(center.x, gridSpacing), WorldToRow(center.y, gridSpacing)));
        }
    }

    private int CountLateralBraceContacts(HashSet<Vector2Int> evaluatedCells)
    {
        int contacts = 0;
        foreach (Vector2Int cell in evaluatedCells)
        {
            Vector2Int left = cell + Vector2Int.left;
            Vector2Int right = cell + Vector2Int.right;

            if (IsExternalBraceCell(left, evaluatedCells)) contacts++;
            if (IsExternalBraceCell(right, evaluatedCells)) contacts++;
        }

        return contacts;
    }

    private bool IsExternalBraceCell(Vector2Int cell, HashSet<Vector2Int> evaluatedCells)
    {
        if (evaluatedCells.Contains(cell)) return false;
        return _occupiedCells.ContainsKey(cell) ||
               _staticSupportCells.Contains(cell) ||
               IsFloorColumn(cell.x, cell.y);
    }

    private bool TryUpdateDropDistanceToSupportedCenterY(
        float supportedCenterY,
        float fallingCenterY,
        float maxDistance,
        ref float moveDistance)
    {
        if (supportedCenterY > fallingCenterY + 0.001f) return false;
        if (fallingCenterY - maxDistance > supportedCenterY + 0.001f) return false;

        moveDistance = Mathf.Min(moveDistance, Mathf.Max(0f, fallingCenterY - supportedCenterY));
        return true;
    }

    private int WorldToColumn(float worldX, float gridSpacing)
    {
        return Mathf.RoundToInt(worldX / gridSpacing);
    }

    private int WorldToRow(float worldY, float gridSpacing)
    {
        return Mathf.RoundToInt((worldY - _originY) / gridSpacing);
    }

    private float GridToWorldX(int column, float gridSpacing)
    {
        return column * gridSpacing;
    }

    private float GridToWorldY(int row, float gridSpacing)
    {
        return _originY + row * gridSpacing;
    }
}
