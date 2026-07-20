using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The visual half of an Airtight air pocket (AirPocketModifier owns the rules): ONE quad
/// over the pocket's bounds, driven by the shared AirPocketSmoke shader. The pocket's cell
/// set is baked into a tiny bilinear mask texture (one texel per cell, transparent border),
/// so the smoke's boundary is the mask's soft edge torn by noise - it bulges a fraction of
/// a cell past the bricks like pressure straining at the seams, and never reads as a clean
/// rectangle, at any fill level. Vent() drains the smoke out fast with a fade (the rescue
/// read); Detonate() flashes the cavity white-hot through the SAME mask (a pocket-shaped,
/// noise-eaten flare - no square), kicks a shockwave ring and leaves brief scorch wisps.
/// The hazard look is theme-independent on purpose - identical in every chapter, like Magma.
/// </summary>
public sealed class AirPocketFx : MonoBehaviour
{
    // Behind bricks (0) so the smoke's bulge tucks under the surrounding walls, in front of
    // the floor stack (-50..-43) so smoke inside a terrain pocket still reads.
    private const int SmokeSortingOrder = -1;
    private const int FlashSortingOrder = 60; // momentary overlay, in front of the tower (ZapBeam's slot)
    private const float VentSeconds = 0.45f;
    private const float DetonateLingerSeconds = 0.9f;

    private static Shader _smokeShader; // pockets rebuild their FX as they grow - load once
    private static Shader _flashShader;
    private static AudioClip _fillClip; // the 16s rising tension bed, loaded once

    private readonly List<Vector2> _cellCenters = new(); // kept for per-cell detonation bursts
    private Material _material;
    private Material _flashMaterial;
    private Texture2D _mask;
    private AudioSource _fillAudio;
    private float _fill;
    private float _ventFrom = -1f;
    private float _stateTime;
    private bool _detonated;
    private Bounds _area;
    private Vector2 _maskOrigin;    // world position of the mask texture's (0,0) corner
    private Vector2 _maskWorldSize; // world size of the whole mask (pocket bounds + 1-cell border)

    public static AirPocketFx Create(List<Vector2> cellWorldCenters, float gridSpacing)
    {
        var go = new GameObject("AirPocketFx");
        var fx = go.AddComponent<AirPocketFx>();
        fx.Build(cellWorldCenters, gridSpacing);
        return fx;
    }

