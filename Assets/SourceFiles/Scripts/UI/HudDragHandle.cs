using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Makes a HUD element draggable within a bounds rect (the layout editor's safe-area container).
/// A pointer-down selects it; a drag reports the new position as a normalized [0,1] point inside
/// the bounds. Editor-only — the in-game HUD positions its elements straight from HudLayout.
/// </summary>
public class HudDragHandle : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    public RectTransform bounds;
    public Action onSelected;
    public Action<Vector2> onMovedNormalized;

    public void OnPointerDown(PointerEventData eventData) => onSelected?.Invoke();

    public void OnDrag(PointerEventData eventData)
    {
        if (bounds == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                bounds, eventData.position, eventData.pressEventCamera, out Vector2 local))
            return;

        Rect r = bounds.rect;
        float nx = Mathf.Clamp01((local.x - r.xMin) / Mathf.Max(1f, r.width));
        float ny = Mathf.Clamp01((local.y - r.yMin) / Mathf.Max(1f, r.height));
        onMovedNormalized?.Invoke(new Vector2(nx, ny));
    }
}
