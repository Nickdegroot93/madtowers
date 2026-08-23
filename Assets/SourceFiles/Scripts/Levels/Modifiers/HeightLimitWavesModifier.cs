using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Laser limit" puzzle mode: blocks arrive in ENDLESS waves and the whole tower must stay
/// below a glowing limit line. A wave clears when the LIVE standing-block count reaches its
/// cumulative target AND SURVIVES a short confirm window (the count signal fires inside the
/// lock, before the laser has looked at the new block - see the clear-confirm gate) - a block
/// that is zapped, scrapped, sacrificed or knocked off stops counting, so the wave genuinely
/// reopens (10 blocks means 10 blocks STANDING; there is no
/// crediting a block that no longer exists). When a wave clears the line rises and the next,
/// bigger wave begins - forever. The line never descends: losses show as a deficit in the
/// counter, never as the laser sinking into a standing tower.
///
/// Author the level with LevelTargetType.ClearWaves and targetValue = waves to win; the win
/// lands exactly when that wave clears (same standing-count signal, via ClearWavesWinCondition
/// reading <see cref="ActiveRun"/>), and the waves simply continue as the endless score chase.
///
/// WAVE MATH lives in <see cref="WaveSolver"/> (pure, shared with the editor's Puzzle Wave
/// Report): nothing is hand-authored, every height is SOLVED from the floor, the shape bag and
/// the asset's difficulty rank. Deterministic per level, so leaderboard runs race identical
/// waves. Read WaveSolver's header before touching difficulty - in particular, quota and line
/// height are coupled, so asking for MORE blocks per wave does not make a wave harder.
///
/// Stored/board scores for wave levels are ENCODED: wavesCleared x 1000 + peak in-wave
/// progress, so boards rank sub-wave granularity while every display decodes to waves.
/// </summary>
[CreateAssetMenu(fileName = "HeightLimitWaves", menuName = "Stacking/Levels/Modifiers/Height Limit Waves")]
public class HeightLimitWavesModifier : LevelModifier, ILevelMenuProgressProvider
{
    [Header("Difficulty")]
    [Tooltip("The one difficulty dial. 4 = the shipped standard (tight but human). 3 = a clear " +
             "step easier. 5 = brutal - near-perfect packing AND a sustained 3-column overhang " +
             "from wave 5 on; reserve it for late chapters. 1-2 = easy-mode tail. Sets BOTH the " +
             "required packing density and the flank slope the wave math believes you can build, " +
             "so a lower rank raises the line rather than shrinking the ask.")]
    [Range(1, 5)]
    // Defaults to the SHIPPED STANDARD tier (4, set by the 2026-07-29 playtests: rank 5 walls at
    // wave 5 even on chapter 1's flat floor, rank 3 read as too easy). Raise to 5 per chapter for
    // the late campaign.
    [SerializeField] private int difficultyRank = 4;

    /// <summary>Authored rank, for the editor's Puzzle Wave Report (which solves the same math
    /// over every level without entering play mode).</summary>
    public int DifficultyRank => Mathf.Clamp(difficultyRank, 1, 5);

    /// <summary>NO ABILITIES IN WAVE MODE, full stop (Nick 2026-08-24). The mode is a pure
    /// packing puzzle, and nearly every ability is a skip button against it: shrink/1x1
    /// grants trivialize the fit, anchors freeze free terrain at the line, Hardline mints
    /// airborne platforms where the wave math assumed open air (the 2026-08-10 targeted
    /// bans that preceded this blanket one). The per-wave draft offer is gone from
    /// OnStandingBlocksChanged, wave configs author no block-cadence draft, and this
    /// blanket ban is the backstop for any future offer path.</summary>
    public override bool BansAbility(AbilityDefinition ability) => true;

    [Tooltip("Seconds the line takes to glide to the next wave's height.")]
    [SerializeField] private float lineRiseSeconds = 1.2f;

    [Header("Laser Style (fallback only - the beam takes the active chapter's accent colours)")]
    [Tooltip("FALLBACK beam colour for runs with no chapter context (custom games). Chapter " +
             "runs ignore this: WaveLaserLine is coloured by the chapter's two menu accent " +
             "colours, so every chapter's laser is its own. The look itself (layer spec, " +
             "breath, pulses) is code-owned in WaveLaserLine, never serialized.")]
    [SerializeField] private Color lineColor = new Color(1f, 0.27f, 0.2f, 1f);

