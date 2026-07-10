using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The visual half of an Airtight air pocket (AirPocketModifier owns the rules): one smoke
/// quad per sealed cell driven by the shared AirPocketSmoke shader, filling bottom-up as the
/// fuse burns. Vent() drains the smoke out fast with a fade (the rescue read); Detonate()
/// flashes the cavity white-hot, kicks a shockwave ring and leaves brief scorch wisps. The
/// hazard look is theme-independent on purpose - identical in every chapter, like Magma.
/// </summary>
public sealed class AirPocketFx : MonoBehaviour
{
    // Behind bricks (0) so the quads' edges tuck under the surrounding walls, in front of the
    // floor stack (-50..-43) so smoke inside a terrain pocket still reads.
    private const int SmokeSortingOrder = -1;
    private const int FlashSortingOrder = 60; // momentary overlay, in front of the tower (ZapBeam's slot)
    private const float VentSeconds = 0.45f;
    private const float DetonateLingerSeconds = 0.9f;

    private readonly List<SpriteRenderer> _cells = new();
    private static Shader _smokeShader; // pockets rebuild their FX as they grow - load once
    private Material _material;
    private float _fill;
    private float _ventFrom = -1f;
    private float _stateTime;
    private bool _detonated;
    private Bounds _area;

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

        // The smoke is ONE world-space volume swelling from the region's centroid; the quads
        // are only windows onto it (the shader evaluates density in world coordinates), so a
        // multi-cell pocket reads as a single cloud - never as cells filling one by one.
        Vector2 centroid = Vector2.zero;
        bool first = true;
        for (int i = 0; i < cellWorldCenters.Count; i++)
        {
            centroid += cellWorldCenters[i];
            var cellBounds = new Bounds(cellWorldCenters[i], Vector3.one * gridSpacing);
            if (first) { _area = cellBounds; first = false; }
            else _area.Encapsulate(cellBounds);
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

        for (int i = 0; i < cellWorldCenters.Count; i++)
        {
            Vector2 center = cellWorldCenters[i];
            var cellGo = new GameObject($"SmokeCell{i}");
            cellGo.transform.SetParent(transform, false);
            cellGo.transform.position = center;
            // A whisker over cell size: the world-space field makes adjacent quads render
            // identical values, so the overlap only exists to swallow float hairlines.
            cellGo.transform.localScale = new Vector3(gridSpacing * 1.005f, gridSpacing * 1.005f, 1f);

            var sr = cellGo.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.Square();
            sr.sharedMaterial = _material;
            sr.sortingOrder = SmokeSortingOrder;
            _cells.Add(sr);
        }
    }

    /// <summary>Fuse progress 0..1 - drives the smoke level. Ignored once venting/detonated.</summary>
    public void SetFill(float fill)
    {
        if (_ventFrom >= 0f || _detonated) return;
        _fill = Mathf.Clamp01(fill);
        _material.SetFloat("_Fill", _fill);
    }

    /// <summary>The rescue: the region reconnected to open air - drain the smoke out fast.</summary>
    public void Vent()
    {
        if (_detonated || _ventFrom >= 0f) return;
        _ventFrom = _fill;
        _stateTime = 0f;
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

        var flashGo = new GameObject("PocketFlash");
        flashGo.transform.SetParent(transform, false);
        flashGo.transform.position = _area.center;
        flashGo.transform.localScale = _area.size * 1.15f;
        var flash = flashGo.AddComponent<SpriteRenderer>();
        flash.sprite = RuntimeSprites.Square();
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
            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i] == null) continue;
                GameObject burst = Instantiate(perCellEffect, _cells[i].transform.position, Quaternion.identity);
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
    }
}
