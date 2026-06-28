using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Shared lifecycle for runtime-only ability sessions. It centralizes the "one active instance",
/// ActivePieceSession enter/exit, and OnDestroy cleanup rules that targeting/sequence abilities
/// otherwise have to remember by convention - plus the pointer-picking / easing helpers they all
/// share, so a new session inherits the safety and the plumbing instead of copy-pasting them.
///
/// Whether a session seizes the active falling piece is declared ONCE, as <see cref="SeizesActivePiece"/>,
/// instead of being passed (and possibly mis-passed) at every Begin call - so ActivePieceSession's
/// enter/exit pairing can't be forgotten or get out of balance.
/// </summary>
public abstract class AbilitySessionBase : MonoBehaviour, IAbilitySession
{
    private static readonly HashSet<Type> ActiveSessionTypes = new HashSet<Type>();

    public bool IsFinishing { get; private set; }
    protected bool IsDestroying { get; private set; }
    private bool _enteredActivePieceSession;

    /// <summary>True if this session takes over the active falling piece (Fission/Overdraw/Zap/Magma
    /// melt) and must therefore gate other active-piece consumables out for its lifetime. Extract and
    /// other "presentation only" sessions return false.</summary>
    protected abstract bool SeizesActivePiece { get; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        ActiveSessionTypes.Clear();
    }

    public abstract void CancelSession();

    protected static bool IsSessionActive<T>() where T : AbilitySessionBase
        => ActiveSessionTypes.Contains(typeof(T));

    protected static void ResetSessionState<T>() where T : AbilitySessionBase
    {
        ActiveSessionTypes.Remove(typeof(T));
    }

    /// <summary>Begin this session's lifecycle. Registers the "one active instance" guard (returns
    /// false if one is already running) and, when <see cref="SeizesActivePiece"/> is true, enters
    /// the ActivePieceSession gate - paired with the matching Exit in <see cref="CompleteSessionLifecycle"/>.
    /// Uses the concrete runtime type, so it always matches the type the static IsActive accessors check.</summary>
    protected bool BeginSessionLifecycle()
    {
        if (!ActiveSessionTypes.Add(GetType())) return false;

        if (SeizesActivePiece)
        {
            ActivePieceSession.Enter();
            _enteredActivePieceSession = true;
        }
        return true;
    }

    protected bool BeginFinish()
    {
        if (IsFinishing) return false;
        IsFinishing = true;
        return true;
    }

    protected void CompleteSessionLifecycle(bool destroySelf = true)
    {
        ActiveSessionTypes.Remove(GetType());
        if (_enteredActivePieceSession)
        {
            ActivePieceSession.Exit();
            _enteredActivePieceSession = false;
        }

        if (destroySelf) Destroy(gameObject);
    }

    protected virtual void OnDestroy()
    {
        if (IsFinishing) return;

        IsDestroying = true;
        CancelSession();
        IsDestroying = false;
    }

    // ---- Shared session helpers --------------------------------------------------------------

    /// <summary>Smoothstep easing (0..1). Used by every session's open/fly-in/fade tweens.</summary>
    protected static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    /// <summary>True if the pointer/touch is over a UI element (so a tap on the HUD doesn't also
    /// select a targeted block). pointerId -1 = mouse; otherwise an EnhancedTouch touchId.</summary>
    protected static bool IsPointerOverUi(int pointerId = -1)
    {
        if (EventSystem.current == null) return false;
        return pointerId >= 0
            ? EventSystem.current.IsPointerOverGameObject(pointerId)
            : EventSystem.current.IsPointerOverGameObject();
    }

    /// <summary>This frame's fresh selection point (mouse click in editor/standalone, or the first
    /// began touch), in screen space, ignoring taps that landed on UI. False if there was none.</summary>
    protected static bool TryGetSelectionPoint(out Vector2 screenPoint)
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPoint = Mouse.current.position.ReadValue();
            if (!IsPointerOverUi()) return true;
        }
#endif

        foreach (Touch touch in Touch.activeTouches)
        {
            if (touch.phase != UnityEngine.InputSystem.TouchPhase.Began) continue;
            screenPoint = touch.screenPosition;
            if (!IsPointerOverUi(touch.touchId)) return true;
        }

        screenPoint = default;
        return false;
    }
}
