using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Airtight" level type: build without sealing empty space. Open gaps (reachable from the sky
/// or the tower's flanks) are fine; the moment a placement closes the LAST opening of an empty
/// region, that region becomes an air pocket - dark pressure-smoke fills it over a fuse, and a
/// full pocket detonates and costs a life (via the normal hazard/lives flow, so immunity
/// abilities apply). The fuse is the rescue window by design: destroying any sealing block
/// (Zap, Extract, a lucky topple) reconnects the region to open air and vents the smoke
/// harmlessly. One connected sealed region = one pocket = one life, whatever its size; bigger
/// pockets take longer to fill (same smoke rate, more volume). A popped pocket's cells go
/// INERT - the stack above is untouched, and spent space can never charge a second life.
///
/// Pure modifier - no engine changes. Pair it on a LevelDefinition with a normal PlaceBlocks
/// goal. Detection rasterizes the settled stack onto the placement grid (landed block cells +
/// floor terrain + islands) and flood-fills open air from the outside; anything the flood
/// can't reach is sealed. 4-connectivity on purpose: a diagonal crack is not an opening.
///
/// The raster alone lies once a tower TILTS (cell centres round on and off the lattice, and
/// hairline cracks open between leaning bricks), so a coarse sealed region must pass two
/// stricter gates before it can cost a life:
///  - coverage: every candidate cell is measured against the blocks' live colliders. A cell
///    that is mostly brick is brick (a tilt artifact, not air); a half-covered sliver is
///    passable crack but never pocket volume; and a "solid" wall cell that is physically
///    cracked open is a LEAK - the region is not airtight at all.
///  - persistence: a fresh seal must survive ~1s of scans before it arms. Settle drift
///    flickers regions in and out; only a seal that stays sealed is a player mistake.
/// </summary>
[CreateAssetMenu(fileName = "AirPocket", menuName = "Stacking/Levels/Modifiers/Air Pockets (Airtight)")]
public class AirPocketModifier : LevelModifier, ILevelMenuProgressProvider
{
    // The GAME TYPE this modifier turns a level into: the menu and results card read
    // "AIRTIGHT" as the challenge, while the goal (place N / reach X) keeps owning the
    // progress line and end-of-run metric (label-only claim - nulls fall through).
    public string MenuChallengeLabel => "Airtight";
    public string MenuProgressLabel(LevelDefinition level, ProgressStore.LevelBest best, bool completed) => null;
    public ResultMetric? EndOfRunMetric(LevelDefinition level, RunResult result, ProgressStore.LevelBest best) => null;

    [Header("Fuse")]
    [Tooltip("Seconds for the smoke to fill a 1-cell pocket. The rescue window: destroy a sealing block before it fills to vent the pocket harmlessly.")]
    [Min(1f)]
    [SerializeField] private float fuseSeconds = 5f;
    [Tooltip("Extra seconds per cell beyond the first - bigger pockets hold more air, so they fill slower (and the bigger mistake gets the longer rescue window).")]
    [Min(0f)]
    [SerializeField] private float extraSecondsPerCell = 1f;

    [Header("Juice")]
    [Tooltip("Optional authored burst prefab spawned per cell on detonation (base CFXR prefabs only - see ABILITIES.md). Null-safe: the code-built flash + shockwave always play.")]
    [SerializeField] private GameObject popEffect;
    [SerializeField] private float popEffectScale = 1f;

    [Header("Detonation shake (Tremor-style, scales with pocket size)")]
    [Tooltip("Physical kick per sealed cell, delivered as a TremorBlockBehaviour burst from the pocket's centre. For scale: the Tremor brick runs at 1.5 - a 1-cell pop stays well under it (felt, not tower-threatening), while a 4-cell blunder lands ~4x the 1-cell shake.")]
    [Min(0f)]
    [SerializeField] private float shakeStrengthPerCell = 0.45f;
    [SerializeField] private float shakeDurationSeconds = 0.4f;
    [SerializeField] private float shakeRadius = 7f;
    [Tooltip("Cells beyond this stop adding shake - a huge pocket already costs its life; the quake must stay survivable.")]
    [Min(1)]
    [SerializeField] private int shakeCellCap = 6;

