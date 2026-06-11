using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Theme soundtrack player: plays the active theme's playlist in order and loops the
/// whole sequence (A, B, A, B...). Lives across scene loads, so restarting a level in
/// the same theme never restarts the music - it only changes when the theme changes.
/// Unaffected by pause (audio ignores timeScale), which is the cozy choice.
/// GameManager points it at the theme on every level load.
/// </summary>
public class MusicPlayer : MonoBehaviour
{
    private const float Volume = 0.55f;

    private static MusicPlayer _instance;

    private AudioSource _source;
    private ThemeDefinition _theme;
    private IReadOnlyList<AudioClip> _playlist;
    private int _trackIndex;

    /// <summary>Play the theme's playlist; keeps playing seamlessly if it's already on.</summary>
    public static void PlayForTheme(ThemeDefinition theme)
    {
        EnsureInstance();

        if (_instance._theme == theme) return; // same theme: don't interrupt
        _instance._theme = theme;
        _instance._playlist = theme != null ? theme.MusicPlaylist : null;
        _instance._trackIndex = 0;
        _instance._source.Stop();
        _instance.PlayCurrentTrack();
    }

    private void Update()
    {
        if (_playlist == null || _playlist.Count == 0 || _source.isPlaying) return;

        // Track finished: advance and wrap (A -> B -> A -> ...).
        _trackIndex = (_trackIndex + 1) % _playlist.Count;
        PlayCurrentTrack();
    }

    private void PlayCurrentTrack()
    {
        if (_playlist == null || _playlist.Count == 0) return;

        AudioClip clip = _playlist[_trackIndex];
        if (clip == null) return;

        _source.clip = clip;
        // A single track loops natively (gapless); multi-track playlists advance in Update.
        _source.loop = _playlist.Count == 1;
        _source.Play();
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;

        GameObject host = new GameObject("MusicPlayer");
        DontDestroyOnLoad(host);
        _instance = host.AddComponent<MusicPlayer>();
        _instance._source = host.AddComponent<AudioSource>();
        _instance._source.playOnAwake = false;
        _instance._source.spatialBlend = 0f;
        _instance._source.volume = Volume;
    }
}