    // ---- Wave math lives in WaveSolver (pure, shared with the editor's Puzzle Wave Report so a
    // printed table and a played run can never disagree). Code-owned constants, never
    // serialized - SHOP.md's staleness rule. -------------------------------------------------

    private const float ZapCooldownSeconds = 0.75f; // a collapse can't chain-drain lives in one beat

    /// <summary>Stored scores pack (wavesCleared * this) + peak in-wave standing progress.</summary>
    public const int ScoreEncodeBase = 1000;

    /// <summary>The live run's wave engine (the runtime CLONE - never the asset), published for
    /// ClearWavesWinCondition and the results card. Null outside a wave-level run.</summary>
    public static HeightLimitWavesModifier ActiveRun { get; private set; }

    public static int DecodeWaves(int encodedScore) => Mathf.Max(0, encodedScore) / ScoreEncodeBase;

    private LevelModifierContext _context;
    private WaveLaserLine _laser;
    private Color _resolvedColor; // chapter accent (or the fallback) - shatter FX + WaveHud read it
    private float _floorY;

    // Wave engine state. _currentWave is the 1-based wave being built; cleared waves are
    // monotonic - the line never descends, a deficit reopens the CURRENT wave's counter.
    private int _currentWave;
    private int _wavesCleared;
    private int _standing;
    private int _peakStanding;
    private readonly List<float> _lineHeightsCells = new List<float>(); // [n-1] = line height while wave n runs
    private readonly List<float> _columnTops = new List<float>();       // sorted, cells above the datum
    private float _avgCellsPerPiece = WaveSolver.FallbackCellsPerPiece;
    private object _cachedSegments; // staleness guard: floor config can resolve after level start

    // Half a cell of leeway above the topmost full row that fits (WaveSolver.LaserGraceCells);
    // read live (not cached at OnLevelStart) because the active config can resolve after level
    // start on some paths - the same staleness AirPocketModifier guards against.
    private float LaserGraceWorld => WaveSolver.LaserGraceCells * GridSpacing;
    private float GridSpacing => _context?.GameManager?.ActiveConfig != null
        ? _context.GameManager.ActiveConfig.GridSpacing : 1f;
    private float _lineY;
    private float _lineTargetY;
    private float _zapCooldown;

    // Wave-transition spawn hold (WaveRevealGate -> GameManager spawn hold). While true the next piece is held until the
    // line has settled at its new height and the freshly revealed island band has finished
    // popping in. _settledGenTick is the island manager's pass counter captured the moment the
    // line settles, so we can tell generation has run against the raised ceiling before trusting
    // "no pops pending". _holdElapsed backs a safety timeout so the spawn chain can never
    // soft-lock if the island system is absent or quiet.
    private const float RevealHoldTimeout = 2f;
    private bool _waitingForReveal;
    private int _settledGenTick;
    private float _holdElapsed;

    // Clear-confirm gate. A wave must be SURVIVED, not merely touched: the standing-count signal
    // fires inside BlockController.LockBlock (lock -> ledger -> here), strictly BEFORE the laser
    // has ever looked at the block that reached the target, so advancing on it credited a wave to
    // a block the line then zapped a frame later - and because cleared waves are monotonic, that
    // credit could never be taken back (Nick, July 2026). Reaching the target now only ARMS the
    // clear: the line stays put, and the advance lands only once the window has elapsed with the
    // count still holding and NOTHING standing above the line. A zap/collapse inside the window
    // cancels it and the wave simply stays open, which is what the honest bill already models.
    private const float ClearConfirmSeconds = 0.35f;
    private bool _clearPending;
    private float _clearConfirmRemaining;

    /// <summary>Standing-block count required to have CLEARED the given 1-based wave. Straight
    /// through to WaveSolver rather than a local running total - one owner for the quota rule
    /// AND its summation, so the played run and the editor report can't drift apart.</summary>
    public int StandingTargetForWave(int waveNumber)
        => waveNumber <= 0 ? 0 : WaveSolver.CumulativeQuota(waveNumber);

    public int WavesCleared => _wavesCleared;

