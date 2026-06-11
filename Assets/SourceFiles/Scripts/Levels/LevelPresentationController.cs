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
public partial class LevelPresentationController : MonoBehaviour
{
    [SerializeField] private LevelDefinition levelDefinition;
    [SerializeField] private SpriteRenderer backgroundRenderer;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool fillCameraView = true;
    [SerializeField] private float overscan = 1.12f;
    [SerializeField] private float depthFromCamera = 20f;
    [SerializeField] private int sortingOrder = -100;

    private const int CloudSortingOrder = -90;
    private const int HillFarSortingOrder = -85; // three hill layers: -85, -84, -83
    private const int PropSortingOrder = -82;
    private const int ParticleSortingOrder = -80;

    private BackdropPreset _preset;
    private LevelDefinition _presetLevel;
    private bool _presetResolved;

    private Sprite _skyLowSprite;
    private Sprite _skyHighSprite;
    private SpriteRenderer _skyHighRenderer;

    private float _climbBaseY;    // camera Y when the backdrop spawned; parallax measures
                                  // from here, NOT from the floor (the camera starts well
                                  // above the floor, which lifted parallax elements)
    private Transform _worldRoot; // clouds/hills/sun/props/particles live in world space
    private SpriteRenderer[] _clouds;
    private float[] _cloudSpeeds;
    private float[] _cloudBobPhases;
    private SpriteRenderer[] _hills;
    private SpriteRenderer _hillBase;
    private SpriteRenderer _sun;
    private SpriteRenderer[] _props;
    private float[] _propOffsets;
    private float _propMinFromCenter;
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
        UpdateSun();
        UpdateProps();
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

    // Low->high sky blend by tower height, optionally oscillating (skyShimmer) so the
    // climb passes through gently darker and lighter bands instead of one flat fade.
    private float Altitude01()
    {
        if (!Application.isPlaying || GameManager.Instance == null || _preset == null) return 0f;

        float height = GameManager.Instance.towerHeight;
        float blend = Mathf.Clamp01(height / _preset.AltitudeFadeMeters);
        if (_preset.SkyShimmerAmount > 0f)
        {
            blend = Mathf.Clamp01(blend + _preset.SkyShimmerAmount *
                Mathf.Sin(height * 2f * Mathf.PI / _preset.SkyShimmerPeriodMeters));
        }
        return blend;
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

}
