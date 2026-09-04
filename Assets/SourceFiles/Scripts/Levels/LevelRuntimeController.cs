using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Runs the selected level's meta layer: drives its LevelModifiers, tracks the win target,
/// and owns both end-of-run screens (game over and level complete) via RunResultsScreen.
/// Added to the GameManager's object at runtime.
/// </summary>
public class LevelRuntimeController : MonoBehaviour
{
    // Meeting the win target arms a hold-steady countdown instead of completing instantly:
    // nothing spawns, physics and the loss rules stay live, and only a tower that survives
    // the full window wins. Rapid-dropping the last blocks therefore buys nothing - they
    // must actually stay up. ReachHeight is also re-checked against the LIVE standing
    // tower (the recorded max is monotonic and would stay "met" after a collapse).
    private const float WinVerificationSeconds = 5f;

    /// <summary>True while the hold-steady countdown runs. Kept for older read sites.</summary>
    public static bool IsVerifyingWin =>
        GameManager.Instance != null && GameManager.Instance.CurrentPhase == GamePhase.WinVerifying;

    private readonly List<LevelModifier> _activeModifiers = new List<LevelModifier>();
    private LevelModifierContext _modifierContext;
    private LevelDefinition _level;
    // The BRONZE victory rule (the authored target) - the reference condition for XP progress,
    // end-of-run metrics, HasGoal and the timed clock. The medal ladder arms _armedCondition.
    private WinCondition _winCondition;
    private System.Func<float> _liveHeightFunc; // cached delegate so BuildWinContext never allocates
    private bool _completionPendingWhilePaused;
    private bool _victoryShown;
    // ---- Medal ladder (LevelTiers). The old completed/already-completed binary became a
    // ladder: _armedTier is the lowest UNEARNED tier - the one the hold-steady flow verifies
    // next - and null means the ladder is dormant (all tiers earned, Endless, or no level).
    // _armedCondition is the same goal type re-aimed at that tier's threshold; rebuilt on every
    // rung. _sessionVerifiedValue carries the run's own verified record so Custom Game levels
    // (no store identity) still climb, and so derivation never waits on a save.
    private MedalTier? _armedTier;
    private WinCondition _armedCondition;
    private float _sessionVerifiedValue;
    private MedalTier? _highestTierEarnedThisRun; // feeds the end-of-run card's celebration
    // The run was adjudicated as a WIN: its ReportFinish(won:true) went out (at the run's
    // FIRST newly earned rung - ANY tier, so a replay that newly silvers/golds is a win too:
    // finish_run refunds the attempt per-run, BACKEND.md §6.2, and SHOP.md wins are free).
    // Every later report this run is a score IMPROVEMENT, never a second finish.
    private bool _finishReportedThisRun;
    // Bronze newly earned THIS run (LevelCompleted was raised, CoinLedger granted the win
    // bonus): drives the cards' coin line - a replay never re-banks the bonus.
    private bool _bronzeCompletedThisRun;
    // The best as it stood when the run STARTED, on the board this run plays for (SHOP.md §5:
    // a boosted run races the boosted best). A field copy, not the live LevelBest: the medal
    // ladder banks results MID-run (the win adjudication), and the end-of-run record
    // comparison must not race the run's own writes - a card comparing the run against its
    // own mid-run banked score could never say NEW BEST.
    private ProgressStore.LevelBest _preRunBest;
    // The BRONZE goal was met at least once THIS run (even if the tower later fell, and even on
    // a fully-earned level where nothing arms). Gates the monotonic-goal re-arm polling
    // (TickWinVerification); the card line it once drove was cut (Nick 2026-08-29).
    private bool _targetMetThisRun;
    private RunResultsScreen.Content _victoryContent; // built at CompleteLevel; shown possibly later (paused)
    private float _verificationRemaining;
    private GameObject _countdownRoot;
    private TextMeshProUGUI _countdownDigit;
    private TextMeshProUGUI _countdownDigitShadow; // painted twin behind the digit (see CreateShadowedText)
    private RectTransform _countdownDigitRoot;     // scaling this punches digit + shadow together
    private RectTransform _countdownBarLeft;       // accent fills draining toward the cube
    private RectTransform _countdownBarRight;
    private RectTransform _countdownCube;          // the armed rung's cube, wobbling to steady
    private int _countdownShownSecond = -1;
    private float _countdownDigitPunchAge;
    private bool _hasTimeLimit;
    private float _timeRemaining;
    private GameObject _timerRoot;
    private RectTransform _timerRect;
    private TextMeshProUGUI _timerLabel;
    private int _timerShownSecond = -1;
    // XP (XP.md): the run's peak unclamped goal progress, sampled on every progress signal
    // (a collapse right before the end must not erase what the run reached), and a latch so
    // the win -> game-over and win -> quit sequences award exactly once.
    private float _xpPeakProgress;
    private bool _xpAwarded;
    private float _xpAwardedProgress;   // what the local award has already paid for
    private static readonly EndlessWinCondition XpFallbackCondition = new EndlessWinCondition();

    /// <summary>The live controller of the current run, for callers outside the scene wiring
    /// (the pause menu's quit/restart). Published here, never via Find - a rebuild frame's
    /// Find can grab a dying instance.</summary>
    public static LevelRuntimeController Active { get; private set; }

    /// <summary>True while the timed-goal clock card occupies the top-right HUD row - MedalHud
    /// yields the slot to it (same arrangement as the wave pill). Live, not per-run: the card
    /// dies at gold and at game over, and the pill may then reclaim the row.</summary>
    public static bool TimerCardVisible => Active != null && Active._timerRoot != null;

    private void Start()
    {
        _level = LevelSelectionState.SelectedLevel;
        _winCondition = _level != null ? _level.WinCondition : null;
        // Arm the lowest unearned tier; earned tiers never re-verify (a replay with bronze
        // banked opens straight onto silver's target, and a fully-golded level stays dormant).
        _armedTier = LevelTiers.LowestUnearned(_level);
        RebuildArmedCondition();
        _preRunBest = CapturePreRunBest(_level);
        _liveHeightFunc = LiveTowerHeight;
        _modifierContext = new LevelModifierContext
        {
            GameManager = GameManager.Instance,
            Spawner = FindAnyObjectByType<Spawner>(),
            Status = GetComponent<StatusEffects>(),
            Level = _level
        };

        StartModifiers();
        InitializeTimedGoal();
        // After StartModifiers - the type-claim veto reads _activeModifiers. A silver/gold
        // chase ramps toward ITS cap from the first brick.
        ApplyTierSpeedCap();

        // The level-start goal text ("Stack 100 blocks - ...") is RETIRED (Nick 2026-08-30:
        // it read as awful over the intro camera pan). Its slot will show the game-type LOGO
        // instead once the logo art lands - the banner machinery below stays for that, and
        // for the mid-run "tower fell" abort message. The pre-run modal still carries the
        // goal sentence, so the information is not lost. (GoalBannerSuppressed() remains for
        // the tutorial, which owns the intro messaging when it runs.)
    }

