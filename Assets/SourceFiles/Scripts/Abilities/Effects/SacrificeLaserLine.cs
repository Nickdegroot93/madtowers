using UnityEngine;

/// <summary>
/// World-space armed indicator for Sacrifice. It follows the same line that LossZone
/// uses to charge/intercept bottom-screen losses.
/// </summary>
public sealed class SacrificeLaserLine : MonoBehaviour
{
    private const float LineLength = 90f;
    // Behind the tower (blocks render at sortingOrder 0) but in front of the ground/background
    // (ground skin -50): the persistent warning line shows in the open field and to the sides of
    // the stack, and the tower draws over it instead of the beam cutting across the stack. The
    // layer offsets span -2..+2, so the base stays well under 0. The Sacrifice FLASH is separate
    // and deliberately stays in front (a momentary destructive bang).
    private const int SortingOrder = -10;

    private readonly BeamLayer[] _layers =
    {
        new BeamLayer("OuterGlow", 0.54f, 0f, 0.055f, 4.3f, 0.3f, 0.10f, 0.22f, 0.05f, 0.45f, -2),
        new BeamLayer("BlueBody", 0.24f, 0f, 0.032f, 5.4f, 1.2f, 0.20f, 0.38f, 0.15f, 0.55f, -1),
        new BeamLayer("UpperFilament", 0.055f, 0.08f, 0.045f, 7.6f, 2.1f, 0.36f, 0.66f, 0.35f, 0.82f, 0),
        new BeamLayer("LowerFilament", 0.05f, -0.075f, 0.04f, 8.1f, 4.0f, 0.34f, 0.62f, 0.10f, 0.75f, 0),
        new BeamLayer("HotCore", 0.038f, 0f, 0.024f, 9.2f, 5.7f, 0.74f, 0.96f, 0.55f, 0.98f, 1),
        new BeamLayer("Needle", 0.018f, 0.012f, 0.018f, 11.5f, 3.5f, 0.54f, 0.82f, 0.78f, 1f, 2),
    };

    private Color _color;
    private float _phaseOffset;

    public void Configure(Color color)
    {
        _color = color;
        _phaseOffset = Random.Range(0f, 20f);
        for (int i = 0; i < _layers.Length; i++)
        {
            _layers[i].Create(transform, _color, SortingOrder);
        }
    }

    public static void FlashAtLossLine(Color color)
    {
        Camera cam = Camera.main;

        GameObject go = new GameObject("SacrificeLaserFlash");
        SacrificeLaserFlash flash = go.AddComponent<SacrificeLaserFlash>();
        flash.Play(color, LossZone.CurrentLossLineY(cam), cam != null ? cam.transform.position.x : 0f);
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float t = Time.time + _phaseOffset;
        float mainWobble = Mathf.Sin(t * 3.6f) * 0.018f + Mathf.Sin(t * 6.1f + 1.4f) * 0.01f;
        transform.position = new Vector3(cam.transform.position.x, LossZone.CurrentLossLineY(cam) + mainWobble, 0f);

        for (int i = 0; i < _layers.Length; i++)
        {
            _layers[i].Update(_color, t);
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

        public void Create(Transform parent, Color baseColor, int sortingOrder)
        {
            GameObject child = new GameObject(_name);
            child.transform.SetParent(parent, false);

            _renderer = child.AddComponent<SpriteRenderer>();
            _renderer.sprite = RuntimeSprites.SoftHorizontalBar(_thickness);
            _renderer.sortingOrder = sortingOrder + _sortingOffset;
            Update(baseColor, 0f);
        }

        public void Update(Color baseColor, float time)
        {
            if (_renderer == null) return;

            float wave = Mathf.Sin(time * _speed + _phase);
            float shimmer = Mathf.Sin(time * (_speed * 1.7f) + _phase * 0.6f) * 0.5f + 0.5f;
            Color cyan = Color.Lerp(baseColor, new Color(0.35f, 0.95f, 1f, 1f), _cyanBlend);
            Color color = Color.Lerp(cyan, Color.white, _whiteBlend);
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
