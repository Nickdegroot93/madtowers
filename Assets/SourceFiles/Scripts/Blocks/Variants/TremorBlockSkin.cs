using UnityEngine;

/// <summary>
/// The Tremor look: a fixed, theme-independent slab of warm ochre fault-stone (procedural
/// Resources/Tremor shader), replacing the chapter art. Tremor's identity is MOTION, not material - it is
/// the one brick that never holds still:
///   - a constant fine micro-buzz on the cosmetic overlay cells (held seismic energy),
///   - a pulse of light travelling along the fault cracks (shader _Wave),
/// so it reads as "this one shakes" while it is still falling. On landing the behaviour calls
/// <see cref="PlayQuake"/>: the cracks flash and a shockwave ring rips outward (shader _Quake) plus a
/// hard squash + camera kick, marrying the look to the tower jolt TremorBlockData triggers.
///
/// Purely cosmetic: the buzz offsets only the visual children, never the physical body (PHYSICS.md).
/// See BLOCKVARIANTS.md.
/// </summary>
public sealed class TremorBlockSkin : BlockVariantSkin
{
    private static readonly int WaveId = Shader.PropertyToID("_Wave");
    private static readonly int QuakeId = Shader.PropertyToID("_Quake");

    private const float WaveSpeed = 0.35f;     // travelling-pulse loops per second
    private const float BuzzIdle = 0.006f;     // micro-jitter amplitude while falling (local units)
    private const float BuzzQuake = 0.05f;     // extra jitter at the peak of the discharge
    private const float QuakeDuration = 0.55f;
    private const float SettleDuration = 2.5f; // after landing, the buzz tapers to calm over this long

    protected override string MaterialResource => "Tremor";
    protected override string CellName => "TremorCell";

    private float _wavePhase;
    private float _quakeAge = -1f;             // <0 = no active discharge
    private bool _landed;                      // set on lock; starts the calm-down
    private float _settleAge;                  // seconds since landing
    private Vector3[] _baseCellPositions;      // resting local positions, so the buzz springs back
    private float _armDelay = -1f;             // >=0 = settling, counting down to the eruption
    private System.Action _onArm;              // fired once the settle countdown elapses

    /// <summary>Build the fault-stone look. Called from TremorBlockData.OnApplied.</summary>
    public void Apply()
    {
        BuildCells();
        _baseCellPositions = new Vector3[Cells.Count];
        for (int i = 0; i < Cells.Count; i++)
            if (Cells[i] != null) _baseCellPositions[i] = Cells[i].transform.localPosition;
    }

    /// <summary>Arm the quake to erupt <paramref name="delay"/> seconds after landing - the brick settles
    /// first, then discharges. From TremorBlockData.OnLocked; the countdown runs on scaled time.</summary>
    public void ArmQuake(float delay, System.Action onFire)
    {
        _armDelay = Mathf.Max(0f, delay);
        _onArm = onFire;
    }

    /// <summary>The seismic discharge. Called (after the settle delay) from TremorBlockData.OnLocked.</summary>
    public void PlayQuake()
    {
        _quakeAge = 0f;
        _landed = true;   // begin the calm-down: the restless buzz now tapers to still
        _settleAge = 0f;
        ImpactFx.ImpactPunch(0.04f, 0.18f, 0.22f); // the screen-side of the quake (the tower jolt is physical)
    }

    private void LateUpdate()
    {
        if (!IsBuilt) return;

        float dt = Time.deltaTime; // scaled - a pause freezes the motion (PHYSICS.md)

        // Armed but not yet erupted: settle for the delay, then fire the quake once (the callback calls
        // PlayQuake + the tower jolt). The restless idle buzz keeps running until then (held energy).
        if (_armDelay >= 0f)
        {
            _armDelay -= dt;
            if (_armDelay <= 0f)
            {
                _armDelay = -1f;
                System.Action cb = _onArm;
                _onArm = null;
                cb?.Invoke();
            }
        }

        // Travelling pulse along the faults, always looping.
        _wavePhase += dt * WaveSpeed;
        SetCellsFloat(WaveId, Mathf.Repeat(_wavePhase, 1f));

        // Quake discharge: drive _Quake 0->1 (ring expands + faults flash) then clear.
        float quakeAmp = 0f;
        if (_quakeAge >= 0f)
        {
            _quakeAge += dt;
            float t = _quakeAge / QuakeDuration;
            if (t >= 1f)
            {
                SetCellsFloat(QuakeId, 0f);
                _quakeAge = -1f;
            }
            else
            {
                SetCellsFloat(QuakeId, t);            // ring radius grows to the rim
                quakeAmp = BuzzQuake * (1f - t);      // the buzz spikes then settles
            }
        }

        // The restless buzz: full while falling, then easing to calm over SettleDuration once landed, so
        // a placed Tremor goes still (the glowing faults stay, keeping its identity). The quake spike sits
        // on top. Deterministic per cell (no Random) so it is frame-stable; offsets the visual children only.
        if (_landed) _settleAge += dt;
        // SmoothStep(1,0,u) eases the buzz from full (u=0) to nothing (u>=1); u is the normalised settle.
        float calm = _landed ? Mathf.SmoothStep(1f, 0f, _settleAge / SettleDuration) : 1f;
        float amp = BuzzIdle * calm + quakeAmp;
        float time = _wavePhase * 60f; // a fast carrier for the jitter, derived from the same clock
        for (int i = 0; i < Cells.Count; i++)
        {
            if (Cells[i] == null) continue;
            float phase = i * 1.61f;
            float jx = Mathf.Sin(time * 1.7f + phase);
            float jy = Mathf.Cos(time * 2.1f + phase * 1.3f);
            Cells[i].transform.localPosition = _baseCellPositions[i] + new Vector3(jx, jy, 0f) * amp;
        }
    }
}
