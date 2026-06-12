using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Runs the selected level's meta layer: drives its LevelModifiers, tracks the win target,
/// and shows the completion screen with next-level progression inside the level's theme.
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
    // Abort needs a quarter-cell of slack below the target so a wobbling peak block can't
    // flicker the countdown off; re-arming requires the full target again (hysteresis).
    private const float VerificationAbortTolerance = 0.25f;

    /// <summary>True while the hold-steady countdown runs. The Spawner gates on this.</summary>
    public static bool IsVerifyingWin { get; private set; }

    private readonly List<LevelModifier> _activeModifiers = new List<LevelModifier>();
    private LevelModifierContext _modifierContext;
    private LevelDefinition _level;
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

    private void Start()
    {
        _level = LevelSelectionState.SelectedLevel;
        _modifierContext = new LevelModifierContext
        {
            GameManager = GameManager.Instance,
            Spawner = FindAnyObjectByType<Spawner>(),
            Level = _level
        };

        StartModifiers();

        if (_level != null && !string.IsNullOrWhiteSpace(_level.Instruction))
        {
            StartCoroutine(ShowInstructionBanner(_level.Instruction));
        }
    }

    // One-sentence goal banner in the upper third at level start: fade in, hold, fade out.
    // Unscaled time so it behaves the same if the level opens paused (power-up choice etc.).
    private System.Collections.IEnumerator ShowInstructionBanner(string text)
    {
        GameObject root = RuntimeUiKit.CreateOverlayCanvas("Level Instruction", 3000);

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
        GameEvents.ScoreChanged += HandleScoreChanged;
        GameEvents.HeightChanged += HandleHeightChanged;
        GameEvents.GameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        GameEvents.ScoreChanged -= HandleScoreChanged;
        GameEvents.HeightChanged -= HandleHeightChanged;
        GameEvents.GameOver -= HandleGameOver;
        IsVerifyingWin = false; // static: must not leak a stale gate into the next scene
    }

    // Personal bests are recorded at every end-of-run (monotonic - only improvements stick).
    private void HandleGameOver(int finalScore, float maxHeightMeters)
    {
        if (_level != null) ProgressStore.ReportResult(_level, finalScore, maxHeightMeters);

        // A run can die mid-verification (the dropped blocks took the last life).
        if (IsVerifyingWin)
        {
            IsVerifyingWin = false;
            DestroyCountdownUi();
        }
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
            // A height win must still be STANDING; a collapse hands the level back.
            if (_level.TargetType == LevelTargetType.ReachHeight &&
                LiveTowerHeight() < _level.TargetValue - VerificationAbortTolerance)
            {
                AbortVerification();
                return;
            }

            _verificationRemaining -= Time.deltaTime;
            UpdateCountdownLabel();
            if (_verificationRemaining <= 0f)
            {
                IsVerifyingWin = false;
                DestroyCountdownUi();
                CompleteLevel();
            }
            return;
        }

        // After a collapse aborted a height verification, the monotonic HeightChanged event
        // can never re-fire for the same peak - re-arm from the live tower instead.
        if (_targetReachedOnce && _level.TargetType == LevelTargetType.ReachHeight &&
            LiveTowerHeight() >= _level.TargetValue)
        {
            TryBeginVerification();
        }
    }

    private void TryBeginVerification()
    {
        if (_completed || IsVerifyingWin) return;
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        _targetReachedOnce = true;
        IsVerifyingWin = true;
        _verificationRemaining = WinVerificationSeconds;
        BuildCountdownUi();
        UpdateCountdownLabel();
    }

    private void AbortVerification()
    {
        IsVerifyingWin = false;
        DestroyCountdownUi();
        StartCoroutine(ShowInstructionBanner("The tower fell - keep building!"));

        // The lock->spawn chain is event-driven and was suppressed while verifying;
        // nothing would ever spawn again without an explicit restart.
        _modifierContext?.Spawner?.ResumeSpawning();
    }

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
        Destroy(_countdownRoot);
        _countdownRoot = null;
        _countdownLabel = null;
        _countdownDigit = null;
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

    private void HandleScoreChanged(int score)
    {
        for (int i = 0; i < _activeModifiers.Count; i++)
        {
            _activeModifiers[i].OnBlockLocked(_modifierContext, score);
        }

        if (_level != null && _level.TargetType == LevelTargetType.PlaceBlocks &&
            score >= _level.TargetValue)
        {
            TryBeginVerification();
        }
    }

    private void HandleHeightChanged(float height)
    {
        if (_level != null && _level.TargetType == LevelTargetType.ReachHeight &&
            height >= _level.TargetValue)
        {
            TryBeginVerification();
        }
    }

    private void CompleteLevel()
    {
        if (_completed || GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        _completed = true;
        ProgressStore.MarkLevelCompleted(_level);
        if (GameManager.Instance != null)
        {
            ProgressStore.ReportResult(_level, GameManager.Instance.score, GameManager.Instance.towerHeight);
        }
        GameEvents.RaiseLevelCompleted(_level);

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

        GameManager.Instance.SetGamePaused(true);
        RuntimeUiKit.EnsureEventSystem();
        BuildCompletionPanel();
    }

    private LevelDefinition FindNextLevelInTheme()
    {
        ThemeDefinition theme = Campaign.FindThemeOf(_level);
        return theme != null ? theme.GetNextLevel(_level) : null;
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
            GameManager.Instance.SetGamePaused(false);
        }

        // The verification window suppressed the lock->spawn chain, so unlike the old
        // instant-complete flow there is no piece waiting - restart spawning explicitly.
        _modifierContext?.Spawner?.ResumeSpawning();
    }

    // ---- Runtime UI ---------------------------------------------------------------------------

    private void BuildCompletionPanel()
    {
        _panelRoot = RuntimeUiKit.CreateOverlayCanvas("Level Complete", 6500);
        GameObject panel = RuntimeUiKit.CreateCenteredPanel(_panelRoot.transform, new Vector2(640f, 480f));

        RuntimeUiKit.CreateLabel(panel.transform, "Level Complete!", 52, 82f, FontStyle.Bold,
            new Color(0.55f, 0.95f, 0.6f, 1f));

        LevelDefinition next = FindNextLevelInTheme();
        if (next != null)
        {
            RuntimeUiKit.CreateButton(panel.transform, $"Next: {next.DisplayName}", 88f, () => LoadLevel(next));
        }

        RuntimeUiKit.CreateButton(panel.transform, "Keep Building", 88f, ContinuePlaying);
        RuntimeUiKit.CreateButton(panel.transform, "Replay", 88f, () => LoadLevel(_level));
    }
}
