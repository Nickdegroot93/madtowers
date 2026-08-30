using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Void Zones" level type: forbidden rectangles torn into the sky. They spawn ahead of the
/// tower peak like sky islands (you always see them coming), the FALLING piece steers through
/// them freely - but a LANDED block overlapping one is sucked in and costs a life. Absolute
/// law: blocks pushed in later by a topple or settle drift are devoured too, and cascades can
/// drain multiple lives. The skill axis is ROUTING: the tower must grow around the voids, not
/// straight up.
///
/// Fairness comes from visibility plus the route guarantee: a zone never spawns where it
/// would wall off the sky - at least WidestBlockColumns (4) of clear reachable columns always
/// remain past one of its sides (the island guardrail math, PHYSICS.md reach guarantee), and
/// zones never materialize overlapping islands, other zones, the tower or the falling piece.
///
/// Pure modifier - no engine changes. Detection runs on lock + a settle-drift cadence; the
/// suck is the legal doomed-block animation (kinematic first - the Hardline recipe), then
/// the normal destruction flow (BLOCKS.md accounting) and LoseLifeToHazard.
/// </summary>
[CreateAssetMenu(fileName = "VoidZones", menuName = "Stacking/Levels/Modifiers/Void Zones")]
public class VoidZoneModifier : LevelModifier, ILevelMenuProgressProvider
{
    // The GAME TYPE this modifier turns a level into: the menu and results card read
    // "VOID ZONES" as the challenge, while the goal keeps owning the progress line and
    // end-of-run metric (label-only claim - nulls fall through).
    public string MenuChallengeLabel => "Void Zones";
    public string MenuProgressLabel(LevelDefinition level, ProgressStore.LevelBest best, bool completed) => null;
    public ResultMetric? EndOfRunMetric(LevelDefinition level, RunResult result, ProgressStore.LevelBest best) => null;

    [Header("Spawning (mirrors the sky-island dials)")]
    [Tooltip("Tower height (world units above the floor) at which the first zone appears.")]
    [Min(2f)]
    [SerializeField] private float firstZoneHeight = 10f;
    [Tooltip("Vertical distance between zone rolls after the first.")]
    [Min(2f)]
    [SerializeField] private float heightInterval = 8f;
    [Range(0f, 1f)]
    [SerializeField] private float spawnChance = 0.85f;
    [Tooltip("Zones materialize this far above the tower peak - always visible before you get there, never an ambush.")]
    [Min(2f)]
    [SerializeField] private float spawnAheadHeight = 7f;
    [Tooltip("Zone width in columns, rolled per zone.")]
    [SerializeField] private int zoneWidthMin = 2;
    [SerializeField] private int zoneWidthMax = 3;
    [Tooltip("Zone height in cells.")]
    [Min(1)]
    [SerializeField] private int zoneHeightCells = 2;
    [Tooltip("Lateral placement band in columns (like the island band).")]
    [SerializeField] private int minColumn = -6;
    [SerializeField] private int maxColumn = 6;
    [Tooltip("0 = unlimited. Otherwise the run stops spawning zones after this many.")]
    [Min(0)]
    [SerializeField] private int maxZonesPerRun = 0;
    [Tooltip("The RNG floor (0 = off): by 85% of the goal, at least this many zones have " +
             "spawned, paced proportionally but TRAILING the dice (nothing is owed until a " +
             "full pace-step of the goal has passed; at 43% of the goal, half the minimum). " +
             "The dice keep owning WHERE and exactly WHEN - the quota only forces a spawn " +
             "when a lucky streak of missed rolls falls behind it, so a run can never " +
             "cruise to the win without meeting the mechanic (Nick 2026-08-24: one zone " +
             "all round on the chapter-3 debut). A forced zone also consumes the next " +
             "height-row roll, so quota and dice can never double-spawn in one band.")]
    [Min(0)]
    [SerializeField] private int minZonesPerRun = 0;

    [Header("The suck")]
    [Tooltip("Seconds the devour animation takes before the block is destroyed and the life charged.")]
    [Min(0.1f)]
    [SerializeField] private float suckSeconds = 0.45f;
    [Tooltip("A cell must reach this far INTO the zone (world units) before it counts as touching - a hair's-width physics graze is not a mistake.")]
    [Min(0f)]
    [SerializeField] private float overlapInset = 0.15f;

