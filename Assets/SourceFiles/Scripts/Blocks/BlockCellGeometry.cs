using System.Collections.Generic;
using UnityEngine;

public sealed class BlockCellGeometry
{
    private readonly List<Vector2> _cellCenters = new List<Vector2>(4);
    private Collider2D[] _solidColliders;

    public IReadOnlyList<Vector2> CellCenters => _cellCenters;

    public void Cache(GameObject root)
    {
        Collider2D[] colliders = root.GetComponentsInChildren<Collider2D>();
        int solidCount = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && !colliders[i].isTrigger) solidCount++;
        }

        _solidColliders = new Collider2D[solidCount];
        int writeIndex = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null || colliders[i].isTrigger) continue;

            _solidColliders[writeIndex] = colliders[i];
            writeIndex++;
        }
    }

    public void Refresh()
    {
        _cellCenters.Clear();
        if (_solidColliders == null) return;

        for (int i = 0; i < _solidColliders.Length; i++)
        {
            if (_solidColliders[i] == null) continue;
            _cellCenters.Add(GetColliderCenter(_solidColliders[i]));
        }
    }

    public float GetPrimaryWorldX(float fallback)
    {
        Collider2D collider = GetPrimaryCollider();
        return collider != null ? GetColliderCenter(collider).x : fallback;
    }

    public float GetPrimaryWorldY(float fallback)
    {
        Collider2D collider = GetPrimaryCollider();
        return collider != null ? GetColliderCenter(collider).y : fallback;
    }

    public Vector2 GetPrimaryWorldCenter(Vector2 fallback)
    {
        Collider2D collider = GetPrimaryCollider();
        return collider != null ? GetColliderCenter(collider) : fallback;
    }

    public bool TryGetWorldBounds(out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        if (_solidColliders == null) return false;

        for (int i = 0; i < _solidColliders.Length; i++)
        {
            Collider2D collider = _solidColliders[i];
            if (collider == null) continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    private Collider2D GetPrimaryCollider()
    {
        if (_solidColliders == null || _solidColliders.Length == 0) return null;

        for (int i = 0; i < _solidColliders.Length; i++)
        {
            if (_solidColliders[i] != null) return _solidColliders[i];
        }

        return null;
    }

    private Vector2 GetColliderCenter(Collider2D collider)
    {
        return collider.transform.TransformPoint(collider.offset);
    }
}
