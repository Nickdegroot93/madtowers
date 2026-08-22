using UnityEngine;

/// <summary>
/// "The Flood" level type: the water is the timer. A translucent flood starts below the
/// floor and rises at a steady, visible rate; the run ends the moment the surface passes the
/// TOP of the highest landed brick (Nick 2026-08-10: as long as a piece of your highest brick
/// is above water, you can still build on it). Nothing dissolves and nothing gets buoyancy -
/// submerged bricks keep simulating untouched (PHYSICS.md: the flood is a rule and a picture,
/// never a fluid). Pairs with ReachHeight goals: the same climb as a classic height level,
/// reframed as an escape.
///
/// Lives: the mode GRANTS the full 3 for free (GrantedRunLives below) and fallen bricks
/// charge them like any other level. The original no-lives design ("losing bricks is its own
/// punishment") had a degenerate winning line - throw every piece into the water and stack
/// only the 1x4s vertically, nothing can ever topple - so lives came back as the anti-dump
/// tax (Nick 2026-08-22), the water slowed down to compensate (+0.5 s/m on the schedule),
/// and the granted-3 start closes the pre-run lives shop: nothing to buy at the cap.
/// The swallow itself stays terminal via GameManager.EndRunNow (the timeout precedent),
/// bypassing life charges and immunity - hearts buy mistakes, never time underwater.
///
/// Pacing has ONE dial: secondsToGoal - the flood travels from its start line to the goal
/// height in that many seconds (after the grace), whatever the level's target is. Rise speed
/// derives from it, so retuning a level's goal never silently detunes its flood.
///
/// Look: FloodFx + the Flood shader, coloured from the chapter's two menu accent colours
/// (the laser-line precedent) so one visual floods every chapter; per-chapter asset copies
/// can override the palette outright (a sand-coloured "flood" for the desert chapters).
/// </summary>
[CreateAssetMenu(fileName = "RisingFlood", menuName = "Stacking/Levels/Modifiers/Rising Flood")]
public class RisingFloodModifier : LevelModifier, ILevelMenuProgressProvider
{
    // The GAME TYPE this modifier turns a level into (label-only claim - the ReachHeight
    // goal keeps owning the progress line and end-of-run metric).
    public string MenuChallengeLabel => "The Flood";
    public string MenuProgressLabel(LevelDefinition level, ProgressStore.LevelBest best, bool completed) => null;
    public ResultMetric? EndOfRunMetric(LevelDefinition level, RunResult result, ProgressStore.LevelBest best) => null;

    /// <summary>Every flood run starts with the full 3 lives, free (Nick 2026-08-22): fallen
    /// bricks charge lives again - dumping pieces into the water is no longer a free win -
    /// while the swallow stays terminal (EndRunNow). Granting the cap also closes the pre-run
    /// lives shop; the modal shows the pips as INCLUDED. Lives-economy abilities (ExtraLife,
    /// LastStand, on-life-lost triggers) are live cards here again - no draft bans.</summary>
    public override int GrantedRunLives => RunState.MaxLives;

    [Header("Pacing")]
    [Tooltip("Seconds before the flood starts rising - covers the intro and the first placements.")]
    [Min(0f)]
    [SerializeField] private float graceSeconds = 8f;
    [Tooltip("Seconds the flood takes from its start line to the GOAL height once it starts " +
             "rising - author it as 'the time for this level'. THE pacing dial: rise speed " +
             "derives from this and the level's target, so a goal retune never silently " +
             "detunes the flood. (150 was insanely fast on Reach-75m - Nick 2026-08-10.)")]
    [Min(10f)]
    [SerializeField] private float secondsToGoal = 750f;
    [Tooltip("How far below the floor datum the surface starts - visible from the first " +
             "second, climbing the ground art before it threatens anything.")]
    [Min(0f)]
    [SerializeField] private float startBelowFloor = 2f;

    [Header("Palette (defaults to the chapter's accent colours)")]
    [SerializeField] private bool overridePalette;
    [SerializeField] private Color shallowColor = new Color(0.20f, 0.55f, 0.60f, 0.55f);
    [SerializeField] private Color deepColor = new Color(0.03f, 0.10f, 0.16f, 0.85f);
    [SerializeField] private Color foamColor = new Color(0.85f, 0.97f, 1f, 0.9f);

    // The swallow check sweeps all landed blocks; the flood moves slowly, so a cadence is
    // plenty (and the danger reading it feeds only drives visuals between sweeps).
    private const float CheckInterval = 0.15f;
    // Danger (wave agitation + foam) ramps over the last few meters of margin.
    private const float DangerBandMeters = 4f;

    private LevelModifierContext _context;
    private FloodFx _fx;
    private float _surfaceY;
    private float _riseSpeed;
    private float _elapsed;
    private float _checkTimer;
    private float _floorY;

