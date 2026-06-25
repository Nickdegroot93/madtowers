using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A vertical <see cref="ScrollRect"/> that lets horizontal-dominant drags pass through to
/// a parent drag handler (the menu's chapter-swipe area) instead of swallowing them.
/// Vertical drags scroll the list exactly as a plain ScrollRect would.
/// </summary>
public class DirectionalScrollRect : ScrollRect
{
    private bool _routeToParent;

    // Parent handlers resolved ONCE at drag start and reused for the drag's OnDrag/OnEndDrag
    // frames. OnDrag fires continuously, so re-walking ancestors + GetComponents every frame
    // (the old DoForParents) allocated an array per node, per frame. Lists are filled in place
    // by GetComponentsInParent (no per-call alloc).
    private readonly List<IBeginDragHandler> _parentBegin = new List<IBeginDragHandler>();
    private readonly List<IDragHandler> _parentDrag = new List<IDragHandler>();
    private readonly List<IEndDragHandler> _parentEnd = new List<IEndDragHandler>();

    public override void OnBeginDrag(PointerEventData eventData)
    {
        _routeToParent = Mathf.Abs(eventData.delta.x) > Mathf.Abs(eventData.delta.y);
        if (!_routeToParent)
        {
            base.OnBeginDrag(eventData);
            return;
        }

        GetComponentsInParent(true, _parentBegin);
        GetComponentsInParent(true, _parentDrag);
        GetComponentsInParent(true, _parentEnd);

        for (int i = 0; i < _parentBegin.Count; i++)
            if (!IsSelf(_parentBegin[i])) _parentBegin[i].OnBeginDrag(eventData);
    }

    public override void OnDrag(PointerEventData eventData)
    {
        if (!_routeToParent)
        {
            base.OnDrag(eventData);
            return;
        }

        for (int i = 0; i < _parentDrag.Count; i++)
            if (!IsSelf(_parentDrag[i])) _parentDrag[i].OnDrag(eventData);
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        if (_routeToParent)
        {
            for (int i = 0; i < _parentEnd.Count; i++)
                if (!IsSelf(_parentEnd[i])) _parentEnd[i].OnEndDrag(eventData);
        }
        else
        {
            base.OnEndDrag(eventData);
        }
        _routeToParent = false;
    }

    // GetComponentsInParent includes our own GameObject (where this ScrollRect, itself a drag
    // handler, lives); the old walk started at the parent, so skip anything on our own object.
    private bool IsSelf(IEventSystemHandler handler) => handler is Component c && c.gameObject == gameObject;
}
