using UnityEngine;

/// <summary>
/// Runtime half of the Curse. While ANY top cell of the landed piece is exposed to the sky,
/// every COUNTED placement burns one sigil; at zero the curse fires - one LIFE through the
/// hazard path (<see cref="GameManager.LoseLifeToHazard"/>, same as a Maw bite, so
/// LifeLossImmunity/Ward absorb it) - and the countdown restarts. Burying every top cell
/// pacifies it; a later re-expose (cover destroyed or knocked off) restarts a FRESH countdown.
///
/// The tick source is <see cref="GameEvents.BlockPlaced"/> (the ledger's counted-placement
/// event), NOT BlockLocked - deliberately (review 2026-08-02): BlockLocked also fires for a
/// magma's melt pips (one magma = 5 locks = a whole countdown in one turn) and for a piece
/// dropped off the board (whose lost life would cascade into a second one here). The ledger
/// already gates both. The curse's OWN placement raises BlockPlaced in the same frame Begin
/// runs, so events from the begin frame are swallowed (frame latch - count-based latching
/// would desync if the ledger ever suppressed our event).
///
/// This behaviour is the ONE exposure authority: it scans per cell (read-only physics,
/// PHYSICS.md) and feeds the skin, so eye/smoke and the countdown can never disagree (the
/// old twin-scan drifted up to 0.5s apart). Event-driven rescans are deferred one frame -
/// every destroy site raises BlockDestroyed BEFORE Object.Destroy, so a same-frame scan
/// still sees the dying cover.
/// </summary>
public class CurseBlockBehaviour : MonoBehaviour
{
    private const float CoverFallbackInterval = 0.5f; // catches shoves/slides that raise no event

    private readonly Collider2D[] _hits = new Collider2D[12];

    private CurseBlockSkin _skin;
    private BlockController _self;
    private BoxCollider2D[] _cells;    // non-trigger cells, child order = skin cell order (contract)
    private bool[] _cellExposed;
    private ContactFilter2D _filter;
    private int _countdown;
    private int _remaining;
    private bool _exposed;
    private float _rescanTimer;
    private int _beginFrame;
    private int _dirtyAtFrame = -1;    // event-driven rescan runs on a LATER frame (deferred destroys)
    private float _spacing = 1f;

    public void Begin(CurseBlockSkin skin, int countdown)
    {
        _skin = skin;
        _self = GetComponent<BlockController>();
        _cells = CollectCells();
        _cellExposed = new bool[_cells.Length];
        _filter = new ContactFilter2D { useTriggers = false, useLayerMask = false };
        _countdown = Mathf.Max(1, countdown);
        _remaining = _countdown;
        _beginFrame = Time.frameCount;
        _spacing = _self != null ? Mathf.Max(0.01f, _self.GridSpacing) : 1f;

        GameEvents.BlockPlaced += HandleBlockPlaced;
        GameEvents.BlockDestroyed += HandleTowerChanged;
        _exposed = ScanExposure();
        PushToSkin();
    }

    private BoxCollider2D[] CollectCells()
    {
        BoxCollider2D[] all = GetComponentsInChildren<BoxCollider2D>();
        int n = 0;
        for (int i = 0; i < all.Length; i++) if (all[i] != null && !all[i].isTrigger) n++;
        var cells = new BoxCollider2D[n];
        int w = 0;
        for (int i = 0; i < all.Length; i++) if (all[i] != null && !all[i].isTrigger) cells[w++] = all[i];
        return cells;
    }

    private void OnDestroy()
    {
        GameEvents.BlockPlaced -= HandleBlockPlaced;
        GameEvents.BlockDestroyed -= HandleTowerChanged;
    }

    // Inert while the run is over (no detonations over the results screen) or while the block
    // is being carried off-board by a rescue (Rebound's RescueLift disables the controller
    // and beams the block away over ~0.5s - a "saved" curse must not fire mid-flight).
    private bool Inert =>
        (GameManager.Instance != null && GameManager.Instance.isGameOver)
        || _self == null || !_self.enabled;

