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

    private static AudioSource[] _pool;
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
        source.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    /// <summary>Play a random numbered variant: name_01 .. name_NN.</summary>
    public static void PlayVariant(string baseName, int variantCount, float volume = 1f, float pitchJitter = 0f)
    {
        int pick = Random.Range(1, Mathf.Max(1, variantCount) + 1);
        Play($"{baseName}_{pick:00}", volume, pitchJitter);
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

    private static void EnsurePool()
    {
        if (_pool != null && _pool.Length > 0 && _pool[0] != null) return;

        GameObject host = new GameObject("SfxPlayer");
        Object.DontDestroyOnLoad(host);
        _pool = new AudioSource[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // plain 2D
            _pool[i] = source;
        }
    }
}
