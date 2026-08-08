using System.Collections.Generic;
using UnityEngine;
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
    private WinCondition _winCondition;        // the level's victory rule (polymorphic); cached for the run
    private System.Func<float> _liveHeightFunc; // cached delegate so BuildWinContext never allocates
    private bool _completed;
    private bool _completionPendingWhilePaused;
    private bool _victoryShown;
    // Completed in a PREVIOUS run: the whole win flow (countdown, verification, completion
    // screen) stays dormant - the target passes silently and the run continues for a best.
    private bool _alreadyCompleted;
    // The goal was met at least once THIS run (even if the tower later fell, and even on an
    // already-completed level where nothing arms). Drives the game-over card's "level made" line.
    private bool _targetMetThisRun;
    private RunResultsScreen.Content _victoryContent; // built at CompleteLevel; shown possibly later (paused)
    private float _verificationRemaining;
    private GameObject _countdownRoot;
    private Text _countdownLabel;
    private Text _countdownDigit;
    private int _countdownShownSecond = -1;
    private float _countdownDigitPunchAge;
    private bool _hasTimeLimit;
    private float _timeRemaining;
    private GameObject _timerRoot;
    private RectTransform _timerRect;
    private Text _timerLabel;
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

    private void Start()
    {
        _level = LevelSelectionState.SelectedLevel;
        _winCondition = _level != null ? _level.WinCondition : null;
        _alreadyCompleted = _level != null && ProgressStore.IsLevelCompleted(_level);
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

        // A modifier that owns the intro messaging (the first-run tutorial) suppresses the goal
        // banner - it would talk over the lessons; the tutorial shows the goal itself instead.
        if (_level != null && !string.IsNullOrWhiteSpace(_level.Instruction) && !GoalBannerSuppressed())
        {
            ShowBanner(_level.Instruction);
        }
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
    private System.Collections.IEnumerator ShowInstructionBanner(string text)
    {
        GameObject root = RuntimeUiKit.CreateOverlayCanvas("Level Instruction", 3000);
        _bannerRoot = root;

        GameObject strip = new GameObject("Strip");
        strip.transform.SetParent(root.transform, false);
        RectTransform stripRect = strip.AddComponent<RectTransform>();
        stripRect.anchorMin = new Vector2(0f, 0.74f);
        stripRect.anchorMax = new Vector2(1f, 0.74f);
        stripRect.pivot = new Vector2(0.5f, 0.5f);
        stripRect.sizeDelta = new Vector2(0f, 150f);
        Image background = strip.AddComponent<Image>();
        background.color = new Color(0.03f, 0.05f, 0.07f, 0.62f);
        background.raycastTarget = false;

        Text label = RuntimeUiKit.CreateLabel(strip.transform, text, 38, 150f,
            FontStyle.Bold, RuntimeUiKit.TitleColor);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(40f, 0f);
        labelRect.offsetMax = new Vector2(-40f, 0f);

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
        }
    }

    // Personal bests are recorded at every end-of-run (monotonic - only improvements stick).
    private void HandleGameOver(int finalScore, float maxHeightMeters)
    {
        // A run can die mid-verification (the dropped blocks took the last life).
        if (_countdownRoot != null) DestroyCountdownUi();
        DestroyTimerUi();

        // Screen BEFORE ReportResult: the card compares this run against the best as it
        // stood before the run banked - otherwise a new best could never be detected.
        ShowGameOverScreen();
        int reportedScore = ReportedScore(finalScore);
        if (_level != null) ProgressStore.ReportResult(_level, reportedScore, maxHeightMeters, RunSuppliesState.ActiveRunBoosted);
        AwardRunXp(won: false);
        // Server finish (BACKEND.md §6.2): score submission and the XP award ride the same
        // exchange. Local bests above stay local-first regardless.
        if (_completed)
        {
            // Toppling out of a post-win Keep Playing session. The win already banked the
            // refund and its XP, but everything stacked SINCE is what the player was
            // invited to chase - it has to reach the board, or every winner ends up tied
            // at the target score and the leaderboard says nothing.
            AwardOvershootXp();
            RunGate.ReportScoreImprovement(ProgressStore.LevelId(_level), reportedScore,
                maxHeightMeters, XpProgressForReport());
        }
        else
        {
            RunGate.ReportFinish(won: false, reportedScore, maxHeightMeters, XpProgressForReport());
        }
    }

    /// <summary>Pause-menu quit or restart: the run ends without a game over, but it still
    /// HAPPENED - bank the bests and report the finish so the abandon pays its participation
    /// + progress XP (Nick 2026-08-01) exactly like a loss at the same point would.
    /// Quitting out of a post-win Keep Playing session reports a score IMPROVEMENT instead
    /// of a finish (that run was already adjudicated at the win): do not re-add a
    /// `_completed` early return here, or the whole Keep Playing score is dropped again.
    /// Double-reporting is prevented by construction - ProgressStore.ReportResult is
    /// monotonic, AwardRunXp is latched, and the improvement window is one-shot per run.</summary>
    public void ReportAbandonedRun()
    {
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;
        RunResult result = GameManager.Instance.CurrentRunResult;
        int reportedScore = ReportedScore(result.Score);

        if (_level != null) ProgressStore.ReportResult(_level, reportedScore, result.MaxHeight, RunSuppliesState.ActiveRunBoosted);
        AwardRunXp(won: false);   // latched by _xpAwarded: a no-op once the win awarded
        if (_completed)
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
            RunGate.ReportFinish(won: false, reportedScore, result.MaxHeight, XpProgressForReport());
        }
    }

    private void ShowGameOverScreen()
    {
        RunResult result = GameManager.Instance != null ? GameManager.Instance.CurrentRunResult : default;
        bool endless = _winCondition == null || !_winCondition.HasGoal;
        RunResultsScreen.Show(new RunResultsScreen.Content
        {
            Victory = false,
            Metric = ResolveEndOfRunMetric(result),
            // "You made the level": only when it is genuinely complete AND this run actually
            // reached the goal - a replay that never got close doesn't get the line.
            GoalReached = _targetMetThisRun && (_completed || _alreadyCompleted),
            EndlessHeight = endless ? result.MaxHeight : 0f,
            // A run that already completed showed (and banked) its coins on the victory card,
            // and the ledger blocks further earning - re-advertising them here would read as
            // a second payout that never happens.
            Coins = _completed ? 0 : result.CoinsEarned,
            Boosted = RunSuppliesState.ActiveRunBoosted,
            PrimaryLabel = "Try Again",
            OnPrimary = () => { if (GameManager.Instance != null) GameManager.Instance.RestartGame(); },
        });
    }

    // The goal's own idea of "the score that matters" - a presentation-owning modifier wins
    // (waves, not raw blocks), otherwise the win condition's metric. Same precedence the menu
    // uses; the provider lookup is shared so the two can never disagree.
    private ResultMetric ResolveEndOfRunMetric(RunResult result)
    {
        ProgressStore.LevelBest best = BestForRunBoard();

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
    // fields, so a boosted run gets a view with the boosted pair mapped onto them.
    private ProgressStore.LevelBest BestForRunBoard()
    {
        ProgressStore.LevelBest best = _level != null ? ProgressStore.GetBest(_level) : null;
        if (best == null || !RunSuppliesState.ActiveRunBoosted) return best;
        return new ProgressStore.LevelBest
        {
            levelId = best.levelId,
            bestScore = best.bestScoreBoosted,
            bestHeightMeters = best.bestHeightMetersBoosted,
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
        if (_completed || _level == null) return;

        if (IsVerifyingWin)
        {
            // The goal must still hold through the countdown: a height collapse, or a block
            // destroyed/dropped below the live count, hands the level back. The condition owns
            // the rule (and any hysteresis slack); the controller stays goal-agnostic.
            if (_winCondition != null && !_winCondition.IsStillHeld(BuildWinContext()))
            {
                AbortVerification();
                return;
            }

            _verificationRemaining -= Time.deltaTime;
            UpdateCountdownLabel();
            if (_verificationRemaining <= 0f)
            {
                DestroyCountdownUi();
                CompleteLevel(); // requests the Completed phase
            }
            return;
        }

        // After a collapse aborted verification, a goal that arms from a MONOTONIC signal (the
        // height record only rises) can never re-fire for the same peak - re-arm from the live
        // tower instead. Polled at 5 Hz, not per frame: LiveTowerHeight walks every landed block's
        // cells, and this watch can stay on for minutes while the player rebuilds a tall tower.
        if (_targetMetThisRun && !_alreadyCompleted && _winCondition != null && _winCondition.ReArmsByPolling)
        {
            _rearmPollTimer -= Time.deltaTime;
            if (_rearmPollTimer > 0f) return;
            _rearmPollTimer = RearmPollInterval;

            if (_winCondition.IsMet(BuildWinContext())) TryBeginVerification();
        }
    }

    private const float RearmPollInterval = 0.2f;
    private float _rearmPollTimer;

    // Returns whether verification actually armed. Every arming precondition lives HERE, so
    // callers must branch on the outcome, never re-derive the conditions - a timed goal that
    // assumes arming succeeded would freeze its clock forever (the bug this shape prevents).
    private bool TryBeginVerification()
    {
        if (_completed || IsVerifyingWin) return false;
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return false;

        _targetMetThisRun = true;
        // A level beaten in an earlier run never re-enters the win flow: no countdown, no
        // completion screen - the target passes silently and the player plays on for a best.
        if (_alreadyCompleted) return false;

        GameManager.Instance.RequestPhase(this, GamePhase.WinVerifying);
        _verificationRemaining = WinVerificationSeconds;
        BuildCountdownUi();
        UpdateCountdownLabel();
        return true;
    }

    private void AbortVerification()
    {
        if (GameManager.Instance != null) GameManager.Instance.ReleasePhase(this); // drops WinVerifying -> back to Playing
        DestroyCountdownUi();
        ShowBanner("The tower fell - keep building!");
    }

    // The live snapshot the win condition reads. LiveTowerHeight is passed as a cached delegate so
    // a condition that doesn't need height (PlaceBlocks) never triggers the per-block walk.
    private WinContext BuildWinContext() => new WinContext(GameManager.Instance, _liveHeightFunc);

    // The win target compares against the same cell-center height the goal system uses,
    // but over the blocks actually standing right now instead of the monotonic record.
    private float LiveTowerHeight()
    {
        float floorY = GameManager.Instance != null ? GameManager.Instance.floorOriginY : 0f;
        float highest = floorY;

        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block == null || !block.HasLanded) continue;
            highest = Mathf.Max(highest, block.GetHighestCellY());
        }

        return Mathf.Max(0f, highest - floorY);
    }

    private void BuildCountdownUi()
    {
        if (_countdownRoot != null) return;

        SfxPlayer.PlayLoop("countdown", 0.8f); // clock runs for the 5->0 hold; stopped in DestroyCountdownUi
        _countdownRoot = RuntimeUiKit.CreateOverlayCanvas("Win Verification", 3200);

        GameObject strip = new GameObject("Strip");
        strip.transform.SetParent(_countdownRoot.transform, false);
        RectTransform stripRect = strip.AddComponent<RectTransform>();
        stripRect.anchorMin = new Vector2(0f, 0.74f);
        stripRect.anchorMax = new Vector2(1f, 0.74f);
        stripRect.pivot = new Vector2(0.5f, 0.5f);
        stripRect.sizeDelta = new Vector2(0f, 150f);
        Image background = strip.AddComponent<Image>();
        background.color = new Color(0.03f, 0.05f, 0.07f, 0.62f);
        background.raycastTarget = false;

        _countdownLabel = RuntimeUiKit.CreateLabel(strip.transform, "Hold steady!", 38, 150f,
            FontStyle.Bold, RuntimeUiKit.TitleColor);
        _countdownLabel.raycastTarget = false;

        // The countdown itself: one huge digit below the strip that punches in on every
        // second (5 -> 4 -> 3...), so the wait reads as a countdown, not a frozen banner.
        GameObject digit = new GameObject("Digit");
        digit.transform.SetParent(_countdownRoot.transform, false);
        RectTransform digitRect = digit.AddComponent<RectTransform>();
        digitRect.anchorMin = new Vector2(0.5f, 0.6f);
        digitRect.anchorMax = new Vector2(0.5f, 0.6f);
        digitRect.pivot = new Vector2(0.5f, 0.5f);
        digitRect.sizeDelta = new Vector2(300f, 170f);
        _countdownDigit = digit.AddComponent<Text>();
        _countdownDigit.font = RuntimeUiKit.DefaultFont;
        _countdownDigit.fontSize = 140;
        _countdownDigit.fontStyle = FontStyle.Bold;
        _countdownDigit.alignment = TextAnchor.MiddleCenter;
        _countdownDigit.horizontalOverflow = HorizontalWrapMode.Overflow;
        _countdownDigit.verticalOverflow = VerticalWrapMode.Overflow;
        _countdownDigit.color = RuntimeUiKit.TitleColor;
        _countdownDigit.raycastTarget = false;

        _countdownShownSecond = -1; // force the first digit to set + punch immediately
    }

    private const float DigitPunchSeconds = 0.3f;
    private const float DigitPunchStartScale = 1.7f;

    private void UpdateCountdownLabel()
    {
        if (_countdownDigit == null) return;

        int seconds = Mathf.CeilToInt(Mathf.Max(0f, _verificationRemaining));
        if (seconds != _countdownShownSecond)
        {
            _countdownShownSecond = seconds;
            _countdownDigit.text = seconds.ToString();
            _countdownDigitPunchAge = 0f;
        }

        // Scale-punch: lands big and settles to rest size over the punch window.
        _countdownDigitPunchAge += Time.deltaTime;
        float t = Mathf.Clamp01(_countdownDigitPunchAge / DigitPunchSeconds);
        float eased = 1f - (1f - t) * (1f - t); // ease-out
        float scale = Mathf.Lerp(DigitPunchStartScale, 1f, eased);
        _countdownDigit.rectTransform.localScale = new Vector3(scale, scale, 1f);

        Color color = RuntimeUiKit.TitleColor;
        color.a = Mathf.Lerp(0.55f, 1f, eased);
        _countdownDigit.color = color;
    }

    private void DestroyCountdownUi()
    {
        if (_countdownRoot == null) return;
        SfxPlayer.StopLoop(); // ends the countdown clock on win, abort, or teardown
        Destroy(_countdownRoot);
        _countdownRoot = null;
        _countdownLabel = null;
        _countdownDigit = null;
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
        if (!_hasTimeLimit || _completed || _level == null || GameManager.Instance == null) return;

        // The main clock only burns during active play. The 5-second win verification explicitly
        // freezes it; if verification aborts, the same remaining time resumes.
        if (GameManager.Instance.CurrentPhase != GamePhase.Playing || IsVerifyingWin)
        {
            UpdateTimerLabel();
            return;
        }

        // Freeze the clock only when verification ACTUALLY armed. On an already-completed
        // level the target passes without arming, so the branch falls through and the replay
        // stays an honest best-score chase against the full timer.
        if (GoalMetNow() && TryBeginVerification())
        {
            UpdateTimerLabel();
            return;
        }

        _timeRemaining = Mathf.Max(0f, _timeRemaining - Time.deltaTime);
        UpdateTimerLabel();
        if (_timeRemaining > 0f) return;

        if (GoalMetNow() && TryBeginVerification()) return;

        _hasTimeLimit = false;
        GameManager.Instance.EndRunNow("Time ran out");
    }

    private void BuildTimerUi()
    {
        if (_timerRoot != null) return;

        _timerRoot = RuntimeUiKit.CreateOverlayCanvas("Timed Goal", 3100);

        GameObject panel = new GameObject("Timer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _timerRect = (RectTransform)panel.transform;
        _timerRect.SetParent(_timerRoot.transform, false);
        _timerRect.anchorMin = _timerRect.anchorMax = new Vector2(1f, 1f);
        _timerRect.pivot = new Vector2(1f, 1f);
        _timerRect.sizeDelta = new Vector2(210f, 66f);

        Image background = panel.GetComponent<Image>();
        background.sprite = RuntimeSprites.RoundedPanel();
        background.type = Image.Type.Sliced;
        background.color = new Color(0f, 0f, 0f, 0.68f);
        background.raycastTarget = false;
        RuntimeUiKit.AddOutline(_timerRect, new Color(1f, 1f, 1f, 0.22f));

        _timerLabel = RuntimeUiKit.CreateLabel(panel.transform, "", 38, 66f, FontStyle.Bold,
            RuntimeUiKit.TitleColor);
        _timerLabel.raycastTarget = false;
        _timerLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        RectTransform labelRect = _timerLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        PositionTimerUi();
    }

    private void PositionTimerUi()
    {
        if (_timerRect == null) return;

        Canvas canvas = _timerRoot != null ? _timerRoot.GetComponent<Canvas>() : null;
        float topInset = RuntimeUiKit.SafeAreaTopInset(canvas);
        float rightInset = RuntimeUiKit.SafeAreaRightInset(canvas);
        _timerRect.anchoredPosition = new Vector2(-rightInset - 120f, -topInset - 180f);
    }

    private void UpdateTimerLabel(bool force = false)
    {
        if (_timerLabel == null) return;

        PositionTimerUi();
        int seconds = Mathf.CeilToInt(Mathf.Max(0f, _timeRemaining));
        if (!force && seconds == _timerShownSecond) return;

        _timerShownSecond = seconds;
        _timerLabel.text = TimedWinCondition.FormatDuration(seconds);
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

    private bool GoalMetNow() => _winCondition != null && _winCondition.IsMet(BuildWinContext());

    private void TryArmFromProgress()
    {
        if (GoalMetNow()) TryBeginVerification();
    }

    private void CompleteLevel()
    {
        if (_completed || GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        _completed = true;
        DestroyTimerUi();
        GameManager.Instance.RequestPhase(this, GamePhase.Completed);

        // Card content BEFORE the stores update: the record comparison needs the best as it
        // stood when the run started, and ReportResult mutates that very LevelBest instance.
        RunResult result = GameManager.Instance.CurrentRunResult;
        _victoryContent = BuildVictoryContent(result);

        // Boosted completions still complete (SHOP.md §6) - only the best goes to the
        // boosted board instead of the clean one.
        bool firstCompletion = !ProgressStore.IsLevelCompleted(_level);
        ProgressStore.MarkLevelCompleted(_level);
        // A FIRST completion may unlock the next level or chapter; the menu plays that
        // unlock as a reveal animation instead of showing it silently pre-unlocked.
        if (firstCompletion) UnlockRevealPending.RecordFirstCompletion(_level);
        int reportedScore = ReportedScore(result.Score);
        ProgressStore.ReportResult(_level, reportedScore, result.MaxHeight, RunSuppliesState.ActiveRunBoosted);
        AwardRunXp(won: true);
        // Server finish (BACKEND.md §6.2): the win refunds the attempt and submits the
        // score + XP in one exchange, against the run_id granted at start.
        RunGate.ReportFinish(won: true, reportedScore, result.MaxHeight, XpProgressForReport());
        GameEvents.RaiseLevelCompleted(_level, result);

        if (GameManager.Instance.IsGamePaused)
        {
            _completionPendingWhilePaused = true;
            return;
        }

        ShowCompletionPanel();
    }

    private RunResultsScreen.Content BuildVictoryContent(RunResult result)
    {
        return new RunResultsScreen.Content
        {
            Victory = true,
            Metric = ResolveEndOfRunMetric(result),
            GoalReached = true,
            // The banked total: the run's skill coins plus the once-per-run win bonus
            // (CoinLedger adds the bonus when LevelCompleted is raised below).
            Coins = result.CoinsEarned + CoinLedger.WinBonusCoins,
            Boosted = RunSuppliesState.ActiveRunBoosted,
            PrimaryLabel = "Keep Playing",
            VictorySentence = "Your tower still stands - keep stacking to push your best score even higher.",
            OnPrimary = ContinuePlaying,
        };
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