    private void Build(List<Vector2> cellWorldCenters, float gridSpacing)
    {
        if (_smokeShader == null) _smokeShader = Resources.Load<Shader>("AirPocketSmoke");
        _material = new Material(_smokeShader);

        // The fill bed: a single long crescendo that plays while the smoke rises and is CUT
        // wherever the vent or the pop lands (a fuse is at most ~12s; the bed runs 16). Owned
        // by this object so it dies with the pocket; volume swells with the fill level.
        if (_fillClip == null) _fillClip = Resources.Load<AudioClip>("Audio/Sfx/pocket_fill");
        if (_fillClip != null)
        {
            _fillAudio = gameObject.AddComponent<AudioSource>();
            _fillAudio.clip = _fillClip;
            _fillAudio.spatialBlend = 0f;
            _fillAudio.volume = 0f;
            _fillAudio.Play();
        }

        // The smoke is ONE world-space volume swelling from the region's centroid; the quad
        // is only a window onto it (the shader evaluates density in world coordinates), so a
        // multi-cell pocket reads as a single cloud - never as cells filling one by one.
        Vector2 centroid = Vector2.zero;
        Vector2 minCenter = cellWorldCenters.Count > 0 ? cellWorldCenters[0] : Vector2.zero;
        Vector2 maxCenter = minCenter;
        bool first = true;
        for (int i = 0; i < cellWorldCenters.Count; i++)
        {
            Vector2 c = cellWorldCenters[i];
            centroid += c;
            minCenter = Vector2.Min(minCenter, c);
            maxCenter = Vector2.Max(maxCenter, c);
            var cellBounds = new Bounds(c, Vector3.one * gridSpacing);
            if (first) { _area = cellBounds; first = false; }
            else _area.Encapsulate(cellBounds);
            _cellCenters.Add(c);
        }
        centroid /= Mathf.Max(1, cellWorldCenters.Count);

        float extent = 0.5f * gridSpacing;
        for (int i = 0; i < cellWorldCenters.Count; i++)
        {
            float toFarCorner = (cellWorldCenters[i] - centroid).magnitude + 0.71f * gridSpacing;
            if (toFarCorner > extent) extent = toFarCorner;
        }
        _material.SetVector("_Center", new Vector4(centroid.x, centroid.y, 0f, 0f));
        _material.SetFloat("_Extent", extent);
        _material.SetFloat("_Seed", centroid.x * 3.7f + centroid.y * 17.3f);

        // The pocket mask: one texel per cell (white = pocket, clear = not), plus a one-cell
        // transparent border so the bilinear edge has room to fade out. Sampled in world
        // space by both the smoke and the flash - the pocket's SHAPE lives here, not in the
        // quad's silhouette.
        int cellsW = Mathf.RoundToInt((maxCenter.x - minCenter.x) / gridSpacing) + 1;
        int cellsH = Mathf.RoundToInt((maxCenter.y - minCenter.y) / gridSpacing) + 1;
        _mask = new Texture2D(cellsW + 2, cellsH + 2, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[(cellsW + 2) * (cellsH + 2)];
        for (int i = 0; i < cellWorldCenters.Count; i++)
        {
            int ix = Mathf.RoundToInt((cellWorldCenters[i].x - minCenter.x) / gridSpacing) + 1;
            int iy = Mathf.RoundToInt((cellWorldCenters[i].y - minCenter.y) / gridSpacing) + 1;
            pixels[iy * (cellsW + 2) + ix] = new Color32(255, 255, 255, 255);
        }
        _mask.SetPixels32(pixels);
        _mask.Apply();

        _maskOrigin = minCenter - new Vector2(1.5f * gridSpacing, 1.5f * gridSpacing);
        _maskWorldSize = new Vector2((cellsW + 2) * gridSpacing, (cellsH + 2) * gridSpacing);
        _material.SetTexture("_MaskTex", _mask);
        _material.SetVector("_MaskOrigin", new Vector4(_maskOrigin.x, _maskOrigin.y, 0f, 0f));
        _material.SetVector("_MaskInvSize", new Vector4(1f / _maskWorldSize.x, 1f / _maskWorldSize.y, 0f, 0f));

        var quadGo = new GameObject("SmokeVolume");
        quadGo.transform.SetParent(transform, false);
        quadGo.transform.position = _maskOrigin + 0.5f * _maskWorldSize;
        quadGo.transform.localScale = new Vector3(_maskWorldSize.x, _maskWorldSize.y, 1f);
        var sr = quadGo.AddComponent<SpriteRenderer>();
        sr.sprite = RuntimeSprites.Square();
        sr.sharedMaterial = _material;
        sr.sortingOrder = SmokeSortingOrder;
    }

    /// <summary>Where the tension bed currently is, so a rebuilt FX (pockets grow/merge)
    /// resumes the crescendo instead of audibly restarting it mid-fuse.</summary>
    public float FillAudioTime => _fillAudio != null ? _fillAudio.time : 0f;

    /// <summary>Resume the tension bed at a carried-over position (clamped inside the clip).</summary>
    public void ResumeFillAudioAt(float time)
    {
        if (_fillAudio == null || _fillAudio.clip == null) return;
        _fillAudio.time = Mathf.Clamp(time, 0f, _fillAudio.clip.length - 0.05f);
    }

    /// <summary>Fuse progress 0..1 - drives the smoke level and the tension bed's swell.
    /// Ignored once venting/detonated.</summary>
    public void SetFill(float fill)
    {
        if (_ventFrom >= 0f || _detonated) return;
        _fill = Mathf.Clamp01(fill);
        _material.SetFloat("_Fill", _fill);
        if (_fillAudio != null)
        {
            _fillAudio.volume = (0.35f + 0.65f * _fill) * SettingsService.EffectiveSfx;
        }
    }

    /// <summary>The rescue: the region reconnected to open air - drain the smoke out fast.</summary>
    public void Vent()
    {
        if (_detonated || _ventFrom >= 0f) return;
        _ventFrom = _fill;
        _stateTime = 0f;
        if (_fillAudio != null) _fillAudio.Stop(); // the hiss one-shot takes over
    }

    /// <summary>The pop: white-hot flash filling the cavity, shockwave ring, optional authored
    /// per-cell burst, lingering wisps - then gone. The stack above is untouched (the life is
    /// the price, not the tower).</summary>
    public void Detonate(GameObject perCellEffect, float effectScale)
    {
        if (_detonated) return;
        _detonated = true;
        _stateTime = 0f;
        _material.SetFloat("_Fill", 1f);
        if (_fillAudio != null) _fillAudio.Stop(); // the blast takes over

        // The flash renders through the same pocket mask as the smoke: a cavity-shaped,
        // noise-eaten flare instead of a bounding-box rectangle.
        if (_flashShader == null) _flashShader = Resources.Load<Shader>("AirPocketFlash");
        _flashMaterial = new Material(_flashShader);
        _flashMaterial.SetTexture("_MaskTex", _mask);
        _flashMaterial.SetVector("_MaskOrigin", new Vector4(_maskOrigin.x, _maskOrigin.y, 0f, 0f));
        _flashMaterial.SetVector("_MaskInvSize", new Vector4(1f / _maskWorldSize.x, 1f / _maskWorldSize.y, 0f, 0f));
        _flashMaterial.SetFloat("_Seed", _material.GetFloat("_Seed"));

        var flashGo = new GameObject("PocketFlash");
        flashGo.transform.SetParent(transform, false);
        flashGo.transform.position = _maskOrigin + 0.5f * _maskWorldSize;
        flashGo.transform.localScale = new Vector3(_maskWorldSize.x, _maskWorldSize.y, 1f);
        var flash = flashGo.AddComponent<SpriteRenderer>();
        flash.sprite = RuntimeSprites.Square();
        flash.sharedMaterial = _flashMaterial;
        flash.color = new Color(1f, 0.92f, 0.8f, 0.95f);
        flash.sortingOrder = FlashSortingOrder;
        _flash = flash;

        var ringGo = new GameObject("PocketShockwave");
        ringGo.transform.SetParent(transform, false);
        ringGo.transform.position = _area.center;
        var ring = ringGo.AddComponent<SpriteRenderer>();
        ring.sprite = RuntimeSprites.SoftDot();
        ring.color = new Color(1f, 0.45f, 0.25f, 0.65f);
        ring.sortingOrder = FlashSortingOrder - 1;
        _ring = ring;

        if (perCellEffect != null)
        {
            for (int i = 0; i < _cellCenters.Count; i++)
            {
                GameObject burst = Instantiate(perCellEffect, _cellCenters[i], Quaternion.identity);
                burst.transform.localScale *= effectScale;
            }
        }
    }

    public void DestroyNow()
    {
        if (this != null) Destroy(gameObject);
    }

    private SpriteRenderer _flash;
    private SpriteRenderer _ring;

    private void Update()
    {
        // Unscaled time: these are transient overlays, and a pause during the linger must
        // not freeze a flash/ring on screen indefinitely (they finish behind the menu).
        if (_detonated)
        {
            _stateTime += Time.unscaledDeltaTime;
            float t = _stateTime / DetonateLingerSeconds;
            if (_flash != null)
            {
                // Two-beat decay: instant white pop, quick fall to ember, slow fade.
                float a = t < 0.12f ? 0.95f : Mathf.Lerp(0.7f, 0f, (t - 0.12f) / 0.88f);
                _flash.color = new Color(1f, Mathf.Lerp(0.92f, 0.4f, t), Mathf.Lerp(0.8f, 0.2f, t), a);
            }
            if (_ring != null)
            {
                float grow = Mathf.Lerp(1f, 7f, 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f));
                _ring.transform.localScale = new Vector3(grow, grow, 1f);
                _ring.color = new Color(1f, 0.45f, 0.25f, Mathf.Lerp(0.65f, 0f, t));
            }
            // The smoke itself thins out as the pressure escapes.
            _material.SetFloat("_Fill", Mathf.Lerp(1f, 0f, t * 1.4f));
            if (_stateTime >= DetonateLingerSeconds) Destroy(gameObject);
            return;
        }

        if (_ventFrom >= 0f)
        {
            _stateTime += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_stateTime / VentSeconds);
            _material.SetFloat("_Fill", Mathf.Lerp(_ventFrom, 0f, t));
            if (t >= 1f) Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
        if (_flashMaterial != null) Destroy(_flashMaterial);
        if (_mask != null) Destroy(_mask);
    }
}
