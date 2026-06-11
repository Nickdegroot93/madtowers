using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs the selected level's meta layer: drives its LevelModifiers, tracks the win target,
/// and shows the completion screen with next-level progression inside the level's theme.
/// Added to the GameManager's object at runtime.
/// </summary>
public class LevelRuntimeController : MonoBehaviour
{
    private readonly List<LevelModifier> _activeModifiers = new List<LevelModifier>();
    private LevelModifierContext _modifierContext;
    private LevelDefinition _level;
    private GameObject _panelRoot;
    private bool _completed;
    private bool _completionPendingWhilePaused;

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
    }

    // Personal bests are recorded at every end-of-run (monotonic - only improvements stick).
    private void HandleGameOver(int finalScore, float maxHeightMeters)
    {
        if (_level != null) ProgressStore.ReportResult(_level, finalScore, maxHeightMeters);
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

        for (int i = 0; i < _activeModifiers.Count; i++)
        {
            _activeModifiers[i].OnUpdate(_modifierContext, Time.deltaTime);
        }
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
            CompleteLevel();
        }
    }

    private void HandleHeightChanged(float height)
    {
        if (_level != null && _level.TargetType == LevelTargetType.ReachHeight &&
            height >= _level.TargetValue)
        {
            CompleteLevel();
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
