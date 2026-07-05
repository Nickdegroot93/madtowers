using UnityEngine;

/// <summary>
/// Ambience half of the backdrop: the effects that make a chapter feel alive on top of the
/// static imported art - heat haze (gusting hot-air shimmer) and flybys (rare bird
/// silhouettes crossing the sky). All preset-driven, play-mode only, created/destroyed with
/// the other world elements. New ambience effects belong in this file; the recipe book for
/// tuning them per chapter theme is AMBIENCE.md at the repo root.
/// </summary>
public partial class LevelPresentationController
{
    // Above every imported layer (they stack upward from SpriteBackdropSortingOrder) but
    // well below gameplay, so the shimmer warps scenery only - never the tower.
    private const int HeatHazeSortingOrder = -60;
    // Just in front of the far cloud band, behind mid/near scenery: birds naturally
    // disappear behind treelines and rooftops instead of sliding over them.
    private const int FlybySortingOrder = -87;

    private const float FlybyBaseScale = 0.32f;   // world scale of a flybyScale=1 songbird
    private const float HeatHazeGroundFadeMeters = 30f; // haze is gone this far up the climb

    private SpriteRenderer _heatHaze;
    private Material _heatHazeMaterial;

    private SpriteRenderer[] _flybyBirds;
    private Vector2[] _flybyFormation;   // per-bird offset from the lead bird (x = behind)
    private float[] _flybyFlapPhases;
    private Sprite[] _flybyFrames;
    private bool _flybyAirborne;
    private float _flybyTimer;
    private float _flybyDirection;       // +1 = flying right, -1 = flying left
    private Vector2 _flybyLead;          // lead bird position
    private float _flybyBobPhase;

    // ---- creation / teardown (called from EnsureWorldElements / DestroyWorldElements) ----

    private void CreateAmbienceElements()
    {
        if (_preset.HeatHazeAmount > 0f)
        {
            GameObject haze = new GameObject("HeatHaze");
            haze.transform.SetParent(_worldRoot, false);
            _heatHaze = haze.AddComponent<SpriteRenderer>();
            _heatHaze.sprite = RuntimeSprites.Square();
            _heatHazeMaterial = new Material(Shader.Find("MadTowers/HeatHaze"));
            _heatHaze.sharedMaterial = _heatHazeMaterial;
            _heatHaze.sortingOrder = HeatHazeSortingOrder;
            _heatHaze.enabled = false; // gust envelope enables it when a wave rolls in
        }

        int flockSize = _preset.FlybyFlockSize;
        if (flockSize > 0)
        {
            _flybyFrames = new[] { RuntimeSprites.Bird(0), RuntimeSprites.Bird(1), RuntimeSprites.Bird(2) };
            _flybyBirds = new SpriteRenderer[flockSize];
            _flybyFormation = new Vector2[flockSize];
            _flybyFlapPhases = new float[flockSize];
            float scale = FlybyBaseScale * Mathf.Max(0.1f, _preset.FlybyScale);
            for (int i = 0; i < flockSize; i++)
            {
                GameObject bird = new GameObject($"Flyby{i}");
                bird.transform.SetParent(_worldRoot, false);
                SpriteRenderer sr = bird.AddComponent<SpriteRenderer>();
                sr.sprite = _flybyFrames[1];
                sr.color = _preset.FlybyColor;
                sr.sortingOrder = FlybySortingOrder;
                sr.enabled = false;
                bird.transform.localScale = new Vector3(scale, scale, 1f);
                _flybyBirds[i] = sr;
                _flybyFlapPhases[i] = Random.Range(0f, 4f);
            }
            _flybyAirborne = false;
            // First crossing arrives early so the chapter shows its fauna within the first
            // minute; later crossings use the preset's full interval.
            _flybyTimer = Random.Range(6f, Mathf.Max(8f, _preset.FlybyIntervalSeconds.x));
        }
    }

    private void ResetAmbienceElements()
    {
        // GameObjects die with _worldRoot; the material is ours to destroy or it leaks
        // per level restart (same rule as the generated sky sprites).
        if (_heatHazeMaterial != null)
        {
            if (Application.isPlaying) Destroy(_heatHazeMaterial);
            else DestroyImmediate(_heatHazeMaterial);
        }
        _heatHaze = null;
        _heatHazeMaterial = null;
        _flybyBirds = null;
        _flybyFormation = null;
        _flybyFlapPhases = null;
        _flybyFrames = null;
        _flybyAirborne = false;
    }

    // ---- heat haze -------------------------------------------------------------------------