    // Settle drift and topples move blocks without any lock event - the absolute law needs
    // a cadence sweep on top of the lock-driven checks.
    private const float SweepInterval = 0.5f;
    private const int PlacementAttempts = 24;

    private sealed class Zone
    {
        public Rect WorldRect;
        public VoidZoneFx Fx;
        // A zone only becomes lethal once its tear-open animation has finished - the danger
        // must never outrun what the player can see, whatever spawnAheadHeight is tuned to.
        public float ArmedAtTime;
    }

    private LevelModifierContext _context;
    private readonly List<Zone> _zones = new();
    private readonly List<Vector2> _cellScratch = new();
    private readonly List<Vector2> _islandScratch = new();
    // Quota retry cadence: a forced spawn can fail placement (route guarantee, overlaps) -
    // retry on a beat, not per frame (24 placement attempts per try).
    private const float QuotaRetryInterval = 0.75f;

    private float _nextZoneY;
    private bool _wardUsed;   // VOID WARD boost: one spare per run (clone field - resets per run)
    private int _zonesSpawned;
    private int _blocksPlaced;
    private float _quotaTimer;
    private float _sweepTimer;
    private bool _sweepQueued;
    private float _gridSpacing = 1f;

    public override void OnLevelStart(LevelModifierContext context)
    {
        _context = context;
        _zones.Clear();
        _wardUsed = false;
        _zonesSpawned = 0;
        _blocksPlaced = 0;
        _quotaTimer = 0f;
        _sweepTimer = 0f;
        _sweepQueued = false;

        GameModeConfig config = context.GameManager != null ? context.GameManager.ActiveConfig : null;
        _gridSpacing = config != null ? config.GridSpacing : 1f;
        float floorY = context.GameManager != null ? context.GameManager.floorOriginY : 0f;
        _nextZoneY = floorY + firstZoneHeight;
    }

    public override void OnLevelEnd(LevelModifierContext context)
    {
        for (int i = 0; i < _zones.Count; i++)
        {
            if (_zones[i].Fx != null) Object.Destroy(_zones[i].Fx.gameObject);
        }
        _zones.Clear();
    }

    public override void OnBlockLocked(LevelModifierContext context, int totalBlocksPlaced)
    {
        _blocksPlaced = totalBlocksPlaced;   // feeds the quota's goal-progress fraction
        _sweepQueued = true;
    }

    public override void OnUpdate(LevelModifierContext context, float deltaTime)
    {
        if (context.GameManager == null || context.GameManager.isGameOver) return;

        // Roll zone spawns ahead of the tower peak, one height row at a time (monotonic,
        // each row rolled exactly once - the island scheduler's pattern).
        float peak = context.GameManager.maxHeight;
        while (peak + spawnAheadHeight >= _nextZoneY &&
               (maxZonesPerRun <= 0 || _zonesSpawned < maxZonesPerRun))
        {
            if (Random.value <= spawnChance) TrySpawnZone(_nextZoneY);
            _nextZoneY += heightInterval;
        }

        // THE RNG FLOOR: by 85% of the goal the full minimum must have spawned, paced
        // proportionally along the way. When a lucky streak (missed rolls, or rolls whose
        // placement failed and was silently lost) falls behind that pace, force a spawn at
        // the normal ahead-of-peak position - same placement RNG, same route guarantee,
        // just no dice for WHETHER. Retries on a beat while placement keeps failing.
        // FloorToInt so the quota TRAILS the dice instead of leading them: nothing is owed
        // until a full pace-step of the goal has passed (CeilToInt owed a zone after the
        // very first block - the chapter-8 opening ambush, Nick 2026-08-30).
        _quotaTimer -= deltaTime;
        if (minZonesPerRun > 0 && _zonesSpawned < minZonesPerRun && _quotaTimer <= 0f
            && (maxZonesPerRun <= 0 || _zonesSpawned < maxZonesPerRun))
        {
            int owed = Mathf.FloorToInt(Mathf.Clamp01(GoalProgress01(context) / 0.85f) * minZonesPerRun);
            if (_zonesSpawned < owed)
            {
                _quotaTimer = QuotaRetryInterval;
                float spawnY = peak + spawnAheadHeight;
                if (TrySpawnZone(spawnY))
                {
                    // A forced zone consumes the roller's next row - otherwise the dice
                    // fire again in the same band moments later and two zones cluster.
                    _nextZoneY = Mathf.Max(_nextZoneY, spawnY + heightInterval);
                }
            }
        }

        _sweepTimer -= deltaTime;
        if (_sweepQueued || _sweepTimer <= 0f)
        {
            _sweepQueued = false;
            _sweepTimer = SweepInterval;
            SweepForViolations();
        }
    }

