using UnityEngine;

/// <summary>
/// The Maw look: a fleshy, theme-independent monster brick (procedural Resources/Maw shader), replacing the
/// chapter art. It falls DORMANT (_Active = 0 - a smooth fleshy block with a pressed-shut mouth seam), then
/// the instant it lands the behaviour calls <see cref="Activate"/> and the MOUTH wakes: a toothy grin parts
/// and the eyes blink open. Each devour pulses <see cref="PlayChomp"/> - the jaw gapes past the brick's top
/// edge and slams shut. Purely cosmetic (PHYSICS.md): it only drives shader props, never the body.
///
/// Only truly exposed top cells show the face. The coverage test is a read-only physics probe
/// (<see cref="ScanCoverage"/>) that counts ONLY other maws (and this piece's own upper cells) as cover -
/// maws are the only thing that can rest on a maw permanently; everything else is prey about to be
/// devoured. Counting prey as cover is exactly what made the mouth vanish right before its own bite
/// (prey locks -> scan says covered -> face hides -> prey destroyed -> face pops back). Exposure changes
/// are also FADED (<see cref="ExposeFadeSpeed"/>), never snapped, so a legit change (a maw stacked on
/// top) reads as the face calmly closing instead of a flicker.
/// _UpDir is refreshed every frame from the live rotation, so a rotated/tilted maw still faces world-up.
/// </summary>
public sealed class MawBlockSkin : BlockVariantSkin
{
    private static readonly int ActiveId = Shader.PropertyToID("_Active");
    private static readonly int ChompId = Shader.PropertyToID("_Chomp");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int UpDirId = Shader.PropertyToID("_UpDir");
    private static readonly int ExposeId = Shader.PropertyToID("_Expose");
    private static readonly int BodyHalfId = Shader.PropertyToID("_BodyHalf");

    private const float CellScaleValue = 1.8f; // oversized quad: headroom above the body for the gaping jaw
    private const float OpenDuration = 0.45f;  // how long the mouth takes to wake on landing
    private const float ChompDecay = 5f;       // how fast a bite's gape relaxes
    private const float CoverFallbackInterval = 0.5f; // catches physics movement without lock/destroy events
    private const float ExposeFadeSpeed = 6f;  // exposure fades (~0.17s), never snaps - snapping reads as flicker
    protected override string MaterialResource => "Maw";
    protected override string CellName => "MawCell";
    protected override float CellScale => CellScaleValue;

    private float _blockSeed;
    private float _spacing = 1f;  // grid cell pitch, for sizing the coverage probe
    private float _active;        // 0 dormant -> 1 fully grown
    private bool _activating;
    private float _chomp;
    private MaterialPropertyBlock _cellMpb;
    private bool[] _exposed;      // per cell: no maw resting on top -> show the face
    private float[] _exposeAnim;  // per cell: smoothed 0..1 actually sent to the shader
    private float[] _lastAppliedExpose;
    private bool _coverageDirty = true;
    private float _coverTimer;
    private ContactFilter2D _coverFilter;
    private readonly Collider2D[] _coverHits = new Collider2D[8];
    private float _lastAppliedActive = -1f;
    private float _lastAppliedChomp = -1f;
    private Vector2 _lastAppliedUp = new Vector2(float.NaN, float.NaN);

    /// <summary>Build the dormant maw. Called from MawBlockData.OnApplied.</summary>
    public void Apply()
    {
        _blockSeed = Random.value;
        BlockController ctrl = GetComponent<BlockController>();
        _spacing = ctrl != null ? Mathf.Max(0.01f, ctrl.GridSpacing) : 1f;
        _coverFilter = new ContactFilter2D { useTriggers = false, useLayerMask = false };
        BuildCells();
        _exposed = new bool[Cells.Count];
        _exposeAnim = new float[Cells.Count];
        _lastAppliedExpose = new float[Cells.Count];
        for (int i = 0; i < _exposed.Length; i++) { _exposed[i] = true; _exposeAnim[i] = 1f; _lastAppliedExpose[i] = -1f; }
        _coverageDirty = true;
    }

    /// <summary>Wake the maw - the tentacles grow out. Called from MawBlockData.OnLocked (on landing).</summary>
    public void Activate()
    {
        _activating = true;
        _coverageDirty = true;
    }

    /// <summary>Lash the tentacles for one bite. Called by MawBlockBehaviour each time it devours a block.</summary>
    public void PlayChomp() => _chomp = 1f;

    protected override void ConfigureCell(int index, int col, int row, SpriteRenderer overlay, MaterialPropertyBlock mpb)
    {
        mpb.SetFloat(SeedId, (_blockSeed + index * 0.6180339f) % 1f);
        mpb.SetFloat(ActiveId, 0f);
        mpb.SetFloat(ChompId, 0f);
        mpb.SetFloat(ExposeId, 1f);
        mpb.SetFloat(BodyHalfId, 0.5f / CellScaleValue); // body still spans exactly one cell within the big quad
    }

