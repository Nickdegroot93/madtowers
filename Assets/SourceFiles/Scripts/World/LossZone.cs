using UnityEngine;

/// <summary>
/// Charges a life for every block that drops past the death line near the BOTTOM OF THE SCREEN.
/// Camera-relative on purpose: at altitude a dropped block must not get a free 100 m plunge into
/// the old tower below ("maybe it wedges somewhere") or a ten-second wait while it falls to the
/// world floor - the penalty lands as the player watches the block sink past the line, which is
/// always on screen no matter how high the camera has climbed. Resting tower blocks below the
/// line are the normal state of a tall game; only genuinely falling blocks count (see
/// BlockController.IsLostBelow).
///
/// The object's fixed trigger collider below the floor stays as a backstop for any
/// rigidbody the sweep can't judge.
/// </summary>
public class LossZone : MonoBehaviour
{
    // The death line sits a little ABOVE the bottom screen edge (not below it), so the beam is
    // visible at every camera height and a block is charged as it sinks past the bottom of the
    // view - not after dropping fully out of sight. Camera-relative, so it rises with the camera.
    // Trade-off: at altitude the standing tower reaches the bottom edge, so roughly this many
    // world units of it sit below the line; keep it modest. (Tunable - was 1 unit BELOW the edge,
    // which put the line and the death itself off-screen once the camera climbed.)
    private const float LossLineAboveScreenBottom = 2f;

    // The kill line must never eat a LEGITIMATE landing zone: the floor terrain carves pockets up
    // to ~3 cells below the datum (FLOORS.md), and pieces can hook a floor edge below the datum
    // while still clearly on screen. While the floor is still in play (the screen-relative line
    // within FloorRegimeCeiling of the datum) the line is therefore pinned this far under the
    // datum; once the camera climbs past that band, the screen-relative line takes over.
    // (An earlier unconditional Min here pinned the line under the terrain for the WHOLE game:
    // at altitude a lost brick fell the full tower height off-screen before being charged, and
    // the armed Sacrifice/Hardline lasers - derived from this line - could never rise into view.
    // Fixed July 2026; keep the clamp conditional.)
    private const float TerrainClearanceBelowDatum = 4f;

    // The screen-relative line must clear the floor by this much before it takes over from the
    // terrain clamp: with the floor ~4 units below the screen bottom nothing can legally land
    // off-screen, so "below the view = lost" becomes safe. Camera Y is ratcheted up-only, so the
    // handover happens once per run; a later zoom-out can briefly dip the line back to the
    // terrain clamp, which only ever makes it MORE lenient, never less.
    private const float FloorRegimeCeiling = 6f;

    /// <summary>The world-space line below which a block counts as lost, for the given camera -
    /// the single definition the sweep, the death beam and abilities all consult (a doomed piece
    /// must not accept a consumable spent on it). Camera-relative at altitude; clamped safely
    /// below the floor terrain while the floor is still in play.</summary>
    public static float CullY(Camera camera) => CullY(camera, out _);

    /// <summary>As above; `terrainClamped` reports whether the ground-regime clamp decided the
    /// line. The sweep's pocket veto keys off this same decision, so the veto's active window
    /// and the clamp can never drift apart.</summary>
    public static float CullY(Camera camera, out bool terrainClamped)
    {
        float line = camera.transform.position.y - camera.orthographicSize + LossLineAboveScreenBottom;
        terrainClamped = false;
        GameManager gm = GameManager.Instance;
        if (gm != null && line < gm.floorOriginY + FloorRegimeCeiling)
        {
            line = Mathf.Min(line, gm.floorOriginY - TerrainClearanceBelowDatum);
            terrainClamped = true;
        }
        return line;
    }

    /// <summary>True if a world position sits below the bottom-screen kill line right now - i.e. a
    /// piece that locked/slid off down there is on its way out and the cull sweep owns it. On-land
    /// variant effects (e.g. Magma melt) consult this so they don't fire on a doomed piece. A
    /// non-orthographic / absent camera counts as "not below" (no kill line to be under).</summary>
    public static bool IsBelowCull(Vector3 worldPosition)
    {
        Camera camera = Camera.main;
        return camera != null && camera.orthographic && worldPosition.y < CullY(camera);
    }