    /// <summary>How far through the level's goal the run is, 0..1 - the quota's clock.
    /// Blocks-goals count locked placements; height-goals use the peak tower height.
    /// Goal-less levels (Endless, waves) return 0, which keeps the quota inert - the
    /// height-row dice remain the only spawner there.</summary>
    private float GoalProgress01(LevelModifierContext context)
    {
        LevelDefinition level = context.Level;
        if (level == null || context.GameManager == null) return 0f;
        switch (level.TargetType)
        {
            case LevelTargetType.PlaceBlocks:
            case LevelTargetType.TimedPlaceBlocks:
                return _blocksPlaced / Mathf.Max(1f, level.TargetValue);
            case LevelTargetType.ReachHeight:
            case LevelTargetType.TimedReachHeight:
                return context.GameManager.towerHeight / Mathf.Max(1f, level.TargetValue);
            default:
                return 0f;
        }
    }

    // ---- Spawning ---------------------------------------------------------------------------

    private bool TrySpawnZone(float bottomY)
    {
        GameModeConfig config = _context.GameManager != null ? _context.GameManager.ActiveConfig : null;
        if (config == null) return false;
        if (!TryGetReachableColumnRange(config, out int reachMin, out int reachMax)) return false;

        int bandMin = Mathf.Max(minColumn, reachMin);
        int bandMax = Mathf.Min(maxColumn, reachMax);
        float height = zoneHeightCells * _gridSpacing;

        // One island snapshot for all placement attempts (the copy walks the whole
        // monotonically-growing island list - 24x per roll was pure waste).
        StaticSupportIslandManager.GetWorldCellCenters(_islandScratch);

        for (int attempt = 0; attempt < PlacementAttempts; attempt++)
        {
            int width = Random.Range(zoneWidthMin, zoneWidthMax + 1);
            int leftCol = Random.Range(bandMin, bandMax - width + 2);
            int rightCol = leftCol + width - 1;
            if (rightCol > bandMax) continue;

            // THE ROUTE GUARANTEE: at least a widest-piece lane of clear reachable columns
            // must remain past one side of the zone - a void is a puzzle, never a wall.
            bool routeLeft = (leftCol - reachMin) >= BlockController.WidestBlockColumns;
            bool routeRight = (reachMax - rightCol) >= BlockController.WidestBlockColumns;
            if (!routeLeft && !routeRight) continue;

            var rect = new Rect(
                (leftCol - 0.5f) * _gridSpacing, bottomY,
                width * _gridSpacing, height);

            if (OverlapsExistingZone(rect) || OverlapsIslands(rect) || OverlapsSolids(rect)) continue;

            var zone = new Zone
            {
                WorldRect = rect,
                ArmedAtTime = Time.time + VoidZoneFx.SpawnSeconds,
            };
            zone.Fx = VoidZoneFx.Create(rect);
            _zones.Add(zone);
            _zonesSpawned++;
            SfxPlayer.Play("void_open", 1f);
            return true;
        }
        return false;
    }

    // The island guardrail math (StaticSupportIslandManager.TryGetReachableColumnRange is
    // private, replicated here): columns reachable at the widest zoom-out, anchored at the
    // stable floor centre, inset by the reach clearance.
    private bool TryGetReachableColumnRange(GameModeConfig config, out int min, out int max)
    {
        min = 0;
        max = 0;
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic) return false;

        int floorMin = int.MaxValue, floorMax = int.MinValue;
        IReadOnlyList<FloorSegmentConfig> segments = config.FloorSegments;
        for (int i = 0; segments != null && i < segments.Count; i++)
        {
            if (segments[i] == null) continue;
            floorMin = Mathf.Min(floorMin, segments[i].LeftColumn);
            floorMax = Mathf.Max(floorMax, segments[i].RightColumn);
        }
        if (floorMin > floorMax) return false;

