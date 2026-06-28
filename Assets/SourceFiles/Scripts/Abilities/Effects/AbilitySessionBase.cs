using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared lifecycle for runtime-only ability sessions. It centralizes the "one active instance",
/// ActivePieceSession enter/exit, and OnDestroy cleanup rules that targeting/sequence abilities
/// otherwise have to remember by convention.
/// </summary>
public abstract class AbilitySessionBase : MonoBehaviour, IAbilitySession
{
    private static readonly HashSet<Type> ActiveSessionTypes = new HashSet<Type>();

    public bool IsFinishing { get; private set; }
    protected bool UsesActivePieceSession { get; private set; }
    protected bool IsDestroying { get; private set; }

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

    protected bool BeginSessionLifecycle<T>(bool usesActivePieceSession) where T : AbilitySessionBase
    {
        Type type = typeof(T);
        if (!ActiveSessionTypes.Add(type)) return false;

        UsesActivePieceSession = usesActivePieceSession;
        if (usesActivePieceSession) ActivePieceSession.Enter();
        return true;
    }

    protected bool BeginFinish()
    {
        if (IsFinishing) return false;
        IsFinishing = true;
        return true;
    }

    protected void CompleteSessionLifecycle<T>(bool destroySelf = true) where T : AbilitySessionBase
    {
        ActiveSessionTypes.Remove(typeof(T));
        if (UsesActivePieceSession)
        {
            ActivePieceSession.Exit();
            UsesActivePieceSession = false;
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
}
