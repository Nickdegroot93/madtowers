using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Turns horizontal drags over the main menu into a live pan, reported as a running
/// horizontal offset in screen pixels (positive = finger moved right, negative = left).
/// The pan begins on drag start, updates every frame, and reports the final offset on
/// release so the pager can decide whether to commit or spring back.
///
/// Attach to a full-screen raycast target that parents the menu content. Buttons under it
/// handle their own clicks (a tap that never becomes a drag reports a near-zero offset and
/// changes nothing); nested scroll views forward their horizontal-dominant drags up to this
/// handler (see <see cref="DirectionalScrollRect"/>). Vertical scrolling is unaffected -
/// the pager only acts on the horizontal component.
/// </summary>
public class MenuSwipeArea : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Action OnPanBegin;
    public Action<float> OnPanMove;   // running horizontal offset, screen pixels
    public Action<float> OnPanEnd;    // final horizontal offset, screen pixels

    private Vector2 _startPos;

    public void OnBeginDrag(PointerEventData eventData)
    {
        _startPos = eventData.position;
        OnPanBegin?.Invoke();
    }

    public void OnDrag(PointerEventData eventData)
    {
        OnPanMove?.Invoke(eventData.position.x - _startPos.x);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        OnPanEnd?.Invoke(eventData.position.x - _startPos.x);
    }
}
