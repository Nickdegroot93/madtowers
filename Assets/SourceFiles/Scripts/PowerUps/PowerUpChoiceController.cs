using System.Collections.Generic;
using UnityEngine;
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
    // Milestones are RECORDED in the score event but PRESENTED from Update, only when
    // nothing more important is happening. This decouples the offer from event-subscriber
    // order: when the same block both hits a picker milestone and meets the win target,
    // the hold-steady verification always wins the race - the earned pick appears after
    // the countdown resolves (after "Keep Building" on success, or right after an abort).
    private bool _offerPending;

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
        if (_panelRoot != null || score <= 0 || score == _lastHandledScore) return;

        GameModeConfig config = GameManager.Instance.ActiveConfig;
        if (config == null || config.PowerUpChoiceEveryBlocks <= 0) return;
        if (score % config.PowerUpChoiceEveryBlocks != 0) return;

        IReadOnlyList<PowerUpDefinition> pool = config.PowerUpChoicePool;
        if (pool == null || pool.Count == 0) return;

        _lastHandledScore = score;
        _offerPending = true;
    }

    private void Update()
    {
        if (!_offerPending || _panelRoot != null) return;
        if (GameManager.Instance == null) return;

        if (GameManager.Instance.isGameOver)
        {
            _offerPending = false; // the run ended before the reward could be presented
            return;
        }

        // Wait out the win-verification countdown and any other full-screen pause
        // (level-complete panel, pause menu) - the offer keeps, it doesn't vanish.
        if (LevelRuntimeController.IsVerifyingWin || GameManager.Instance.IsGamePaused) return;

        GameModeConfig config = GameManager.Instance.ActiveConfig;
        IReadOnlyList<PowerUpDefinition> pool = config != null ? config.PowerUpChoicePool : null;
        if (pool == null || pool.Count == 0)
        {
            _offerPending = false;
            return;
        }

        _offerPending = false;
        RollChoices(pool);
        if (_rollBuffer.Count == 0) return;

        GameManager.Instance.SetGamePaused(true);
        RuntimeUiKit.EnsureEventSystem();
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

    // ---- Runtime UI ---------------------------------------------------------------------------

    private void BuildChoicePanel()
    {
        _panelRoot = RuntimeUiKit.CreateOverlayCanvas("PowerUp Choice", 6000);
        RuntimeUiKit.CreateBackdrop(_panelRoot.transform, new Color(0.02f, 0.04f, 0.05f, 0.82f));

        GameObject panel = RuntimeUiKit.CreateCenteredPanel(
            _panelRoot.transform, new Vector2(1000f, 760f), drawBackground: false);

        RuntimeUiKit.CreateLabel(panel.transform, "Choose a Power-Up", 56, 90f, FontStyle.Bold,
            RuntimeUiKit.TitleColor);

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
            CreateCard(cardRow.transform, _rollBuffer[i]);
        }
    }

    private void CreateCard(Transform parent, PowerUpDefinition definition)
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

        RuntimeUiKit.CreateLabel(cardObject.transform, definition.Rarity.ToString().ToUpperInvariant(),
            26, 36f, FontStyle.Bold, rarityColor, TextAnchor.UpperCenter);
        RuntimeUiKit.CreateLabel(cardObject.transform, definition.DisplayName,
            38, 110f, FontStyle.Bold, Color.white, TextAnchor.UpperCenter);
        RuntimeUiKit.CreateLabel(cardObject.transform, definition.Description,
            27, 300f, FontStyle.Normal, new Color(0.82f, 0.88f, 0.92f, 1f), TextAnchor.UpperCenter);

        PowerUpDefinition picked = definition;
        button.onClick.AddListener(() => Pick(picked));
    }
}
