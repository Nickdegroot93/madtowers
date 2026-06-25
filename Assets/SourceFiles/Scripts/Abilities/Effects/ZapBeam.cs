using UnityEngine;

/// <summary>
/// The Zap beam: a column of <see cref="StrandCount"/> loose lightning strands (LineRenderers) in
/// varied blue/white shades plus a soft central glow core. At charge 0 the strands fan out WIDE and
/// jagged; as the shot charges they converge toward the column centre and calm into one thin, bright
/// needle, flaring white on the kill. Glows for free through the global bloom (bright/HDR colours, no
/// shader). Purely cosmetic — <see cref="ZapSession"/> drives the public fields each frame (the column
/// X moves while the player aims; the bottom tracks whatever block the beam currently hits).
/// </summary>
public sealed class ZapBeam : MonoBehaviour
{
    public float BeamX;      // column the beam sits on (moves while aiming)
    public float TopY;       // beam starts here (just above the screen)
    public float BottomY;    // beam ends here (top of the hit block, or the floor)
    public float Charge;     // 0 = wide & loose, 1 = thin & converged
    public float FireFlash;  // 0..1 white spike on detonation

    private const int SortingOrder = 60; // in front of the tower - a momentary dramatic overlay
    private const int StrandCount = 6;
    private const int Segments = 14;
    private const float WideSpread = 0.5f;  // half-width the strands fan to at charge 0 (world units)
    private const float MaxJitter = 0.14f;  // jagged amplitude at charge 0
    private const float MinJitter = 0.012f;

    private Color _color;
    private Color _accent;
    private float _seed;

    private SpriteRenderer _core;
    private LineRenderer[] _strands;
    private float[] _offsetFrac;
    private float[] _shade;
    private static Material _strandMat;

    public void Configure(Color color, Color accent)
    {
        _color = color;
        _accent = accent;
        _seed = Random.Range(0f, 100f);

        _core = new GameObject("Core").AddComponent<SpriteRenderer>();
        _core.transform.SetParent(transform, false);
        _core.sprite = RuntimeSprites.SoftVerticalBar(0.18f);
        _core.sortingOrder = SortingOrder + 3;

        if (_strandMat == null) _strandMat = new Material(Shader.Find("Sprites/Default"));
        _strands = new LineRenderer[StrandCount];
        _offsetFrac = new float[StrandCount];
        _shade = new float[StrandCount];
        for (int k = 0; k < StrandCount; k++)
        {
            _offsetFrac[k] = StrandCount == 1 ? 0f : ((float)k / (StrandCount - 1)) * 2f - 1f;
            _shade[k] = Random.value;

            LineRenderer lr = new GameObject("Strand" + k).AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            lr.sharedMaterial = _strandMat;
            lr.useWorldSpace = true;
            lr.positionCount = Segments + 1;
            lr.numCapVertices = 2;
            lr.textureMode = LineTextureMode.Stretch;
            lr.sortingOrder = SortingOrder + (k == 0 ? 2 : 1);
            lr.widthMultiplier = 0.05f;
            _strands[k] = lr;
        }
    }

    private void LateUpdate()
    {
        if (_strands == null) return;

        float t = Time.time;
        float charge = Mathf.Clamp01(Charge);
        float length = Mathf.Max(0.01f, TopY - BottomY);

        // Core glow: wide+faint -> thin+bright, whitens and flares on the kill.
        float coreWidth = Mathf.Lerp(2.2f, 0.7f, charge) + FireFlash * 1.4f;
        Color coreCol = Color.Lerp(_color, Color.white, Mathf.Clamp01(charge * 0.6f + FireFlash));
        coreCol.a = Mathf.Lerp(0.22f, 0.72f, charge) + FireFlash * 0.28f;
        _core.color = coreCol;
        _core.transform.position = new Vector3(BeamX, (TopY + BottomY) * 0.5f, 0f);
        _core.transform.localScale = new Vector3(coreWidth,
            length / Mathf.Max(0.0001f, _core.sprite.bounds.size.y), 1f);

        float spread = Mathf.Lerp(WideSpread, 0f, charge);
        float jitter = Mathf.Lerp(MaxJitter, MinJitter, charge);

        for (int k = 0; k < StrandCount; k++)
        {
            LineRenderer lr = _strands[k];
            float baseX = BeamX + _offsetFrac[k] * spread;
            for (int i = 0; i <= Segments; i++)
            {
                float f = (float)i / Segments;
                float y = Mathf.Lerp(TopY, BottomY, f);
                float taper = Mathf.Sin(f * Mathf.PI); // 0 at ends, 1 mid - so strands meet at top & hit point
                float n = Mathf.PerlinNoise(_seed + k * 3.3f + f * 5f, t * 11f + k) - 0.5f;
                float x = baseX + n * jitter * (0.35f + 0.65f * taper);
                lr.SetPosition(i, new Vector3(x, y, 0f));
            }

            float flick = 0.6f + 0.4f * Mathf.PerlinNoise(k * 7.1f, t * 14f);
            Color c = Color.Lerp(_color, _accent, _shade[k]);
            c = Color.Lerp(c, Color.white, Mathf.Clamp01(_shade[k] * 0.4f + charge * 0.5f + FireFlash));
            c.a = flick * Mathf.Lerp(0.5f, 0.95f, charge);
            lr.startColor = c;
            lr.endColor = c;
            lr.widthMultiplier = Mathf.Lerp(0.06f, 0.03f, charge) + FireFlash * 0.06f;
        }
    }
}