    /// <summary>Highest standing count the run ever reached. MONOTONIC - rarity escalation
    /// reads run progress from this, so a zap/collapse never de-escalates an earned offer
    /// tier (BLOCKS.md: losing blocks must not rewind difficulty or revoke an earned picker).</summary>
    public int PeakStanding => _peakStanding;

    // ---- ILevelMenuProgressProvider: claim the type name only. The metric itself (menu
    // progress, results card) lives on ClearWavesWinCondition, which decodes stored scores -
    // one owner, so the two can never disagree. ---------------------------------------------
    public string MenuChallengeLabel => "PUZZLE WAVES";
    public string MenuProgressLabel(LevelDefinition level, ProgressStore.LevelBest best, bool completed) => null;
    public ResultMetric? EndOfRunMetric(LevelDefinition level, RunResult result, ProgressStore.LevelBest best) => null;

    /// <summary>Bests and boards store waves, not raw blocks: wavesCleared x 1000 plus the PEAK
    /// standing progress inside the wave after it (monotonic, so a final collapse can't erase a
    /// run's reach). Sub-wave granularity keeps board ties rare; displays decode via
    /// <see cref="DecodeWaves"/>.</summary>
    public override int? OverrideReportedScore(LevelModifierContext context, int rawScore)
        => _wavesCleared * ScoreEncodeBase +
           Mathf.Clamp(_peakStanding - StandingTargetForWave(_wavesCleared), 0, ScoreEncodeBase - 1);

    public override void OnLevelStart(LevelModifierContext context)
    {
        _context = context;
        _floorY = context.GameManager != null ? context.GameManager.floorOriginY : 0f;
        _currentWave = 1;
        _wavesCleared = 0;
        _standing = 0;
        _peakStanding = 0;
        _lineHeightsCells.Clear();
        _waitingForReveal = false;
        _clearPending = false;
        _clearConfirmRemaining = 0f;
        WaveRevealGate.Reset(); // GameManager.Awake also clears it; belt-and-braces for a retry

        // The win comes from the level's ClearWaves goal; catch mismatched wiring early.
        // Endless is a legal pairing (waves with no win - practice/daily material).
        if (context.Level != null &&
            context.Level.TargetType != LevelTargetType.ClearWaves &&
            context.Level.TargetType != LevelTargetType.Endless)
        {
            Debug.LogWarning(
                $"[HeightLimitWaves] '{context.Level.DisplayName}' should use targetType " +
                $"ClearWaves (targetValue = waves to win) or Endless - waves keyed to a " +
                $"'{context.Level.TargetType}' goal will fight over the standing count.", this);
        }

        RebuildWaveMath();
        _lineY = _lineTargetY = CurrentLineWorldY();
        // Publish the build ceiling so support islands only generate below the line
        // (GameManager.Awake reset it to infinity at scene load). The ceiling gets the ROW
        // BOUNDARY under the laser (grace removed): the grace is zap/visual leeway only -
        // letting it raise the island band could admit an island sitting flush with the line.
        TowerHeightLimit.Set(_lineY - LaserGraceWorld);
        CreateLineVisual();
        ActiveRun = this;
    }

    public override void OnLevelEnd(LevelModifierContext context)
    {
        // The visual is unparented (it tracks the camera, not any scene root), so nothing
        // else tears it down - without this a retry stacked a dead beam per run.
        if (_laser != null) Object.Destroy(_laser.gameObject);
        _laser = null;
        if (ActiveRun == this) ActiveRun = null;
    }

    // The wave engine's one input: the LIVE standing count (BLOCKS.md). A zapped, scrapped,
    // sacrificed or fallen block lowers it, reopening its wave through the very same signal -
    // no destruction path needs to know waves exist.
    public override void OnStandingBlocksChanged(LevelModifierContext context, int standingBlocks)
    {
        _standing = standingBlocks;
        if (standingBlocks > _peakStanding) _peakStanding = standingBlocks;

        // Reaching the target ARMS the clear; OnUpdate confirms it (see the clear-confirm gate).
        // Arming must happen HERE, on the lock, because the spawn hold has to be raised before
        // the imminent SpawnNextBlock - a hold raised a third of a second later would arrive
        // after the next piece already dropped.
        if (_standing >= StandingTargetForWave(_currentWave))
        {
            if (!_clearPending) BeginClearConfirm();
        }
        else if (_clearPending)
        {
            // The wave reopened mid-confirm - the laser zapped the very block that reached the
            // target, or the tower shed one. No advance, no rise, and the next piece resumes.
            CancelClearConfirm();
        }
        // On a decrease nothing else moves: the line never descends; the deficit simply shows in
        // the counter (a "10 block" wave can owe 13 after losses - that's the honest bill).
    }

