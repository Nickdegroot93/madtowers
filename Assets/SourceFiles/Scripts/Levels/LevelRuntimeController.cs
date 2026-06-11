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
