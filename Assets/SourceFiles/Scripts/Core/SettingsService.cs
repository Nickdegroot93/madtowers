using System;
using UnityEngine;

/// <summary>
/// Single source of truth for user settings, persisted via PlayerPrefs. Owns the audio channel
/// (music / sfx volume, mute-all) and graphics (frame rate, visual effects, screen shake); future
/// settings tabs add their keys the same way.
/// Any write raises <see cref="Changed"/> so live consumers (e.g. MusicPlayer) re-read, and the
/// players read <see cref="EffectiveMusic"/> / <see cref="EffectiveSfx"/> which fold in mute-all.
/// See SETTINGS.md §6. PlayerPrefs is flushed by <see cref="Save"/> (Unity also auto-saves on
/// quit / app-pause), so callers Save() on a slider release or when leaving the settings screen.
/// </summary>
public static class SettingsService
{
    /// <summary>Raised after any setting changes (already persisted in memory).</summary>
    public static event Action Changed;

    private const string MusicKey = "settings.audio.music";
    private const string SfxKey = "settings.audio.sfx";
    private const string MuteKey = "settings.audio.muteAll";

    private const float DefaultMusic = 0.8f;
    private const float DefaultSfx = 0.9f;

    private const string FrameRateKey = "settings.graphics.frameRate";
    private const string VisualEffectsKey = "settings.graphics.visualEffects";
    private const string ScreenShakeKey = "settings.graphics.screenShake";
    private const string HapticsKey = "settings.haptics.enabled";
    private const string HudLayoutKey = "settings.controls.hudLayout";

    private const string AlertsKey = "settings.alerts.enabled";

    private const int DefaultFrameRate = 60;

