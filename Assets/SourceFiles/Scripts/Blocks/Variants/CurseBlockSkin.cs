using UnityEngine;

/// <summary>
/// The Curse look (v3): a fixed, theme-independent CURSED OBSIDIAN brick (procedural
/// Resources/Curse shader), replacing the chapter art. Falling it is DORMANT - near-black
/// stone, a closed eyelid seam per cell, faint dark crack engraving. On landing
/// (<see cref="Activate"/>) the hex wakes, and the EYE IS the countdown: every cell's eye
/// opens a visible step per burned placement, and on the last one it is huge, round,
/// bloodshot and staring. Hairline crack veins glow acid green with rising doom (the
/// whole-brick alarm), soul-smoke streams up from every EXPOSED top cell (the bury-me
/// beacon), and a gentle whole-piece tremble grows toward zero. Firing is a radial
/// detonation; buried is eyes shut, cracks dark, smoke out.
///
/// This skin is a pure PUPPET: CurseBlockBehaviour is the one exposure/countdown authority
/// and feeds everything through <see cref="SetExposure"/> / <see cref="SetCountdown"/> /
/// <see cref="PlaySigilBurn"/> / <see cref="PlayFire"/> (the old twin physics scan drifted
/// out of sync with the countdown - review 2026-08-02). Demo stages have no behaviour, so
/// <see cref="SetDemoExposure"/> drives exposure directly, masked by a geometric self-cover
/// pass so interior cells never smoke. Purely cosmetic (PHYSICS.md): drives shader props and
/// the overlay children only, never the body.
/// </summary>
public sealed class CurseBlockSkin : BlockVariantSkin
{
    private static readonly int ActiveId = Shader.PropertyToID("_Active");
    private static readonly int ExposeId = Shader.PropertyToID("_Expose");
    private static readonly int LeftId = Shader.PropertyToID("_Left");
    private static readonly int MaxLeftId = Shader.PropertyToID("_MaxLeft");
    private static readonly int TickId = Shader.PropertyToID("_Tick");
    private static readonly int FireId = Shader.PropertyToID("_Fire");
    private static readonly int PulseId = Shader.PropertyToID("_Pulse");
    private static readonly int PhaseId = Shader.PropertyToID("_Phase");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int UpDirId = Shader.PropertyToID("_UpDir");
    private static readonly int BodyHalfId = Shader.PropertyToID("_BodyHalf");

    private const float CellScaleValue = 1.8f; // headroom above the body for the rising soul-smoke
    private const float OpenDuration = 0.5f;   // how long the hex takes to wake on landing
    private const float TickDecay = 3.2f;      // how fast a sigil-burn flash relaxes
    private const float FireDecay = 1.4f;      // how fast the detonation flare relaxes
    private const float ExposeFadeSpeed = 6f;  // exposure fades, never snaps (Maw precedent)
    private const float BaseBeatHz = 0.9f;     // calm breathing at full countdown
    private const float PeakBeatHz = 5f;       // frantic throb on the last placement (Bomb precedent)
    private const float TrembleMax = 0.024f;   // cosmetic jitter at full doom (halved after Nick's look review)
    private const float FirePunchScale = 0.22f; // detonation pops the overlay cells outward then settles

    protected override string MaterialResource => "Curse";
    protected override string CellName => "CurseCell";
    protected override float CellScale => CellScaleValue;

    private float _blockSeed;
    private float _spacing = 1f;
    private float _active;
    private bool _activating;
    private float _tick;
    private float _fire;
    private float _left;           // eased toward the authoritative count so changes read as motion
    private int _leftTarget;
    private int _maxLeft = 4;
    private float _beatPhase;
    private float _phase;
    private MaterialPropertyBlock _cellMpb;
    private bool[] _exposed;       // fed by the behaviour (or the demo override); index = cell build order
    private float[] _exposeAnim;