    // Cheap periodic rescan on top of the event-driven ones: settle drift and topples can open
    // or close a region without any lock/destroy event firing.
    private const float RescanInterval = 0.5f;

    // Strictness gates (code-owned policy, not asset data - see the class comment). Coverage
    // is the fraction of a cell's area physically occupied by settled geometry, sampled
    // against the blocks' live colliders on a CoverageSamples^2 point grid.
    private const float SolidCoverage = 0.65f;  // >= this: the cell IS brick, whatever the raster says
    private const float OpenCoverage = 0.35f;   // <= this: genuinely breathable air (pocket volume)
    private const int CoverageSamples = 4;
    private const float ArmDelaySeconds = 0.9f; // a seal must survive this long before it arms

    private sealed class Pocket
    {
        public readonly HashSet<Vector2Int> Cells = new();
        public float Elapsed;
        public float FuseTotal;
        public AirPocketFx Fx;
    }

    // A sealed region on probation: seen, not yet armed. No FX, no sound - if it flickers
    // away before ArmDelaySeconds it never existed as far as the player is concerned.
    private sealed class PendingSeal
    {
        public readonly HashSet<Vector2Int> Cells = new();
        public float FirstSeen;
        public bool Touched; // matched by a region this scan (unmatched pendings are pruned)
    }

    private LevelModifierContext _context;
    private readonly List<Pocket> _pockets = new();
    private readonly List<PendingSeal> _pending = new();
    private readonly HashSet<Vector2Int> _spentCells = new();
    private System.Predicate<Vector2Int> _isSpent;
    private float _rescanTimer;
    private bool _rescanQueued;
    private bool _baselineScanDone;
    private float _gridSpacing = 1f;
    private float _datumY;
    private float _clock; // run time accumulated from OnUpdate - pending seals age against it

    // Reused scan buffers - the scan runs on lock/destroy events and a 0.5 s cadence; it must
    // not allocate per pass (GC spikes read as physics stutter).
    private readonly HashSet<Vector2Int> _solid = new();
    private readonly HashSet<Vector2Int> _terrain = new(); // floor + islands: axis-aligned, grid-exact, coverage 1
    private readonly HashSet<Vector2Int> _reached = new();
    private readonly Queue<Vector2Int> _floodQueue = new();
    private readonly List<Vector2> _cellScratch = new();
    private readonly List<HashSet<Vector2Int>> _regionScratch = new();
    private readonly List<Collider2D> _blockColliders = new();
    private readonly List<Bounds> _blockColliderBounds = new(); // AABB prefilter for OverlapPoint
    private readonly Dictionary<Vector2Int, float> _coverageCache = new(); // per-scan memo
    private readonly HashSet<Vector2Int> _volumeScratch = new();

    public override void OnLevelStart(LevelModifierContext context)
    {
        _context = context;
        _pockets.Clear();
        _pending.Clear();
        _spentCells.Clear();
        _isSpent = c => _spentCells.Contains(c); // cached predicate - RemoveWhere without a per-call closure
        _rescanTimer = 0f;
        _rescanQueued = false;
        _baselineScanDone = false;
        _clock = 0f;

        GameModeConfig config = context.GameManager != null ? context.GameManager.ActiveConfig : null;
        _gridSpacing = config != null ? config.GridSpacing : 1f;
        _datumY = context.GameManager != null ? context.GameManager.floorOriginY : 0f;

        // Destroys re-open regions (the rescue path); locks are covered by OnBlockLocked.
        GameEvents.BlockDestroyed += HandleBlockDestroyed;
    }

    public override void OnLevelEnd(LevelModifierContext context)
    {
        GameEvents.BlockDestroyed -= HandleBlockDestroyed;
        for (int i = 0; i < _pockets.Count; i++)
        {
            if (_pockets[i].Fx != null) _pockets[i].Fx.DestroyNow();
        }
        _pockets.Clear();
    }

    public override void OnBlockLocked(LevelModifierContext context, int totalBlocksPlaced)
    {
        _rescanQueued = true;
    }

    private void HandleBlockDestroyed(BlockController block)
    {
        _rescanQueued = true;
    }