    // While a beam-drawing loss interceptor (Sacrifice/Hardline) is armed, interception happens
    // at InterceptLineY instead: the loss line raised to ALWAYS sit a readable margin above the
    // screen bottom, so the player sees the laser and watches the block get saved BEFORE it
    // leaves the view. (The charge line's datum - 4 clamp is off-screen at tight zoom - a laser
    // drawn there was invisible for the whole ground game.)
    private const float InterceptLineScreenHeightFraction = 0.08f;

    /// <summary>The line at which armed loss-intercepting beams (Sacrifice/Hardline) sit and
    /// trigger: the charge line, but never below the visible bottom band of the screen - the
    /// armed laser is a status light and must be in view at EVERY camera height. Legitimate
    /// landings below it (deep-pocket/notch settles) are protected by the sweep's floor-span
    /// veto at trigger time, not by capping this line's height (a datum cap here left the beam
    /// under the screen for the whole mid-game band, July 2026). Laser visuals and the sweep's
    /// landed-intercept trigger consult THIS, never CurrentLossLineY, so the beam and the save
    /// always agree.</summary>
    public static float InterceptLineY(Camera camera)
    {
        float line = CurrentLossLineY(camera);
        if (camera == null || !camera.orthographic) return line;

        float visibleLine = camera.transform.position.y - camera.orthographicSize
                            + camera.orthographicSize * 2f * InterceptLineScreenHeightFraction;
        return Mathf.Max(line, visibleLine);
    }

    /// <summary>
    /// The highest line that can currently charge a bottom-screen loss. Early in a run
    /// the fixed trigger is usually the visible "hit" line; once the camera climbs, the
    /// camera-relative cull can become higher and takes over.
    /// </summary>
    public static float CurrentLossLineY(Camera camera)
    {
        bool hasLine = false;
        float lineY = 0f;

        if (camera != null && camera.orthographic)
        {
            lineY = CullY(camera);
            hasLine = true;
        }

        Collider2D activeTrigger = _active != null ? _active._triggerCollider : null;
        if (activeTrigger != null && activeTrigger.enabled)
        {
            float triggerTopY = activeTrigger.bounds.max.y;
            lineY = hasLine ? Mathf.Max(lineY, triggerTopY) : triggerTopY;
            hasLine = true;
        }

        return hasLine ? lineY : 0f;
    }

    // The sweep reads collider bounds for every tracked block; at late-game scale that is
    // hundreds of native calls, so it runs at 10 Hz instead of per frame. A block a full
    // margin below the screen cannot un-lose itself in 100 ms, and the timer uses scaled
    // time so it naturally freezes with the physics it observes.
    private const float SweepInterval = 0.1f;
    private static LossZone _active;

    private float _nextSweepTime;

    private Camera _camera;
    private Collider2D _triggerCollider;

    private void Awake()
    {
        _triggerCollider = GetComponent<Collider2D>();

        // The red translucent bar on this object is an editor-only guide showing where
        // the backstop trigger sits; players should never see it.
        SpriteRenderer guide = GetComponent<SpriteRenderer>();
        if (guide != null) guide.enabled = false;
    }

    private void Start()
    {
        // Keep the fixed backstop below every legit landing zone too (same clearance rule as
        // CullY) - a scene-authored trigger sitting higher would still eat terrain pockets.
        // NOTE: the backstop's landed path does NOT run the sweep's floor-span pocket veto; it
        // is safe only because this clamp keeps the trigger top at datum - 4 while pockets stop
        // at ~datum - 3 (GameModeConfig caps depths at 3). If pocket depths or
        // TerrainClearanceBelowDatum ever change, revisit both together.
        GameManager gm = GameManager.Instance;
        if (gm == null || _triggerCollider == null) return;

        float maxTop = gm.floorOriginY - TerrainClearanceBelowDatum;
        float overshoot = _triggerCollider.bounds.max.y - maxTop;
        if (overshoot > 0f)
        {
            transform.position += Vector3.down * overshoot;
        }
    }

