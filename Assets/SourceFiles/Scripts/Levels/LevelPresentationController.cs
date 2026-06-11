using UnityEngine;

/// <summary>
/// Layered, theme-driven backdrop - no background images. Driven entirely by the active
/// theme's BackdropPreset (or the built-in classic defaults):
///   - sky: a generated vertical gradient glued to the camera, crossfading to a second
///     "high altitude" gradient as the tower climbs
///   - clouds: procedural sprites drifting horizontally, recycled around the camera so
///     coverage is infinite in height
///   - hills: ground-level silhouettes with slight parallax that sink out of view as the
///     camera rises (the ground disappears, only sky and clouds remain)
///   - ambient particles: falling, swaying soft dots (snow, petals, embers - just data)
/// Purely visual; nothing here is read by physics. In edit mode only the sky preview
/// runs - the world elements exist in play mode.
/// </summary>
[ExecuteAlways]
public class LevelPresentationController : MonoBehaviour
{
    [SerializeField] private LevelDefinition levelDefinition;
    [SerializeField] private SpriteRenderer backgroundRenderer;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool fillCameraView = true;
    [SerializeField] private float overscan = 1.12f;
    [SerializeField] private float depthFromCamera = 20f;
    [SerializeField] private int sortingOrder = -100;

    private const int CloudSortingOrder = -90;
    private const int HillFarSortingOrder = -85;
    private const int HillNearSortingOrder = -84;
    private const int ParticleSortingOrder = -80;

    private BackdropPreset _preset;
    private LevelDefinition _presetLevel;
    private bool _presetResolved;

    private Sprite _skyLowSprite;
    private Sprite _skyHighSprite;
    private SpriteRenderer _skyHighRenderer;

    private Transform _worldRoot; // clouds/hills/particles live in world space, not on the camera
    private SpriteRenderer[] _clouds;
    private float[] _cloudSpeeds;
    private SpriteRenderer[] _hills;
    private Transform[] _particles;
    private float[] _particlePhases;

    private void LateUpdate()
    {
        ResolveReferences();
        ResolvePreset();
        UpdateSky();

        if (!Application.isPlaying) return;

        EnsureWorldElements();
        UpdateClouds();
        UpdateHills();
        UpdateParticles();
    }

    private void ResolveReferences()
    {
        if (backgroundRenderer == null) backgroundRenderer = GetComponent<SpriteRenderer>();
        if (targetCamera == null) targetCamera = Camera.main;
    }

    private void ResolvePreset()
    {
        LevelDefinition activeLevel = LevelSelectionState.SelectedLevel != null
            ? LevelSelectionState.SelectedLevel
            : levelDefinition;
        if (_presetResolved && activeLevel == _presetLevel) return;

        _presetLevel = activeLevel;
        _presetResolved = true;
        ThemeDefinition theme = Campaign.FindThemeOf(activeLevel);
        BackdropPreset preset = theme != null ? theme.Backdrop : null;
        SetPreset(preset != null ? preset : BackdropPreset.Defaults);
    }

    private void SetPreset(BackdropPreset preset)
    {
        if (_preset == preset) return;
        _preset = preset;

        DestroySprite(ref _skyLowSprite);
        DestroySprite(ref _skyHighSprite);
        // The gradient lives in the bottom 60% of the quad and is solid top color above;
        // the gentle curve keeps the blend smooth inside that band.
        const float SkyGradientCurve = 0.8f;
        const float SkyTopReachedAt = 0.6f;
        _skyLowSprite = RuntimeSprites.VerticalGradient(_preset.SkyTopLow, _preset.SkyBottomLow,
            SkyGradientCurve, SkyTopReachedAt);
        _skyHighSprite = RuntimeSprites.VerticalGradient(_preset.SkyTopHigh, _preset.SkyBottomHigh,
            SkyGradientCurve, SkyTopReachedAt);
    }

    private static void DestroySprite(ref Sprite sprite)
    {
        if (sprite == null) return;
        DestroyImmediate(sprite.texture);
        DestroyImmediate(sprite);
        sprite = null;
    }

    // Generated sky sprites are HideAndDontSave (they survive scene loads), so they must
    // be destroyed with their owner or every level restart leaks two textures.
    private void OnDestroy()
    {
        DestroySprite(ref _skyLowSprite);
        DestroySprite(ref _skyHighSprite);
    }

    // ---- sky -----------------------------------------------------------------------------

