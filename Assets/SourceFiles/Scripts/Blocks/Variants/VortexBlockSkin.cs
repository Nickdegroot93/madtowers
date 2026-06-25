using UnityEngine;

/// <summary>
/// The Vortex look: a swirling pink-marble vortex set into each cell (procedural Resources/Vortex shader),
/// drawn OVER the kept chapter art - the brick stays solid and reads as itself; only the inset gem moves.
/// The vortex is driven by <c>_Swirl</c> (an accumulated angle) rather than the shader clock, so it runs on
/// scaled time and a pause freezes it (PHYSICS.md). The angular velocity itself oscillates, so the swirl
/// winds up, slows, and REVERSES - a woozy churn that doubles as the cue for inverted steering. Per-cell
/// <c>_Seed</c> desyncs the cells of a multi-cell piece so they don't churn in lockstep. See BLOCKVARIANTS.md.
/// </summary>
public sealed class VortexBlockSkin : BlockVariantSkin
{
    private static readonly int SwirlId = Shader.PropertyToID("_Swirl");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");

    private const float MaxAngVel = 2.0f;    // rad/s peak churn speed
    private const float ReverseRate = 0.34f; // rad/s of the speed oscillator (sign flips every PI/ReverseRate)

    protected override string MaterialResource => "Vortex";
    protected override bool HidesChapterArt => false; // the vortex sits over the chapter colour
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