    private void Update()
    {
        if (Inert) return;

        // Event-driven rescan, one frame late: Destroy is deferred to end of frame, so only
        // NOW is the destroyed cover actually gone from the physics world.
        if (_dirtyAtFrame >= 0 && Time.frameCount > _dirtyAtFrame)
        {
            _dirtyAtFrame = -1;
            _rescanTimer = CoverFallbackInterval;
            RefreshExposure();
            return;
        }

        // Fallback rescan on scaled time (a pause freezes the curse, PHYSICS.md): covers
        // slides and shoves that raise no event, e.g. the burying brick getting nudged off.
        _rescanTimer -= Time.deltaTime;
        if (_rescanTimer > 0f) return;
        _rescanTimer = CoverFallbackInterval;
        RefreshExposure();
    }

    private void HandleTowerChanged(BlockController _) => _dirtyAtFrame = Time.frameCount;

    private void HandleBlockPlaced(int totalPlaced)
    {
        if (Inert) return;
        if (Time.frameCount == _beginFrame) return; // our own placement never ticks

        // Scan FIRST, with the just-locked piece already at its landed pose: a placement that
        // buries the curse pacifies instead of ticking - and one that knocks the cover OFF
        // gets the fresh-countdown grace (it re-exposed; it doesn't also burn sigil one).
        bool wasExposed = _exposed;
        RefreshExposure();
        if (!_exposed || !wasExposed) return;

        _remaining--;
        if (_remaining > 0)
        {
            _skin?.PlaySigilBurn(_remaining, _countdown);
            SfxPlayer.Play("curse_tick", 0.6f, 0.05f);
            return;
        }
        Fire();
    }

    private void Fire()
    {
        _remaining = _countdown;   // the hex re-arms immediately; ignoring it forever keeps costing
        _skin?.PlayFire(_remaining, _countdown);
        SfxPlayer.Play("curse_fire", 1f);  // bespoke detonation (ElevenLabs, Nick 2026-08-02)
        ImpactFx.ImpactPunch(0.05f, 0.16f, 0.18f);
        if (GameManager.Instance != null) GameManager.Instance.LoseLifeToHazard();
    }

    private void RefreshExposure()
    {
        bool wasExposed = _exposed;
        _exposed = ScanExposure();
        if (_exposed != wasExposed)
        {
            // Freshly dug up -> a brand-new countdown (never resume a half-burned one: the
            // player just lost their cover, piling a near-instant life loss on top is unfair).
            if (_exposed) _remaining = _countdown;
            else SfxPlayer.Play("curse_seal", 0.8f);
        }
        PushToSkin(); // always: per-cell exposure can shift without the aggregate flipping
    }

    private void PushToSkin() => _skin?.SetExposure(_cellExposed);

    // Fill the per-cell exposure map; returns whether ANY cell is open to the sky. A cell is
    // covered when something rests in the probe band just above it: the piece's own higher
    // cells, any LANDED block, or static terrain (frozen blocks, islands). A falling piece is
    // not cover - it hasn't committed yet.
    private bool ScanExposure()
    {
        bool any = false;
        Vector2 size = new Vector2(_spacing * 0.72f, _spacing * 0.18f);
        for (int c = 0; c < _cells.Length; c++)
        {
            BoxCollider2D cell = _cells[c];
            if (cell == null) { _cellExposed[c] = false; continue; }

            Vector2 probe = (Vector2)cell.transform.TransformPoint(cell.offset) + Vector2.up * (_spacing * 0.62f);
            bool exposed = !IsCovered(probe, size, cell);
            _cellExposed[c] = exposed;
            any |= exposed;
        }
        return any;
    }

    private bool IsCovered(Vector2 probe, Vector2 size, BoxCollider2D probingCell)
    {
        int n = Physics2D.OverlapBox(probe, size, 0f, _filter, _hits);
        for (int h = 0; h < n; h++)
        {
            Collider2D hit = _hits[h];
            if (hit == null || hit == probingCell) continue;
            if (hit.transform.IsChildOf(transform))
            {
                // Own upper cell: only counts as cover if it actually sits above the probing cell
                // (a side-by-side neighbour overlapping the probe band must not self-bury the piece).
                if (hit.transform.position.y > probingCell.transform.position.y + _spacing * 0.25f) return true;
                continue;
            }
            BlockController bc = hit.GetComponentInParent<BlockController>();
            if (bc != null) { if (bc.HasLanded) return true; continue; }
            // No controller: static world geometry (frozen terrain, islands) is honest cover.
            Rigidbody2D body = hit.attachedRigidbody;
            if (body == null || body.bodyType == RigidbodyType2D.Static) return true;
        }
        return false;
    }
}