    private void UpdateSky()
    {
        if (backgroundRenderer == null || _preset == null) return;

        if (backgroundRenderer.sprite != _skyLowSprite) backgroundRenderer.sprite = _skyLowSprite;
        if (backgroundRenderer.color != Color.white) backgroundRenderer.color = Color.white;
        backgroundRenderer.sortingOrder = sortingOrder;

        float altitude01 = Altitude01();
        if (Application.isPlaying)
        {
            EnsureSkyHighOverlay();
            if (_skyHighRenderer.sprite != _skyHighSprite) _skyHighRenderer.sprite = _skyHighSprite;
            _skyHighRenderer.color = new Color(1f, 1f, 1f, altitude01);
        }

        if (targetCamera != null)
        {
            targetCamera.backgroundColor = Color.Lerp(_preset.SkyBottomLow, _preset.SkyBottomHigh, altitude01);
        }

        if (fillCameraView) FitBackgroundToCamera();
    }

    private void EnsureSkyHighOverlay()
    {
        if (_skyHighRenderer != null) return;

        GameObject overlay = new GameObject("SkyHighOverlay");
        overlay.transform.SetParent(transform, false);
        _skyHighRenderer = overlay.AddComponent<SpriteRenderer>();
        _skyHighRenderer.sortingOrder = sortingOrder + 1;
    }

    private float Altitude01()
    {
        if (!Application.isPlaying || GameManager.Instance == null || _preset == null) return 0f;
        return Mathf.Clamp01(GameManager.Instance.towerHeight / _preset.AltitudeFadeMeters);
    }

    private void FitBackgroundToCamera()
    {
        if (targetCamera == null || backgroundRenderer.sprite == null) return;

        Vector3 cameraPosition = targetCamera.transform.position;
        transform.position = new Vector3(cameraPosition.x, cameraPosition.y, cameraPosition.z + depthFromCamera);

        if (!targetCamera.orthographic) return;

        Vector2 spriteSize = backgroundRenderer.sprite.bounds.size;
        if (spriteSize.x <= 0f || spriteSize.y <= 0f) return;

        float cameraHeight = targetCamera.orthographicSize * 2f;
        float cameraWidth = cameraHeight * targetCamera.aspect;
        // Non-uniform: each axis fits the view independently. Uniform scaling let the
        // 1px-wide gradient's width requirement blow its height to thousands of units,
        // so the screen only ever saw a sliver of one color.
        transform.localScale = new Vector3(
            cameraWidth / spriteSize.x * Mathf.Max(1f, overscan),
            cameraHeight / spriteSize.y * Mathf.Max(1f, overscan),
            1f);
    }

    // ---- world elements (play mode only) -------------------------------------------------

    private void EnsureWorldElements()
    {
        if (_worldRoot != null || _preset == null || targetCamera == null) return;

        _worldRoot = new GameObject("BackdropElements").transform;

        // Clouds: spread through a band around the camera, recycled as it climbs.
        int cloudCount = _preset.CloudCount;
        _clouds = new SpriteRenderer[cloudCount];
        _cloudSpeeds = new float[cloudCount];
        for (int i = 0; i < cloudCount; i++)
        {
            GameObject cloud = new GameObject($"Cloud{i}");
            cloud.transform.SetParent(_worldRoot, false);
            SpriteRenderer sr = cloud.AddComponent<SpriteRenderer>();
            sr.sprite = _preset.Clouds == BackdropPreset.CloudStyle.Blocky
                ? RuntimeSprites.BlockyCloud(i)
                : RuntimeSprites.Cloud(i);
            sr.color = _preset.CloudColor;
            sr.sortingOrder = CloudSortingOrder;
            float scale = Random.Range(_preset.CloudScaleRange.x, _preset.CloudScaleRange.y);
            cloud.transform.localScale = new Vector3(scale, scale, 1f);
            cloud.transform.position = RandomCloudPosition(initialSpread: true);
            _clouds[i] = sr;
            _cloudSpeeds[i] = _preset.CloudDriftSpeed * Random.Range(0.6f, 1.4f) * (Random.value < 0.5f ? -1f : 1f);
        }

        // Hills: two silhouettes parked at the floor; they leave the frame as you climb.
        if (_preset.HillsEnabled)
        {
            _hills = new SpriteRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                GameObject hill = new GameObject(i == 0 ? "HillFar" : "HillNear");
                hill.transform.SetParent(_worldRoot, false);
                SpriteRenderer sr = hill.AddComponent<SpriteRenderer>();
                sr.sprite = _preset.Hills == BackdropPreset.HillStyle.Mesa
                    ? RuntimeSprites.SteppedMesa(i)
                    : RuntimeSprites.HillSilhouette(i);
                sr.color = i == 0 ? _preset.HillFarColor : _preset.HillNearColor;
                sr.sortingOrder = i == 0 ? HillFarSortingOrder : HillNearSortingOrder;
                _hills[i] = sr;
            }
        }