    // Target reached: freeze the transition until the block that reached it has proven it stands.
    // The spawn hold goes up immediately (same instant the old code advanced), the line does NOT -
    // violations must resolve against the wave the player is actually completing, and the zap
    // check needs a frame with the line still at the old height to see the offender at all.
    private void BeginClearConfirm()
    {
        _clearPending = true;
        _clearConfirmRemaining = ClearConfirmSeconds;
        WaveRevealGate.Hold();
    }

    // Confirmed only when the window has elapsed AND nothing landed sits above the line. The
    // geometry check is deliberately independent of _zapCooldown: a violation still waiting out
    // the cooldown must block the advance just as hard as one already zapped, or the cooldown
    // becomes a hole to clear waves through. Nothing can spawn while the hold is up, so the
    // offender always resolves (zapped within the cooldown, or the run ends on lives).
    private void TickClearConfirm(float deltaTime)
    {
        if (_standing < StandingTargetForWave(_currentWave))
        {
            CancelClearConfirm();   // belt-and-braces: the count signal cancels this too
            return;
        }

        _clearConfirmRemaining -= deltaTime;
        if (_clearConfirmRemaining > 0f || FirstBlockAboveLine() != null) return;

        _clearPending = false;
        while (_standing >= StandingTargetForWave(_currentWave))
        {
            _wavesCleared = Mathf.Max(_wavesCleared, _currentWave);
            _currentWave++;
        }
        // No ability offer here (removed 2026-08-24): wave mode runs WITHOUT abilities -
        // see BansAbility above. The per-cleared-wave draft this used to queue was the
        // mode's only offer source (wave configs author powerUpChoiceEveryBlocks 0).
        // Mathf.Max, not a bare assign: a mid-run re-solve (procedural floors) can leave the
        // live line ABOVE the next wave's solved height, and the laser must never descend into
        // a standing tower - the tighter solve waits for a later wave to catch up.
        _lineTargetY = Mathf.Max(_lineTargetY, CurrentLineWorldY());
        BeginRevealHold();          // re-arms the reveal timers; the gate is already held
    }

    private void CancelClearConfirm()
    {
        _clearPending = false;
        _clearConfirmRemaining = 0f;
        if (!_waitingForReveal) WaveRevealGate.Release();
    }

    // A wave just cleared: hold the next piece until the line settles at the new height and the
    // band it reveals has popped in. SpawnNextBlock was about to run for this very lock (the lock
    // raised BlockPlaced -> StandingBlocksChanged -> here -> the gate, all before BlockController
    // fires OnBlockLocked -> Spawner), so setting the gate now suppresses that imminent spawn;
    // releasing the hold raises spawn availability and the Spawner retries itself.
    private void BeginRevealHold()
    {
        _waitingForReveal = true;
        _settledGenTick = -1; // captured once the line reaches its new height
        _holdElapsed = 0f;
        WaveRevealGate.Hold();
    }

    private void EndRevealHold()
    {
        _waitingForReveal = false;
        WaveRevealGate.Release();
    }

    // Release the next piece once the transition has fully settled: the line has reached its new
    // height AND the island manager has run a generation pass against the raised ceiling (so any
    // band it was going to reveal now exists) AND no revealed island is still popping. When the
    // new band is empty (or islands are disabled) there are no pops to wait on, so this releases
    // the instant the line settles. A timeout backstops a missing/quiet island system.
    private void TickRevealHold(float deltaTime, bool lineSettled)
    {
        _holdElapsed += deltaTime;

        if (lineSettled && _settledGenTick < 0)
        {
            _settledGenTick = StaticSupportIslandManager.GenerationTick;
        }

        bool generationRan = _settledGenTick >= 0 &&
            StaticSupportIslandManager.GenerationTick > _settledGenTick;
        bool revealComplete = generationRan && !StaticSupportIslandManager.HasPendingPops;

        // Backstop only for a missing/quiet island system (generation never ran): gated on no
        // pops pending so it can never truncate a band still scaling in - a dense reveal whose
        // staggered pops outlast the budget is always waited out in full.
        bool timedOut = _holdElapsed > lineRiseSeconds + RevealHoldTimeout &&
            !StaticSupportIslandManager.HasPendingPops;

        if (revealComplete || timedOut)
        {
            EndRevealHold();
        }
    }

