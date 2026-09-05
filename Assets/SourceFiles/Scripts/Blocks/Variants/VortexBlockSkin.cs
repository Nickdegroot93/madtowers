using UnityEngine;

/// <summary>
/// The Vortex look: the WHOLE brick is churning dusk marble (procedural Resources/Vortex shader) - a
/// fixed, theme-independent full look that replaces the chapter art (the original inset-gem-over-chapter-
/// art overlay was rejected as ugly - Nick, July 2026; the palette echoes Fangkuai District's dusk pinks,
/// its home chapter). The vortex is driven by <c>_Swirl</c> (an accumulated angle) rather than the shader
/// clock, so it runs on scaled time and a pause freezes it (PHYSICS.md). The angular velocity itself
/// oscillates, so the swirl winds up, slows, and REVERSES - a woozy churn that doubles as the cue for
/// inverted steering. Per-cell <c>_Seed</c> desyncs the cells of a multi-cell piece. See BLOCKVARIANTS.md.
/// </summary>
public sealed class VortexBlockSkin : BlockVariantSkin
{
    private static readonly int SwirlId = Shader.PropertyToID("_Swirl");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");

    private const float MaxAngVel = 0.65f;    // rad/s peak churn speed
    private const float ReverseRate = 0.34f; // rad/s of the speed oscillator (sign flips every PI/ReverseRate)

    protected override string MaterialResource => "Vortex";
    protected override bool HidesChapterArt => true; // full look - the brick IS the vortex
    protected override string CellName => "VortexCell";

    private float _age;
    private float _swirl;

    /// <summary>Build the vortex look. Called from VortexBlockData.OnApplied.</summary>
    public void Apply() => BuildCells();

    protected override void ConfigureCell(int index, int col, int row, SpriteRenderer overlay, MaterialPropertyBlock mpb)
    {
        mpb.SetFloat(SeedId, (index * 0.6180339f) % 1f); // golden-ratio offset so cells don't clone
        mpb.SetFloat(SwirlId, 0f);
    }

    private void LateUpdate()
    {
        if (!IsBuilt) return;

        _age += Time.deltaTime; // scaled - a pause freezes the swirl (PHYSICS.md)
        // Integrate an oscillating angular velocity: the churn winds one way, eases, then reverses.
        float angVel = MaxAngVel * Mathf.Sin(_age * ReverseRate);
        _swirl += angVel * Time.deltaTime;
        SetCellsFloat(SwirlId, _swirl);
    }
}
