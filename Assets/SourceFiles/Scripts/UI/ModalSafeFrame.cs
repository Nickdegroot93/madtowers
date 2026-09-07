using UnityEngine;

/// <summary>Fits an authored modal sheet inside the safe area. Scales a parent so entrance
/// animation can continue to own the sheet's scale; updates on resize and content reflow.</summary>
public sealed class ModalSafeFrame : MonoBehaviour
{
    private RectTransform _panel;
    private RectTransform _frame;

    public static void Attach(RectTransform panel)
    {
        var safe = RuntimeUiKit.CreateRect(panel.parent, "SheetSafeArea", Vector2.zero, Vector2.one,
            new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        safe.gameObject.AddComponent<SafeAreaFitter>();
        var frame = RuntimeUiKit.CreateRect(safe, "SheetFrame", Vector2.zero, Vector2.one,
            new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        panel.SetParent(frame, false);
        var fit = safe.gameObject.AddComponent<ModalSafeFrame>();
        fit._panel = panel;
        fit._frame = frame;
    }

    private void LateUpdate()
    {
        if (_panel == null) return;
        var safe = (RectTransform)transform;
        float scale = Mathf.Min(1f, (safe.rect.width - 48f) / Mathf.Max(1f, _panel.rect.width),
            (safe.rect.height - 64f) / Mathf.Max(1f, _panel.rect.height));
        _frame.localScale = Vector3.one * Mathf.Max(.1f, scale);
    }
}