    /// <summary>Blocks still to STAND before the current wave clears and the line rises. Can
    /// exceed the wave's own quota after losses - destroyed blocks reopen the bill. Public for
    /// WaveHud, which shows this in the top-right pill (the old world-space counter sat under
    /// the overlay-canvas consumable slots - unwinnable by construction, see WaveHud).</summary>
    public int BlocksRemaining => Mathf.Max(0, StandingTargetForWave(_currentWave) - _standing);

    /// <summary>The beam's resolved primary colour (chapter accent, or the authored fallback) -
    /// WaveHud borrows it so the HUD counter and the laser read as one system.</summary>
    public Color LaserColor => _resolvedColor;

    public override void OnUpdate(LevelModifierContext context, float deltaTime)
    {
        if (_laser == null) return;

        // Floor config can resolve after level start on some paths (procedural floors); the
        // wave math must follow it or every solved height is wrong for the real terrain.
        GameModeConfig config = context?.GameManager != null ? context.GameManager.ActiveConfig : null;
        if (config != null && !ReferenceEquals(config.FloorSegments, _cachedSegments))
        {
            RebuildWaveMath();
            // The line NEVER descends, even when the re-solve against the real floor comes
            // out lower than the boot solve - a descending laser into a standing tower is a
            // zap cascade. The tighter solve simply waits for the next wave to catch up.
            _lineTargetY = Mathf.Max(_lineTargetY, CurrentLineWorldY());
        }

        // BEFORE the glide: a confirm raises _lineTargetY, and lineSettled below must already
        // account for it. Ticked after the glide, the confirm frame would compute lineSettled
        // against the OLD target (still true), and TickRevealHold would capture its "settled"
        // island tick while the line had not started rising - releasing the next piece into a
        // reveal that had not happened yet, the exact race the hold exists to prevent.
        if (_clearPending) TickClearConfirm(deltaTime);

        // Glide toward the current wave's height, pulse, and track the camera horizontally.
        float riseSpeed = Mathf.Abs(_lineTargetY - _lineY) / Mathf.Max(0.05f, lineRiseSeconds);
        _lineY = Mathf.MoveTowards(_lineY, _lineTargetY, Mathf.Max(riseSpeed, 2f) * deltaTime);

        // The ceiling follows the SETTLED line, so the freshly revealed island band pops
        // in after the rise completes, not while the line is still gliding through it.
        // Row boundary only - the half-cell grace never feeds the island ceiling (see OnLevelStart).
        bool lineSettled = Mathf.Approximately(_lineY, _lineTargetY);
        if (lineSettled) TowerHeightLimit.Set(_lineY - LaserGraceWorld);

        if (_waitingForReveal) TickRevealHold(deltaTime, lineSettled);

        // Camera tracking, pulse and flash live inside WaveLaserLine; the modifier only owns
        // the height (the same value the ceiling and the zap check read).
        _laser.SetY(_lineY);

        _zapCooldown -= deltaTime;
        if (_zapCooldown <= 0f) CheckViolations();
    }

    // A landed block whose top crosses the line is zapped: destroyed + one life lost (the
    // normal GameOver flow ends the run when lives are out). One zap per cooldown window so
    // the collapse caused by a zap can't instantly drain every life.
    private void CheckViolations()
    {
        BlockController block = FirstBlockAboveLine();
        if (block == null || !block.TryGetWorldBounds(out Bounds bounds)) return;

        BlockShatterFx.Spawn(bounds, _resolvedColor);
        // The zapped block leaves the board - drop it from the live placed-block total
        // (which reopens its wave's quota through the standing-count signal).
        GameEvents.RaiseBlockDestroyed(block);
        Object.Destroy(block.gameObject);
        _zapCooldown = ZapCooldownSeconds;
        _laser.Flash();
        TowerCameraController.Impact(0.15f, 0.2f);
        _context?.GameManager?.GameOver();
    }

