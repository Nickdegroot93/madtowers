using UnityEngine;

/// <summary>
/// The Maw look: a fleshy, theme-independent monster brick (procedural Resources/Maw shader), replacing the
/// chapter art. It falls DORMANT (_Active = 0 - plain fleshy block, no tentacles), then the instant it lands
/// the behaviour calls <see cref="Activate"/> and thick suckered TENTACLES grow out of the top edge and
/// writhe. Each devour pulses <see cref="PlayChomp"/> - the tentacles lash and flush. Purely cosmetic
/// (PHYSICS.md): it only drives shader props, never the body.
///
/// Two things make the tentacles read as emerging rather than painted on:
///  - they root at the brick's top EDGE (shader rootU), so almost nothing sits over the brick face;
///  - a cell sprouts them only if nothing rests directly on top of it. That coverage test is a read-only
///    physics probe (<see cref="ScanCoverage"/>), so it naturally handles BOTH this piece's own internal
///    cells AND another maw stacked on top - a covered cell stays a smooth fleshy block, only the truly
///    exposed top sprouts tentacles.
/// _UpDir is refreshed every frame from the live rotation, so a rotated/tilted maw still sprouts straight up.
/// </summary>
public sealed class MawBlockSkin : BlockVariantSkin
{
    private static readonly int ActiveId = Shader.PropertyToID("_Active");
    private static readonly int ChompId = Shader.PropertyToID("_Chomp");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int UpDirId = Shader.PropertyToID("_UpDir");
    private static readonly int ExposeId = Shader.PropertyToID("_Expose");
    private static readonly int BodyHalfId = Shader.PropertyToID("_BodyHalf");

    private const float CellScaleValue = 1.8f; // oversized quad: headroom above the body for the tentacles
    private const float OpenDuration = 0.45f;  // how long the tentacles take to grow on landing
    private const float ChompDecay = 5f;       // how fast a bite's lash relaxes
    private const float CoverFallbackInterval = 0.5f; // catches physics movement without lock/destroy events
    protected override string MaterialResource => "Maw";
    protected override string CellName => "MawCell";
    protected override float CellScale => CellScaleValue;

    private float _blockSeed;
    private float _spacing = 1f;  // grid cell pitch, for sizing the coverage probe
    private float _active;        // 0 dormant -> 1 fully grown
    private bool _activating;
    private float _chomp;
    private MaterialPropertyBlock _cellMpb;
    private bool[] _exposed;      // per cell: nothing resting on top -> sprout tentacles
    private bool[] _lastAppliedExposed;
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
        _lastAppliedExposed = new bool[Cells.Count];
        for (int i = 0; i < _exposed.Length; i++) _exposed[i] = true;
        for (int i = 0; i < _lastAppliedExposed.Length; i++) _lastAppliedExposed[i] = false;
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

        // World-up expressed in the cell UV frame, so tentacles always reach up however the piece landed.
        Vector3 upL = transform.InverseTransformDirection(Vector3.up);
        Vector2 up = new Vector2(upL.x, upL.y);
        up = up.sqrMagnitude < 1e-5f ? Vector2.up : up.normalized;

        bool activeChanged = !Mathf.Approximately(_active, _lastAppliedActive);
        bool chompChanged = !Mathf.Approximately(_chomp, _lastAppliedChomp);
        bool upChanged = (_lastAppliedUp - up).sqrMagnitude > 0.000001f;
        bool exposureChanged = HasExposureChanged();
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
            _cellMpb.SetFloat(ExposeId, _exposed[i] ? 1f : 0f);
            sr.SetPropertyBlock(_cellMpb);
        }

        _lastAppliedActive = _active;
        _lastAppliedChomp = _chomp;
        _lastAppliedUp = up;
        if (_lastAppliedExposed != null && _exposed != null)
        {
            int count = Mathf.Min(_lastAppliedExposed.Length, _exposed.Length);
            for (int i = 0; i < count; i++) _lastAppliedExposed[i] = _exposed[i];
        }
    }

    private bool HasExposureChanged()
    {
        if (_exposed == null || _lastAppliedExposed == null || _exposed.Length != _lastAppliedExposed.Length) return true;
        for (int i = 0; i < _exposed.Length; i++)
        {
            if (_exposed[i] != _lastAppliedExposed[i]) return true;
        }
        return false;
    }

    /// <summary>Per cell: is the space directly above it (world-up) clear of any block? Read-only physics, so
    /// it covers both our own stacked cells and another maw resting on top (PHYSICS.md - never writes).</summary>
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
                if (hit != null && hit.GetComponentInParent<BlockController>() != null) { covered = true; break; }
            }
            _exposed[i] = !covered;
        }
    }
}