    private void UpdateHeatHaze()
    {
        if (_heatHaze == null || targetCamera == null) return;

        float strength = HeatHazeStrength();
        if (strength < 0.01f)
        {
            // Procedural shaders here ignore renderer alpha - hide via .enabled, never color.a.
            if (_heatHaze.enabled) _heatHaze.enabled = false;
            return;
        }

        if (!_heatHaze.enabled) _heatHaze.enabled = true;
        Vector3 cam = targetCamera.transform.position;
        _heatHaze.transform.position = new Vector3(cam.x, cam.y, 0f);
        _heatHaze.transform.localScale = new Vector3(CameraHalfWidth * 2.1f, CameraHalfHeight * 2.1f, 1f);
        _heatHazeMaterial.SetFloat("_Strength", strength);
    }

    // Preset amount x gust envelope x ground fade. The envelope is two slow out-of-sync
    // sine waves pushed through a threshold: shimmer waves roll in for a stretch, die to
    // exactly zero, and return - hot air breathes, it doesn't buzz constantly.
    private float HeatHazeStrength()
    {
        float amount = _preset.HeatHazeAmount;
        if (amount <= 0f) return 0f;

        float t = Time.time;
        float gust = 0.6f * Mathf.Sin(t * 0.31f) + 0.4f * Mathf.Sin(t * 0.113f + 2.4f);
        float envelope = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((gust - 0.1f) / 0.55f));

        // Heat haze is a ground phenomenon: gone once the tower climbs into cooler air.
        float groundFade = 1f - Mathf.Clamp01(Climbed(targetCamera.transform.position) / HeatHazeGroundFadeMeters);
        return amount * envelope * groundFade;
    }

    // ---- flybys ----------------------------------------------------------------------------

    private void UpdateFlybys()
    {
        if (_flybyBirds == null || _flybyBirds.Length == 0 || targetCamera == null) return;

        if (!_flybyAirborne)
        {
            _flybyTimer -= Time.deltaTime;
            if (_flybyTimer > 0f) return;
            LaunchFlyby();
            return;
        }

        Vector3 cam = targetCamera.transform.position;
        _flybyLead.x += _flybyDirection * _preset.FlybySpeed * Time.deltaTime;
        _flybyBobPhase += Time.deltaTime;

        // Crossing complete (lead bird plus the whole trailing formation cleared the far
        // edge), or the camera climbed away and left the flock below the view.
        float tailLength = _flybyBirds.Length * 0.8f * _preset.FlybyScale;
        bool crossed = Mathf.Abs(_flybyLead.x - cam.x) > CameraHalfWidth + tailLength + 1.5f
            && (_flybyLead.x - cam.x) * _flybyDirection > 0f;
        bool leftBehind = _flybyLead.y < cam.y - CameraHalfHeight * 1.6f;
        if (crossed || leftBehind)
        {
            LandFlyby();
            return;
        }

        // Bigger birds flap slower; ping-pong the three frames (up, level, down, level).
        float flapRate = 9f / Mathf.Max(0.4f, _preset.FlybyScale);
        for (int i = 0; i < _flybyBirds.Length; i++)
        {
            SpriteRenderer sr = _flybyBirds[i];
            Vector2 offset = _flybyFormation[i];
            float bob = Mathf.Sin(_flybyBobPhase * 0.9f + i * 1.7f) * 0.12f;
            sr.transform.position = new Vector3(
                _flybyLead.x - offset.x * _flybyDirection,
                _flybyLead.y + offset.y + bob,
                0f);

            int step = (int)(Time.time * flapRate + _flybyFlapPhases[i]) % 4;
            sr.sprite = _flybyFrames[step == 3 ? 1 : step];
        }
    }

    private void LaunchFlyby()
    {
        Vector3 cam = targetCamera.transform.position;
        _flybyDirection = Random.value < 0.5f ? -1f : 1f;

        _flybyLead = new Vector2(
            cam.x - _flybyDirection * (CameraHalfWidth + 1f),
            cam.y + Random.Range(-0.1f, 0.65f) * CameraHalfHeight);
        _flybyBobPhase = 0f;

        // Trailing V: birds alternate above/below the lead, each further back, with a
        // little jitter so the formation reads organic rather than stamped.
        float spacing = 0.75f * _preset.FlybyScale;
        for (int i = 0; i < _flybyBirds.Length; i++)
        {
            int rank = (i + 1) / 2;
            float side = i % 2 == 1 ? 1f : -1f;
            _flybyFormation[i] = i == 0
                ? Vector2.zero
                : new Vector2(
                    rank * spacing + Random.Range(-0.12f, 0.12f),
                    side * rank * spacing * 0.45f + Random.Range(-0.08f, 0.08f));
            _flybyBirds[i].enabled = true;
        }
        _flybyAirborne = true;
    }

    private void LandFlyby()
    {
        for (int i = 0; i < _flybyBirds.Length; i++) _flybyBirds[i].enabled = false;
        _flybyAirborne = false;
        _flybyTimer = Random.Range(_preset.FlybyIntervalSeconds.x, _preset.FlybyIntervalSeconds.y);
    }
}