        int particleCount = _preset.ParticleCount;
        _particles = new Transform[particleCount];
        _particlePhases = new float[particleCount];
        for (int i = 0; i < particleCount; i++)
        {
            GameObject particle = new GameObject($"Ambient{i}");
            particle.transform.SetParent(_worldRoot, false);
            SpriteRenderer sr = particle.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.SoftDot();
            sr.color = _preset.ParticleColor;
            sr.sortingOrder = ParticleSortingOrder;
            float size = _preset.ParticleSize * Random.Range(0.7f, 1.3f);
            particle.transform.localScale = new Vector3(size, size, 1f);
            particle.transform.position = RandomParticlePosition(anywhere: true);
            _particles[i] = particle.transform;
            _particlePhases[i] = Random.Range(0f, Mathf.PI * 2f);
        }
    }

    private float CameraHalfHeight => targetCamera.orthographicSize;
    private float CameraHalfWidth => targetCamera.orthographicSize * targetCamera.aspect;

    private Vector3 RandomCloudPosition(bool initialSpread)
    {
        Vector3 cam = targetCamera.transform.position;
        float x = cam.x + Random.Range(-CameraHalfWidth, CameraHalfWidth) * 1.2f;
        float y = initialSpread
            ? cam.y + Random.Range(-CameraHalfHeight, CameraHalfHeight * 2f)
            : cam.y + Random.Range(CameraHalfHeight * 1.1f, CameraHalfHeight * 1.8f);
        return new Vector3(x, y, 0f);
    }

    private void UpdateClouds()
    {
        if (_clouds == null) return;

        Vector3 cam = targetCamera.transform.position;
        float wrapX = CameraHalfWidth * 1.5f;
        for (int i = 0; i < _clouds.Length; i++)
        {
            Transform cloud = _clouds[i].transform;
            Vector3 pos = cloud.position;
            pos.x += _cloudSpeeds[i] * Time.deltaTime;

            if (pos.x > cam.x + wrapX) pos.x = cam.x - wrapX;
            else if (pos.x < cam.x - wrapX) pos.x = cam.x + wrapX;

            // Fell far below the view (camera climbed past it): respawn above.
            if (pos.y < cam.y - CameraHalfHeight * 1.6f)
            {
                pos = RandomCloudPosition(initialSpread: false);
            }
            cloud.position = pos;
        }
    }

    private void UpdateHills()
    {
        if (_hills == null || GameManager.Instance == null) return;

        // Anchored at the floor with slight upward parallax: distant hills track the
        // camera a touch, so they linger longer before sinking out of view.
        float floorY = GameManager.Instance.floorOriginY;
        Vector3 cam = targetCamera.transform.position;
        float climbed = Mathf.Max(0f, cam.y - floorY);
        float width = CameraHalfWidth * 2.6f;

        for (int i = 0; i < _hills.Length; i++)
        {
            SpriteRenderer hill = _hills[i];
            float parallax = i == 0 ? 0.18f : 0.07f;       // far hill clings to the view longer
            float centerOffsetY = i == 0 ? 0f : -1f;       // far crests peek above the near ones
            Vector2 size = hill.sprite.bounds.size;
            float scale = width / size.x;
            // Full-height scale: the solid base must always reach below the visible bottom.
            hill.transform.localScale = new Vector3(scale, scale, 1f);
            hill.transform.position = new Vector3(cam.x, floorY + centerOffsetY + climbed * parallax, 0f);
        }
    }

    private Vector3 RandomParticlePosition(bool anywhere)
    {
        Vector3 cam = targetCamera.transform.position;
        float x = cam.x + Random.Range(-CameraHalfWidth, CameraHalfWidth) * 1.1f;
        float y = anywhere
            ? cam.y + Random.Range(-CameraHalfHeight, CameraHalfHeight)
            : cam.y + CameraHalfHeight * Random.Range(1.05f, 1.3f);
        return new Vector3(x, y, 0f);
    }

    private void UpdateParticles()
    {
        if (_particles == null || _particles.Length == 0) return;

        Vector3 cam = targetCamera.transform.position;
        for (int i = 0; i < _particles.Length; i++)
        {
            Transform particle = _particles[i];
            Vector3 pos = particle.position;
            pos.y -= _preset.ParticleFallSpeed * Time.deltaTime;
            pos.x += Mathf.Sin(Time.time * 1.3f + _particlePhases[i]) * _preset.ParticleSwayAmount * Time.deltaTime;

            if (pos.y < cam.y - CameraHalfHeight * 1.15f)
            {
                pos = RandomParticlePosition(anywhere: false);
            }
            particle.position = pos;
        }
    }
}
