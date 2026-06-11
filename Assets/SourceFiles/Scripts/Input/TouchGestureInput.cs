using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>
/// Mobile gesture controls, translated into the active piece's existing control calls
/// (so every move obeys the same grid/physics rules as the keyboard):
///   - horizontal drag: position-based column steps (one column of world width per step)
///   - tap: rotate - left half of the screen = counter-clockwise, right half = clockwise
///   - drag down past a threshold and hold: fast drop until the finger lifts
///   - quick short downward flick: latched full-speed auto-drop
///   - tap in a bottom CORNER zone: nudge - a precision dash of exactly one column
///     toward that side (consumed immediately; never becomes a drag or rotate)
/// A second finger can tap to rotate while the first is dragging. Self-installs at
/// startup (no scene wiring). On desktop the left mouse button acts as one finger -
/// deliberately NOT via TouchSimulation, which suppresses the mouse device and killed
/// UI clicks in the level-select menu.
/// </summary>
public class TouchGestureInput : MonoBehaviour
{
    private const float TapMaxDuration = 0.25f;        // seconds
    private const float TapMaxMoveInches = 0.07f;      // movement budget before a touch becomes a drag
    private const float DropEngageInches = 0.30f;      // downward pull needed to engage held fast drop
    private const float FlickMaxDuration = 0.28f;      // a quick short downward swipe ...
    private const float FlickMinInches = 0.18f;        // ... at least this far down ...
    private const float FlickDominance = 1.5f;         // ... and clearly vertical = latched auto-drop
    private const float HudTopIgnoreFraction = 0.10f;  // touches starting in the HUD strip are ignored

    // Bottom-corner nudge zones. Public: UIManager draws its faint markers from these
    // same fractions, so the on-screen hint always matches the real hitbox.
    public const float NudgeZoneWidthFraction = 0.22f;
    public const float NudgeZoneHeightFraction = 0.16f;
    private const float FallbackDpi = 160f;
    private const int MouseId = -1;

    private sealed class TouchState
    {
        public Vector2 StartPos;
        public Vector2 LastPos;
        public float StartTime;
        public bool IsDrag;
        public bool OwnsDrag;
        public bool FastDropEngaged;
        public int StepsApplied;
    }

    private readonly Dictionary<int, TouchState> _touches = new Dictionary<int, TouchState>();
    private readonly List<int> _staleIds = new List<int>();
    private int _dragOwnerId = -2; // -2 = none (-1 is the mouse pointer id)
    private Camera _camera;
    private BlockController _lastActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        GameObject host = new GameObject("TouchGestureInput");
        DontDestroyOnLoad(host);
        host.AddComponent<TouchGestureInput>();
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
    }

    private void Update()
    {
        BlockController active = BlockController.ActiveControlled;
        bool paused = GameManager.Instance != null && GameManager.Instance.IsGamePaused;

        // A new piece spawned mid-gesture: rebase the drag so leftover pointer offset from
        // the previous piece doesn't teleport this one, and require fast drop to re-engage.
        if (active != _lastActive)
        {
            _lastActive = active;
            foreach (TouchState state in _touches.Values)
            {
                state.StartPos = state.LastPos;
                state.StepsApplied = 0;
                state.FastDropEngaged = false;
            }
        }

        bool sawRealTouch = false;
        foreach (Touch touch in Touch.activeTouches)
        {
            sawRealTouch = true;
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    PointerBegan(touch.touchId, touch.screenPosition);
                    break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    PointerHeld(touch.touchId, touch.screenPosition, active);
                    break;
                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    PointerEnded(touch.touchId, touch.screenPosition, active, paused);
                    break;
            }
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        // Desktop testing: the left mouse button is one finger. The mouse device stays
        // fully alive for the UI event system.
        if (!sawRealTouch && Mouse.current != null)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            if (Mouse.current.leftButton.wasPressedThisFrame) PointerBegan(MouseId, mousePos);
            else if (Mouse.current.leftButton.wasReleasedThisFrame) PointerEnded(MouseId, mousePos, active, paused);
            else if (Mouse.current.leftButton.isPressed) PointerHeld(MouseId, mousePos, active);
            else if (_touches.ContainsKey(MouseId)) PointerEnded(MouseId, mousePos, active, paused);
        }
