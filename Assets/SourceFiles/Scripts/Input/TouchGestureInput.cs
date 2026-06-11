using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>
/// Mobile gesture controls, translated into the active piece's existing control calls
/// (so every move obeys the same grid/physics rules as the keyboard):
///   - horizontal drag: position-based column steps (one column of world width per step)
///   - tap: rotate - left half of the screen = counter-clockwise, right half = clockwise
///   - drag down past a threshold and hold: fast drop until the finger lifts
/// A second finger can tap to rotate while the first is dragging. Self-installs at
/// startup (no scene wiring); in the editor the mouse simulates a touch.
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
    private const float FallbackDpi = 160f;

    private sealed class TouchState
    {
        public Vector2 StartPos;
        public float StartTime;
        public bool IsDrag;
        public bool OwnsDrag;
        public bool FastDropEngaged;
        public int StepsApplied;
    }

    private readonly Dictionary<int, TouchState> _touches = new Dictionary<int, TouchState>();
    private readonly List<int> _staleIds = new List<int>();
    private int _dragOwnerId = -1;
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
#if UNITY_EDITOR
        TouchSimulation.Enable();
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        TouchSimulation.Disable();
#endif
        EnhancedTouchSupport.Disable();
    }

    private void Update()
    {
        BlockController active = BlockController.ActiveControlled;
        bool paused = GameManager.Instance != null && GameManager.Instance.IsGamePaused;

        // A new piece spawned mid-gesture: rebase the drag so leftover finger offset from
        // the previous piece doesn't teleport this one, and require fast drop to re-engage.
        if (active != _lastActive)
        {
            _lastActive = active;
            foreach (TouchState state in _touches.Values)
            {
                state.StartPos = LastKnownPosition(state);
                state.StepsApplied = 0;
                state.FastDropEngaged = false;
            }
        }

        bool fastDrop = false;
        foreach (Touch touch in Touch.activeTouches)
        {
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    HandleBegan(touch);
                    break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    HandleHeld(touch, active);
                    break;
                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    HandleEnded(touch, active, paused);
                    break;
            }

            if (_touches.TryGetValue(touch.touchId, out TouchState held) && held.FastDropEngaged)
            {
                fastDrop = true;
            }
        }

        PruneVanishedTouches();

        if (active != null)
        {
            active.SetFastDrop(fastDrop && !paused);
        }
    }

    private void HandleBegan(Touch touch)
    {
        Vector2 pos = touch.screenPosition;
        if (pos.y > Screen.height * (1f - HudTopIgnoreFraction)) return; // HUD strip

        _touches[touch.touchId] = new TouchState
        {
            StartPos = pos,
            StartTime = Time.unscaledTime
        };
    }

    private void HandleHeld(Touch touch, BlockController active)
    {
        if (!_touches.TryGetValue(touch.touchId, out TouchState state)) return;

        Vector2 delta = touch.screenPosition - state.StartPos;
        float dpi = ScreenDpi();

        if (!state.IsDrag &&
            (delta.magnitude > TapMaxMoveInches * dpi || Time.unscaledTime - state.StartTime > TapMaxDuration))
        {
            state.IsDrag = true;
            if (_dragOwnerId == -1)
            {
                _dragOwnerId = touch.touchId;
                state.OwnsDrag = true;
            }
        }

        if (!state.OwnsDrag || active == null) return;

        // Position-based column stepping: finger offset maps 1:1 to grid columns.
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

        // Fast drop: a clear downward pull (dominant axis) engages it until release.
        if (!state.FastDropEngaged &&
            -delta.y > DropEngageInches * dpi &&
            Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
        {
            state.FastDropEngaged = true;
        }
    }

    private void HandleEnded(Touch touch, BlockController active, bool paused)
    {
        if (!_touches.TryGetValue(touch.touchId, out TouchState state)) return;
        _touches.Remove(touch.touchId);
        if (state.OwnsDrag) _dragOwnerId = -1;

        if (paused || active == null) return;

        float duration = Time.unscaledTime - state.StartTime;
        if (state.IsDrag)
        {
            // A quick, short, clearly-downward swipe that ends = flick: latch full-speed
            // descent all the way down, no held finger needed.
            Vector2 delta = touch.screenPosition - state.StartPos;
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
    private void PruneVanishedTouches()
    {
        if (_touches.Count == 0) return;

        _staleIds.Clear();
        foreach (int id in _touches.Keys)
        {
            bool seen = false;
            foreach (Touch touch in Touch.activeTouches)
            {
                if (touch.touchId == id) { seen = true; break; }
            }
            if (!seen) _staleIds.Add(id);
        }
        foreach (int id in _staleIds)
        {
            if (_touches[id].OwnsDrag) _dragOwnerId = -1;
            _touches.Remove(id);
        }
    }

    private Vector2 LastKnownPosition(TouchState state)
    {
        foreach (Touch touch in Touch.activeTouches)
        {
            if (_touches.TryGetValue(touch.touchId, out TouchState s) && s == state)
            {
                return touch.screenPosition;
            }
        }
        return state.StartPos;
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
