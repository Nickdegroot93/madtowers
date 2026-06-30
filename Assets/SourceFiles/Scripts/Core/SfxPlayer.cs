using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minimal one-shot sound player: a small pool of 2D AudioSources on a persistent
/// object, clips loaded once from Resources/Audio/Sfx and cached. Pitch jitter keeps
/// repeated sounds (landings!) from feeling machine-gunned. Clips are synthesized by
/// Tools/generate_sfx.py - regenerate and Unity hot-reloads them.
/// </summary>
public static class SfxPlayer
{
    private const int PoolSize = 6;

    private static GameObject _host;
    private static AudioSource[] _pool;
    private static AudioSource _loop; // dedicated, stoppable source for sustained sounds (e.g. countdown)
    private static float _loopBaseVolume = 1f; // the loop's per-call level, before the SFX setting
    private static int _next;
    private static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

    /// <summary>Play one-shot by name (file in Resources/Audio/Sfx, no extension).</summary>
    public static void Play(string name, float volume = 1f, float pitchJitter = 0f)
    {
        AudioClip clip = LoadClip(name);
        if (clip == null) return;

        EnsurePool();
        AudioSource source = _pool[_next];
        _next = (_next + 1) % _pool.Length;

        source.pitch = 1f + (pitchJitter > 0f ? Random.Range(-pitchJitter, pitchJitter) : 0f);
        // Per-call volume scaled by the user's master SFX level (0 while muted).
        source.PlayOneShot(clip, Mathf.Clamp01(volume) * SettingsService.EffectiveSfx);
    }

    /// <summary>Play a random numbered variant: name_01 .. name_NN.</summary>
    public static void PlayVariant(string baseName, int variantCount, float volume = 1f, float pitchJitter = 0f)
    {
        int pick = Random.Range(1, Mathf.Max(1, variantCount) + 1);
        Play($"{baseName}_{pick:00}", volume, pitchJitter);
    }

    /// <summary>Start a sustained, looping clip on a dedicated source. Call <see cref="StopLoop"/>
    /// to end it (e.g. a level-finish countdown that runs while the timer counts down). Idempotent:
    /// starting the same clip again while it plays is a no-op so it doesn't restart each frame.</summary>
    public static void PlayLoop(string name, float volume = 1f)
    {
        AudioClip clip = LoadClip(name);
        if (clip == null) return;

        EnsureLoop();
        _loopBaseVolume = Mathf.Clamp01(volume);
        ApplyLoopVolume(); // refresh volume even when this clip is already looping
        if (_loop.isPlaying && _loop.clip == clip) return;
        _loop.clip = clip;
        _loop.pitch = 1f;
        _loop.loop = true;
        _loop.Play();
    }

    // Re-applies the sustained loop's volume from its base level and the current SFX setting.
    // Subscribed to SettingsService.Changed so the loop honours live mute / slider changes the
    // same way MusicPlayer does (otherwise a held loop would ignore the settings screen).
    private static void ApplyLoopVolume()
    {
        if (_loop != null) _loop.volume = _loopBaseVolume * SettingsService.EffectiveSfx;
    }

    /// <summary>Stop the sustained loop started by <see cref="PlayLoop"/> (safe if nothing is playing).</summary>
    public static void StopLoop()
    {
        if (_loop != null) _loop.Stop();
    }

    private static AudioClip LoadClip(string name)
    {
        if (_clips.TryGetValue(name, out AudioClip cached)) return cached;

        AudioClip clip = Resources.Load<AudioClip>($"Audio/Sfx/{name}");
        if (clip == null)
        {
            Debug.LogWarning($"[Sfx] No clip at Resources/Audio/Sfx/{name}");
        }
        _clips[name] = clip; // cache nulls too - don't re-hit Resources every call
        return clip;
    }

    private static GameObject EnsureHost()
    {
        if (_host == null)
        {
            _host = new GameObject("SfxPlayer");
            Object.DontDestroyOnLoad(_host);
        }
        return _host;
    }

    private static void EnsurePool()
    {
        if (_pool != null && _pool.Length > 0 && _pool[0] != null) return;

        GameObject host = EnsureHost();
        _pool = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // plain 2D
            _pool[i] = source;
        }
    }

    private static void EnsureLoop()
    {
        if (_loop != null) return;
        _loop = EnsureHost().AddComponent<AudioSource>();
        _loop.playOnAwake = false;
        _loop.spatialBlend = 0f;
        // Static class has no teardown hook and fast-enter-playmode keeps statics, so subscribe
        // idempotently (-= then +=) like MusicPlayer does for its GameEvents handler.
        SettingsService.Changed -= ApplyLoopVolume;
        SettingsService.Changed += ApplyLoopVolume;
    }
}