    public override void OnLevelStart(LevelModifierContext context)
    {
        _context = context;
        _elapsed = 0f;
        _checkTimer = 0f;

        GameManager gm = context.GameManager;
        _floorY = gm != null ? gm.floorOriginY : 0f;
        _surfaceY = _floorY - startBelowFloor;

        float goalHeight = 20f;
        if (context.Level != null && context.Level.TargetType == LevelTargetType.ReachHeight)
        {
            goalHeight = context.Level.TargetValue;
        }
        else
        {
            Debug.LogWarning("[Flood] level goal is not ReachHeight - the flood paces itself " +
                             "against a 20m stand-in. Author flood levels with ReachHeight.");
        }
        _riseSpeed = (goalHeight + startBelowFloor) / Mathf.Max(10f, secondsToGoal);

        // WATER-BIASED palette: a base teal pulled 25% toward the chapter accents. Pure
        // accent derivation failed its first contact (Jungle: green accents + green bricks
        // = camouflage, and the flood stopped reading as liquid - Nick 2026-08-10); the
        // teal bias keeps it unmistakably water in every chapter while the accent tint
        // keeps it belonging to the world it drowns. Deserts/lava want overridePalette.
        Color shallow = shallowColor, deep = deepColor, foam = foamColor;
        ChapterDefinition chapter = ChapterSkins.ActiveChapter;
        if (!overridePalette && chapter != null)
        {
            Color a = chapter.MenuAccentColor;
            Color b = chapter.MenuAccentSecondaryColor;
            shallow = Color.Lerp(new Color(0.16f, 0.52f, 0.55f), a, 0.25f); shallow.a = 0.82f;
            deep = Color.Lerp(new Color(0.05f, 0.24f, 0.30f), Color.Lerp(a, Color.black, 0.5f), 0.25f); deep.a = 0.88f;
            foam = Color.Lerp(Color.Lerp(b, Color.white, 0.75f), Color.white, 0.3f); foam.a = 0.95f;
        }
        _fx = FloodFx.Create(shallow, deep, foam, FloorCenterX());
        _fx.SetSurfaceY(_surfaceY);
    }

    public override void OnLevelEnd(LevelModifierContext context)
    {
        if (_fx != null) Object.Destroy(_fx.gameObject);
        _fx = null;
    }

    public override void OnUpdate(LevelModifierContext context, float deltaTime)
    {
        if (context.GameManager == null || context.GameManager.isGameOver || _fx == null) return;

        // The flood exists only while the player can actually build: it must not rise (or
        // kill) during the intro pan, an ability draft, or - the killer - the hold-steady
        // WIN VERIFICATION, where the photo-finish the pacing engineers would otherwise
        // flip a reached goal into a loss mid-countdown (review 2026-08-11). Timed goals
        // freeze their clock the same way. The grace timer freezes with it.
        if (context.GameManager.CurrentPhase != GamePhase.Playing) return;

        _elapsed += deltaTime;
        if (_elapsed > graceSeconds)
        {
            _surfaceY += _riseSpeed * deltaTime;
            _fx.SetSurfaceY(_surfaceY);
        }

        _checkTimer -= deltaTime;
        if (_checkTimer > 0f) return;
        _checkTimer = CheckInterval;

        // The highest thing you could still build on: the top of the highest landed brick,
        // or the floor datum while nothing stands. The falling piece never counts - it
        // hasn't committed. (A raised terrain shelf above the datum isn't credited either;
        // with any sane grace the first real placement lands long before that matters.)
        float highestTop = _floorY;
        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            // IsFallingClearOfTower, NOT the sticky IsFallingAway latch: the latch stays set
            // on a jolted-but-reseated brick until it re-earns sleep, and excluding the real
            // top for that window drowned players whose highest brick never went under
            // (review 2026-08-11) - a mid-tower Magma/zap drop was an instant wrongful death
            // whenever the water was close.
            if (block == null || !block.HasLanded || block.IsFallingClearOfTower) continue;
            if (!block.TryGetWorldBounds(out Bounds bounds)) continue;
            if (bounds.max.y > highestTop) highestTop = bounds.max.y;
        }

        float margin = highestTop - _surfaceY;
        _fx.SetDanger(1f - Mathf.Clamp01(margin / DangerBandMeters));

        // Nick's rule, verbatim: you lose when the water is above your highest brick.
        if (margin < 0f)
        {
            context.GameManager.EndRunNow("The flood swallowed the tower", RunEndCause.Flood);
        }
    }

    private float FloorCenterX()
    {
        GameModeConfig config = _context.GameManager != null ? _context.GameManager.ActiveConfig : null;
        var segments = config != null ? config.FloorSegments : null;
        if (segments == null || segments.Count == 0) return 0f;

        float spacing = config.GridSpacing;
        int min = int.MaxValue, max = int.MinValue;
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i] == null) continue;
            min = Mathf.Min(min, segments[i].LeftColumn);
            max = Mathf.Max(max, segments[i].RightColumn);
        }
        return min > max ? 0f : (min + max) * 0.5f * spacing;
    }
}
