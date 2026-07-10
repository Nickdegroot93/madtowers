using UnityEngine;

/// <summary>
/// World-space armed indicator for Sacrifice. It follows the same line that LossZone
/// uses to charge/intercept bottom-screen losses.
/// </summary>
public sealed class SacrificeLaserLine : MonoBehaviour
{
    private const float LineLength = 90f;
    // In front of the ENTIRE floor stack but behind tower blocks (0): the line is the armed
    // ability's status light, so it must never hide behind the floor (it previously sat at -55,
    // under the ground fill, invisible across the whole floor width at ground level - July 2026;
    // the loss-trigger side protects pocket landings now, not the render order). The floor draws
    // fill -50, shade -49, detail -48, fade -45, back fog -44 and wisps -43 (FloorTerrain), and
    // front particles sit at -45 - the beam's layers (offsets -2..+3) must not collide with any
    // of those or the draw order is undefined per frame. Base -40 puts the whole beam at
    // -42..-37: above every floor layer, below bricks. The Sacrifice FLASH is separate and
    // deliberately stays in front (a momentary destructive bang).
    private const int DefaultSortingOrder = -40;

    // Four coherent layers breathing SLOWLY and in phase (one confident line, not six nervous
    // ones): a wide ambient glow, a soft body, a hot core and a bright needle. Life comes from
    // the travelling energy pulses below, never from jitter.
    private readonly BeamLayer[] _layers =
    {
        new BeamLayer("OuterGlow", 0.52f, 0f, 0.010f, 1.1f, 0f, 0.10f, 0.16f, 0.05f, 0.45f, -2),
        new BeamLayer("Body",      0.20f, 0f, 0.006f, 1.1f, 0f, 0.24f, 0.34f, 0.15f, 0.55f, -1),
        new BeamLayer("HotCore",   0.05f, 0f, 0.004f, 1.1f, 0f, 0.70f, 0.85f, 0.55f, 0.95f, 1),
        new BeamLayer("Needle",    0.02f, 0f, 0.000f, 1.1f, 0f, 0.80f, 0.95f, 0.85f, 1f, 2),
    };

    // Bright short dashes drifting along the line: the "energy is flowing" read.
    private const int PulseCount = 3;
    private const float PulseSpan = 22f;      // world units of drift range around the camera
    private const float PulseSpeed = 3.2f;
    private readonly SpriteRenderer[] _pulses = new SpriteRenderer[PulseCount];

    private Color _color;
    private Color _accentColor;
    private float _phaseOffset;
    private float _verticalOffset;

    public void Configure(Color color, float verticalOffset = 0f, int sortingOrder = DefaultSortingOrder,
        Color? accentColor = null)
    {
        _color = color;
        _accentColor = accentColor ?? new Color(0.35f, 0.95f, 1f, 1f);
        _verticalOffset = verticalOffset;
        _phaseOffset = Random.Range(0f, 20f);
        for (int i = 0; i < _layers.Length; i++)
        {
            _layers[i].Create(transform, _color, _accentColor, sortingOrder);
        }

        for (int i = 0; i < PulseCount; i++)
        {
            var go = new GameObject("EnergyPulse");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.SoftHorizontalBar(0.06f);
            sr.sortingOrder = sortingOrder + 3;
            sr.transform.localScale = new Vector3(90f, 1f, 1f); // a short bright dash (~1.3 world units)
            _pulses[i] = sr;
        }
    }

    public static void FlashAtLossLine(Color color, float verticalOffset = 0f)
    {
        Camera cam = Camera.main;

        GameObject go = new GameObject("SacrificeLaserFlash");
        SacrificeLaserFlash flash = go.AddComponent<SacrificeLaserFlash>();
        flash.Play(color, LossZone.InterceptLineY(cam) + verticalOffset, cam != null ? cam.transform.position.x : 0f);
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float t = Time.time + _phaseOffset;
        float breath = Mathf.Sin(t * 1.1f) * 0.008f; // one slow calm breath, no jitter
        transform.position = new Vector3(cam.transform.position.x, LossZone.InterceptLineY(cam) + _verticalOffset + breath, 0f);

        for (int i = 0; i < _layers.Length; i++)
        {
            _layers[i].Update(_color, _accentColor, t);
        }

        // Energy pulses drift steadily along the line and soft-fade near their turnaround.
        for (int i = 0; i < PulseCount; i++)
        {
            SpriteRenderer pulse = _pulses[i];
            if (pulse == null) continue;
            float cycle = Mathf.Repeat(t * PulseSpeed + i * (PulseSpan / PulseCount), PulseSpan);
            float x = cycle - PulseSpan * 0.5f;
            pulse.transform.localPosition = new Vector3(x, 0f, 0f);
            float edgeFade = Mathf.InverseLerp(0f, 2.5f, Mathf.Min(cycle, PulseSpan - cycle));
            Color c = Color.Lerp(_color, Color.white, 0.75f);
            c.a = 0.85f * edgeFade;
            pulse.color = c;
        }
    }

    private sealed class BeamLayer
    {
        private readonly string _name;
        private readonly float _thickness;
        private readonly float _baseY;
        private readonly float _wobble;
        private readonly float _speed;
        private readonly float _phase;
        private readonly float _minAlpha;
        private readonly float _maxAlpha;
        private readonly float _whiteBlend;
        private readonly float _cyanBlend;
        private readonly int _sortingOffset;

        private SpriteRenderer _renderer;

        public BeamLayer(string name, float thickness, float baseY, float wobble, float speed,
            float phase, float minAlpha, float maxAlpha, float whiteBlend, float cyanBlend,
            int sortingOffset)
        {
            _name = name;
            _thickness = thickness;
            _baseY = baseY;
            _wobble = wobble;
            _speed = speed;
            _phase = phase;
            _minAlpha = minAlpha;
            _maxAlpha = maxAlpha;
            _whiteBlend = whiteBlend;
            _cyanBlend = cyanBlend;
            _sortingOffset = sortingOffset;
        }

        public void Create(Transform parent, Color baseColor, Color accentColor, int sortingOrder)
        {
            GameObject child = new GameObject(_name);
            child.transform.SetParent(parent, false);

            _renderer = child.AddComponent<SpriteRenderer>();
            _renderer.sprite = RuntimeSprites.SoftHorizontalBar(_thickness);
            _renderer.sortingOrder = sortingOrder + _sortingOffset;
            Update(baseColor, accentColor, 0f);
        }

        public void Update(Color baseColor, Color accentColor, float time)
        {
            if (_renderer == null) return;

            float wave = Mathf.Sin(time * _speed + _phase);
            float shimmer = Mathf.Sin(time * (_speed * 1.7f) + _phase * 0.6f) * 0.5f + 0.5f;
            Color accent = Color.Lerp(baseColor, accentColor, _cyanBlend);
            Color color = Color.Lerp(accent, Color.white, _whiteBlend);
            color.a = Mathf.Lerp(_minAlpha, _maxAlpha, shimmer);
            _renderer.color = color;

            Transform tr = _renderer.transform;
            tr.localPosition = new Vector3(0f, _baseY + wave * _wobble, 0f);
            tr.localScale = new Vector3(
                (LineLength + wave * 0.45f) / _renderer.sprite.bounds.size.x,
                Mathf.Lerp(0.82f, 1.18f, shimmer),
                1f);
        }
    }
}