    /// <summary>Build the sealed dormant tomb. Called from CurseBlockData.OnApplied.</summary>
    public void Apply()
    {
        _blockSeed = Random.value;
        BlockController ctrl = GetComponent<BlockController>();
        _spacing = ctrl != null ? Mathf.Max(0.01f, ctrl.GridSpacing) : 1f;
        BuildCells();
        _exposed = new bool[Cells.Count];
        _exposeAnim = new float[Cells.Count];
        for (int i = 0; i < _exposed.Length; i++) { _exposed[i] = true; _exposeAnim[i] = 1f; }
        _left = _maxLeft;
        _leftTarget = _maxLeft;
    }

    /// <summary>Wake the hex - the eyes creak open. Called from CurseBlockData.OnLocked.</summary>
    public void Activate() => _activating = true;

    /// <summary>Per-cell exposure verdict from the behaviour's scan. Index contract: the
    /// behaviour's non-trigger cell colliders and this skin's overlay cells are both built
    /// from the same GetComponentsInChildren order, so index i is the same physical cell.</summary>
    public void SetExposure(bool[] exposedByCell)
    {
        if (exposedByCell == null || _exposed == null) return;
        int n = Mathf.Min(exposedByCell.Length, _exposed.Length);
        for (int i = 0; i < n; i++) _exposed[i] = exposedByCell[i];
    }

    /// <summary>Authoritative countdown push (also the initial state).</summary>
    public void SetCountdown(int left, int max)
    {
        _maxLeft = Mathf.Max(1, max);
        _leftTarget = Mathf.Clamp(left, 0, _maxLeft);
    }

    /// <summary>A placement burned one step - flash the cracks, quicken the eye.</summary>
    public void PlaySigilBurn(int left, int max)
    {
        SetCountdown(left, max);
        _tick = 1f;
    }

    /// <summary>The curse fired (a life is being taken) and re-armed to <paramref name="left"/>.</summary>
    public void PlayFire(int left, int max)
    {
        SetCountdown(left, max);
        _fire = 1f;
    }

    /// <summary>Demo stages have no behaviour to feed <see cref="SetExposure"/> - drive it
    /// directly. Masked by geometric self-cover (a cell with an own cell above stays quiet)
    /// so demo pieces never smoke from their interior cells.</summary>
    public void SetDemoExposure(bool exposed)
    {
        if (_exposed == null) return;
        for (int i = 0; i < _exposed.Length; i++)
            _exposed[i] = exposed && !HasOwnCellAbove(i);
    }

    // World-space geometry check against the piece's own cells only (no physics) - enough for
    // the demo, where pieces are frozen squarely on the stage grid.
    private bool HasOwnCellAbove(int index)
    {
        if (index < 0 || index >= Cells.Count || Cells[index] == null) return false;
        Vector3 p = Cells[index].transform.position;
        for (int i = 0; i < Cells.Count; i++)
        {
            if (i == index || Cells[i] == null) continue;
            Vector3 o = Cells[i].transform.position;
            if (Mathf.Abs(o.x - p.x) < _spacing * 0.5f
                && o.y > p.y + _spacing * 0.5f && o.y < p.y + _spacing * 1.5f) return true;
        }
        return false;
    }

    protected override void ConfigureCell(int index, int col, int row, SpriteRenderer overlay, MaterialPropertyBlock mpb)
    {
        mpb.SetFloat(SeedId, (_blockSeed + index * 0.6180339f) % 1f);
        mpb.SetFloat(ActiveId, 0f);
        mpb.SetFloat(ExposeId, 1f);
        mpb.SetFloat(LeftId, _maxLeft);
        mpb.SetFloat(MaxLeftId, _maxLeft);
        mpb.SetFloat(BodyHalfId, 0.5f / CellScaleValue); // body still spans exactly one cell in the big quad
    }