    public override void OnUpdate(LevelModifierContext context, float deltaTime)
    {
        if (context.GameManager == null || context.GameManager.isGameOver) return;

        _clock += deltaTime;
        _rescanTimer -= deltaTime;
        if (_rescanQueued || _rescanTimer <= 0f)
        {
            _rescanQueued = false;
            _rescanTimer = RescanInterval;
            Rescan();
        }

        // Tick armed fuses; drive the smoke fill; pop what reaches full.
        for (int i = _pockets.Count - 1; i >= 0; i--)
        {
            Pocket pocket = _pockets[i];
            pocket.Elapsed += deltaTime;
            if (pocket.Fx != null) pocket.Fx.SetFill(pocket.Elapsed / pocket.FuseTotal);

            if (pocket.Elapsed >= pocket.FuseTotal)
            {
                Detonate(pocket);
                _pockets.RemoveAt(i);
                // The pop may have taken the last life - leave the wreckage in peace (no
                // second blast, quake or charge after the run has ended).
                if (context.GameManager.isGameOver) return;
            }
        }
    }

    // ---- Detection: raster -> flood -> regions -> pocket bookkeeping -----------------------

    private void Rescan()
    {
        BuildSolidRaster();
        FindSealedRegions(_regionScratch);

        // Anything sealed BEFORE the first placement is terrain, not a player mistake (a
        // mis-authored interior floor socket, or any future floor/island quirk): mark it
        // inert from birth instead of charging a life five seconds into an untouched round.
        if (!_baselineScanDone)
        {
            _baselineScanDone = true;
            for (int r = 0; r < _regionScratch.Count; r++)
            {
                foreach (Vector2Int cell in _regionScratch[r]) _spentCells.Add(cell);
            }
            _regionScratch.Clear();
            return;
        }

        RefineRegions(_regionScratch);
        ReconcilePockets(_regionScratch);
    }

