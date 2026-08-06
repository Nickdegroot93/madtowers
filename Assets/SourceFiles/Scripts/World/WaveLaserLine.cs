using UnityEngine;

/// <summary>
/// The height-limit laser's look (HeightLimitWavesModifier owns the rules; this owns only the
/// light). A THIN hard line, drawn as three stacked LineRenderers sharing one set of ripple
/// points (ZapBeam's construction): a narrow chapter-coloured halo, a bright core and a
/// near-white needle. No wide feathered glow - the line is a hard thing, you touch it, you
/// die, and it must LOOK like a hard thing (Nick, Aug 2026; a 4-layer soft beam clone and an
/// additive-shader glow were both tried and cut - too wide, too soft, and the Sacrifice
/// clone's travelling dashes read as weird left-to-right motion).
///
/// The life is in the SHAPE: the polyline is dead straight most of the time, with a very
/// slight electric ripple that swells up every few seconds (Perlin-gated bursts) and surges
/// on a zap. All three layers bend through the same points, so it reads as ONE live wire,
/// not layers drifting apart. Amplitudes are hundredths of a world unit - far inside the
/// half-cell zap grace, so the death boundary never reads ambiguous.
///
/// Colours: the active chapter's two menu accent colours plus white for the needle - every
/// chapter's laser is its own, while the near-white centre stays the invariant "touch this
/// and die" signal (also the colour-blind read).
///
/// World-space renderers in the 49-51 sorting band on purpose: the blackout curtain
/// (order 200) must darken the laser - LEVELS.md: "you stay under the line you MEMORIZED".
///
/// The per-chapter laser.png hook survives: authored art replaces the halo+core (authored
/// height kept, stretched to the same span the polyline covers), while the rippling needle
/// still renders.
/// </summary>
public sealed class WaveLaserLine : MonoBehaviour
{
    private const int BaseOrder = 50; // halo 49 · core 50 · needle 51 (ART.md registry)
    private const float FlashDecayPerSecond = 2.5f;

    // Polyline: enough points that the ripple curves smoothly at phone widths.
    private const int PointCount = 96;
    private const float MinSpan = 24f;        // world units; also covers a null camera
    private const float SpanOverscan = 1.2f;  // past the screen edges so the ends never show

    // The electric ripple. Base keeps the wire barely alive; bursts are Perlin-gated so the
    // line visibly ripples "every now and then", not constantly; a zap surges it. VERY slight
    // on purpose - the wobble must never make the boundary ambiguous.
    private const float BaseAmplitude = 0.005f;
    private const float BurstAmplitude = 0.04f;
    private const float FlashAmplitude = 0.03f;

    // Layer widths (world units). Thin is the whole point: the halo is a tight colour edge,
    // not a feathered glow.
    private const float HaloWidth = 0.16f;
    private const float CoreWidth = 0.055f;
    private const float NeedleWidth = 0.022f;

    private static Material _lineMat; // Sprites/Default, shared (the ZapBeam pattern)

    private LineRenderer _halo;
    private LineRenderer _core;
    private LineRenderer _needle;
    private SpriteRenderer _chapterBeam; // authored laser.png, when a chapter supplies one
    private Vector3[] _points;

    private Color _color;
    private Color _accentColor;
    private Color _coreBase; // Lerp(_color, _accentColor, 0.35), fixed for the object's life
    private float _seed;
    private float _y;
    private float _flash;

    // Change detection so idle frames stay cheap: a paused or laser-quiet frame produces
    // byte-identical output, so the polyline is rebuilt only while it visibly ripples (or
    // the span changes), colours only when the glow level moves, the transform only when
    // the camera or the line actually moved.
    private float _lastCamX = float.NaN;
    private float _lastY = float.NaN;
    private float _lastT = float.NaN;
    private float _lastSpan = -1f;
    private float _lastGlow = -1f;

    /// <summary>Builds the beam in the given chapter colours (primary + secondary accent; the
    /// modifier resolves them, falling back to its authored lineColor). The caller drives
    /// height via <see cref="SetY"/> - the same value the ceiling and the zap check read.</summary>
    public static WaveLaserLine Create(Color color, Color accentColor)
    {
        GameObject go = new GameObject("HeightLimitLine");
        WaveLaserLine line = go.AddComponent<WaveLaserLine>();
        color.a = 1f;
        accentColor.a = 1f;
        line._color = color;
        line._accentColor = accentColor;
        line._seed = Random.Range(0f, 100f);
        line.Build();
        return line;
    }

    public void SetY(float y) => _y = y;

    /// <summary>Zap feedback: a brightness spike plus a ripple surge that decay on their own.</summary>
    public void Flash() => _flash = 0.6f;

