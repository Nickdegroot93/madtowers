using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Runs the selected level's meta layer: drives its LevelModifiers, tracks the win target,
/// and shows the completion screen with next-level progression inside the level's chapter.
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
    private GameObject _panelRoot;
    private bool _completed;
    private bool _completionPendingWhilePaused;
    private bool _targetReachedOnce;
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

    private void Start()
    {
        _level = LevelSelectionState.SelectedLevel;
        _winCondition = _level != null ? _level.WinCondition : null;
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

        if (_level != null && !string.IsNullOrWhiteSpace(_level.Instruction))
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
        GameEvents.BlockPlaced += HandleBlockPlaced;
        GameEvents.StandingBlocksChanged += HandleStandingBlocksChanged;
        GameEvents.HeightChanged += HandleHeightChanged;
        GameEvents.GameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        GameEvents.BlockPlaced -= HandleBlockPlaced;
        GameEvents.StandingBlocksChanged -= HandleStandingBlocksChanged;
        GameEvents.HeightChanged -= HandleHeightChanged;
        GameEvents.GameOver -= HandleGameOver;
        DestroyCountdownUi();   // also stops the countdown loop - SfxPlayer persists across scenes
        DestroyTimerUi();
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PopPause(this);
            GameManager.Instance.ReleasePhase(this);
        }
    }

    // Personal bests are recorded at every end-of-run (monotonic - only improvements stick).
    private void HandleGameOver(int finalScore, float maxHeightMeters)
    {
        if (_level != null) ProgressStore.ReportResult(_level, finalScore, maxHeightMeters);

        // A run can die mid-verification (the dropped blocks took the last life).
        if (_countdownRoot != null) DestroyCountdownUi();
        DestroyTimerUi();
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
        if (_targetReachedOnce && _winCondition != null && _winCondition.ReArmsByPolling)
        {
            _rearmPollTimer -= Time.deltaTime;
            if (_rearmPollTimer > 0f) return;
            _rearmPollTimer = RearmPollInterval;

            if (_winCondition.IsMet(BuildWinContext())) TryBeginVerification();
        }
    }

    private const float RearmPollInterval = 0.2f;
    private float _rearmPollTimer;

    private void TryBeginVerification()
    {
        if (_completed || IsVerifyingWin) return;
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        _targetReachedOnce = true;
        GameManager.Instance.RequestPhase(this, GamePhase.WinVerifying);
        _verificationRemaining = WinVerificationSeconds;
        BuildCountdownUi();
        UpdateCountdownLabel();
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

        if (_winCondition != null && _winCondition.IsMet(BuildWinContext()))
        {
            TryBeginVerification();
            UpdateTimerLabel();
            return;
        }

        _timeRemaining = Mathf.Max(0f, _timeRemaining - Time.deltaTime);
        UpdateTimerLabel();
        if (_timeRemaining > 0f) return;

        if (_winCondition != null && _winCondition.IsMet(BuildWinContext()))
        {
            TryBeginVerification();
            return;
        }

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
            runtimeCopy.OnLevelStart(_modifierContext);
        }
    }

    private void HandleBlockPlaced(int totalBlocksPlaced)
    {
        // Cumulative physical placements - modifiers ramp per real piece, never score bonuses.
        for (int i = 0; i < _activeModifiers.Count; i++)
        {
            _activeModifiers[i].OnBlockLocked(_modifierContext, totalBlocksPlaced);
        }
    }

    // PlaceBlocks wins on the LIVE standing count, not cumulative score - so destroying
    // or dropping placed blocks genuinely sets the goal back. Re-arms for free: this event
    // fires on every increment too, so re-crossing the target re-triggers verification.
    // Both progress signals (live block count, tower height) funnel through the condition: it
    // decides whether its goal is met now. A signal the goal doesn't care about is a cheap no-op.
    private void HandleStandingBlocksChanged(int placedBlocks) => TryArmFromProgress();
    private void HandleHeightChanged(float height) => TryArmFromProgress();

    private void TryArmFromProgress()
    {
        if (_winCondition != null && _winCondition.IsMet(BuildWinContext())) TryBeginVerification();
    }

    private void CompleteLevel()
    {
        if (_completed || GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        _completed = true;
        DestroyTimerUi();
        GameManager.Instance.RequestPhase(this, GamePhase.Completed);
        ProgressStore.MarkLevelCompleted(_level);
        if (GameManager.Instance != null)
        {
            RunResult result = GameManager.Instance.CurrentRunResult;
            ProgressStore.ReportResult(_level, result.Score, result.MaxHeight);
            GameEvents.RaiseLevelCompleted(_level, result);
        }

        if (GameManager.Instance.IsGamePaused)
        {
            _completionPendingWhilePaused = true;
            return;
        }

        ShowCompletionPanel();
    }

    private void ShowCompletionPanel()
    {
        if (_panelRoot != null || GameManager.Instance == null) return;

        GameManager.Instance.PushPause(this);
        RuntimeUiKit.EnsureEventSystem();
        BuildCompletionPanel();
    }

    private LevelDefinition FindNextLevelInChapter()
    {
        ChapterDefinition chapter = Campaign.FindChapterOf(_level);
        return chapter != null ? chapter.GetNextLevel(_level) : null;
    }

    private void LoadLevel(LevelDefinition level)
    {
        LevelSelectionState.SelectLevel(level);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void ContinuePlaying()
    {
        Destroy(_panelRoot);
        _panelRoot = null;
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PopPause(this);
            GameManager.Instance.ReleasePhase(this);
        }
    }

    // ---- Runtime UI ---------------------------------------------------------------------------

    private void BuildCompletionPanel()
    {
        _panelRoot = RuntimeUiKit.CreateOverlayCanvas("Level Complete", 6500);
        GameObject panel = RuntimeUiKit.CreateCenteredPanel(_panelRoot.transform, new Vector2(640f, 480f));

        RuntimeUiKit.CreateLabel(panel.transform, "Level Complete!", 52, 82f, FontStyle.Bold,
            new Color(0.55f, 0.95f, 0.6f, 1f));

        LevelDefinition next = FindNextLevelInChapter();
        if (next != null)
        {
            RuntimeUiKit.CreateButton(panel.transform, $"Next: {next.DisplayName}", 88f, () => LoadLevel(next));
        }

        RuntimeUiKit.CreateButton(panel.transform, "Keep Building", 88f, ContinuePlaying);
        RuntimeUiKit.CreateButton(panel.transform, "Replay", 88f, () => LoadLevel(_level));
    }
}
