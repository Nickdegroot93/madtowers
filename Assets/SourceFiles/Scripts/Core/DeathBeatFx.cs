using UnityEngine;

/// <summary>
/// The moment of death, staged (Nick 2026-09-04: the results card used to pop the same frame
/// the last life went - "I don't even know what happened"). Runs the beat between the fatal
/// event and the card, all on unscaled time:
///   - HIT-STOP: the world freezes for a few frames at the fatal contact, so the eye locks on it,
///   - SLOW BEAT: then runs at half speed until the card arrives - the fall reads as deliberate,
///     never as lag, and half a second at half speed shows a full second of consequence,
///   - CAMERA: eases in a few percent ABOUT the death point (the fatal brick stays put, the
///     rest of the world expands),
///   - COLOUR: the post stack drains toward grey and the vignette closes, so the card lands on
///     a dead scene and stays that way until the scene reloads (PostFxController resets),
///   - HUD: every overlay canvas under the modal tier fades out with it (post-processing never
///     touches overlay UI, so a coloured HUD hairline was the one thing left in colour).
/// A loss that still banked an achievement (a new tier, a new best) keeps hit-stop, slow beat
/// and the push-in but skips the drain and the HUD fade - it is not a dark moment.
/// Owns Time.timeScale for the beat with HitStop's discipline: only writes it back if nothing
/// else (a pause opened mid-beat) has taken it. LevelRuntimeController starts it; LifeLossFx /
/// FloodSplashFx suggest the focus point since the GameOver event carries none.
/// </summary>
public sealed class DeathBeatFx : MonoBehaviour
{
    private const float FreezeSeconds = 0.12f;
    private const float FreezeScale = 0.02f;
    private const float SlowScale = 0.5f;
    private const float ZoomIn = 0.04f;         // fraction of the orthographic size
    private const float FocusFreshSeconds = 0.25f;
    private const int HudFadeMaxSortingOrder = 6999;   // pause menu 7000 / results 7100 stay

    private bool _drain;
    private readonly System.Collections.Generic.List<CanvasGroup> _hudGroups =
        new System.Collections.Generic.List<CanvasGroup>();

    private static Vector3 _focus;
    private static float _focusAt = -1f;
    private static DeathBeatFx _active;

    private float _start;
    private float _beat;
    private Vector2 _focusPoint;
    private float _ownedScale;   // the timescale value we last wrote (restore only if unchanged)

    /// <summary>Where the fatal thing happened, in world space (the visible splash point). Read
    /// by <see cref="Play"/> if fresh enough; stale suggestions fall back to the camera centre.</summary>
    public static void SuggestFocus(Vector3 worldPoint)
    {
        _focus = worldPoint;
        _focusAt = Time.unscaledTime;
    }

    /// <param name="drain">False for a loss that banked an achievement: no grey-out, no HUD fade.</param>
    public static void Play(float beatSeconds, bool drain = true)
    {
        if (_active != null) return; // a second game over in one run cannot happen; be safe anyway

        var go = new GameObject("DeathBeatFx");
        var fx = go.AddComponent<DeathBeatFx>();
        _active = fx;
        fx._start = Time.unscaledTime;
        fx._beat = Mathf.Max(FreezeSeconds, beatSeconds);
        fx._drain = drain;
        if (drain) fx.CollectHud();

        Camera cam = TowerCameraController.Camera ?? Camera.main;
        bool fresh = _focusAt >= 0f && Time.unscaledTime - _focusAt < FocusFreshSeconds;
        fx._focusPoint = fresh ? (Vector2)_focus
            : cam != null ? (Vector2)cam.transform.position : Vector2.zero;
        _focusAt = -1f;

        // Take the clock from any micro hit-stop already running (the LifeLossFx punch fired
        // this same frame) - the beat owns it now, and HitStop must not restore to 1 under us.
        HitStop.Cancel();
        if (Time.timeScale > 0f) fx.WriteScale(FreezeScale); // a real pause (0) keeps its clock
        fx.Apply(0f);
    }

    private void Update()
    {
        float age = Time.unscaledTime - _start;
        float t = Mathf.Clamp01(age / _beat);

        if (age >= FreezeSeconds && age < _beat && OwnsClock() && !Mathf.Approximately(Time.timeScale, SlowScale))
        {
            WriteScale(SlowScale);
        }

        Apply(t);

        if (age >= _beat)
        {
            if (OwnsClock()) WriteScale(1f);
            TowerCameraController.SetDeathFocus(_focusPoint, ZoomIn); // hold the push-in under the card
            Destroy(gameObject);
        }
    }

    private void Apply(float t)
    {
        float eased = t * t * (3f - 2f * t);
        TowerCameraController.SetDeathFocus(_focusPoint, ZoomIn * eased);
        if (!_drain) return;
        PostFxController.SetDrain(eased);
        for (int i = 0; i < _hudGroups.Count; i++)
        {
            if (_hudGroups[i] != null) _hudGroups[i].alpha = 1f - eased;
        }
    }

    // Every root overlay canvas below the modal tier in the ACTIVE scene (HUD bar, wave/coin/
    // medal cards, ability slots, banners). They die with the scene, so a held alpha of 0 can
    // never leak into the menu; nothing in this project keeps an overlay canvas alive across loads.
    private void CollectHud()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas c = canvases[i];
            if (c == null || !c.isRootCanvas) continue;
            if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            if (c.sortingOrder > HudFadeMaxSortingOrder) continue;
            CanvasGroup group = c.GetComponent<CanvasGroup>();
            if (group == null) group = c.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;   // a dead HUD takes no taps (the skip tap reads the pointer directly)
            group.blocksRaycasts = false;
            _hudGroups.Add(group);
        }
    }

    private bool OwnsClock() => _ownedScale > 0f && Mathf.Approximately(Time.timeScale, _ownedScale);

    private void WriteScale(float scale)
    {
        Time.timeScale = scale;
        _ownedScale = scale;
    }

    private void OnDestroy()
    {
        if (_active == this) _active = null;
        // Torn down early (scene reload mid-beat): the clock must never stay slow.
        if (OwnsClock() && !Mathf.Approximately(_ownedScale, 1f)) Time.timeScale = 1f;
    }
}
