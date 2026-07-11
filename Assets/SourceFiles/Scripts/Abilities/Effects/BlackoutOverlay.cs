using UnityEngine;

/// <summary>
/// The Blackout game state's on-screen half: the district loses power. Spawned and torn down
/// by StatusFieldController via Status_Blackout's ScreenEffect (the state owns its look - any
/// ability, scheduler or future brick that applies the status shares this), it covers the
/// camera with the BlackoutOverlay shader: a pitch-black curtain with a single lantern hole
/// riding the falling piece - outside it the tower is a MEMORY (you study it during the slow
/// power-down). The HUD lives on the screen-space canvas and stays readable by construction.
///
/// Lifecycle: fades IN over fadeInSeconds when the status starts; fades OUT on its own when
/// the status is about to end (GetRemaining) or has ended (StatusFieldController destroys the
/// prefab FadeOutSeconds=1.2s after expiry - our fade must fit inside that window).
/// </summary>
public sealed class BlackoutOverlay : MonoBehaviour
{
    [Tooltip("The status this overlay visualises - polled for remaining time so the relight starts BEFORE the state ends instead of the curtain popping off.")]
    [SerializeField] private StatusEffectDefinition status;
    [Header("Look (the playtest dials)")]
    [Tooltip("1 = pitch black outside the lantern - the tower must be a MEMORY, not a silhouette. Anything below ~0.998 still ghosts through after gamma (1.5% linear transmission reads as ~12% on screen); 0.93 and 0.985 were both playtested and rejected.")]
    [Range(0f, 1f)]
    [SerializeField] private float darkness = 1f;
    [SerializeField] private float lanternRadius = 7f;
    [Tooltip("Gentle breathing of the lantern radius - a live flame, not a spotlight.")]
    [SerializeField] private float lanternFlicker = 0.18f;
    [Header("Timing")]
    [Tooltip("The memorize window: the power-down is deliberately SLOW so the player sees it coming and studies the tower before the dark hits.")]
    [SerializeField] private float fadeInSeconds = 3f;
    [Tooltip("Must stay under StatusFieldController.FadeOutSeconds (1.2) so the relight completes before the prefab is destroyed.")]
    [SerializeField] private float fadeOutSeconds = 1.1f;

    private const int SortingOrder = 200; // above all world FX; below ability sessions (220+) and the UI canvas

    private Camera _camera;
    private StatusEffects _statusRuntime;
    private SpriteRenderer _curtain;
    private Material _material;
    private Vector2 _lantern;
    private bool _lanternSeeded;
    private float _fade;
    private bool _relighting;

    private void Awake()
    {
        _camera = Camera.main;
        if (GameManager.Instance != null)
        {
            _statusRuntime = GameManager.Instance.GetComponent<StatusEffects>();
        }

        var go = new GameObject("BlackoutCurtain");
        go.transform.SetParent(transform, false);
        _curtain = go.AddComponent<SpriteRenderer>();
        _curtain.sprite = RuntimeSprites.Square();
        _material = new Material(Resources.Load<Shader>("BlackoutOverlay"));
        _curtain.sharedMaterial = _material;
        _curtain.sortingOrder = SortingOrder;

        SfxPlayer.Play("blackout_in", 0.9f);
    }

    private void LateUpdate()
    {
        if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
        if (_camera == null || _curtain == null) return;

        // Cover the view (with overscan) wherever the camera pans/zooms.
        float h = _camera.orthographicSize * 2f;
        float w = h * _camera.aspect;
        _curtain.transform.position = new Vector3(_camera.transform.position.x, _camera.transform.position.y, 0f);
        _curtain.transform.localScale = new Vector3(w * 1.05f, h * 1.05f, 1f);

        // The lantern rides the falling piece; between pieces it drifts to the spawn area
        // (top-centre) so the relight of a fresh piece never teleports the light.
        BlockController active = BlockController.ActiveControlled;
        Vector2 target;
        if (active != null && active.TryGetWorldBounds(out Bounds bounds))
        {
            target = bounds.center;
        }
        else
        {
            target = new Vector2(_camera.transform.position.x,
                _camera.transform.position.y + _camera.orthographicSize * 0.75f);
        }
        if (!_lanternSeeded) { _lantern = target; _lanternSeeded = true; }
        _lantern = Vector2.Lerp(_lantern, target, 1f - Mathf.Exp(-8f * Time.deltaTime));

        // The controller lives on the GameManager, so Instance is normally set before we
        // spawn - but the status is designed to be applied from anywhere, so re-acquire
        // lazily rather than assume the Awake-time order.
        if (_statusRuntime == null && GameManager.Instance != null)
        {
            _statusRuntime = GameManager.Instance.GetComponent<StatusEffects>();
        }

        // Fade: in at start, out when the state is about to end (or was cleared early, or
        // the run ended - a game-over must relight gracefully, not pop off frozen-black
        // when StatusFieldController's teardown grace expires).
        float remaining = _statusRuntime != null && status != null ? _statusRuntime.GetRemaining(status) : float.MaxValue;
        bool over = GameManager.Instance != null && GameManager.Instance.isGameOver;
        bool ending = over || remaining <= fadeOutSeconds ||
                      (_statusRuntime != null && status != null && !_statusRuntime.IsActive(status));
        if (ending && !_relighting)
        {
            _relighting = true;
            SfxPlayer.Play("blackout_out", 0.9f);
        }
        else if (!ending && _relighting && remaining > fadeOutSeconds + 0.5f)
        {
            // The status was refreshed/re-applied mid-relight (RefreshDuration stacking, or
            // a second source) - without this the one-way latch left the curtain permanently
            // clear while the blackout was still active. Re-arm and darken again.
            _relighting = false;
        }
        float fadeStep = Time.deltaTime / (_relighting ? Mathf.Max(0.1f, fadeOutSeconds) : Mathf.Max(0.1f, fadeInSeconds));
        _fade = Mathf.Clamp01(_fade + (_relighting ? -fadeStep : fadeStep));

        float flicker = 1f + lanternFlicker * 0.5f *
                        (Mathf.Sin(Time.time * 7.3f) * 0.6f + Mathf.Sin(Time.time * 13.1f) * 0.4f) * 0.2f;
        _material.SetFloat("_Darkness", darkness);
        _material.SetFloat("_Fade", _fade);
        _material.SetVector("_LanternPos", new Vector4(_lantern.x, _lantern.y, 0f, 0f));
        _material.SetFloat("_LanternRadius", lanternRadius * flicker);
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }
}