#endif

        PruneVanishedTouches(sawRealTouch);

        bool fastDrop = false;
        foreach (TouchState state in _touches.Values)
        {
            if (state.FastDropEngaged) { fastDrop = true; break; }
        }
        if (active != null)
        {
            active.SetFastDrop(fastDrop && !paused);
        }
    }

    private void PointerBegan(int id, Vector2 pos)
    {
        if (pos.y > Screen.height * (1f - HudTopIgnoreFraction)) return; // HUD strip

        // Corner nudge: fires on touch DOWN (last-second tucks can't wait for a tap to
        // end) and consumes the touch - one tap, one dash, never a drag/rotate.
        if (pos.y < Screen.height * NudgeZoneHeightFraction)
        {
            if (pos.x < Screen.width * NudgeZoneWidthFraction) { Nudge(-1); return; }
            if (pos.x > Screen.width * (1f - NudgeZoneWidthFraction)) { Nudge(1); return; }
        }

        _touches[id] = new TouchState
        {
            StartPos = pos,
            LastPos = pos,
            StartTime = Time.unscaledTime
        };
    }

    private static void Nudge(int direction)
    {
        BlockController active = BlockController.ActiveControlled;
        if (active != null) active.Nudge(direction); // pause/control checks live in Nudge
    }

    private void PointerHeld(int id, Vector2 pos, BlockController active)
    {
        if (!_touches.TryGetValue(id, out TouchState state)) return;
        state.LastPos = pos;

        Vector2 delta = pos - state.StartPos;
        float dpi = ScreenDpi();

        if (!state.IsDrag &&
            (delta.magnitude > TapMaxMoveInches * dpi || Time.unscaledTime - state.StartTime > TapMaxDuration))
        {
            state.IsDrag = true;
            if (_dragOwnerId == -2)
            {
                _dragOwnerId = id;
                state.OwnsDrag = true;
            }
        }

        if (!state.OwnsDrag || active == null) return;

        // Position-based column stepping: pointer offset maps 1:1 to grid columns.
        float columnPixels = ColumnWidthPixels(active);
        int desiredSteps = Mathf.RoundToInt(delta.x / columnPixels);
        while (state.StepsApplied < desiredSteps)
        {
            active.StepColumn(1);
            state.StepsApplied++;
        }
        while (state.StepsApplied > desiredSteps)
        {
            active.StepColumn(-1);
            state.StepsApplied--;
        }

        // Held fast drop: a clear downward pull (dominant axis) engages it until release.
        if (!state.FastDropEngaged &&
            -delta.y > DropEngageInches * dpi &&
            Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
        {
            state.FastDropEngaged = true;
        }
    }

    private void PointerEnded(int id, Vector2 pos, BlockController active, bool paused)
    {
        if (!_touches.TryGetValue(id, out TouchState state)) return;
        _touches.Remove(id);
        if (state.OwnsDrag) _dragOwnerId = -2;

        if (paused || active == null) return;

        float duration = Time.unscaledTime - state.StartTime;
        if (state.IsDrag)
        {
            // A quick, short, clearly-downward swipe that ends = flick: latch full-speed
            // descent all the way down, no held finger needed.
            Vector2 delta = pos - state.StartPos;
            if (duration <= FlickMaxDuration &&
                -delta.y > FlickMinInches * ScreenDpi() &&
                Mathf.Abs(delta.y) > FlickDominance * Mathf.Abs(delta.x))
            {
                active.StartAutoDrop();
            }
            return;
        }

        // A short, near-still touch is a tap: rotate toward the tapped half of the screen.
        if (duration > TapMaxDuration) return;

        if (state.StartPos.x < Screen.width * 0.5f) active.RotateLeft();
        else active.RotateRight();
    }

    // Touches can vanish without an Ended phase (focus loss, device hiccups); drop their state.
    private void PruneVanishedTouches(bool sawRealTouch)
    {
        if (_touches.Count == 0) return;

        _staleIds.Clear();
        foreach (int id in _touches.Keys)
        {
            if (id == MouseId) continue; // mouse lifecycle is handled inline in Update
            bool seen = false;
            if (sawRealTouch)
            {
                foreach (Touch touch in Touch.activeTouches)
                {
                    if (touch.touchId == id) { seen = true; break; }
                }
            }
            if (!seen) _staleIds.Add(id);
        }
        foreach (int id in _staleIds)
        {
            if (_touches[id].OwnsDrag) _dragOwnerId = -2;
            _touches.Remove(id);
        }
    }

    private float ColumnWidthPixels(BlockController active)
    {
        if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
        if (_camera == null || !_camera.orthographic)
        {
            return Mathf.Max(20f, Screen.width / 11f); // sane fallback: ~11 columns across
        }

        float pixelsPerUnit = Screen.height / (2f * _camera.orthographicSize);
        return Mathf.Max(20f, active.GridSpacing * pixelsPerUnit);
    }

    private static float ScreenDpi()
    {
        float dpi = Screen.dpi;
        return dpi > 0f ? dpi : FallbackDpi;
    }
}