    // Solid cells on the placement grid: settled block cells, floor terrain columns (minus
    // carved pockets - those are OPEN until built over), and island cells. The active piece
    // and falling-away debris are NOT solid: a seal only counts once its lid has LANDED, and
    // a block tumbling through a gap must not flicker regions closed mid-flight.
    private void BuildSolidRaster()
    {
        _solid.Clear();
        _terrain.Clear();
        _blockColliders.Clear();
        _blockColliderBounds.Clear();

        // Refresh the grid mapping from the live config each scan: OnLevelStart can run
        // before the mode resolves, and every raster below (blocks, islands, floor) must
        // share one lattice with the segments being read.
        GameModeConfig config = _context.GameManager != null ? _context.GameManager.ActiveConfig : null;
        if (config != null) _gridSpacing = config.GridSpacing;
        if (_context.GameManager != null) _datumY = _context.GameManager.floorOriginY;

        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded || block.IsFallingAway) continue;
            block.GetWorldCellCenters(_cellScratch);
            for (int c = 0; c < _cellScratch.Count; c++)
            {
                _solid.Add(WorldToCell(_cellScratch[c]));
            }
            // The same blocks' live colliders, for the coverage pass: where a tilted brick
            // PHYSICALLY is, as opposed to where its centres round to.
            IReadOnlyList<Collider2D> solids = block.SolidColliders;
            for (int c = 0; c < solids.Count; c++)
            {
                if (solids[c] == null) continue;
                _blockColliders.Add(solids[c]);
                _blockColliderBounds.Add(solids[c].bounds);
            }
        }

        StaticSupportIslandManager.GetWorldCellCenters(_cellScratch);
        for (int c = 0; c < _cellScratch.Count; c++)
        {
            Vector2Int cell = WorldToCell(_cellScratch[c]);
            _solid.Add(cell);
            _terrain.Add(cell);
        }

        IReadOnlyList<FloorSegmentConfig> segments = config != null ? config.FloorSegments : null;
        for (int s = 0; segments != null && s < segments.Count; s++)
        {
            FloorSegmentConfig segment = segments[s];
            if (segment == null) continue;
            for (int i = 0; i < segment.ColumnCount; i++)
            {
                int column = segment.LeftColumn + i;
                int height = segment.GetColumnHeightCells(i);
                // The floor body extends far below the datum (24 u of collider): rasterize
                // sub-datum rows per column so the border flood can never tunnel UNDER a flat
                // floor. Depth -5 leaves two solid rows under the deepest legal pocket carve
                // (depths cap at 3 - GameModeConfig), so a carved socket at row -3 still has
                // sealed ground beneath it instead of silently no-oping the Remove below.
                for (int r = -5; r < height; r++)
                {
                    var cell = new Vector2Int(column, r);
                    _solid.Add(cell);
                    _terrain.Add(cell);
                }
            }
            IReadOnlyList<FloorPocketConfig> floorPockets = segment.Pockets;
            for (int p = 0; floorPockets != null && p < floorPockets.Count; p++)
            {
                FloorPocketConfig fp = floorPockets[p];
                if (fp == null) continue;
                int height = segment.GetColumnHeightCells(fp.Column);
                var carved = new Vector2Int(segment.LeftColumn + fp.Column, height - fp.DepthCells);
                _solid.Remove(carved);
                _terrain.Remove(carved);
            }
        }
    }

    private Vector2Int WorldToCell(Vector2 worldCenter)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldCenter.x / _gridSpacing),
            Mathf.RoundToInt((worldCenter.y - _datumY) / _gridSpacing - 0.5f));
    }

    private Vector2 CellToWorld(Vector2Int cell)
    {
        return new Vector2(cell.x * _gridSpacing, _datumY + (cell.y + 0.5f) * _gridSpacing);
    }

    // Flood open air inward from a border ring around the solids' bounding box; every empty
    // in-box cell the flood can't reach is sealed. Regions made ONLY of spent cells are inert.
    private void FindSealedRegions(List<HashSet<Vector2Int>> regions)
    {
        regions.Clear();
        if (_solid.Count == 0) return;

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (Vector2Int cell in _solid)
        {
            if (cell.x < minX) minX = cell.x;
            if (cell.x > maxX) maxX = cell.x;
            if (cell.y < minY) minY = cell.y;
            if (cell.y > maxY) maxY = cell.y;
        }
        // One ring of guaranteed-open border; the flood enters every gap from there.
        minX--; maxX++; minY--; maxY++;

        _reached.Clear();
        _floodQueue.Clear();
        for (int x = minX; x <= maxX; x++)
        {
            TryFlood(new Vector2Int(x, minY));
            TryFlood(new Vector2Int(x, maxY));
        }
        for (int y = minY; y <= maxY; y++)
        {
            TryFlood(new Vector2Int(minX, y));
            TryFlood(new Vector2Int(maxX, y));
        }
        while (_floodQueue.Count > 0)
        {
            Vector2Int cell = _floodQueue.Dequeue();
            if (cell.x > minX) TryFlood(new Vector2Int(cell.x - 1, cell.y));
            if (cell.x < maxX) TryFlood(new Vector2Int(cell.x + 1, cell.y));
            if (cell.y > minY) TryFlood(new Vector2Int(cell.x, cell.y - 1));
            if (cell.y < maxY) TryFlood(new Vector2Int(cell.x, cell.y + 1));
        }

        // Sealed = empty, in-box, unreached. Group into 4-connected regions.
        for (int x = minX + 1; x < maxX; x++)
        {
            for (int y = minY + 1; y < maxY; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (_solid.Contains(cell) || _reached.Contains(cell)) continue;
                if (InAnyRegion(regions, cell)) continue;

                var region = new HashSet<Vector2Int>();
                _floodQueue.Clear();
                _floodQueue.Enqueue(cell);
                region.Add(cell);
                while (_floodQueue.Count > 0)
                {
                    Vector2Int current = _floodQueue.Dequeue();
                    GrowRegion(region, new Vector2Int(current.x - 1, current.y));
                    GrowRegion(region, new Vector2Int(current.x + 1, current.y));
                    GrowRegion(region, new Vector2Int(current.x, current.y - 1));
                    GrowRegion(region, new Vector2Int(current.x, current.y + 1));
                }

                // Already-detonated cells are inert scar tissue: they may CONNECT fresh
                // sealed cells (the flood treats them as passable space) but they never
                // count again - not toward the fuse, the shake, the smoke, or a region
                // being "live" at all. Without this a later 1-cell seal next to an old
                // scar re-blasted at the scar's full size.
                region.RemoveWhere(_isSpent);
                if (region.Count > 0) regions.Add(region);
            }
        }
    }

    private void TryFlood(Vector2Int cell)
    {
        if (_solid.Contains(cell) || _reached.Contains(cell)) return;
        _reached.Add(cell);
        _floodQueue.Enqueue(cell);
    }

    private void GrowRegion(HashSet<Vector2Int> region, Vector2Int cell)
    {
        if (_solid.Contains(cell) || _reached.Contains(cell) || region.Contains(cell)) return;
        region.Add(cell);
        _floodQueue.Enqueue(cell);
    }

    private static bool InAnyRegion(List<HashSet<Vector2Int>> regions, Vector2Int cell)
    {
        for (int i = 0; i < regions.Count; i++)
        {
            if (regions[i].Contains(cell)) return true;
        }
        return false;
    }

    // Second, stricter pass over the coarse regions: keep only genuinely breathable volume,
    // sealed behind genuinely solid walls. The lattice lies under tilt - a mostly-covered
    // crack cell must read as brick, and a "solid" wall cell with a real crack is a leak.
    // Strictness errs toward the player: a wrongly-vented pocket costs nothing, a wrongly
    // armed one costs a life.
    private void RefineRegions(List<HashSet<Vector2Int>> regions)
    {
        if (regions.Count == 0) return;
        _coverageCache.Clear();

        for (int r = regions.Count - 1; r >= 0; r--)
        {
            HashSet<Vector2Int> region = regions[r];

            _volumeScratch.Clear();
            foreach (Vector2Int cell in region)
            {
                float coverage = CellCoverage(cell);
                if (coverage >= SolidCoverage) continue;                // mostly brick: a tilt artifact, not air
                if (coverage <= OpenCoverage) _volumeScratch.Add(cell); // true air: pocket volume
                // in between: a sliver crack - passable space, but never volume to fill
            }

            bool leaky = _volumeScratch.Count == 0;
            if (!leaky)
            {
                foreach (Vector2Int cell in region)
                {
                    if (WallLeaksAt(region, new Vector2Int(cell.x - 1, cell.y)) ||
                        WallLeaksAt(region, new Vector2Int(cell.x + 1, cell.y)) ||
                        WallLeaksAt(region, new Vector2Int(cell.x, cell.y - 1)) ||
                        WallLeaksAt(region, new Vector2Int(cell.x, cell.y + 1)))
                    {
                        leaky = true;
                        break;
                    }
                }
            }

            if (leaky)
            {
                regions.RemoveAt(r);
                continue;
            }

            // The pocket is its breathable cells only: slivers neither fill with smoke nor
            // count toward the fuse or the blast.
            region.Clear();
            foreach (Vector2Int cell in _volumeScratch) region.Add(cell);
        }
    }

    private bool WallLeaksAt(HashSet<Vector2Int> region, Vector2Int neighbor)
    {
        // A raster-solid wall cell that is physically cracked open means the region is not
        // airtight at all, whatever the flood said.
        return _solid.Contains(neighbor) && !region.Contains(neighbor)
            && CellCoverage(neighbor) < SolidCoverage;
    }

    // Fraction of the cell's area occupied by settled geometry, on a CoverageSamples^2 point
    // grid. Terrain cells are axis-aligned and grid-exact (coverage 1 by construction); block
    // cells are sampled against the live colliders, so tilt contributes what it PHYSICALLY
    // covers. Memoized per scan - regions and their walls share cells across checks.
    private float CellCoverage(Vector2Int cell)
    {
        if (_terrain.Contains(cell)) return 1f;
        if (_coverageCache.TryGetValue(cell, out float cached)) return cached;

        Vector2 center = CellToWorld(cell);
        float step = _gridSpacing / CoverageSamples;
        float originX = center.x - 0.5f * _gridSpacing + 0.5f * step;
        float originY = center.y - 0.5f * _gridSpacing + 0.5f * step;

        int hits = 0;
        for (int sy = 0; sy < CoverageSamples; sy++)
        {
            for (int sx = 0; sx < CoverageSamples; sx++)
            {
                var point = new Vector2(originX + sx * step, originY + sy * step);
                for (int i = 0; i < _blockColliders.Count; i++)
                {
                    // 2D-only AABB prefilter (Bounds.Contains also tests z, which would
                    // reject everything for blocks parked at a nonzero transform z).
                    Bounds b = _blockColliderBounds[i];
                    if (point.x < b.min.x || point.x > b.max.x ||
                        point.y < b.min.y || point.y > b.max.y) continue;
                    Collider2D collider = _blockColliders[i];
                    if (collider != null && collider.OverlapPoint(point))
                    {
                        hits++;
                        break;
                    }
                }
            }
        }

        float result = hits / (float)(CoverageSamples * CoverageSamples);
        _coverageCache[cell] = result;
        return result;
    }

    // Match this scan's sealed regions to armed pockets by cell overlap: a matched pocket
    // keeps its fuse (merges keep the FURTHEST fuse - the older mistake stays the deadline),
    // an unmatched pocket has been re-opened and VENTS harmlessly, an unmatched region is a
    // fresh seal and arms a new fuse.
    private void ReconcilePockets(List<HashSet<Vector2Int>> regions)
    {
        // The overwhelmingly common scan: nothing sealed, nothing armed, nothing pending.
        if (regions.Count == 0 && _pockets.Count == 0 && _pending.Count == 0) return;

        var matched = new bool[regions.Count];

        for (int p = _pockets.Count - 1; p >= 0; p--)
        {
            Pocket pocket = _pockets[p];
            int bestRegion = -1;
            foreach (Vector2Int cell in pocket.Cells)
            {
                for (int r = 0; r < regions.Count; r++)
                {
                    if (regions[r].Contains(cell)) { bestRegion = r; break; }
                }
                if (bestRegion >= 0) break;
            }

            if (bestRegion < 0)
            {
                Vent(pocket);
                _pockets.RemoveAt(p);
                continue;
            }

            if (matched[bestRegion])
            {
                // Two old pockets now share one region: keep the further-along fuse on the
                // survivor (the pocket matched earlier), drop this one quietly.
                Pocket survivor = FindPocketForRegion(regions[bestRegion]);
                if (survivor != null && pocket.Elapsed > survivor.Elapsed) survivor.Elapsed = pocket.Elapsed;
                if (pocket.Fx != null) pocket.Fx.DestroyNow();
                _pockets.RemoveAt(p);
                continue;
            }

            matched[bestRegion] = true;
            if (!pocket.Cells.SetEquals(regions[bestRegion]))
            {
                // Preserve the fill FRACTION across the size change: a split that shrinks a
                // pocket must not leave Elapsed past the new, shorter fuse (that detonated
                // instantly with no rescue window), and a grown pocket keeps its visual level.
                float fillFraction = pocket.FuseTotal > 0f ? Mathf.Clamp01(pocket.Elapsed / pocket.FuseTotal) : 0f;
                pocket.Cells.Clear();
                pocket.Cells.UnionWith(regions[bestRegion]);
                pocket.FuseTotal = FuseFor(pocket.Cells.Count);
                pocket.Elapsed = fillFraction * pocket.FuseTotal;
                RebuildFx(pocket);
            }
        }

        // Fresh seals go on probation, not straight to a fuse: the region must be seen again
        // across ArmDelaySeconds of scans before it arms. Settle drift and tilt flicker
        // regions into existence for a scan or two; those evaporate here, silently.
        for (int p = 0; p < _pending.Count; p++) _pending[p].Touched = false;

        for (int r = 0; r < regions.Count; r++)
        {
            if (matched[r]) continue;

            PendingSeal pending = FindPendingForRegion(regions[r]);
            if (pending == null)
            {
                pending = new PendingSeal { FirstSeen = _clock, Touched = true };
                pending.Cells.UnionWith(regions[r]);
                _pending.Add(pending);
                continue;
            }

            pending.Touched = true;
            pending.Cells.Clear();
            pending.Cells.UnionWith(regions[r]);
            if (_clock - pending.FirstSeen < ArmDelaySeconds) continue;

            _pending.Remove(pending);
            var pocket = new Pocket { FuseTotal = FuseFor(regions[r].Count) };
            pocket.Cells.UnionWith(regions[r]);
            RebuildFx(pocket);
            _pockets.Add(pocket);
            SfxPlayer.Play("pocket_seal", 0.9f);
        }

        // A pending seal no region claimed this scan has re-opened (or was never real).
        for (int p = _pending.Count - 1; p >= 0; p--)
        {
            if (!_pending[p].Touched) _pending.RemoveAt(p);
        }
    }

    private PendingSeal FindPendingForRegion(HashSet<Vector2Int> region)
    {
        for (int i = 0; i < _pending.Count; i++)
        {
            if (_pending[i].Touched) continue; // already claimed by another region this scan
            foreach (Vector2Int cell in _pending[i].Cells)
            {
                if (region.Contains(cell)) return _pending[i];
            }
        }
        return null;
    }

    private Pocket FindPocketForRegion(HashSet<Vector2Int> region)
    {
        for (int i = 0; i < _pockets.Count; i++)
        {
            foreach (Vector2Int cell in _pockets[i].Cells)
            {
                if (region.Contains(cell)) return _pockets[i];
            }
        }
        return null;
    }

    private float FuseFor(int cellCount) => fuseSeconds + extraSecondsPerCell * Mathf.Max(0, cellCount - 1);

    // ---- Outcomes ---------------------------------------------------------------------------

    private void Vent(Pocket pocket)
    {
        if (pocket.Fx != null) pocket.Fx.Vent();
        SfxPlayer.Play("pocket_vent", 0.95f);
    }

    private void Detonate(Pocket pocket)
    {
        foreach (Vector2Int cell in pocket.Cells)
        {
            _spentCells.Add(cell);
        }

        if (pocket.Fx != null) pocket.Fx.Detonate(popEffect, popEffectScale);
        SfxPlayer.Play("pocket_pop", 1f);

        // The blast rocks the tower, scaled by how much air was trapped: reuses the Tremor
        // brick's burst (velocity kicks with shear + epicenter falloff - PHYSICS.md I1-safe,
        // Static/frozen blocks immune). A 1-cell pop is a shiver; a 4-cell one is a real quake.
        int shakeCells = Mathf.Min(pocket.Cells.Count, shakeCellCap);
        float strength = shakeStrengthPerCell * shakeCells;
        if (strength > 0f)
        {
            Vector2 epicenter = Vector2.zero;
            foreach (Vector2Int cell in pocket.Cells) epicenter += CellToWorld(cell);
            epicenter /= pocket.Cells.Count;

            var quake = new GameObject("AirPocketQuake");
            quake.transform.position = epicenter;
            quake.AddComponent<TremorBlockBehaviour>()
                .Arm(epicenter, strength, shakeDurationSeconds, shakeRadius);
            Object.Destroy(quake, shakeDurationSeconds + 0.25f);
        }
        TowerCameraController.Impact(Mathf.Min(0.2f + 0.06f * shakeCells, 0.5f));

        // The normal hazard charge: respects life-loss immunity, updates the hearts HUD, and
        // ends the run on the last life - identical policy to every other life the game takes.
        _context.GameManager.LoseLifeToHazard();
    }

    private void RebuildFx(Pocket pocket)
    {
        float fill = pocket.Fx != null ? pocket.Elapsed / pocket.FuseTotal : 0f;
        float audioTime = pocket.Fx != null ? pocket.Fx.FillAudioTime : 0f;
        if (pocket.Fx != null) pocket.Fx.DestroyNow();

        _cellScratch.Clear();
        foreach (Vector2Int cell in pocket.Cells)
        {
            _cellScratch.Add(CellToWorld(cell));
        }
        pocket.Fx = AirPocketFx.Create(_cellScratch, _gridSpacing);
        pocket.Fx.SetFill(fill);
        // A grown/merged pocket keeps its crescendo where it was - an audible restart
        // mid-fuse broke the "one long rising bed" promise.
        pocket.Fx.ResumeFillAudioAt(audioTime);
    }
}
