using System;
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

    private void DoForParents<T>(Action<T> action) where T : IEventSystemHandler
    {
        Transform parent = transform.parent;
        while (parent != null)
        {
            Component[] components = parent.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is T handler) action(handler);
            }
            parent = parent.parent;
        }
    }

    public override void OnBeginDrag(PointerEventData eventData)
    {
        _routeToParent = Mathf.Abs(eventData.delta.x) > Mathf.Abs(eventData.delta.y);
        if (_routeToParent) DoForParents<IBeginDragHandler>(p => p.OnBeginDrag(eventData));
        else base.OnBeginDrag(eventData);
    }

    public override void OnDrag(PointerEventData eventData)
    {
        if (_routeToParent) DoForParents<IDragHandler>(p => p.OnDrag(eventData));
        else base.OnDrag(eventData);
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        if (_routeToParent) DoForParents<IEndDragHandler>(p => p.OnEndDrag(eventData));
        else base.OnEndDrag(eventData);
        _routeToParent = false;
    }
}
