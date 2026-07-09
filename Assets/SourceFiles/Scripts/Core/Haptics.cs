using UnityEngine;

/// <summary>
/// The single wrapper for vibration feedback (JUICE.md): every haptic in the game routes
/// through here, so the SettingsService.HapticsEnabled gate lives at one chokepoint, exactly
/// like SfxPlayer/EffectiveSfx and TowerCameraController/ScreenShake.
///
/// Android plays real amplitude-scaled transients via VibrationEffect (API 26+; older devices
/// fall back to a plain short buzz). iOS is a NO-OP until the open-source Nice Vibrations
/// package is imported (JUICE.md §4 follow-up) - wire its transient call into PlayTransient.
/// Editor/desktop are no-ops.
/// </summary>
public static class Haptics
{
    // Rapid landings must not blur into one long buzz - transients closer together than
    // this are dropped (the visual/audio feedback still fires per landing).
    private const float MinIntervalSeconds = 0.05f;
    private static float _lastPlayTime = -1f;

    /// <summary>Light tick: soft landings, UI confirms.</summary>
    public static void Light() => PlayTransient(0.35f, 15);

    /// <summary>Medium thump: ordinary hard landings.</summary>
    public static void Medium() => PlayTransient(0.6f, 20);

    /// <summary>Heavy hit: flick slams, future Tier 2+ celebrations.</summary>
    public static void Heavy() => PlayTransient(1f, 30);

    /// <summary>Amplitude-scaled transient. intensity01 maps to vibration strength where the
    /// platform supports it; durationMs stays short (10-40) so it reads as a tap, not a buzz.</summary>
    public static void PlayTransient(float intensity01, int durationMs)
    {
        if (!SettingsService.HapticsEnabled) return;

        // Unscaled time: haptics accompany landings that can happen during slow-mo windows.
        if (_lastPlayTime >= 0f && Time.unscaledTime - _lastPlayTime < MinIntervalSeconds) return;
        _lastPlayTime = Time.unscaledTime;

#if UNITY_ANDROID && !UNITY_EDITOR
        PlayAndroid(Mathf.Clamp01(intensity01), Mathf.Clamp(durationMs, 5, 100));
#endif
        // iOS: intentionally silent until Nice Vibrations is imported - Handheld.Vibrate()
        // is a fixed long buzz there and would feel worse than nothing.
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaObject _vibrator;
    private static AndroidJavaClass _vibrationEffect;
    private static bool _vibratorResolved;

    private static void PlayAndroid(float intensity01, int durationMs)
    {
        try
        {
            if (!_vibratorResolved)
            {
                _vibratorResolved = true;
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
                _vibrationEffect = new AndroidJavaClass("android.os.VibrationEffect");
            }
            if (_vibrator == null) return;

            if (_vibrationEffect != null)
            {
                int amplitude = Mathf.Clamp(Mathf.RoundToInt(intensity01 * 255f), 1, 255);
                using (AndroidJavaObject effect = _vibrationEffect.CallStatic<AndroidJavaObject>(
                           "createOneShot", (long)durationMs, amplitude))
                {
                    _vibrator.Call("vibrate", effect);
                }
            }
            else
            {
                _vibrator.Call("vibrate", (long)durationMs); // pre-API-26 fallback
            }
        }
        catch (System.Exception)
        {
            // A device without a vibrator (or a changed API surface) must never break a
            // landing - haptics are garnish. Disable further attempts this session.
            _vibrator = null;
            _vibrationEffect = null;
        }
    }
#endif
}
