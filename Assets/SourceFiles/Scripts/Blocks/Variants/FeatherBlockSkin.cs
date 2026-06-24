using UnityEngine;

/// <summary>
/// The Feather look: a fixed, theme-independent TRANSLUCENT frosted block (procedural Resources/Feather
/// shader). You can see through it, so it reads as weightless / made of air; the brick silhouette + bevel
/// keep it reading as a block, and a soft glowing rim makes it ethereal. Lightness is doubled by MOTION -
/// a gentle perpetual float (a Y bob + X sway), and a soft landing FLUTTER instead of an impact (the
/// deliberate inverse of Boulder's slam: no hit-stop, no camera kick). A per-instance phase keeps multiple
/// feather blocks out of sync; the per-cell seed varies the frosted cloud. See BLOCKVARIANTS.md.
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

    /// <summary>Build the feather look. Called from FeatherBlockData.OnApplied.</summary>
    public void Apply()
    {
        _phase = Random.value * 6.2831f;
        BuildCells();
    }

    /// <summary>A soft settle on landing - the float briefly flutters wider then calms. No camera kick.</summary>
    public void PlayLandFlutter() => _flutterAge = 0f;

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

        // The whole block floats as one (cells move together so the piece never looks like it's coming
        // apart); the flutter just widens the float briefly on landing.
        float bob = Mathf.Sin(t * BobSpeed + _phase) * (BobAmp + boost);
        float sway = Mathf.Sin(t * SwaySpeed + _phase * 1.3f) * (SwayAmp + boost * 0.5f);
        var offset = new Vector3(sway, bob, 0f);

        for (int i = 0; i < Cells.Count; i++)
            if (Cells[i] != null) Cells[i].transform.localPosition = BasePositions[i] + offset;
    }
}