    private void LateUpdate()
    {
        if (!IsBuilt) return;

        float dt = Time.deltaTime; // scaled - a pause freezes the hex (PHYSICS.md)
        if (_activating && _active < 1f) _active = Mathf.Min(1f, _active + dt / OpenDuration);
        if (_tick > 0f) _tick = Mathf.Max(0f, _tick - dt * TickDecay);
        if (_fire > 0f) _fire = Mathf.Max(0f, _fire - dt * FireDecay);
        _left = Mathf.MoveTowards(_left, _leftTarget, dt * 6f);

        bool buried = true;
        bool exposureSettled = true;
        for (int i = 0; i < _exposeAnim.Length; i++)
        {
            float target = _exposed[i] ? 1f : 0f;
            if (target > 0f) buried = false;
            _exposeAnim[i] = Mathf.MoveTowards(_exposeAnim[i], target, dt * ExposeFadeSpeed);
            if (!Mathf.Approximately(_exposeAnim[i], target)) exposureSettled = false;
        }

        // Fully quiescent buried curse: nothing phase-driven renders (the shader gates the
        // veins, eye glow and smoke on _Active * _Expose), so skip the per-cell MPB pushes
        // entirely - landed-forever curses shouldn't cost per-frame block copies.
        bool quiescent = buried && exposureSettled && _tick <= 0f && _fire <= 0f
                         && (!_activating || _active >= 1f);
        if (quiescent) return;

        // Heartbeat quickens as the countdown burns (Bomb's accelerating dread).
        float doom = 1f - Mathf.Clamp01(_left / Mathf.Max(1, _maxLeft));
        float hz = buried ? 0.4f : Mathf.Lerp(BaseBeatHz, PeakBeatHz, doom * doom);
        _beatPhase += dt * hz;
        _phase += dt;
        float pulse = 0.5f + 0.5f * Mathf.Sin(_beatPhase * Mathf.PI * 2f);

        // Tremble grows with doom while anything is exposed, applied to the WHOLE piece
        // uniformly - per-cell gating left a T-brick's self-covered cell still while its
        // neighbours shook (Nick's catch). Cosmetic children only, never the body (PHYSICS.md).
        float exposureMax = 0f;
        for (int i = 0; i < _exposeAnim.Length; i++) exposureMax = Mathf.Max(exposureMax, _exposeAnim[i]);
        float trembleAmp = TrembleMax * doom * doom * _active * exposureMax;
        float punch = 1f + FirePunchScale * _fire * _fire;
        bool moveCells = trembleAmp > 0.0001f || _fire > 0f;
        float jx = Mathf.Sin(_beatPhase * 5.3f) * trembleAmp;
        float jy = Mathf.Cos(_beatPhase * 6.1f) * trembleAmp;

        // World-up in the cell frame so eyes and smoke stay upright however the piece landed.
        Vector3 upL = transform.InverseTransformDirection(Vector3.up);
        Vector2 up = new Vector2(upL.x, upL.y);
        up = up.sqrMagnitude < 1e-5f ? Vector2.up : up.normalized;

        _cellMpb ??= new MaterialPropertyBlock();
        for (int i = 0; i < Cells.Count; i++)
        {
            SpriteRenderer sr = Cells[i];
            if (sr == null) continue;

            if (moveCells)
            {
                sr.transform.localPosition = BasePositions[i] + new Vector3(jx, jy, 0f);
                sr.transform.localScale = BaseScales[i] * punch;
            }
            else if (sr.transform.localPosition != BasePositions[i])
            {
                sr.transform.localPosition = BasePositions[i];
                sr.transform.localScale = BaseScales[i];
            }

            sr.GetPropertyBlock(_cellMpb); // preserves the per-cell _Seed/_BodyHalf set at build
            _cellMpb.SetVector(UpDirId, new Vector4(up.x, up.y, 0f, 0f));
            _cellMpb.SetFloat(ActiveId, _active);
            _cellMpb.SetFloat(ExposeId, _exposeAnim[i]);
            _cellMpb.SetFloat(LeftId, _left);
            _cellMpb.SetFloat(MaxLeftId, _maxLeft);
            _cellMpb.SetFloat(TickId, _tick);
            _cellMpb.SetFloat(FireId, _fire);
            _cellMpb.SetFloat(PulseId, pulse);
            _cellMpb.SetFloat(PhaseId, _phase);
            sr.SetPropertyBlock(_cellMpb);
        }
    }
}
