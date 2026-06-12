using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// The translucent landing-preview beam under the falling piece (visual only - no
// collider, so casts and cell geometry never see it).
public partial class BlockController
{
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

}
