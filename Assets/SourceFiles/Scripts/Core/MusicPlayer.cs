using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Chapter soundtrack player. A chapter has 1..N tracks: a RANDOM one opens, then the
/// rotation is fixed (A, B, A, B...) while the level is alive. Lives across scene
/// loads, so restarting a level in the same chapter never restarts the music - it only
/// changes when the chapter changes. Unaffected by pause (audio ignores timeScale).
/// Stops on game over; a retry starts the rotation fresh (random opener again).
/// GameManager points it at the chapter on every level load.
/// </summary>
public class MusicPlayer : MonoBehaviour
{
    private const float Volume = 0.55f;

    private static MusicPlayer _instance;

    private AudioSource _source;
    private ChapterDefinition _chapter;
    private IReadOnlyList<AudioClip> _playlist;
    private int _trackIndex;
    private bool _halted; // game over: silence until the next PlayForChapter

    /// <summary>Play the chapter's playlist; keeps playing seamlessly if it's already on.</summary>
    public static void PlayForChapter(ChapterDefinition chapter)
    {
        EnsureInstance();

        // Idempotent resubscribe every level load (-= then +=): GameEvents.Reset clears
        // handlers at play-mode start, and this instance can outlive that in the editor.
        GameEvents.GameOver -= HandleGameOver;
        GameEvents.GameOver += HandleGameOver;

        // Same chapter and still playing: don't interrupt. (After a game-over halt, the
        // retry falls through and starts the rotation fresh.)
        if (_instance._chapter == chapter && _instance._source.isPlaying) return;

        _instance._chapter = chapter;
        _instance._playlist = chapter != null ? chapter.MusicPlaylist : null;
        _instance._halted = false;
        // Any track may open the rotation; the order is fixed after that.
        _instance._trackIndex = _instance._playlist != null && _instance._playlist.Count > 1
            ? Random.Range(0, _instance._playlist.Count)
            : 0;
        _instance._source.Stop();
        _instance.PlayCurrentTrack();
    }

    private void Update()
    {
        if (_halted || _playlist == null || _playlist.Count == 0 || _source.isPlaying) return;

        // Track finished: advance and wrap (A -> B -> A -> ...).
        _trackIndex = (_trackIndex + 1) % _playlist.Count;
        PlayCurrentTrack();
    }

    private static void HandleGameOver(int score, float maxHeight)
    {
        if (_instance == null) return;
        _instance._halted = true;
        _instance._source.Stop();
        // Future: a game-over jingle (shared by all chapters) plays here.
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