    private static float _music;
    private static float _sfx;
    private static bool _muteAll;
    private static int _frameRate;
    private static bool _visualEffects;
    private static bool _screenShake;
    private static bool _haptics;
    private static HudLayout _hud;
    private static bool _alerts;
    private static bool _loaded;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, DefaultMusic));
        _sfx = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, DefaultSfx));
        _muteAll = PlayerPrefs.GetInt(MuteKey, 0) != 0;
        _frameRate = PlayerPrefs.GetInt(FrameRateKey, DefaultFrameRate);
        _visualEffects = PlayerPrefs.GetInt(VisualEffectsKey, 1) != 0;
        _screenShake = PlayerPrefs.GetInt(ScreenShakeKey, 1) != 0;
        _haptics = PlayerPrefs.GetInt(HapticsKey, 1) != 0;
        _hud = HudLayout.FromJsonOrDefault(PlayerPrefs.GetString(HudLayoutKey, string.Empty));
        // Defaults ON in-app: the OS permission dialog (never shown at boot) is the
        // real gate, so this only ever matters after an explicit player yes.
        _alerts = PlayerPrefs.GetInt(AlertsKey, 1) != 0;
        _loaded = true;
    }

    public static float MusicVolume
    {
        get { EnsureLoaded(); return _music; }
        set
        {
            EnsureLoaded();
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(_music, value)) return;
            _music = value;
            PlayerPrefs.SetFloat(MusicKey, value);
            Changed?.Invoke();
        }
    }

    public static float SfxVolume
    {
        get { EnsureLoaded(); return _sfx; }
        set
        {
            EnsureLoaded();
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(_sfx, value)) return;
            _sfx = value;
            PlayerPrefs.SetFloat(SfxKey, value);
            Changed?.Invoke();
        }
    }

    public static bool MuteAll
    {
        get { EnsureLoaded(); return _muteAll; }
        set
        {
            EnsureLoaded();
            if (_muteAll == value) return;
            _muteAll = value;
            PlayerPrefs.SetInt(MuteKey, value ? 1 : 0);
            Changed?.Invoke();
        }
    }

    /// <summary>Music level the player should actually use (0 while muted).</summary>
    public static float EffectiveMusic { get { EnsureLoaded(); return _muteAll ? 0f : _music; } }

    /// <summary>SFX level the player should actually use (0 while muted).</summary>
    public static float EffectiveSfx { get { EnsureLoaded(); return _muteAll ? 0f : _sfx; } }

    // ---- Graphics (applied at their render chokepoints; see GraphicsSettingsApplier + GRAPHICS.md) ----

    /// <summary>Frame-rate cap (e.g. 30 / 60 / 120), applied via Application.targetFrameRate.</summary>
    public static int TargetFrameRate
    {
        get { EnsureLoaded(); return _frameRate; }
        set
        {
            EnsureLoaded();
            if (_frameRate == value) return;
            _frameRate = value;
            PlayerPrefs.SetInt(FrameRateKey, value);
            Changed?.Invoke();
        }
    }

    /// <summary>Post-processing (bloom/glow) and decorative prefab VFX on/off.</summary>
    public static bool VisualEffects
    {
        get { EnsureLoaded(); return _visualEffects; }
        set
        {
            EnsureLoaded();
            if (_visualEffects == value) return;
            _visualEffects = value;
            PlayerPrefs.SetInt(VisualEffectsKey, value ? 1 : 0);
            Changed?.Invoke();
        }
    }

    /// <summary>Camera screen-shake on impacts on/off (enforced in TowerCameraController.Impact).</summary>
    public static bool ScreenShake
    {
        get { EnsureLoaded(); return _screenShake; }
        set
        {
            EnsureLoaded();
            if (_screenShake == value) return;
            _screenShake = value;
            PlayerPrefs.SetInt(ScreenShakeKey, value ? 1 : 0);
            Changed?.Invoke();
        }
    }

    /// <summary>Vibration feedback on impacts on/off (enforced in Haptics, the single wrapper).</summary>
    public static bool HapticsEnabled
    {
        get { EnsureLoaded(); return _haptics; }
        set
        {
            EnsureLoaded();
            if (_haptics == value) return;
            _haptics = value;
            PlayerPrefs.SetInt(HapticsKey, value ? 1 : 0);
            Changed?.Invoke();
        }
    }

    // ---- Alerts (local notifications; enforced in NotificationScheduler, which simply
    // never queues anything while this says no). ONE toggle for everything, by design
    // (Nick 2026-08-12, second round: per-type toggles beg a question nobody asks -
    // "do you want notifications, yes or no" is the whole setting). ----

    /// <summary>All local notifications: lives refilled, comeback nudges.</summary>
    public static bool AlertsEnabled
    {
        get { EnsureLoaded(); return _alerts; }
        set
        {
            EnsureLoaded();
            if (_alerts == value) return;
            _alerts = value;
            PlayerPrefs.SetInt(AlertsKey, value ? 1 : 0);
            Changed?.Invoke();
        }
    }

    // ---- Controls / HUD layout (read by UIManager nudge guides + AbilityHud slots; edited by the
    // layout editor). See HudLayout + SETTINGS.md. ----

    /// <summary>The full HUD layout. Treat as read-only for display; commit edits via
    /// <see cref="ApplyHudLayout"/> so they persist and notify.</summary>
    public static HudLayout Hud { get { EnsureLoaded(); return _hud; } }

    /// <summary>Opacity (0..1) of the nudge corner guides — visual only; the touch zones are fixed.
    /// Written as part of the whole layout via <see cref="ApplyHudLayout"/> (the editor).</summary>
    public static float NudgeGuideOpacity { get { EnsureLoaded(); return _hud.nudgeGuideOpacity; } }

    /// <summary>Replace the whole HUD layout (the editor commits its draft here).</summary>
    public static void ApplyHudLayout(HudLayout layout)
    {
        EnsureLoaded();
        if (layout == null) return;
        _hud = layout;
        PersistHud();
        Changed?.Invoke();
    }

    private static void PersistHud() => PlayerPrefs.SetString(HudLayoutKey, JsonUtility.ToJson(_hud));

    /// <summary>Flush in-memory PlayerPrefs writes to disk. Unity also auto-saves on quit/pause,
    /// so this is belt-and-suspenders for slider releases / leaving the settings screen.</summary>
    public static void Save() => PlayerPrefs.Save();
}