    // Only one banner at a time: the verification abort/re-arm cycle can fire repeatedly
    // (a wobbling peak), and stacked translucent strips render as doubled text.
    private GameObject _bannerRoot;
    private Coroutine _bannerCoroutine;

    private void ShowBanner(string text)
    {
        if (_bannerCoroutine != null) StopCoroutine(_bannerCoroutine);
        if (_bannerRoot != null) Destroy(_bannerRoot);
        _bannerCoroutine = StartCoroutine(ShowInstructionBanner(text));
    }

    // One-sentence goal banner in the upper third at level start: fade in, hold, fade out.
    // Unscaled time so it behaves the same if the level opens paused (power-up choice etc.).
    // Free-floating shadowed text - the old full-width black strip read as a debug bar
    // (Nick 2026-08-29, with the hold-steady restyle).
    private System.Collections.IEnumerator ShowInstructionBanner(string text)
    {
        GameObject root = RuntimeUiKit.CreateOverlayCanvas("Level Instruction", 3000);
        _bannerRoot = root;

        CreateShadowedText(root.transform, "Banner", text, 38, RuntimeUiKit.TitleColor,
            RuntimeUiKit.TitleFont, 2f, new Vector2(0.5f, 0.74f), new Vector2(940f, 160f),
            wrap: true, display: false, out _, out _);

        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;

        const float fadeIn = 0.35f, hold = 2.8f, fadeOut = 0.8f;
        for (float t = 0f; t < fadeIn; t += Time.unscaledDeltaTime)
        {
            group.alpha = t / fadeIn;
            yield return null;
        }
        group.alpha = 1f;
        yield return new WaitForSecondsRealtime(hold);
        for (float t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
        {
            group.alpha = 1f - t / fadeOut;
            yield return null;
        }
        Destroy(root);
    }

    private void OnEnable()
    {
        Active = this;
        GameEvents.BlockPlaced += HandleBlockPlaced;
        GameEvents.StandingBlocksChanged += HandleStandingBlocksChanged;
        GameEvents.HeightChanged += HandleHeightChanged;
        GameEvents.GameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
        GameEvents.BlockPlaced -= HandleBlockPlaced;
        GameEvents.StandingBlocksChanged -= HandleStandingBlocksChanged;
        GameEvents.HeightChanged -= HandleHeightChanged;
        GameEvents.GameOver -= HandleGameOver;
        DestroyCountdownUi();   // also stops the countdown loop - SfxPlayer persists across scenes
        DestroyTimerUi();
        EndModifiers();
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PopPause(this);
            GameManager.Instance.ReleasePhase(this);
            SetVerificationSpawnHold(false);
        }
    }

    // Personal bests are recorded at every end-of-run (monotonic - only improvements stick).
    private void HandleGameOver(int finalScore, float maxHeightMeters)
    {
        // A run can die mid-verification (the dropped blocks took the last life).
        if (_countdownRoot != null) DestroyCountdownUi();
        DestroyTimerUi();

        // Card content BEFORE ReportResult: the card compares this run against the best as it
        // stood before the run banked - otherwise a new best could never be detected.
        RunResultsScreen.Content content = BuildGameOverContent();
        // The sting lands on the moment of death; the card follows after a beat (Nick
        // 2026-09-04: with the card popping the same frame, "I don't even know what happened" -
        // the brick clipping the laser line was hidden under the modal). DeathBeatFx stages the
        // beat: hit-stop, half speed, a push-in about the death point, colour draining - so the
        // fatal fall and the tower's reaction play out on screen. Any tap skips to the card.
        RunResultsScreen.PlaySting(content);
        // A loss that banked an achievement (new tier, or a new best above it) is not a dark
        // moment: keep the physical beat, skip the grey-out and the HUD fade (Nick 2026-09-04).
        bool achievement = content.TierEarnedThisRun.HasValue || content.Metric.IsNewRecord;
        DeathBeatFx.Play(GameOverCardDelaySeconds, drain: !achievement);
        StartCoroutine(ShowGameOverCardAfterBeat(content));
        int reportedScore = ReportedScore(finalScore);
        if (_level != null) ProgressStore.ReportResult(_level, reportedScore, maxHeightMeters, RunSuppliesState.ActiveRunBoosted);
        AwardRunXp(won: false);
        // Server finish (BACKEND.md §6.2): score submission and the XP award ride the same
        // exchange. Local bests above stay local-first regardless.
        if (_finishReportedThisRun)
        {
            // Toppling out of a run already adjudicated as a win (whether the player kept
            // going for the next rung or past the victory card). The win already banked
            // the refund and its XP, but everything stacked SINCE is what the player was
            // invited to chase - it has to reach the board, or every winner ends up tied
            // at the target score and the leaderboard says nothing.
            AwardOvershootXp();
            RunGate.ReportScoreImprovement(ProgressStore.LevelId(_level), reportedScore,
                maxHeightMeters, XpProgressForReport());
        }
        else
        {
            RunGate.ReportFinish(won: false, reportedScore, maxHeightMeters, XpProgressForReport(),
                GameManager.Instance != null ? GameManager.Instance.EndCause : RunEndCause.Other);
        }
    }

    /// <summary>Pause-menu quit or restart: the run ends without a game over, but it still
    /// HAPPENED - bank the bests and report the finish so the abandon pays its participation
    /// + progress XP (Nick 2026-08-01) exactly like a loss at the same point would.
    /// Quitting out of a run already adjudicated as a win reports a score IMPROVEMENT
    /// instead of a finish (the finish went out at the first earned rung): do not
    /// re-add an early return here, or the whole post-win score is dropped again.
    /// Double-reporting is prevented by construction - ProgressStore.ReportResult is
    /// monotonic, AwardRunXp is latched, and the improvement window is one-shot per run.</summary>
    public void ReportAbandonedRun()
    {
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;
        RunResult result = GameManager.Instance.CurrentRunResult;
        int reportedScore = ReportedScore(result.Score);

        if (_level != null) ProgressStore.ReportResult(_level, reportedScore, result.MaxHeight, RunSuppliesState.ActiveRunBoosted);
        AwardRunXp(won: false);   // latched by _xpAwarded: a no-op once the win awarded
        if (_finishReportedThisRun)
        {
            // Quitting out of a post-win Keep Playing session. The run is already finished
            // server-side, so this is a score improvement rather than a second finish - but
            // the score counts either way, exactly as toppling out of it does.
            AwardOvershootXp();
            RunGate.ReportScoreImprovement(ProgressStore.LevelId(_level), reportedScore,
                result.MaxHeight, XpProgressForReport());
        }
        else
        {
            RunGate.ReportFinish(won: false, reportedScore, result.MaxHeight, XpProgressForReport(),
                RunEndCause.Abandon);
        }
    }

