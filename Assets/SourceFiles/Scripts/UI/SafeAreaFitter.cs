using UnityEngine;

/// <summary>
/// Resizes its RectTransform to the device safe area (the region not covered by the camera
/// notch / cutout, status bar, rounded corners, or the home indicator) by driving its anchors
/// to the safe-area fractions of the screen. Parent any edge-pinned UI inside a fitter and it
/// keeps clear of cutouts on every phone, with no per-element inset math.
///
/// Contract (see RESPONSIVE.md):
/// - The fitter's RectTransform must fill the screen at rest - attach it to a full-screen child
///   of the (overlay) canvas. Anchors are interpreted as fractions of the screen, so a fitter on
///   a non-full-screen rect would mis-place its contents.
/// - It re-applies whenever Screen.safeArea or the screen size changes (rotation, resize,
///   foldables, multitasking), so it is safe to build the UI once.
/// - Insets are clamped (see <see cref="RuntimeUiKit.SafeAreaMaxInsetFraction"/>) so a degenerate
///   safe-area read on the first frame can never hide the UI; it self-corrects next frame.
/// - Backgrounds/art that should bleed behind the notch must NOT be parented under a fitter.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    private RectTransform _rect;
    private Rect _lastSafeArea = new Rect(float.NaN, float.NaN, float.NaN, float.NaN);
    private Vector2Int _lastScreen = new Vector2Int(-1, -1);

    private void Awake() => _rect = (RectTransform)transform;

    private void OnEnable() => Apply();

    private void Update()
    {
        // Cheap guard: only touch anchors when the geometry actually changed.
        if (Screen.safeArea != _lastSafeArea ||
            Screen.width != _lastScreen.x || Screen.height != _lastScreen.y)
        {
            Apply();
        }
    }

    /// <summary>Re-applies the safe area immediately (e.g. after reparenting the fitter).</summary>
    public void Apply()
    {
        if (_rect == null) _rect = (RectTransform)transform;

        int w = Screen.width;
        int h = Screen.height;
        if (w <= 0 || h <= 0) return;

        _lastSafeArea = Screen.safeArea;
        _lastScreen = new Vector2Int(w, h);

        // Clamped per-edge insets in pixels, then expressed as normalized anchor fractions so the
        // fit is independent of canvas scale.
        Vector4 inset = RuntimeUiKit.SafeAreaInsetsPixels(); // (left, right, top, bottom)
        Vector2 anchorMin = new Vector2(inset.x / w, inset.w / h);
        Vector2 anchorMax = new Vector2(1f - inset.y / w, 1f - inset.z / h);

        _rect.anchorMin = anchorMin;
        _rect.anchorMax = anchorMax;
        _rect.offsetMin = Vector2.zero;
        _rect.offsetMax = Vector2.zero;
    }
}