        float halfWidth = config.MaximumCameraSize * camera.aspect;
        float floorCenterX = (floorMin + floorMax) * 0.5f * _gridSpacing;
        int reach = BlockController.WidestBlockColumns;
        min = Mathf.CeilToInt((floorCenterX - halfWidth) / _gridSpacing + 0.5f) + reach;
        max = Mathf.FloorToInt((floorCenterX + halfWidth) / _gridSpacing - 0.5f) - reach;
        return min <= max;
    }

    private bool OverlapsExistingZone(Rect rect)
    {
        var padded = Grow(rect, _gridSpacing); // a cell of breathing room between zones
        for (int i = 0; i < _zones.Count; i++)
        {
            if (padded.Overlaps(_zones[i].WorldRect)) return true;
        }
        return false;
    }

    private bool OverlapsIslands(Rect rect)
    {
        // _islandScratch is snapshotted once per TrySpawnZone, before the attempt loop.
        var padded = Grow(rect, _gridSpacing * 0.5f);
        for (int i = 0; i < _islandScratch.Count; i++)
        {
            if (padded.Contains(_islandScratch[i])) return true;
        }
        return false;
    }

    // Never materialize a zone around existing solids: the tower or the falling piece being
    // inside a fresh zone would be an instant unearned execution (islands get the same
    // courtesy at their spawn).
    private bool OverlapsSolids(Rect rect)
    {
        var padded = Grow(rect, _gridSpacing * 0.5f);
        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null) continue;
            if (!block.TryGetWorldBounds(out Bounds bounds)) continue;
            var blockRect = new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
            if (padded.Overlaps(blockRect)) return true;
        }
        return false;
    }

    /// <summary>Marker: this block was spared by the Void Ward boost and is exempt from every
    /// later sweep (it may still be standing inside the zone).</summary>
    private sealed class VoidWardedMark : MonoBehaviour { }

    private static Rect Grow(Rect rect, float by)
    {
        return new Rect(rect.xMin - by, rect.yMin - by, rect.width + 2f * by, rect.height + 2f * by);
    }

    // ---- The absolute law ---------------------------------------------------------------------

    private void SweepForViolations()
    {
        if (_zones.Count == 0) return;

        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded) continue;
            if (block.IsFallingAway) continue; // already doomed - the loss line owns it, no double jeopardy
            if (block.GetComponent<VoidSuckFx>() != null) continue; // already being devoured
            if (block.GetComponent<VoidWardedMark>() != null) continue; // spared by the ward - permanently exempt
            // Maws are exempt (the Extract precedent: maws never participate in removal
            // effects) - their welds are UNBREAKABLE by design, and dragging one member of
            // a fused cluster kinematically would haul the rest through the tower.
            if (block.GetComponent<MawBlockBehaviour>() != null) continue;

            block.GetWorldCellCenters(_cellScratch);
            for (int z = 0; z < _zones.Count; z++)
            {
                if (Time.time < _zones[z].ArmedAtTime) continue; // still tearing open - not lethal yet
                Rect inset = Grow(_zones[z].WorldRect, -overlapInset);
                for (int c = 0; c < _cellScratch.Count; c++)
                {
                    if (!inset.Contains(_cellScratch[c])) continue;

                    // VOID WARD (boost, SHOP.md §3): the FIRST grabbed block this run is
                    // spared - and marked permanently exempt (the maw-exemption pattern),
                    // or the next sweep would devour it where it stands.
                    if (!_wardUsed && RunSuppliesState.HasActiveBoost(BoostId.VoidWard))
                    {
                        _wardUsed = true;
                        block.gameObject.AddComponent<VoidWardedMark>();
                        TransmutePulseFx.Play(block);
                        SfxPlayer.Play("transmute", 0.8f);
                        z = _zones.Count;
                        break;
                    }

                    VoidSuckFx.Begin(block, _zones[z].WorldRect.center, suckSeconds, _context.GameManager);
                    if (_zones[z].Fx != null) _zones[z].Fx.Feed(suckSeconds + 0.4f);
                    z = _zones.Count; // this block is done; next block
                    break;
                }
            }
        }
    }
}