    // How long the player gets to SEE the death before the card covers it (unscaled seconds).
    // 2 s read as "nothing happens, then the modal" (Nick 2026-09-04); 0.7 s is long enough to
    // register the fatal fall without an empty pause.
    private const float GameOverCardDelaySeconds = 0.7f;
    private const float GameOverCardSkipArmSeconds = 0.25f; // the fatal tap itself must not skip

    private System.Collections.IEnumerator ShowGameOverCardAfterBeat(RunResultsScreen.Content content)
    {
        float start = Time.unscaledTime;
        while (Time.unscaledTime - start < GameOverCardDelaySeconds)
        {
            yield return null;
            if (Time.unscaledTime - start < GameOverCardSkipArmSeconds) continue;
            Pointer pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame) break;
        }
        RunResultsScreen.Show(content, muted: true); // sting already played at the moment of death
    }

    private RunResultsScreen.Content BuildGameOverContent()
    {
        RunResult result = GameManager.Instance != null ? GameManager.Instance.CurrentRunResult : default;
        bool endless = _winCondition == null || !_winCondition.HasGoal;
        RunResultsScreen.Content content = new RunResultsScreen.Content
        {
            Victory = false,
            Metric = ResolveEndOfRunMetric(result),
            EndlessHeight = endless ? result.MaxHeight : 0f,
            // A run that showed the victory card already advertised (and banked) its coins
            // there - re-advertising them here would read as a second payout that never happens.
            // A bronze earned via the in-run toast never got a card, so its win bonus (banked by
            // CoinLedger when LevelCompleted was raised) surfaces here instead.
            Coins = _victoryShown
                ? 0
                : result.CoinsEarned + (_bronzeCompletedThisRun ? CoinLedger.WinBonusCoins : 0),
            Boosted = RunSuppliesState.ActiveRunBoosted,
            PrimaryLabel = "Try Again",
            OnPrimary = () => { if (GameManager.Instance != null) GameManager.Instance.RestartGame(); },
        };
        PopulateTierContent(ref content);
        // A run that newly earned a rung celebrates it on THIS card too, even when the gold
        // victory card already showed (Nick 2026-08-29: a game over after an achievement must
        // never read as a plain failure screen) - only the coin line stays suppressed above,
        // because those coins were genuinely already advertised and banked.
        return content;
    }

    // The goal's own idea of "the score that matters" - a presentation-owning modifier wins
    // (waves, not raw blocks), otherwise the win condition's metric. Same precedence the menu
    // uses; the provider lookup is shared so the two can never disagree.
    private ResultMetric ResolveEndOfRunMetric(RunResult result)
    {
        ProgressStore.LevelBest best = _preRunBest;

        ILevelMenuProgressProvider provider = LevelMenuPresentation.FindProgressProvider(_level);
        ResultMetric? overrideMetric = provider?.EndOfRunMetric(_level, result, best);
        if (overrideMetric.HasValue) return overrideMetric.Value;

        WinCondition condition = _winCondition ?? new EndlessWinCondition();
        return condition.EndOfRunMetric(result, best);
    }

    // The score banked to bests and submitted to the board. A metric-owning modifier replaces
    // the raw run score (puzzle waves report encoded waves-cleared); everything else reports
    // the run's cumulative score untouched. First non-null override wins.
    private int ReportedScore(int rawScore)
    {
        for (int i = 0; i < _activeModifiers.Count; i++)
        {
            int? overridden = _activeModifiers[i] != null
                ? _activeModifiers[i].OverrideReportedScore(_modifierContext, rawScore)
                : null;
            if (overridden.HasValue) return overridden.Value;
        }
        return rawScore;
    }

    // The results card compares against the board this run played for (SHOP.md §5): a boosted
    // run races the boosted best, never the clean one. Metric providers only read the clean
    // fields, so a boosted run gets a view with the boosted pair mapped onto them. Always a
    // field COPY, captured at Start - see _preRunBest for why the live instance won't do.
    private static ProgressStore.LevelBest CapturePreRunBest(LevelDefinition level)
    {
        ProgressStore.LevelBest best = level != null ? ProgressStore.GetBest(level) : null;
        if (best == null) return null;
        bool boosted = RunSuppliesState.ActiveRunBoosted;
        return new ProgressStore.LevelBest
        {
            levelId = best.levelId,
            bestScore = boosted ? best.bestScoreBoosted : best.bestScore,
            bestHeightMeters = boosted ? best.bestHeightMetersBoosted : best.bestHeightMeters,
            achievedAtUnixUtc = best.achievedAtUnixUtc,
        };
    }

    private void Update()
    {
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        if (GameManager.Instance.IsGamePaused)
        {
            return;
        }

        // A win that landed while the power-up choice was open is shown once that closes.
        if (_completionPendingWhilePaused)
        {
            _completionPendingWhilePaused = false;
            ShowCompletionPanel();
            return;
        }

        TickWinVerification();
        TickTimedGoal();

        for (int i = 0; i < _activeModifiers.Count; i++)
        {
            _activeModifiers[i].OnUpdate(_modifierContext, Time.deltaTime);
        }
    }

    // ---- Win verification (hold steady) -------------------------------------------------------

    private void TickWinVerification()
    {
        if (_armedTier == null || _level == null) return;

        if (IsVerifyingWin)
        {
            // The armed tier's goal must still hold through the countdown: a height collapse, or
            // a block destroyed/dropped below the live count, hands the level back. The condition
            // owns the rule (and any hysteresis slack); the controller stays goal-agnostic.
            if (_armedCondition != null && !_armedCondition.IsStillHeld(BuildWinContext()))
            {
                AbortVerification();
                return;
            }

            // HOLD STEADY means PHYSICALLY steady: mid-collapse bricks keep counting until
            // they cross the loss line, so on a live-count goal a collapse in the countdown's
            // final seconds could still "win" with the tower airborne (Nick 2026-08-30).
            // Several landed blocks moving fast at once is the collapse signature - abort and
            // hand the level back; once the wreckage settles the goal re-arms (the motion
            // aborts opt into the 5 Hz re-arm poll below, since a live count that never
            // dipped under the target fires no fresh crossing event).
            if (TowerInMotion())
            {
                _rearmAfterMotionAbort = true;
                AbortVerification();
                return;
            }

            _verificationRemaining -= Time.deltaTime;
            UpdateCountdownLabel();
            if (_verificationRemaining <= 0f)
            {
                DestroyCountdownUi();
                OnHoldSteadyComplete(); // banks the tier; gold requests the Completed phase
            }
            return;
        }

        // A pending debounced arm confirms (or cancels) here every frame: event-driven goals
        // fire no further events while nothing changes, so the retry can't ride on them.
        if (_armMetSince >= 0f)
        {
            if (ArmedGoalMetNow())
            {
                TryBeginVerification();
            }
            else
            {
                _armMetSince = -1f; // the ghost passed - never armed, nothing shown
                SetVerificationSpawnHold(false);
            }
            return;
        }

        // After a collapse aborted verification, a goal that arms from a MONOTONIC signal (the
        // height record only rises) can never re-fire for the same peak - re-arm from the live
        // tower instead. Polled at 5 Hz, not per frame: LiveTowerHeight walks every landed block's
        // cells, and this watch can stay on for minutes while the player rebuilds a tall tower.
        // Gate on the BRONZE goal having been met this run: any armed tier's threshold is at or
        // above bronze, so a higher rung can never be met without this flag already set.
        if (_targetMetThisRun && _armedCondition != null &&
            (_armedCondition.ReArmsByPolling || _rearmAfterMotionAbort))
        {
            _rearmPollTimer -= Time.deltaTime;
            if (_rearmPollTimer > 0f) return;
            _rearmPollTimer = RearmPollInterval;

            // A motion abort must wait out the wreckage: re-arming while bricks still fly
            // would flicker countdown on/off through the collapse.
            if (_rearmAfterMotionAbort && TowerInMotion()) return;
            if (_armedCondition.IsMet(BuildWinContext())) TryBeginVerification();
        }
    }

    private const float RearmPollInterval = 0.2f;
    private float _rearmPollTimer;

    // Motion-abort tuning: a settling just-locked brick drifts well under 1 u/s; a topple
    // sends several bricks past 2 u/s at once. One fast block alone (a knocked-off straggler)
    // is the live count's business, not a collapse.
    private const float MotionAbortSpeed = 1.75f;
    private const int MotionAbortBlockCount = 3;
    private bool _rearmAfterMotionAbort; // cleared when verification actually re-arms

    /// <summary>The collapse signature: several LANDED blocks in fast motion at once.</summary>
    private static bool TowerInMotion()
    {
        int moving = 0;
        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded) continue;
            if (block.CurrentSpeed < MotionAbortSpeed) continue;
            if (++moving >= MotionAbortBlockCount) return true;
        }
        return false;
    }

    // A ghost hold (a falling brick's progress event momentarily satisfying the armed
    // threshold, common right after a rung completes with the next one nearly met) must not
    // flash the overlay, blip the countdown loop, or flip the phase - the goal has to stay
    // met THIS long before verification actually arms. The abort banner has its own guard
    // (AbortBannerMinHoldSeconds) for holds that armed legitimately and fell early.
    private const float ArmDebounceSeconds = 0.2f;
    private float _armMetSince = -1f;

    // Returns whether verification actually armed. Every arming precondition lives HERE, so
    // callers must branch on the outcome, never re-derive the conditions - a timed goal that
    // assumes arming succeeded would freeze its clock forever (the bug this shape prevents).
    // During the debounce window it returns false; TickWinVerification re-drives the pending
    // arm every frame (event-driven goals get no further events while nothing changes).
    private bool TryBeginVerification(bool immediate = false)
    {
        // No armed tier = ladder dormant (all tiers earned, Endless): the target passes
        // silently and the player plays on for a best, exactly like the old already-completed
        // rule. _targetMetThisRun is maintained by TryArmFromProgress, not here.
        if (_armedTier == null || IsVerifyingWin) return false;
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return false;

        if (!immediate)
        {
            if (_armMetSince < 0f) _armMetSince = Time.unscaledTime;
            // The debounce window is still phase-Playing, so it holds spawns itself: without
            // this, the goal-crossing lock's own (frame-deferred) spawn chain drops the next
            // brick INTO the imminent countdown.
            SetVerificationSpawnHold(true);
            if (Time.unscaledTime - _armMetSince < ArmDebounceSeconds) return false;
        }
        _armMetSince = -1f;

        GameManager.Instance.RequestPhase(this, GamePhase.WinVerifying);
        SetVerificationSpawnHold(false); // the WinVerifying phase gates spawning from here
        _rearmAfterMotionAbort = false;  // armed again - the motion-abort poll did its job
        _verificationRemaining = WinVerificationSeconds;
        BuildCountdownUi();
        UpdateCountdownLabel();
        return true;
    }

    private bool _verificationSpawnHold;

    // NO brick may ever enter play once a hold-steady is pending or running (Nick 2026-08-29).
    // The WinVerifying phase covers the countdown itself; this hold covers the arm-debounce
    // window before it, through the same owner-keyed gate every other spawn suppressor uses.
    private void SetVerificationSpawnHold(bool held)
    {
        if (_verificationSpawnHold == held || GameManager.Instance == null) return;
        _verificationSpawnHold = held;
        GameManager.Instance.SetSpawnSuspended(this, held);
    }

    // "The tower fell" is only an honest message when the player SAW a countdown running: a
    // hold can arm and abort within a frame or two (a falling brick fires a progress event
    // while the standing count still momentarily satisfies the armed threshold - common right
    // after a rung completes with the next one nearly met), and a banner for that ghost hold
    // reads as a bug (Nick 2026-08-29). Shorter than this = release silently.
    private const float AbortBannerMinHoldSeconds = 0.75f;

    private void AbortVerification()
    {
        if (GameManager.Instance != null) GameManager.Instance.ReleasePhase(this); // drops WinVerifying -> back to Playing
        DestroyCountdownUi();
        if (WinVerificationSeconds - _verificationRemaining >= AbortBannerMinHoldSeconds)
        {
            ShowBanner("The tower fell - keep building!");
        }
    }

    // The live snapshot the win condition reads. LiveTowerHeight is passed as a cached delegate so
    // a condition that doesn't need height (PlaceBlocks) never triggers the per-block walk.
    private WinContext BuildWinContext() => new WinContext(GameManager.Instance, _liveHeightFunc);

    // One owner for "the tower standing right now": GameManager.liveTowerHeight - the HUD
    // counter, the camera and this check all read the same walk (throttled to 0.15s), so they
    // can never disagree about whether the tower fell. A block only stops counting once it is
    // CLEARLY falling off (IsFallingClearOfTower: fast-fall latch + still descending + fallen
    // more than a cell below its seat) - a jolted-but-recovering peak block keeps counting, so
    // it can't flicker the hold-steady countdown into a false "tower fell" abort.
    private float LiveTowerHeight()
        => GameManager.Instance != null ? GameManager.Instance.liveTowerHeight : 0f;

    private void BuildCountdownUi()
    {
        if (_countdownRoot != null) return;

        SfxPlayer.PlayLoop("countdown", 0.8f); // clock runs for the 5->0 hold; stopped in DestroyCountdownUi
        _countdownRoot = RuntimeUiKit.CreateOverlayCanvas("Win Verification", 3200);
        Color accent = GameMenuStyle.Accent;

        // HOLD STEADY: display-font wordmark, horizontal light-to-accent gradient (the hero
        // number's language), painted shadow - no strip, no bar.
        TextMeshProUGUI wordmark = CreateShadowedText(_countdownRoot.transform, "HoldSteady",
            "HOLD STEADY", 46, Color.white, RuntimeUiKit.TitleFont, 10f,
            new Vector2(0.5f, WordmarkY), new Vector2(960f, 90f),
            wrap: false, display: true, out _, out _);
        RuntimeUiKit.ApplyHorizontalGradient(wordmark, Color.Lerp(accent, Color.white, 0.65f), accent);

        // The centerpiece line: ---- cube ---- (Nick 2026-08-29, "one composition"). The armed
        // rung's cube sits IN the progress line, floating on a slow bob that glides to a dead
        // stop as the window runs down (the hold, embodied - a perfectly still cube marks the
        // landing); the two accent bars drain from their outer ends toward the cube.
        BuildCountdownSegment("TrackL", GameMenuStyle.WithAlpha(accent, 0.18f), new Vector2(1f, 0.5f), -CubeGapHalf);
        BuildCountdownSegment("TrackR", GameMenuStyle.WithAlpha(accent, 0.18f), new Vector2(0f, 0.5f), CubeGapHalf);
        _countdownBarLeft = BuildCountdownSegment("FillL", GameMenuStyle.WithAlpha(accent, 0.95f), new Vector2(1f, 0.5f), -CubeGapHalf).rectTransform;
        _countdownBarRight = BuildCountdownSegment("FillR", GameMenuStyle.WithAlpha(accent, 0.95f), new Vector2(0f, 0.5f), CubeGapHalf).rectTransform;

        if (_armedTier.HasValue)
        {
            GameObject cube = new GameObject("TierCube", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _countdownCube = (RectTransform)cube.transform;
            _countdownCube.SetParent(_countdownRoot.transform, false);
            _countdownCube.anchorMin = _countdownCube.anchorMax = new Vector2(0.5f, LineY);
            _countdownCube.pivot = new Vector2(0.5f, 0.5f);
            _countdownCube.sizeDelta = new Vector2(96f, 96f);
            Image cubeImage = cube.GetComponent<Image>();
            cubeImage.sprite = MedalStyle.Sprite(_armedTier.Value, earned: true);
            cubeImage.color = MedalStyle.IconTint(earned: true);
            cubeImage.preserveAspect = true;
            cubeImage.raycastTarget = false;
        }

        // The countdown itself: one huge digit tucked under the line that punches in on every
        // second (5 -> 4 -> 3...), so the wait reads as a countdown, not a frozen banner.
        _countdownDigit = CreateShadowedText(_countdownRoot.transform, "Digit", "", 140,
            RuntimeUiKit.TitleColor, RuntimeUiKit.TitleFont, 0f,
            new Vector2(0.5f, DigitY), new Vector2(400f, 180f),
            wrap: false, display: true, out _countdownDigitShadow, out _countdownDigitRoot);

        _countdownShownSecond = -1; // force the first digit to set + punch immediately
    }

    // One tight stack (Nick 2026-08-29: minimal, little vertical margin): wordmark, the
    // cube-in-line right under it, the digit tucked under the cube.
    private const float WordmarkY = 0.745f;
    private const float LineY = 0.700f;
    private const float DigitY = 0.625f;
    private const float CountdownSegmentWidth = 150f;
    private const float CountdownBarHeight = 8f;
    private const float CubeGapHalf = 58f; // cube half (48) + breathing room
    private const float CubeBobPixels = 6f;      // full bob amplitude - subtle, never a shake
    private const float CubeBobHz = 0.6f;        // slow float, ~1.7s per cycle
    private const float CubeBobSettleFrac = 0.4f; // glide to a dead stop over the final 40%

    // One side of the ---- cube ---- line. The pivot sits at the INNER end (next to the
    // cube), so a shrinking fill drains from its outer edge toward the cube.
    private Image BuildCountdownSegment(string name, Color color, Vector2 pivot, float innerX)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(_countdownRoot.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, LineY);
        rect.pivot = pivot;
        rect.anchoredPosition = new Vector2(innerX, 0f);
        rect.sizeDelta = new Vector2(CountdownSegmentWidth, CountdownBarHeight);
        Image image = go.GetComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }


    /// <summary>Free-floating overlay text with a painted shadow twin behind it (UI.Shadow
    /// does not touch TMP meshes, so the shadow is a second TMP). The main text is a CHILD of
    /// the shadow, both under <paramref name="root"/> - scaling the root punches the pair
    /// together. <paramref name="display"/> = the Archivo display voice.</summary>
    private static TextMeshProUGUI CreateShadowedText(Transform parent, string name, string text,
        int size, Color color, Font font, float spacing, Vector2 anchor, Vector2 sizeDelta,
        bool wrap, bool display, out TextMeshProUGUI shadow, out RectTransform root)
    {
        root = RuntimeUiKit.CreateRect(parent, name, anchor, anchor, new Vector2(0.5f, 0.5f),
            Vector2.zero, sizeDelta);

        shadow = RuntimeUiKit.CreateTmp(root, "Shadow", text, size, new Color(0f, 0f, 0f, 0.55f),
            TextAnchor.MiddleCenter, FontStyle.Normal, font);
        shadow.rectTransform.anchoredPosition = new Vector2(0f, -4f);

        TextMeshProUGUI main = RuntimeUiKit.CreateTmp(shadow.rectTransform, "Text", text, size,
            color, TextAnchor.MiddleCenter, FontStyle.Normal, font);
        main.rectTransform.anchoredPosition = new Vector2(0f, 4f); // cancels the shadow offset

        foreach (TextMeshProUGUI tmp in new[] { shadow, main })
        {
            if (display) tmp.font = RuntimeUiKit.TmpDisplayFont;
            tmp.characterSpacing = spacing;
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
        }
        return main;
    }

    private const float DigitPunchSeconds = 0.3f;
    private const float DigitPunchStartScale = 1.7f;

    private void UpdateCountdownLabel()
    {
        if (_countdownDigit == null) return;

        // Everything but the cube is built unconditionally alongside _countdownDigit (the
        // early-return above), so only the cube gets a guard - it exists per armed tier.
        float remainingFrac = Mathf.Clamp01(_verificationRemaining / WinVerificationSeconds);
        Vector2 fillSize = new Vector2(CountdownSegmentWidth * remainingFrac, CountdownBarHeight);
        _countdownBarLeft.sizeDelta = fillSize;
        _countdownBarRight.sizeDelta = fillSize;

        // The cube floats on a slow, subtle bob (never a rotation shake - rejected as ugly,
        // Nick 2026-08-29) and glides to a dead stop over the final stretch of the window, so
        // a perfectly still cube marks the hold landing - the beat the earned pill pops on.
        if (_countdownCube != null)
        {
            float amp = CubeBobPixels * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, remainingFrac / CubeBobSettleFrac));
            float bob = Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI * CubeBobHz) * amp;
            _countdownCube.anchoredPosition = new Vector2(0f, bob);
        }

        int seconds = Mathf.CeilToInt(Mathf.Max(0f, _verificationRemaining));
        if (seconds != _countdownShownSecond)
        {
            _countdownShownSecond = seconds;
            _countdownDigit.text = seconds.ToString();
            _countdownDigitShadow.text = _countdownDigit.text;
            _countdownDigitPunchAge = 0f;
        }

        // Scale-punch: lands big and settles to rest size over the punch window. The root
        // carries the scale so the digit and its painted shadow punch as one.
        _countdownDigitPunchAge += Time.deltaTime;
        float t = Mathf.Clamp01(_countdownDigitPunchAge / DigitPunchSeconds);
        float eased = 1f - (1f - t) * (1f - t); // ease-out
        float scale = Mathf.Lerp(DigitPunchStartScale, 1f, eased);
        _countdownDigitRoot.localScale = new Vector3(scale, scale, 1f);

        float presence = Mathf.Lerp(0.55f, 1f, eased);
        Color color = RuntimeUiKit.TitleColor;
        color.a = presence;
        _countdownDigit.color = color;
        Color shadowColor = _countdownDigitShadow.color;
        shadowColor.a = 0.55f * presence;
        _countdownDigitShadow.color = shadowColor;
    }

    private void DestroyCountdownUi()
    {
        if (_countdownRoot == null) return;
        SfxPlayer.StopLoop(); // ends the countdown clock on win, abort, or teardown
        Destroy(_countdownRoot);
        _countdownRoot = null;
        _countdownDigit = null;
        _countdownDigitShadow = null;
        _countdownDigitRoot = null;
        _countdownBarLeft = null;
        _countdownBarRight = null;
        _countdownCube = null;
    }

    // ---- Timed goals ---------------------------------------------------------------------------

    private void InitializeTimedGoal()
    {
        _hasTimeLimit = _winCondition != null && _winCondition.HasTimeLimit;
        _timeRemaining = _hasTimeLimit ? _winCondition.TimeLimitSeconds : 0f;
        if (!_hasTimeLimit) return;

        BuildTimerUi();
        UpdateTimerLabel(force: true);
    }

    private void TickTimedGoal()
    {
        if (!_hasTimeLimit || _level == null || GameManager.Instance == null) return;

        // The main clock only burns during active play. The 5-second win verification explicitly
        // freezes it (for EVERY tier's hold); if verification aborts, the same remaining time
        // resumes. It never resets between tiers - bronze and silver holds spend no clock, but
        // the chase for the next rung burns the same run's remaining time. Gold kills the clock
        // (OnHoldSteadyComplete); expiry after bronze/silver ends the run through the game-over
        // path, which celebrates whatever the run earned.
        if (GameManager.Instance.CurrentPhase != GamePhase.Playing || IsVerifyingWin)
        {
            UpdateTimerLabel();
            return;
        }

        // Freeze the clock only when verification ACTUALLY armed. On a fully-earned level the
        // target passes without arming, so the branch falls through and the replay stays an
        // honest best-score chase against the full timer.
        if (ArmedGoalMetNow() && TryBeginVerification())
        {
            UpdateTimerLabel();
            return;
        }

        _timeRemaining = Mathf.Max(0f, _timeRemaining - Time.deltaTime);
        UpdateTimerLabel();
        if (_timeRemaining > 0f) return;

        // Clock expiry with the goal genuinely met arms WITHOUT the debounce: a player who
        // crossed the line in the final fifth of a second must get the hold, not the timeout.
        if (ArmedGoalMetNow() && TryBeginVerification(immediate: true)) return;

        _hasTimeLimit = false;
        GameManager.Instance.EndRunNow("Time ran out", RunEndCause.Timeout);
    }

    private void BuildTimerUi()
    {
        if (_timerRoot != null) return;

        _timerRoot = RuntimeUiKit.CreateOverlayCanvas("Timed Goal", 3100);

        // A HudSubCard under the bar's RIGHT segment: the same card the NEXT WAVE countdown
        // and the medal marker use, so every corner tenant shares one width and one row grid.
        _timerRect = HudSubCard.Create(_timerRoot.transform, "Timer", HudSubCard.Side.Right);
        RuntimeUiKit.AddOutline(_timerRect, new Color(1f, 1f, 1f, 0.22f));

        RectTransform row = HudSubCard.CreateRow(_timerRect);
        HudSubCard.AddText(row, "Caption", "TIME", HudSubCard.CaptionFontSize, HudSubCard.CaptionColor,
            characterSpacing: 8f);
        _timerLabel = HudSubCard.AddText(row, "Value", "", HudSubCard.ValueFontSize, RuntimeUiKit.TitleColor);

        PositionTimerUi();
    }

    private void PositionTimerUi()
    {
        if (_timerRect == null) return;

        Canvas canvas = _timerRoot != null ? _timerRoot.GetComponent<Canvas>() : null;
        HudSubCard.Place(_timerRect, canvas, 0);
    }

    private void UpdateTimerLabel(bool force = false)
    {
        if (_timerLabel == null) return;

        PositionTimerUi();
        int seconds = Mathf.CeilToInt(Mathf.Max(0f, _timeRemaining));
        if (!force && seconds == _timerShownSecond) return;

        _timerShownSecond = seconds;
        _timerLabel.text = TimedWinCondition.FormatDuration(seconds);
        HudSubCard.MarkDirty(_timerLabel.transform.parent as RectTransform);
        _timerLabel.color = seconds <= 10 ? new Color(1f, 0.48f, 0.42f, 1f) : RuntimeUiKit.TitleColor;
    }

    private void DestroyTimerUi()
    {
        if (_timerRoot == null) return;

        Destroy(_timerRoot);
        _timerRoot = null;
        _timerRect = null;
        _timerLabel = null;
    }

    // Modifier assets are cloned per run so their instance fields are per-play state and never
    // leak between sessions (ScriptableObject instances outlive scene reloads in the editor).
    private void StartModifiers()
    {
        _activeModifiers.Clear();
        IReadOnlyList<LevelModifier> modifiers = _level != null ? _level.Modifiers : null;
        if (modifiers == null) return;

        for (int i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i] == null) continue;

            LevelModifier runtimeCopy = Instantiate(modifiers[i]);
            _activeModifiers.Add(runtimeCopy);
            // Isolated like EndModifiers: one modifier failing to start must not abort the
            // rest of them - or the remainder of the controller's own Start.
            try { runtimeCopy.OnLevelStart(_modifierContext); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
    }

    // Checked after StartModifiers so each clone has decided whether it actually runs.
    // The medal ladder's speed escalation (MEDALS.md addendum, Nick 2026-08-30): on levels
    // where the fall-speed ramp IS the difficulty (pure BLOCK COUNT - the authored caps land
    // right at bronze), the cap scales with the ARMED rung: x1.0 bronze, x1.15 silver,
    // x1.30 gold. Applied at run start (a silver chase ramps toward the silver cap from the
    // first brick) and on every rung advance; never after the ladder completes (Keep Playing
    // holds the last cap). Levels whose game type is claimed by a modifier (Void Zones,
    // Airtight, The Flood, Puzzle Waves) carry their own difficulty and are untouched.
    private const float TierSpeedCapStep = 0.15f;

    private void ApplyTierSpeedCap()
    {
        if (_armedTier == null || GameManager.Instance == null) return;
        if (_winCondition == null || !_winCondition.SpeedCapChasesTiers) return;
        for (int i = 0; i < _activeModifiers.Count; i++)
        {
            if (_activeModifiers[i] is ILevelMenuProgressProvider claim &&
                !string.IsNullOrEmpty(claim.MenuChallengeLabel)) return;
        }

        GameManager.Instance.SetSpeedCapScale(1f + TierSpeedCapStep * (int)_armedTier.Value);
    }

    private bool GoalBannerSuppressed()
    {
        for (int i = 0; i < _activeModifiers.Count; i++)
        {
            if (_activeModifiers[i] != null && _activeModifiers[i].SuppressesGoalBanner) return true;
        }
        return false;
    }

    // Symmetric with StartModifiers: let each modifier release its subscriptions/UI before the
    // scene unloads, then drop the clones (they are otherwise pinned alive by static-event handlers).
    // Isolated per modifier: one throwing teardown must not rob the rest of theirs - the tutorial's
    // teardown restores global input state, which must never be skippable.
    private void EndModifiers()
    {
        for (int i = 0; i < _activeModifiers.Count; i++)
        {
            if (_activeModifiers[i] == null) continue;
            try { _activeModifiers[i].OnLevelEnd(_modifierContext); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
        _activeModifiers.Clear();
    }

    private void HandleBlockPlaced(int totalBlocksPlaced)
    {
        // Cumulative physical placements - modifiers ramp per real piece, never score bonuses.
        for (int i = 0; i < _activeModifiers.Count; i++)
        {
            _activeModifiers[i].OnBlockLocked(_modifierContext, totalBlocksPlaced);
        }
        SampleXpProgress();
    }

    // ---- XP (XP.md) ---------------------------------------------------------------------

    // Peak, not final: goal progress can rewind (collapses, destroyed blocks), and the XP
    // award honors what the run reached, not what survived the last second.
    private void SampleXpProgress()
    {
        WinCondition condition = _winCondition ?? XpFallbackCondition;
        float progress = condition.RunProgressRaw(GameManager.Instance);
        if (progress > _xpPeakProgress) _xpPeakProgress = progress;
    }

    private float XpProgressForReport() => Mathf.Clamp(_xpPeakProgress, 0f, 2f);

    /// <summary>Award the run's XP once, on its FIRST reported outcome (win, game over, or
    /// pause-menu abandon - later outcomes for the same run are latched out). A win reports
    /// at verification with progress 1.0 (XP.md §1: the refund/score must not wait for a
    /// Keep Playing session that may never end); the OVERSHOOT earned after that is paid
    /// separately by <see cref="AwardOvershootXp"/>, mirroring the server's
    /// improve_run_score delta. Custom Game runs have no level identity and never earn.
    /// Online the server pays inside finish_run (the ReportFinish alongside this call
    /// carries the progress); the local grant only exists for online-layer-disabled play.
    /// Premium-offline runs are unranked and deliberately earn nothing - the next server
    /// verdict would visibly rewind a local grant (see XpSystem).</summary>
    private void AwardRunXp(bool won)
    {
        if (_xpAwarded || ProgressStore.LevelId(_level) == null) return;
        _xpAwarded = true;
        SampleXpProgress();
        _xpAwardedProgress = XpProgressForReport();
        if (!OnlineService.Enabled) XpSystem.ReportLocalRun(_xpAwardedProgress, won);
    }

    /// <summary>Pay the local XP earned AFTER the win was banked. Online this is the
    /// server's job (improve_run_score pays the same delta against runs.paid_progress);
    /// this exists so an online-layer-disabled build does not quietly pay less for the
    /// identical player action.</summary>
    private void AwardOvershootXp()
    {
        if (!_xpAwarded || ProgressStore.LevelId(_level) == null) return;
        SampleXpProgress();
        float now = XpProgressForReport();
        if (now <= _xpAwardedProgress) return;
        if (!OnlineService.Enabled) XpSystem.ReportLocalOvershoot(_xpAwardedProgress, now, won: true);
        _xpAwardedProgress = now;
    }

    // PlaceBlocks/ClearWaves win on the LIVE standing count, not cumulative score - so destroying
    // or dropping placed blocks genuinely sets the goal back. Re-arms for free: this event
    // fires on every increment too, so re-crossing the target re-triggers verification.
    // Both progress signals (live block count, tower height) funnel through the condition: it
    // decides whether its goal is met now. A signal the goal doesn't care about is a cheap no-op.
    // Modifiers hear the signal FIRST: the wave engine must advance before the ClearWaves
    // condition reads its thresholds, or an arming check could run against a stale wave.
    private void HandleStandingBlocksChanged(int placedBlocks)
    {
        // Isolated per modifier (like EndModifiers): this fires deep inside BlockLedger's
        // landing/destroy chain - one throwing modifier must not desync the cumulative
        // placement bookkeeping behind it or rob the other modifiers of the signal.
        for (int i = 0; i < _activeModifiers.Count; i++)
        {
            try { _activeModifiers[i].OnStandingBlocksChanged(_modifierContext, placedBlocks); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
        SampleXpProgress();
        TryArmFromProgress();
    }
    private void HandleHeightChanged(float height)
    {
        SampleXpProgress();
        TryArmFromProgress();
    }

    // Two "met" reads with different owners: the BRONZE condition feeds _targetMetThisRun (the
    // authored goal - what "made the level" means), the ARMED condition feeds the ladder.
    private bool BronzeGoalMetNow() => _winCondition != null && _winCondition.IsMet(BuildWinContext());
    private bool ArmedGoalMetNow() => _armedCondition != null && _armedCondition.IsMet(BuildWinContext());

    private void TryArmFromProgress()
    {
        if (!_targetMetThisRun && BronzeGoalMetNow()) _targetMetThisRun = true;
        if (ArmedGoalMetNow()) TryBeginVerification();
    }

    private void RebuildArmedCondition()
    {
        _armedCondition = _armedTier.HasValue && _level != null
            ? _level.WinConditionFor(LevelTiers.Threshold(_level, _armedTier.Value))
            : null;
    }

    /// <summary>A tier's hold-steady just survived its full window. Each hold banks the armed
    /// rung's THRESHOLD, nothing above it: IsStillHeld enforced only that tier's threshold
    /// through the window, so a higher threshold the tower happened to cross at expiry was
    /// never held and must not bank - a tower already above the next rung's goal simply
    /// re-arms and holds again. (The one exception: a rung whose CLAMPED-EQUAL threshold ties
    /// the armed one is earned by the same value - see the highestNow upgrade below.)
    /// The run's FIRST earned rung (any tier) adjudicates it as a win; bronze additionally
    /// runs the completion side effects; the top rung shows the victory card, lower rungs
    /// debut in-run (MedalHud) and play straight on.</summary>
    private void OnHoldSteadyComplete()
    {
        if (_armedTier == null || _level == null) return;
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        MedalTier earnedTier = _armedTier.Value;
        bool bronzeWasEarned = LevelTiers.IsEarned(_level, MedalTier.Bronze, _sessionVerifiedValue);

        _sessionVerifiedValue = Mathf.Max(_sessionVerifiedValue, LevelTiers.Threshold(_level, earnedTier));
        ProgressStore.ReportVerified(_level, _sessionVerifiedValue); // no-op for Custom Game (no identity)

        bool ladderDone = LevelTiers.LowestUnearned(_level, _sessionVerifiedValue) == null;

        // Degenerate clamped-equal thresholds can earn rungs ABOVE the armed one in the same
        // hold (they share its verified value). Announce the HIGHEST newly earned rung so every
        // surface agrees - a run that just golded must never show a SILVER pill (Nick's repro
        // 2026-08-29). Anything above the armed rung that reads earned now is new by
        // construction: had it been earned before, the armed rung would have been higher.
        MedalTier? highestNow = LevelTiers.HighestEarned(_level, _sessionVerifiedValue);
        if (highestNow.HasValue && highestNow.Value > earnedTier) earnedTier = highestNow.Value;
        _highestTierEarnedThisRun = earnedTier;

        // Card content BEFORE the stores update: the run snapshot (score, coins) must be the
        // pre-adjudication view. The record comparison reads _preRunBest either way.
        RunResult result = GameManager.Instance.CurrentRunResult;
        if (ladderDone)
        {
            _victoryContent = BuildVictoryContent(result,
                bronzeCompletesThisRun: !bronzeWasEarned || _bronzeCompletedThisRun);
        }

        // The run's first earned rung adjudicates it as a WIN: bests, XP and the server finish
        // in one exchange, against the run_id granted at start (BACKEND.md §6.2 - finish_run
        // refunds the attempt PER-RUN, so a replay that newly silvers/golds is exactly as free
        // as a first completion; SHOP.md §7 wins are free). Later rungs the same run are score
        // improvements, reported at run end (HandleGameOver / ReportAbandonedRun).
        if (!_finishReportedThisRun)
        {
            int reportedScore = ReportedScore(result.Score);
            ProgressStore.ReportResult(_level, reportedScore, result.MaxHeight, RunSuppliesState.ActiveRunBoosted);
            AwardRunXp(won: true);
            RunGate.ReportFinish(won: true, reportedScore, result.MaxHeight, XpProgressForReport());
            _finishReportedThisRun = true;
        }

        if (!bronzeWasEarned)
        {
            // Bronze IS completion - everything the old single-target win did, minus the
            // full-screen card (lower rungs celebrate in-run; only the top rung owns the card).
            // Boosted completions still complete (SHOP.md §6) - only the best goes to the
            // boosted board instead of the clean one.
            bool firstCompletion = !ProgressStore.IsLevelCompleted(_level);
            ProgressStore.MarkLevelCompleted(_level);
            // A FIRST completion may unlock the next level or chapter; the menu plays that
            // unlock as a reveal animation instead of showing it silently pre-unlocked.
            if (firstCompletion) UnlockRevealPending.RecordFirstCompletion(_level);
            _bronzeCompletedThisRun = true;
            GameEvents.RaiseLevelCompleted(_level, result);
        }

        // Announce the rung - the HUD's target label rolls to the next threshold on this.
        GameEvents.RaiseTierEarned(_level, earnedTier);

        if (ladderDone)
        {
            _armedTier = null;
            _armedCondition = null;
            _hasTimeLimit = false; // the ladder is done; Keep Playing is untimed, as post-win always was
            DestroyTimerUi();
            GameManager.Instance.RequestPhase(this, GamePhase.Completed);

            if (GameManager.Instance.IsGamePaused)
            {
                _completionPendingWhilePaused = true;
                return;
            }

            ShowCompletionPanel();
            return;
        }

        // A lower rung: celebrate in-run and play straight on toward the next one - the same
        // phase release the abort path uses, so spawning resumes on its own.
        GameManager.Instance.ReleasePhase(this);
        _armedTier = LevelTiers.LowestUnearned(_level, _sessionVerifiedValue);
        RebuildArmedCondition();
        ApplyTierSpeedCap(); // raise the ceiling for the new rung; the ramp climbs into it
        // The celebration itself is MedalHud's debut fly-in (big pill center-screen settling
        // into its slot) - a text toast on top would double-tell it (Nick 2026-08-29).
        SfxPlayer.Play("ui-star-earned");
    }

    // The medal ladder as the results card shows it: thresholds + earned state AFTER this
    // run's writes, and the highest tier NEWLY earned this run (null = nothing new).
    private void PopulateTierContent(ref RunResultsScreen.Content content)
    {
        if (!LevelTiers.HasTiers(_level)) return;
        content.TierEarnedThisRun = _highestTierEarnedThisRun;
        content.TierThresholds = new float[LevelTiers.TierCount];
        content.TierEarnedState = new bool[LevelTiers.TierCount];
        for (int i = 0; i < LevelTiers.TierCount; i++)
        {
            content.TierThresholds[i] = LevelTiers.Threshold(_level, (MedalTier)i);
            content.TierEarnedState[i] = LevelTiers.IsEarned(_level, (MedalTier)i, _sessionVerifiedValue);
        }
    }

    private RunResultsScreen.Content BuildVictoryContent(RunResult result, bool bronzeCompletesThisRun)
    {
        RunResultsScreen.Content content = new RunResultsScreen.Content
        {
            Victory = true,
            Metric = ResolveEndOfRunMetric(result),
            // The banked total: the run's skill coins plus the once-per-run win bonus - but
            // only when bronze completes THIS run (CoinLedger banks the bonus on the
            // LevelCompleted this run raises); a replay that golds a long-completed level
            // never re-banks, so advertising the bonus would promise a payout that never lands.
            Coins = result.CoinsEarned + (bronzeCompletesThisRun ? CoinLedger.WinBonusCoins : 0),
            Boosted = RunSuppliesState.ActiveRunBoosted,
            PrimaryLabel = "Keep Playing",
            VictorySentence = "Your tower still stands - keep stacking to push your best score even higher.",
            OnPrimary = ContinuePlaying,
        };
        // PopulateTierContent already carries the top rung: this card only builds when the
        // ladder is done, and the clamped-equal upgrade in OnHoldSteadyComplete lifts
        // _highestTierEarnedThisRun to the highest rung before content is built.
        PopulateTierContent(ref content);
        return content;
    }

    private void ShowCompletionPanel()
    {
        // Once per run: a second entry would double-PushPause with only one PopPause in
        // ContinuePlaying, leaving the game frozen after "Keep Playing".
        if (_victoryShown || GameManager.Instance == null) return;

        _victoryShown = true;
        GameManager.Instance.PushPause(this);
        RunResultsScreen.Show(_victoryContent);
    }

    private void ContinuePlaying()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PopPause(this);
            GameManager.Instance.ReleasePhase(this);
        }
    }
}