    private void OnEnable()
    {
        _active = this;
    }

    private void OnDisable()
    {
        if (_active == this) _active = null;
    }

    private void Update()
    {
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;
        if (GameManager.Instance.IsGamePaused) return; // no verdicts under the pause menu

        if (Time.time < _nextSweepTime) return;
        _nextSweepTime = Time.time + SweepInterval;

        if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
        if (_camera == null || !_camera.orthographic) return;

        EnsureAbilities();

        float cullY = CullY(_camera, out bool groundRegime);
        float landedInterceptCullY = cullY;
        if (_abilities != null)
        {
            // A beam-drawing interceptor triggers at its visible laser (InterceptLineY), never at
            // the possibly off-screen charge line - the save must happen where the player looks.
            if (_abilities.HasLossInterceptLine)
            {
                landedInterceptCullY = Mathf.Max(landedInterceptCullY, InterceptLineY(_camera));
            }
            landedInterceptCullY += Mathf.Max(0f, _abilities.LossInterceptLineOffset);
        }

        // While the floor is still in play (the charge line terrain-clamped), the raised intercept
        // line sits above legitimate landings: deep pockets legally rest blocks at datum - 2.5 and
        // a block still settling INTO one is dynamic and briefly fast enough to read as lost at
        // the visible line. A falling block OVER the floor can never truly be lost there - the
        // terrain catches everything above the charge line - so overlapping a floor segment
        // vetoes the raised-line intercept. Once the charge line goes camera-relative (floor out
        // of play, nothing can legally land off-screen) the veto ends - a catch must not be
        // skipped at altitude.

        // The Flood's death line: a brick fully under the water is gone THERE, not seconds
        // later at the screen bottom (Nick 2026-08-22). Applied PER BLOCK and only to bricks
        // the water can claim - the active piece and landed bricks falling clear of the
        // tower. Never the shared CullY: submerged RESTING tower bricks are normal state
        // (nothing dissolves), and IsLostBelow's landed branch (dynamic + awake + falling)
        // matches jolted-but-reseating rows for up to ~0.75s after any slam, so a raised
        // global line culled live tower rows mid-run (review 2026-08-22). CullY consumers
        // (Magma's doomed-check, Curse wake, Zap targeting) keep their original meaning
        // for the same reason. -infinity when no flood runs.
        float floodKillY = RisingFloodModifier.FloodKillY;

        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null) continue;

            float blockCullY = block.HasLanded ? landedInterceptCullY : cullY;
            if ((!block.HasLanded || block.IsFallingClearOfTower) && floodKillY > blockCullY)
            {
                blockCullY = floodKillY;
            }
            if (!block.IsLostBelow(blockCullY)) continue;
            if (block.HasLanded && groundRegime && blockCullY > cullY &&
                !block.IsLostBelow(cullY) && IsOverFloorSpan(block)) continue;

            ResolveLostBlock(block);

