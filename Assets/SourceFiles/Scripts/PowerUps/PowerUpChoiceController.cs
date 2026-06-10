using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Archero-style power-up choices: every N placed blocks (per GameModeConfig) the game fully
/// pauses and the player picks one of three rarity-weighted power-ups. Added to the
/// GameManager's object at runtime; the UI is built in code like LevelSelectRuntimeMenu.
/// </summary>
public class PowerUpChoiceController : MonoBehaviour
{
    private const int ChoiceCount = 3;

    private readonly List<PowerUpDefinition> _rollBuffer = new List<PowerUpDefinition>();
    private GameObject _panelRoot;
    private Spawner _spawner;
    private int _lastHandledScore;

    private void Awake()
    {
        _spawner = FindAnyObjectByType<Spawner>();
    }

    private void OnEnable()
    {
        GameEvents.ScoreChanged += HandleScoreChanged;
    }

    private void OnDisable()
    {
        GameEvents.ScoreChanged -= HandleScoreChanged;
        CloseChoicePanel();
    }

    private void HandleScoreChanged(int score)
    {
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;
        // Don't offer on top of another full-screen pause (e.g. the level-complete panel).
        if (GameManager.Instance.IsGamePaused) return;
        if (_panelRoot != null || score <= 0 || score == _lastHandledScore) return;

        GameModeConfig config = GameManager.Instance.ActiveConfig;
        if (config == null || config.PowerUpChoiceEveryBlocks <= 0) return;
        if (score % config.PowerUpChoiceEveryBlocks != 0) return;

        IReadOnlyList<PowerUpDefinition> pool = config.PowerUpChoicePool;
        if (pool == null || pool.Count == 0) return;

        _lastHandledScore = score;
        RollChoices(pool);
        if (_rollBuffer.Count == 0) return;

        GameManager.Instance.SetGamePaused(true);
        EnsureEventSystem();
        BuildChoicePanel();
    }

    // Weighted sample without replacement: each pick's probability follows its rarity weight,
    // and the same power-up never appears twice in one offer.
    private void RollChoices(IReadOnlyList<PowerUpDefinition> pool)
    {
        _rollBuffer.Clear();
        List<PowerUpDefinition> candidates = new List<PowerUpDefinition>(pool.Count);
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null) candidates.Add(pool[i]);
        }

        while (_rollBuffer.Count < ChoiceCount && candidates.Count > 0)
        {
            int totalWeight = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                totalWeight += PowerUpRarityInfo.GetRollWeight(candidates[i].Rarity);
            }

            int roll = Random.Range(0, totalWeight);
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= PowerUpRarityInfo.GetRollWeight(candidates[i].Rarity);
                if (roll < 0)
                {
                    _rollBuffer.Add(candidates[i]);
                    candidates.RemoveAt(i);
                    break;
                }
            }
        }
    }

    private void Pick(PowerUpDefinition definition)
    {
        CloseChoicePanel();

        PowerUpContext context = new PowerUpContext
        {
            GameManager = GameManager.Instance,
            Spawner = _spawner != null ? _spawner : FindAnyObjectByType<Spawner>()
        };
        definition.Apply(context);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetGamePaused(false);
        }
    }

    private void CloseChoicePanel()
    {
        if (_panelRoot == null) return;

        Destroy(_panelRoot);
        _panelRoot = null;
    }

    // ---- Runtime UI (same conventions as LevelSelectRuntimeMenu) -----------------------------

    private void BuildChoicePanel()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _panelRoot = new GameObject("PowerUp Choice");
        Canvas canvas = _panelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 6000;

        CanvasScaler scaler = _panelRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        _panelRoot.AddComponent<GraphicRaycaster>();

        GameObject backdropObject = new GameObject("Backdrop");
        backdropObject.transform.SetParent(_panelRoot.transform, false);
        RectTransform backdropRect = backdropObject.AddComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;
        Image backdrop = backdropObject.AddComponent<Image>();
        backdrop.color = new Color(0.02f, 0.04f, 0.05f, 0.82f);

        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(_panelRoot.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(1000f, 760f);

        VerticalLayoutGroup panelLayout = panel.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(24, 24, 24, 24);
        panelLayout.spacing = 26f;
        panelLayout.childAlignment = TextAnchor.MiddleCenter;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = false;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        CreateLabel(panel.transform, font, "Choose a Power-Up", 56, 90f, FontStyle.Bold,
            new Color(0.92f, 0.97f, 1f, 1f));

        GameObject cardRow = new GameObject("Cards");
        cardRow.transform.SetParent(panel.transform, false);
        LayoutElement rowElement = cardRow.AddComponent<LayoutElement>();
        rowElement.preferredHeight = 560f;

        HorizontalLayoutGroup rowLayout = cardRow.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 24f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;

        for (int i = 0; i < _rollBuffer.Count; i++)
        {
            CreateCard(cardRow.transform, font, _rollBuffer[i]);
        }
    }

    private void CreateCard(Transform parent, Font font, PowerUpDefinition definition)
    {
        Color rarityColor = PowerUpRarityInfo.GetColor(definition.Rarity);

        GameObject cardObject = new GameObject(definition.DisplayName);
        cardObject.transform.SetParent(parent, false);

        Image frame = cardObject.AddComponent<Image>();
        frame.color = Color.Lerp(new Color(0.09f, 0.12f, 0.14f, 1f), rarityColor, 0.18f);

        Button button = cardObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.targetGraphic = frame;

        VerticalLayoutGroup cardLayout = cardObject.AddComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(20, 20, 26, 26);
        cardLayout.spacing = 16f;
        cardLayout.childAlignment = TextAnchor.UpperCenter;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = false;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;

        CreateLabel(cardObject.transform, font, definition.Rarity.ToString().ToUpperInvariant(),
            26, 36f, FontStyle.Bold, rarityColor);
        CreateLabel(cardObject.transform, font, definition.DisplayName,
            38, 110f, FontStyle.Bold, Color.white);
        CreateLabel(cardObject.transform, font, definition.Description,
            27, 300f, FontStyle.Normal, new Color(0.82f, 0.88f, 0.92f, 1f));

        PowerUpDefinition picked = definition;
        button.onClick.AddListener(() => Pick(picked));
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
        label.alignment = TextAnchor.UpperCenter;
        label.color = color;

        LayoutElement layout = labelObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<StandaloneInputModule>();
    }
}
