using UnityEngine;

/// <summary>
/// Keeps this RectTransform covering the whole root canvas in screen space, no matter where its
/// (scrolling / swiping) parent card sits. Paired with a rounded <see cref="UnityEngine.UI.Mask"/>
/// on the parent and a blurred copy of the chapter background, it makes the card read as a
/// frosted-glass window: it shows a blurred slice of the scene exactly where the card is on screen.
/// Re-evaluated every LateUpdate so it tracks scroll and chapter-swipe motion.
/// </summary>
[DisallowMultipleComponent]
public sealed class MenuFrostedBackdrop : MonoBehaviour
{
    private RectTransform _rect;
    private RectTransform _canvas;

    private void OnEnable()
    {
        _rect = (RectTransform)transform;
        _rect.anchorMin = new Vector2(0.5f, 0.5f);
        _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.localRotation = Quaternion.identity;
        Align();
    }

    private void LateUpdate() => Align();

    private void Align()
    {
        if (_canvas == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            _canvas = (RectTransform)canvas.rootCanvas.transform;
        }

        // Match the canvas rectangle in world space, then express its size in this rect's local
        // units (canvas and card normally share the overlay scale, so the ratio is ~1).
        _rect.position = _canvas.TransformPoint(_canvas.rect.center);
        Vector3 canvasScale = _canvas.lossyScale;
        Vector3 localScale = _rect.lossyScale;
        float sx = Mathf.Approximately(localScale.x, 0f) ? 1f : canvasScale.x / localScale.x;
        float sy = Mathf.Approximately(localScale.y, 0f) ? 1f : canvasScale.y / localScale.y;
        Vector2 size = _canvas.rect.size;
        _rect.sizeDelta = new Vector2(size.x * sx, size.y * sy);
    }
}