            // The final life may just have gone - leave the wreckage in peace.
            if (GameManager.Instance.isGameOver) return;
        }
    }

    // Resolve a block that has crossed the loss line, AT MOST ONCE. The camera sweep and
    // the backstop trigger can both catch the same falling piece (a straight drop off-screen
    // hits both), so the first to resolve it consumes the block's one-shot loss guard;
    // whichever arrives second skips - otherwise one block costs two lives, or a block an
    // armed ability already saved gets charged anyway. Resolution = let an ability intercept,
    // else charge the normal per-block loss (DuringBlockLoss owns the life/count/score policy).
    private void ResolveLostBlock(BlockController block)
    {
        if (block.TryGetComponent(out BlockIdentity identity) && !identity.TryConsumeLoss()) return;
        if (TryInterceptLoss(block)) return;
        // A brick the WATER claimed plops as it goes (the modifier's slower sweep can
        // miss a fast drop's brief surface crossing) - the loss line is at the flood's
        // surface when a flood runs, so this is the swallow moment, not decoration.
        if (block.transform.position.y < RisingFloodModifier.FloodSurfaceY)
        {
            SfxPlayer.Play("flood_plip", 0.55f, 0.08f);
        }
        GameManager.Instance.DuringBlockLoss(block, block.HandleLostBelowScreen);
    }

    // An armed ability (e.g. Sacrifice) may handle a loss instead of the
    // life charge. LANDED blocks only: saving the active piece would strand the
    // spawner's ActiveControlled gate (control can't be ended from outside) - the
    // active piece always takes the normal loss path.
    private bool TryInterceptLoss(BlockController block)
    {
        if (!block.HasLanded) return false;

        EnsureAbilities();
        return _abilities != null && _abilities.TryInterceptLoss(block);
    }

    // Lazily cached world X spans of the floor terrain, one per segment (NOT their union - a
    // block falling through a gap between segments is genuinely lost and must stay
    // interceptable), for the ground-regime intercept veto. The config comes from GameManager's
    // resolved ActiveConfig - the same source the floor itself is built from; resolving the
    // selection directly (with a null fallback) silently disabled the veto in any run without a
    // SelectedLevel. Segments are fixed for the run; the cache retries until a config exists
    // (the first sweep can fire before initialization settles).
    private readonly System.Collections.Generic.List<Vector2> _floorSegmentSpans = new();
    private bool _floorSpansResolved;

    private bool IsOverFloorSpan(BlockController block)
    {
        if (!_floorSpansResolved)
        {
            GameManager gm = GameManager.Instance;
            GameModeConfig config = gm != null ? gm.ActiveConfig : null;
            if (config == null) return false; // not resolvable yet - retry next sweep

            _floorSpansResolved = true;
            _floorSegmentSpans.Clear();
            var segments = config.FloorSegments;
            for (int i = 0; segments != null && i < segments.Count; i++)
            {
                FloorSegmentConfig segment = segments[i];
                if (segment == null) continue;
                _floorSegmentSpans.Add(new Vector2(
                    (segment.LeftColumn - 0.5f) * config.GridSpacing,
                    (segment.RightColumn + 0.5f) * config.GridSpacing));
            }
        }

        if (!block.TryGetWorldBounds(out Bounds bounds)) return false;
        for (int i = 0; i < _floorSegmentSpans.Count; i++)
        {
            // Any X overlap counts: a piece hooked on a floor edge (bounds straddling it) is a
            // legitimate landing even though its centre may hang past the edge.
            if (bounds.min.x <= _floorSegmentSpans[i].y && bounds.max.x >= _floorSegmentSpans[i].x)
                return true;
        }
        return false;
    }

    // Lazily cache the AbilityRuntime. Both the sweep and the backstop trigger consult it, and
    // the trigger can fire before the first sweep, so the lookup lives in one place.
    private void EnsureAbilities()
    {
        if (_abilities == null && GameManager.Instance != null)
        {
            _abilities = GameManager.Instance.GetComponent<AbilityRuntime>();
        }
    }

    private AbilityRuntime _abilities;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        // If the collider belongs to a block, the Rigidbody2D may live on its parent.
        Rigidbody2D rb = other.attachedRigidbody;
        if (rb == null) return;

        BlockController block = rb.GetComponent<BlockController>();
        if (block != null)
        {
            // The backstop fires on the FIRST cell that touches it - but for the still-steered
            // active piece that can be one overhanging cell mid-way into a legitimate deep-pocket
            // entry (the trigger top sits at datum - 4, pockets reach ~3 cells below the datum).
            // Defer to the same whole-piece-below test the cull sweep uses; a piece that really
            // keeps falling is caught by the sweep (or re-judged here) moments later.
            if (!block.HasLanded)
            {
                if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
                if (_camera != null && _camera.orthographic && !block.IsLostBelow(CullY(_camera)))
                    return;
            }

            ResolveLostBlock(block); // same once-guarded path as the screen-bottom cull
            return;
        }

        GameManager.Instance.GameOver();
        Destroy(rb.gameObject);
    }
}
