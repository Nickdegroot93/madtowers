using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
            Spawner = FindAnyObjectByType<Spawner>()
        };

        StartModifiers();
    }

    private void OnEnable()
    {
        GameEvents.ScoreChanged += HandleScoreChanged;
        GameEvents.HeightChanged += HandleHeightChanged;
    }

    private void OnDisable()
    {
        GameEvents.ScoreChanged -= HandleScoreChanged;
        GameEvents.HeightChanged -= HandleHeightChanged;
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
        EnsureEventSystem();
        BuildCompletionPanel();
    }

    private LevelDefinition FindNextLevelInTheme()
    {
        if (_level == null) return null;

        ThemeDefinition[] themes = Resources.LoadAll<ThemeDefinition>("Themes");
        for (int i = 0; i < themes.Length; i++)
        {
            LevelDefinition next = themes[i].GetNextLevel(_level);
            if (next != null) return next;
        }

        return null;
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

    // ---- Runtime UI (same conventions as LevelSelectRuntimeMenu) -----------------------------

    private void BuildCompletionPanel()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _panelRoot = new GameObject("Level Complete");
        Canvas canvas = _panelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 6500;

        CanvasScaler scaler = _panelRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        _panelRoot.AddComponent<GraphicRaycaster>();

        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(_panelRoot.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(640f, 480f);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.075f, 0.105f, 0.125f, 0.97f);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(36, 36, 36, 36);
        layout.spacing = 22f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateLabel(panel.transform, font, "Level Complete!", 52, 82f, FontStyle.Bold,
            new Color(0.55f, 0.95f, 0.6f, 1f));

        LevelDefinition next = FindNextLevelInTheme();
        if (next != null)
        {
            CreateButton(panel.transform, font, $"Next: {next.DisplayName}", () => LoadLevel(next));
        }

        CreateButton(panel.transform, font, "Keep Building", ContinuePlaying);
        CreateButton(panel.transform, font, "Replay", () => LoadLevel(_level));
    }

    private static void CreateLabel(Transform parent, Font font, string text, int fontSize,
        float height, FontStyle style, Color color)
    {
        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(parent, false);

        Text label = labelObject.AddComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = color;

        LayoutElement layout = labelObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
    }

    private static void CreateButton(Transform parent, Font font, string text, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new GameObject(text);
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.13f, 0.19f, 0.22f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        LayoutElement buttonLayout = buttonObject.AddComponent<LayoutElement>();
        buttonLayout.preferredHeight = 88f;

        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 0f);
        textRect.offsetMax = new Vector2(-24f, 0f);

        Text label = textObject.AddComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = 32;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<StandaloneInputModule>();
    }
}