    private void Build()
    {
        _points = new Vector3[PointCount];
        _coreBase = Color.Lerp(_color, _accentColor, 0.35f);

        Sprite chapterSprite = ChapterSkins.LoadLaser();
        if (chapterSprite != null)
        {
            // Authored art replaces halo+core; the needle stays so the death boundary reads
            // the same in every chapter. Sized to the live span in LateUpdate.
            GameObject child = new GameObject("ChapterBeam");
            child.transform.SetParent(transform, false);
            _chapterBeam = child.AddComponent<SpriteRenderer>();
            _chapterBeam.sprite = chapterSprite;
            _chapterBeam.sortingOrder = BaseOrder - 1;
        }
        else
        {
            _halo = CreateLine("Halo", HaloWidth, BaseOrder - 1);
            _core = CreateLine("Core", CoreWidth, BaseOrder);
        }

        _needle = CreateLine("Needle", NeedleWidth, BaseOrder + 1);
        Color needle = Color.Lerp(_color, Color.white, 0.92f);
        needle.a = 0.98f;
        SetLineColor(_needle, needle); // constant for the object's life
    }

    private LineRenderer CreateLine(string name, float width, int order)
    {
        if (_lineMat == null) _lineMat = new Material(Shader.Find("Sprites/Default"));

        LineRenderer lr = new GameObject(name).AddComponent<LineRenderer>();
        lr.transform.SetParent(transform, false);
        lr.sharedMaterial = _lineMat;
        lr.useWorldSpace = false;
        lr.positionCount = PointCount;
        lr.numCapVertices = 2;
        lr.textureMode = LineTextureMode.Stretch;
        lr.sortingOrder = order;
        lr.widthMultiplier = width;
        return lr;
    }

    private void LateUpdate()
    {
        Camera cam = TowerCameraController.Camera != null ? TowerCameraController.Camera : Camera.main;
        float camX = cam != null ? cam.transform.position.x : 0f;
        // 0 is safe when the camera is missing/perspective: the span clamps to MinSpan below.
        float halfWidth = cam != null && cam.orthographic ? cam.orthographicSize * cam.aspect : 0f;

        if (camX != _lastCamX || _y != _lastY)
        {
            _lastCamX = camX;
            _lastY = _y;
            transform.position = new Vector3(camX, _y, 0f);
        }

        float t = Time.time;
        bool timeAdvanced = t != _lastT; // false while paused - everything below is frozen
        _lastT = t;
        _flash = Mathf.Max(0f, _flash - Time.deltaTime * FlashDecayPerSecond);

        // Ripple envelope: near zero most of the time, an occasional Perlin-gated swell, and
        // a surge while the zap flash decays.
        float burst = Mathf.InverseLerp(0.62f, 0.85f, Mathf.PerlinNoise(t * 0.35f, _seed));
        float span = Mathf.Max(MinSpan, halfWidth * 2f * SpanOverscan);

        bool spanChanged = span != _lastSpan;
        if (spanChanged || (timeAdvanced && (burst > 0f || _flash > 0f)))
        {
            float amplitude = BaseAmplitude + BurstAmplitude * burst + FlashAmplitude * _flash;
            for (int i = 0; i < PointCount; i++)
            {
                float x = (i / (float)(PointCount - 1) - 0.5f) * span;
                // Two Perlin octaves, both scrolled in TIME only - the wire vibrates in
                // place, nothing travels along it (travelling motion was reviewed out).
                float ripple = 0.65f * (Mathf.PerlinNoise(_seed + x * 0.45f, t * 2.2f) - 0.5f) * 2f
                             + 0.35f * (Mathf.PerlinNoise(_seed + 40f + x * 1.6f, t * 5.5f) - 0.5f) * 2f;
                _points[i] = new Vector3(x, ripple * amplitude, 0f);
            }

            if (_halo != null)
            {
                _halo.SetPositions(_points);
                _core.SetPositions(_points);
            }
            _needle.SetPositions(_points);

            if (spanChanged)
            {
                _lastSpan = span;
                if (_chapterBeam != null)
                {
                    // The authored strip covers the same width the polyline does.
                    _chapterBeam.transform.localScale =
                        new Vector3(span / _chapterBeam.sprite.bounds.size.x, 1f, 1f);
                }
            }
        }

        // Colours brighten (never widen) with the burst/flash; rewritten only when that
        // level actually moves. The needle's colour is constant - set once in Build.
        float glow = Mathf.Clamp01(burst * 0.25f + _flash);
        if (Mathf.Approximately(glow, _lastGlow)) return;
        _lastGlow = glow;

        if (_halo != null)
        {
            Color halo = _color;
            halo.a = 0.30f + 0.12f * glow;
            SetLineColor(_halo, halo);

            Color core = Color.Lerp(_coreBase, Color.white, 0.5f + glow * 0.3f);
            core.a = 0.9f;
            SetLineColor(_core, core);
        }
        else
        {
            Color art = _color;
            art.a = Mathf.Clamp01(0.8f + _flash);
            _chapterBeam.color = art;
        }
    }

    private static void SetLineColor(LineRenderer lr, Color color)
    {
        lr.startColor = color;
        lr.endColor = color;
    }
}