    private void OnEnable()
    {
        GameEvents.BlockLocked += HandleTowerGeometryChanged;
        GameEvents.BlockDestroyed += HandleTowerGeometryChanged;
    }

    private void OnDisable()
    {
        GameEvents.BlockLocked -= HandleTowerGeometryChanged;
        GameEvents.BlockDestroyed -= HandleTowerGeometryChanged;
    }

    private void HandleTowerGeometryChanged(BlockController block) => _coverageDirty = true;

    private void LateUpdate()
    {
        if (!IsBuilt) return;

        float dt = Time.deltaTime; // scaled - a pause freezes the maw (PHYSICS.md)
        if (_activating && _active < 1f) _active = Mathf.Min(1f, _active + dt / OpenDuration);
        if (_chomp > 0f) _chomp = Mathf.Max(0f, _chomp - dt * ChompDecay);

        if (_activating)
        {
            _coverTimer -= dt;
            if (_coverTimer <= 0f)
            {
                _coverTimer = CoverFallbackInterval;
                _coverageDirty = true;
            }
        }

        if (_coverageDirty) ScanCoverage();

        // Exposure eases toward its target so a change never pops the face on/off in one frame.
        bool exposureChanged = false;
        for (int i = 0; i < _exposeAnim.Length; i++)
        {
            _exposeAnim[i] = Mathf.MoveTowards(_exposeAnim[i], _exposed[i] ? 1f : 0f, dt * ExposeFadeSpeed);
            if (!Mathf.Approximately(_exposeAnim[i], _lastAppliedExpose[i])) exposureChanged = true;
        }

        // World-up expressed in the cell UV frame, so the face is upright however the piece landed.
        Vector3 upL = transform.InverseTransformDirection(Vector3.up);
        Vector2 up = new Vector2(upL.x, upL.y);
        up = up.sqrMagnitude < 1e-5f ? Vector2.up : up.normalized;

        bool activeChanged = !Mathf.Approximately(_active, _lastAppliedActive);
        bool chompChanged = !Mathf.Approximately(_chomp, _lastAppliedChomp);
        bool upChanged = (_lastAppliedUp - up).sqrMagnitude > 0.000001f;
        if (!activeChanged && !chompChanged && !upChanged && !exposureChanged) return;

        _cellMpb ??= new MaterialPropertyBlock();
        for (int i = 0; i < Cells.Count; i++)
        {
            SpriteRenderer sr = Cells[i];
            if (sr == null) continue;
            sr.GetPropertyBlock(_cellMpb); // preserves the per-cell _Seed/_BodyHalf set at build
            _cellMpb.SetVector(UpDirId, new Vector4(up.x, up.y, 0f, 0f));
            _cellMpb.SetFloat(ActiveId, _active);
            _cellMpb.SetFloat(ChompId, _chomp);
            _cellMpb.SetFloat(ExposeId, _exposeAnim[i]);
            sr.SetPropertyBlock(_cellMpb);
            _lastAppliedExpose[i] = _exposeAnim[i];
        }

        _lastAppliedActive = _active;
        _lastAppliedChomp = _chomp;
        _lastAppliedUp = up;
    }

    /// <summary>Per cell: is the space directly above it (world-up) free of another MAW? Only maws count as
    /// cover - they're the only thing that rests on a maw permanently (the piece's own upper cells included);
    /// anything else in the probe is prey about to be devoured, and counting it would hide the mouth at the
    /// exact moment it bites. Read-only physics (PHYSICS.md - never writes).</summary>
    private void ScanCoverage()
    {
        _coverageDirty = false;
        Vector2 size = new Vector2(_spacing * 0.72f, _spacing * 0.18f);
        for (int i = 0; i < Cells.Count; i++)
        {
            SpriteRenderer sr = Cells[i];
            if (sr == null) { _exposed[i] = false; continue; }

            Vector2 probe = (Vector2)sr.transform.position + Vector2.up * (_spacing * 0.62f);
            int n = Physics2D.OverlapBox(probe, size, 0f, _coverFilter, _coverHits);
            bool covered = false;
            for (int h = 0; h < n; h++)
            {
                Collider2D hit = _coverHits[h];
                if (hit == null) continue;
                MawBlockSkin maw = hit.GetComponentInParent<MawBlockSkin>();
                if (maw != null && (maw != this || hit.transform.position.y > sr.transform.position.y + _spacing * 0.25f))
                {
                    covered = true;
                    break;
                }
            }
            _exposed[i] = !covered;
        }
    }
}
