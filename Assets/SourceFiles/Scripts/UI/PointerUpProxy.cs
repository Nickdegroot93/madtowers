using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Fires a callback when a pointer is released over this object. Used to commit a slider value
/// (persist to disk, play a preview) only when the drag ends, instead of on every value change.
/// </summary>
public class PointerUpProxy : MonoBehaviour, IPointerUpHandler
{
    public Action OnRelease;

    public void OnPointerUp(PointerEventData eventData) => OnRelease?.Invoke();
}
