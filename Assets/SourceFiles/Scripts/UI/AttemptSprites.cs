using UnityEngine;

/// <summary>
/// The one place that resolves the ATTEMPTS icon - the summit flag (Nick's art, 2026-08-30).
/// Attempts (the account meter: tries left + regen timer) and RUN LIVES (the 3 in-run
/// hearts) are different resources and must never share a glyph: the heart belongs to run
/// lives exclusively (HeartSprites/UIManager), the flag-on-a-rock to attempts - the top-bar
/// chip, the pause/game-over meter row, the premium "unlimited" pitches. An empty attempt
/// pip is the flag art dimmed (the Supplies pip precedent); no dedicated empty asset.
/// </summary>
public static class AttemptSprites
{
    /// <summary>The dim tint for a spent attempt pip (paired with the full-color flag).</summary>
    public static readonly Color EmptyTint = new Color(1f, 1f, 1f, 0.18f);

    private static Sprite _flag;
    private static bool _loaded;

    public static Sprite Flag()
    {
        if (!_loaded)
        {
            _loaded = true;
            _flag = Resources.Load<Sprite>("Menu/attempts_flag");
        }
        // Fallback if the art ever goes missing: the heart keeps the meter legible.
        return _flag != null ? _flag : HeartSprites.Full();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        _flag = null;
        _loaded = false;
    }
}
