using UnityEngine;

/// <summary>
/// The one place that resolves the run-life heart art (SHOP.md §2: run lives have their own
/// icon language). Two states: FULL (a life you hold) and EMPTY (the socket a lost life
/// leaves behind). Default source is the PROCEDURAL bevel-language bake
/// (RuntimeSprites.HeartFull/HeartEmpty - Nick's call: code-owned like the bricks, easy to
/// animate). Hand-made assets still win if they're ever dropped in as
/// Resources/Menu/heart_full + heart_empty - no code change needed.
/// </summary>
public static class HeartSprites
{
    private static Sprite _full;
    private static Sprite _empty;
    private static bool _loaded;

    /// <summary>Always true now that the procedural socket exists; kept so callers that
    /// tint-faked an empty state stay compilable and honest.</summary>
    public static bool HasDedicatedEmpty
    {
        get { EnsureLoaded(); return _empty != null; }
    }

    public static Sprite Full()
    {
        EnsureLoaded();
        return _full;
    }

    public static Sprite Empty()
    {
        EnsureLoaded();
        return _empty != null ? _empty : _full;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _full = Resources.Load<Sprite>("Menu/heart_full");    // hand-made override, if ever
        _empty = Resources.Load<Sprite>("Menu/heart_empty");
        if (_full == null) _full = RuntimeSprites.HeartFull();
        if (_empty == null) _empty = RuntimeSprites.HeartEmpty();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        _full = null;
        _empty = null;
        _loaded = false;
    }
}
