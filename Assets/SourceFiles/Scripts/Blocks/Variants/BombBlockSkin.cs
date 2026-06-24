using UnityEngine;

/// <summary>
/// The Bomb look: a fixed, theme-independent powder-keg / sea-mine casing of near-black riveted iron
/// (procedural Resources/Bomb shader), replacing the chapter art. The casing's seams glow from within -
/// a faint warm ember at rest (so the brick reads as explosive while it is still falling and being
/// steered), then a rising countdown once the fuse is lit.
///
/// The "about to explode soon" signal is layered, all driven by fuse progress t (0..1):
///   - the seams heat ember-orange -> white-hot (shader _Fuse),
///   - a heartbeat whose frequency accelerates toward detonation (shader _Pulse),
///   - a tremble that grows on the cosmetic overlay cells (never the physical body - PHYSICS.md),
///   - a hard pre-flash in the final beat so the blast never comes out of nowhere.
///
/// The authoritative clock + detonation live in BombBlockBehaviour, which calls <see cref="SetFuse"/>
/// each frame; this skin is purely cosmetic. See BLOCKVARIANTS.md.
/// </summary>
public sealed class BombBlockSkin : BlockVariantSkin
{
    private static readonly int FuseId = Shader.PropertyToID("_Fuse");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");

    private const float BaseBeatHz = 1.6f;    // resting heartbeat once lit
    private const float PeakBeatHz = 9f;      // heartbeat just before detonation
    private const float TrembleMax = 0.045f;  // max cosmetic jitter (local units) at t = 1
    private const float PreFlashFrom = 0.86f; // t at which the final anticipation flash kicks in

    protected override string MaterialResource => "Bomb";
    protected override string CellName => "BombCell";

    private bool _lit;
    private float _fuse;       // 0..1 countdown progress, pushed in by the behaviour
    private float _beatPhase;  // accumulated heartbeat phase
    private Vector3[] _baseCellPositions; // resting local positions, so the tremble can spring back

    /// <summary>Build the iron casing. Called from BombBlockData.OnApplied.</summary>
    public void Apply()
    {
        BuildCells();
        _baseCellPositions = new Vector3[Cells.Count];
        for (int i = 0; i < Cells.Count; i++)
            if (Cells[i] != null) _baseCellPositions[i] = Cells[i].transform.localPosition;
    }

    /// <summary>Light the fuse - the skin starts breathing/trembling. Called from OnLocked.</summary>
    public void Ignite() => _lit = true;

    /// <summary>Feed the authoritative fuse progress (0..1) from BombBlockBehaviour.</summary>
    public void SetFuse(float t01) => _fuse = Mathf.Clamp01(t01);

    private void LateUpdate()
    {
        if (!IsBuilt) return;

        float dt = Time.deltaTime; // scaled - a pause freezes the countdown (PHYSICS.md)

        if (!_lit)
        {
            // Idle identity: a slow, calm ember breath, no heat, no tremble.
            _beatPhase += dt * 0.6f;
            SetCellsFloat(FuseId, 0f);
            SetCellsFloat(PulseId, 0.5f + 0.5f * Mathf.Sin(_beatPhase * Mathf.PI * 2f));
            return;
        }

        float t = _fuse;

        // Heartbeat: frequency accelerates as the fuse runs out, and a sharp pre-flash in the last beat.
        float hz = Mathf.Lerp(BaseBeatHz, PeakBeatHz, t * t);
        _beatPhase += dt * hz;
        float beat = 0.5f + 0.5f * Mathf.Sin(_beatPhase * Mathf.PI * 2f);
        if (t >= PreFlashFrom)
        {
            float k = (t - PreFlashFrom) / (1f - PreFlashFrom); // 0..1 over the final beat
            beat = Mathf.Max(beat, k);                          // ramp to a held bright flash
        }

        SetCellsFloat(FuseId, t);
        SetCellsFloat(PulseId, beat);

        // Tremble: cosmetic jitter on the overlay cells, amplitude rising with t^2. Deterministic per
        // cell (no Random) so it stays frame-stable; offsets the visual children only, never the body.
        float amp = TrembleMax * t * t;
        for (int i = 0; i < Cells.Count; i++)
        {
            if (Cells[i] == null) continue;
            float phase = i * 1.37f;
            float jx = Mathf.Sin(_beatPhase * 5.3f + phase);
            float jy = Mathf.Cos(_beatPhase * 6.1f + phase * 1.7f);
            Cells[i].transform.localPosition = _baseCellPositions[i] + new Vector3(jx, jy, 0f) * amp;
        }
    }
}