    // The one definition of "in violation": a LANDED block whose top crosses the line (the
    // falling piece passes freely - it spawns above it). Shared by the zap and the clear-confirm
    // gate so a wave can never be credited past a block the laser is about to take.
    private BlockController FirstBlockAboveLine()
    {
        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded) continue;
            if (!block.TryGetWorldBounds(out Bounds bounds)) continue;
            if (bounds.max.y <= _lineY + 0.02f) continue;
            return block;
        }
        return null;
    }

    // ---- Wave math (delegated to WaveSolver - one implementation, shared with the editor
    // report so a printed table and a played run can never disagree) --------------------------

    /// <summary>Line height (cells above the datum) while the given 1-based wave runs: the
    /// smallest h whose capacity, at the wave's required density, holds every block asked for
    /// so far. Cached per wave; strictly rising by at least WaveSolver.MinRiseCells.</summary>
    private float LineHeightCellsForWave(int waveNumber)
    {
        if (_lineHeightsCells.Count < waveNumber)
        {
            WaveSolver.SolveLineHeights(_columnTops, _avgCellsPerPiece, DifficultyRank,
                waveNumber, _lineHeightsCells);
        }
        return _lineHeightsCells[waveNumber - 1];
    }

    // Rebuilds everything the solver depends on (column tops, cells-per-piece) and invalidates
    // solved heights. Cleared-wave STATE survives - only geometry is recomputed.
    private void RebuildWaveMath()
    {
        GameModeConfig config = _context?.GameManager != null ? _context.GameManager.ActiveConfig : null;
        _cachedSegments = config != null ? config.FloorSegments : null;
        _avgCellsPerPiece = WaveSolver.AverageCellsPerPiece(
            _context?.Spawner != null ? _context.Spawner.ConfiguredBlockBag : null,
            WaveSolver.MagmaRate(config));
        WaveSolver.BuildColumnTops(config != null ? config.FloorSegments : null, _columnTops,
            DifficultyRank);
        _lineHeightsCells.Clear();
    }

    // The line hangs half a cell above the NEAREST ROW BOUNDARY to the solved height (draw,
    // island ceiling and zap check all use this one value, so they can never disagree). Blocks
    // only ever top out on row boundaries, so the clearance is always exactly half a cell: a
    // flush-full tower can settle and wobble without grazing the laser, and one more full row
    // still clearly crosses. See WaveSolver.LaserCellsForSolvedHeight for why the snap matters -
    // a bare `solved + 0.5` left as little as 0.08 cells of clearance, and none at all whenever
    // a solve ended in ~.5: the laser then sat exactly on the top of the block that legally
    // filled that row, and any settle jiggle zapped it (Nick, July 2026). The snap never changes
    // how many rows fit - only the margin above them.
    private float CurrentLineWorldY()
        => _floorY + WaveSolver.LaserCellsForSolvedHeight(LineHeightCellsForWave(_currentWave)) * GridSpacing;

    // The layered beam (WaveLaserLine owns the whole look, including the chapter laser.png
    // hook), coloured by the active chapter's two accent colours - each chapter authored its
    // own pair, so the laser belongs to the world it cuts across. lineColor is only the
    // fallback for runs without a chapter (custom games). The blocks-remaining countdown
    // lives in WaveHud on the screen-space HUD, not here: a world-space number is
    // unconditionally composited UNDER every overlay canvas, so the player-arranged
    // consumable slots could always occlude it.
    private void CreateLineVisual()
    {
        // ChapterSkins.ActiveChapter, not GameMenuStyle.ActiveChapter: the skin snapshot is
        // latched by GameManager.Awake for THIS run, while the menu helper re-derives from
        // live selection state - art and colour must come from the same chapter.
        ChapterDefinition chapter = ChapterSkins.ActiveChapter;
        _resolvedColor = chapter != null ? chapter.MenuAccentColor : lineColor;
        Color accent = chapter != null
            ? chapter.MenuAccentSecondaryColor
            : Color.Lerp(lineColor, Color.white, 0.5f);
        _laser = WaveLaserLine.Create(_resolvedColor, accent);
        _laser.SetY(_lineY);
    }
}
