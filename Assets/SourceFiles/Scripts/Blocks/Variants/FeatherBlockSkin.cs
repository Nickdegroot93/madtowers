using UnityEngine;

/// <summary>
/// The Feather look: a fixed, theme-independent TRANSLUCENT frosted block (procedural Resources/Feather
/// shader). You can see through it, so it reads as weightless / made of air; the brick silhouette + bevel
/// keep it reading as a block, and a soft glowing rim makes it ethereal. Lightness is doubled by MOTION -
/// a gentle float (a Y bob + X sway) while the piece is airborne, and a soft landing FLUTTER instead of an
/// impact (the deliberate inverse of Boulder's slam: no hit-stop, no camera kick). Once landed the float
/// eases out and the block sits dead still - placed masonry doesn't hover, however downy. A per-instance
/// phase keeps multiple feather blocks out of sync; the per-cell seed varies the frosted cloud.
/// See BLOCKVARIANTS.md.
/// </summary>
public sealed class FeatherBlockSkin : BlockVariantSkin
{
    private static readonly int SeedId = Shader.PropertyToID("_Seed");

    private const float BobSpeed = 1.8f, BobAmp = 0.03f;
    private const float SwaySpeed = 1.2f, SwayAmp = 0.018f;
    private const float FlutterDecay = 4f, FlutterBoost = 0.05f;

    protected override string MaterialResource => "Feather";
    protected override string CellName => "FeatherCell";

    private float _phase;
    private float _flutterAge = -1f; // <0 = not fluttering
    private float _restBlend;        // 0 = airborne float, eases to 1 once landed
    private bool _landed;

    /// <summary>Build the feather look. Called from FeatherBlockData.OnApplied.</summary>
    public void Apply()
    {
        // Full reset: OnApplied get-or-adds this component, so a re-apply must not inherit a
        // previous life's settled state (or the fresh piece would fall without its float).
        _phase = Random.value * 6.2831f;
        _flutterAge = -1f;
        _restBlend = 0f;
        _landed = false;
        enabled = true;
        BuildCells();
    }

    /// <summary>A soft settle on landing - one last wider flutter, then the float eases out for good.</summary>
    public void PlayLandFlutter()
    {
        _landed = true;
        _flutterAge = 0f;
        enabled = true; // a re-land (e.g. after an ability flight) restarts the taper
    }

    protected override void ConfigureCell(int index, int col, int row, SpriteRenderer overlay, MaterialPropertyBlock mpb)
    {
        mpb.SetFloat(SeedId, (index * 0.6180339f + _phase * 0.1591549f) % 1f); // per-cell + per-instance variation
    }

    private void LateUpdate()
    {
        if (!IsBuilt) return;

        float t = Time.time; // scaled - a pause stills the float (PHYSICS.md)
        float boost = 0f;
        if (_flutterAge >= 0f)
        {
            _flutterAge += Time.deltaTime;
            boost = FlutterBoost * Mathf.Exp(-_flutterAge * FlutterDecay);
            if (boost < 0.0005f) _flutterAge = -1f;
        }

        // Landed: the base float eases out (~0.5 s) under the one landing flutter; when both are done
        // the cells snap to rest and the component turns itself off (no per-frame dispatch for the
        // dozens of placed feathers a long run accumulates) - a placed feather doesn't hover.
        if (_landed)
        {
            _restBlend = Mathf.Min(1f, _restBlend + Time.deltaTime * 2f);
            if (_restBlend >= 1f && _flutterAge < 0f)
            {
                for (int i = 0; i < Cells.Count; i++)
                    if (Cells[i] != null) Cells[i].transform.localPosition = BasePositions[i];
                enabled = false; // PlayLandFlutter/Apply re-enable
                return;
            }
        }

        // The whole block floats as one (cells move together so the piece never looks like it's coming
        // apart); the flutter just widens the float briefly on landing.
        float amp = 1f - _restBlend;
        float bob = Mathf.Sin(t * BobSpeed + _phase) * (BobAmp * amp + boost);
        float sway = Mathf.Sin(t * SwaySpeed + _phase * 1.3f) * (SwayAmp * amp + boost * 0.5f);
        var offset = new Vector3(sway, bob, 0f);

        for (int i = 0; i < Cells.Count; i++)
            if (Cells[i] != null) Cells[i].transform.localPosition = BasePositions[i] + offset;
    }
}
