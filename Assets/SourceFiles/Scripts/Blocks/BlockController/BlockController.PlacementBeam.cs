using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// The translucent landing-preview beam under the falling piece (visual only - no
// collider, so casts and cell geometry never see it).
public partial class BlockController
{
    private const float VectorGuideFillAlpha = 0.025f;
    private const float VectorGuideInnerLineAlpha = 0.11f;
    private const float VectorGuideOuterLineAlpha = 0.38f;
    private const float VectorGuideCellFillScale = 0.78f;
    private const float VectorGuideLineThicknessFraction = 0.035f;

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
        if (_placementBeamRenderer != null)
        {
            Destroy(_placementBeamRenderer.gameObject);
            _placementBeamRenderer = null;
        }

        DestroyVectorGuideGhost();
    }

    private void UpdatePlacementBeam()
    {
        if (_placementBeamRenderer == null)
        {
            UpdateVectorGuideGhost(false);
            return;
        }

        bool shouldShow = showPlacementBeam && _isControlEnabled && !HasLanded && !_hasTouchedDown;
        if (!shouldShow || !TryGetPlacementBeamFootprint(out float centerX, out float width) ||
            !TryGetPlacementBeamVerticalSpan(out float centerY, out float height))
        {
            _placementBeamRenderer.enabled = false;
            UpdateVectorGuideGhost(false);
            return;
        }

        Transform beamTransform = _placementBeamRenderer.transform;
        beamTransform.position = new Vector3(centerX, centerY, transform.position.z);
        beamTransform.rotation = Quaternion.identity;
        beamTransform.localScale = Vector3.one;
        _placementBeamRenderer.size = new Vector2(width, height);
        _placementBeamRenderer.enabled = true;

        UpdateVectorGuideGhost(_vectorGuideEnabled);
    }

    private void UpdateVectorGuideGhost(bool shouldShow)
    {
        if (!shouldShow || !TryGetVectorGuideLandingPose(out Vector3 blockPosition, out Quaternion blockRotation,
                out SpriteRenderer sourceRenderer))
        {
            SetVectorGuideGhostVisible(false);
            return;
        }

        EnsureVectorGuideGhost();
        if (_vectorGuideGhostRoot == null) return;

        _vectorGuideGhostRoot.position = blockPosition;
        _vectorGuideGhostRoot.rotation = blockRotation;
        _vectorGuideGhostRoot.localScale = Vector3.one;
        RenderVectorGuideCells(sourceRenderer.sortingLayerID);
    }

    private void EnsureVectorGuideGhost()
    {
        if (_vectorGuideGhostRoot != null) return;

        GameObject ghost = new GameObject($"{name}_VectorGuideGhost");
        _vectorGuideGhostRoot = ghost.transform;
    }

    private void DestroyVectorGuideGhost()
    {
        if (_vectorGuideGhostRoot == null) return;

        Destroy(_vectorGuideGhostRoot.gameObject);
        _vectorGuideGhostRoot = null;
        _vectorGuideFillRenderers.Clear();
        _vectorGuideLineRenderers.Clear();
    }

    private void SetVectorGuideGhostVisible(bool visible)
    {
        for (int i = 0; i < _vectorGuideFillRenderers.Count; i++)
        {
            if (_vectorGuideFillRenderers[i] != null) _vectorGuideFillRenderers[i].enabled = visible;
        }

        for (int i = 0; i < _vectorGuideLineRenderers.Count; i++)
        {
            if (_vectorGuideLineRenderers[i] != null) _vectorGuideLineRenderers[i].enabled = visible;
        }
    }

    private void RenderVectorGuideCells(int sortingLayerId)
    {
        _cellGeometry.Refresh();
        IReadOnlyList<Vector2> cells = _cellGeometry.CellCenters;
        if (cells.Count == 0)
        {
            SetVectorGuideGhostVisible(false);
            return;
        }

        float cellSize = Mathf.Max(0.01f, gridSpacing);
        float fillSize = cellSize * VectorGuideCellFillScale;
        float lineThickness = cellSize * VectorGuideLineThicknessFraction;

        EnsureRendererCount(_vectorGuideFillRenderers, cells.Count, "Fill");

        for (int i = 0; i < cells.Count; i++)
        {
            SpriteRenderer fill = _vectorGuideFillRenderers[i];
            fill.transform.localPosition = transform.InverseTransformPoint(cells[i]);
            fill.transform.localRotation = Quaternion.identity;
            fill.transform.localScale = new Vector3(fillSize, fillSize, 1f);
            ConfigureVectorGuideRenderer(fill, RuntimeSprites.VectorGuideGhostFill(), sortingLayerId, VectorGuideGhostSortingOrder - 2,
                new Color(1f, 1f, 1f, VectorGuideFillAlpha));
        }

        for (int i = cells.Count; i < _vectorGuideFillRenderers.Count; i++)
        {
            if (_vectorGuideFillRenderers[i] != null) _vectorGuideFillRenderers[i].enabled = false;
        }

        int lineIndex = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            Vector2 localCell = transform.InverseTransformPoint(cells[i]);
            lineIndex = AddVectorGuideCellEdgeLines(
                localCell,
                sortingLayerId,
                cellSize,
                lineThickness,
                HasVectorGuideNeighbor(localCell, Vector2.left, cellSize),
                HasVectorGuideNeighbor(localCell, Vector2.right, cellSize),
                HasVectorGuideNeighbor(localCell, Vector2.up, cellSize),
                HasVectorGuideNeighbor(localCell, Vector2.down, cellSize),
                lineIndex);
        }

        for (int i = lineIndex; i < _vectorGuideLineRenderers.Count; i++)
        {
            if (_vectorGuideLineRenderers[i] != null) _vectorGuideLineRenderers[i].enabled = false;
        }
    }

    private int AddVectorGuideCellEdgeLines(Vector2 localCell, int sortingLayerId, float cellSize,
        float lineThickness, bool hasLeft, bool hasRight, bool hasUp, bool hasDown, int lineIndex)
    {
        if (!hasLeft)
        {
            AddVectorGuideLine(localCell + Vector2.left * cellSize * 0.5f, false, sortingLayerId,
                cellSize, lineThickness, VectorGuideOuterLineAlpha, ref lineIndex);
        }
        if (!hasRight)
        {
            AddVectorGuideLine(localCell + Vector2.right * cellSize * 0.5f, false, sortingLayerId,
                cellSize, lineThickness, VectorGuideOuterLineAlpha, ref lineIndex);
        }
        else
        {
            AddVectorGuideLine(localCell + Vector2.right * cellSize * 0.5f, false, sortingLayerId,
                cellSize, lineThickness, VectorGuideInnerLineAlpha, ref lineIndex);
        }

        if (!hasUp)
        {
            AddVectorGuideLine(localCell + Vector2.up * cellSize * 0.5f, true, sortingLayerId,
                cellSize, lineThickness, VectorGuideOuterLineAlpha, ref lineIndex);
        }
        else
        {
            AddVectorGuideLine(localCell + Vector2.up * cellSize * 0.5f, true, sortingLayerId,
                cellSize, lineThickness, VectorGuideInnerLineAlpha, ref lineIndex);
        }
        if (!hasDown)
        {
            AddVectorGuideLine(localCell + Vector2.down * cellSize * 0.5f, true, sortingLayerId,
                cellSize, lineThickness, VectorGuideOuterLineAlpha, ref lineIndex);
        }

        return lineIndex;
    }

    private bool HasVectorGuideNeighbor(Vector2 localCell, Vector2 direction, float cellSize)
    {
        IReadOnlyList<Vector2> cells = _cellGeometry.CellCenters;
        Vector2 target = localCell + direction * cellSize;
        float tolerance = cellSize * 0.15f;
        float toleranceSqr = tolerance * tolerance;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2 otherLocal = transform.InverseTransformPoint(cells[i]);
            if ((otherLocal - target).sqrMagnitude <= toleranceSqr) return true;
        }

        return false;
    }

    private void AddVectorGuideLine(Vector2 localPosition, bool horizontal, int sortingLayerId,
        float length, float thickness, float alpha, ref int lineIndex)
    {
        EnsureRendererCount(_vectorGuideLineRenderers, lineIndex + 1, "Line");

        SpriteRenderer line = _vectorGuideLineRenderers[lineIndex];
        line.transform.localPosition = localPosition;
        line.transform.localRotation = Quaternion.identity;
        line.transform.localScale = horizontal
            ? new Vector3(length + thickness, thickness, 1f)
            : new Vector3(thickness, length + thickness, 1f);
        ConfigureVectorGuideRenderer(line, RuntimeSprites.VectorGuideGhostLine(), sortingLayerId, VectorGuideGhostSortingOrder,
            new Color(1f, 1f, 1f, alpha));
        lineIndex++;
    }

    private void EnsureRendererCount(List<SpriteRenderer> renderers, int count, string label)
    {
        while (renderers.Count < count)
        {
            GameObject child = new GameObject($"{label}_{renderers.Count}");
            child.transform.SetParent(_vectorGuideGhostRoot, false);
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderers.Add(renderer);
        }
    }

    private void ConfigureVectorGuideRenderer(SpriteRenderer renderer, Sprite sprite, int sortingLayerId, int sortingOrder, Color color)
    {
        renderer.sprite = sprite;
        renderer.sortingLayerID = sortingLayerId;
        renderer.sortingOrder = sortingOrder;
        renderer.color = color;
        renderer.enabled = true;
    }

    private bool TryGetVectorGuideLandingPose(out Vector3 blockPosition, out Quaternion blockRotation,
        out SpriteRenderer sourceRenderer)
    {
        blockPosition = transform.position;
        blockRotation = transform.rotation;
        sourceRenderer = FindVectorGuideSourceRenderer();
        if (sourceRenderer == null || _rb == null) return false;

        Physics2D.SyncTransforms();

        float castDistance = GetVectorGuideCastDistance();
        int count = _rb.Cast(Vector2.down, _contactFilter, _castResults, castDistance);
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

        blockPosition = transform.position + Vector3.down * Mathf.Max(0f, closestContactDistance);
        return true;
    }

    private SpriteRenderer FindVectorGuideSourceRenderer()
    {
        if (_vectorGuideSourceRenderer != null && _vectorGuideSourceRenderer.sprite != null &&
            _vectorGuideSourceRenderer.enabled)
        {
            return _vectorGuideSourceRenderer;
        }

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null || !renderer.enabled) continue;
            _vectorGuideSourceRenderer = renderer;
            return renderer;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sprite == null) continue;

            _vectorGuideSourceRenderer = renderer;
            return renderer;
        }

        return null;
    }

    private float GetVectorGuideCastDistance()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
        }

        if (_mainCamera == null || !_mainCamera.orthographic) return gridSpacing * 16f;

        float viewBottom = _mainCamera.transform.position.y - _mainCamera.orthographicSize;
        return Mathf.Max(gridSpacing, transform.position.y - viewBottom + gridSpacing * 2f);
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

}
