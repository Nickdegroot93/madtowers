using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("HUD Elements")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI heightText;
    [SerializeField] private TextMeshProUGUI livesText;
    [SerializeField] private TextMeshProUGUI nextBlockText;

    [Header("Game Over UI")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI finalScoreText;

    [Header("HUD Style")]
    [SerializeField] private bool restyleHud = true;
    [SerializeField] private Color hudCardColor = new Color(0.035f, 0.055f, 0.075f, 0.74f);
    [SerializeField] private Color hudTextColor = new Color(0.95f, 0.98f, 1f, 1f);
    [SerializeField] private Color hudLabelColor = new Color(0.66f, 0.77f, 0.88f, 1f);

    private Spawner _spawner;
    private string _labelColorTag;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        ConfigureHudStyle();
        _spawner = Object.FindAnyObjectByType<Spawner>();
        
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    private void OnEnable()
    {
        GameEvents.ScoreChanged += HandleScoreChanged;
        GameEvents.HeightChanged += HandleHeightChanged;
        GameEvents.LivesChanged += HandleLivesChanged;
        GameEvents.NextBlockChanged += HandleNextBlockChanged;
        GameEvents.GameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        GameEvents.ScoreChanged -= HandleScoreChanged;
        GameEvents.HeightChanged -= HandleHeightChanged;
        GameEvents.LivesChanged -= HandleLivesChanged;
        GameEvents.NextBlockChanged -= HandleNextBlockChanged;
        GameEvents.GameOver -= HandleGameOver;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            HandleScoreChanged(GameManager.Instance.score);
            HandleHeightChanged(GameManager.Instance.maxHeight);
            HandleLivesChanged(GameManager.Instance.lives);
        }

        if (_spawner != null) HandleNextBlockChanged(_spawner.GetNextBlockName());
    }

    private void HandleScoreChanged(int score)
    {
        if (scoreText != null) scoreText.text = FormatStat("Score", score.ToString());
    }

    private void HandleHeightChanged(float height)
    {
        if (heightText != null) heightText.text = FormatStat("Height", $"{height:F1}m");
    }

    private void HandleLivesChanged(int lives)
    {
        if (livesText != null) livesText.text = FormatStat("Lives", lives.ToString());
    }

    private void HandleNextBlockChanged(string blockName)
    {
        if (nextBlockText != null) nextBlockText.text = FormatStat("Next", blockName);
    }

    private void HandleGameOver(int finalScore, float maxHeight)
    {
        ShowGameOver(finalScore, maxHeight);
    }

    private void ShowGameOver(int finalScore, float maxHeight)
    {
        if (gameOverPanel != null && !gameOverPanel.activeSelf)
        {
            gameOverPanel.SetActive(true);
            if (finalScoreText != null)
            {
                finalScoreText.text = $"Final Score: {finalScore}\nMax Height: {maxHeight:F1}m";
            }
        }
    }

    public void RestartGame()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.RestartGame();
        }
    }

    private void ConfigureHudStyle()
    {
        _labelColorTag = ColorUtility.ToHtmlStringRGB(hudLabelColor);

        if (!restyleHud) return;

        BuildHudCard(scoreText, "ScoreCard", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(190f, 72f), TextAlignmentOptions.MidlineLeft);
        BuildHudCard(heightText, "HeightCard", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(220f, 72f), TextAlignmentOptions.MidlineRight);
        BuildHudCard(livesText, "LivesCard", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(170f, 64f), TextAlignmentOptions.MidlineLeft);
        BuildHudCard(nextBlockText, "NextBlockCard", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(240f, 64f), TextAlignmentOptions.MidlineRight);

        StyleGameOverText();
    }

    private void BuildHudCard(
        TextMeshProUGUI text,
        string cardName,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 size,
        TextAlignmentOptions alignment)
    {
        if (text == null) return;

        RectTransform textRect = text.rectTransform;
        Transform hudRoot = textRect.parent;
        if (hudRoot == null) return;

        RectTransform cardRect;
        if (hudRoot.name == cardName)
        {
            cardRect = (RectTransform)hudRoot;
        }
        else
        {
            GameObject card = new GameObject(cardName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            card.transform.SetParent(hudRoot, false);
            card.transform.SetAsFirstSibling();

            cardRect = (RectTransform)card.transform;
            Image image = card.GetComponent<Image>();
            image.color = hudCardColor;
            image.raycastTarget = false;

            textRect.SetParent(cardRect, false);
        }

        cardRect.anchorMin = anchorMin;
        cardRect.anchorMax = anchorMax;
        cardRect.pivot = pivot;
        cardRect.anchoredPosition = anchoredPosition;
        cardRect.sizeDelta = size;

        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;
        textRect.offsetMin = new Vector2(18f, 8f);
        textRect.offsetMax = new Vector2(-18f, -8f);

        text.color = hudTextColor;
        text.alignment = alignment;
        text.enableAutoSizing = true;
        text.fontSizeMin = 16f;
        text.fontSizeMax = 30f;
        text.raycastTarget = false;
    }

    private void StyleGameOverText()
    {
        if (finalScoreText == null) return;

        finalScoreText.color = hudTextColor;
        finalScoreText.enableAutoSizing = true;
        finalScoreText.fontSizeMin = 22f;
        finalScoreText.fontSizeMax = 38f;
    }

    private string FormatStat(string label, string value)
    {
        string safeValue = string.IsNullOrWhiteSpace(value) ? "-" : value;
        return $"<size=58%><color=#{_labelColorTag}>{label.ToUpperInvariant()}</color></size>\n<b>{safeValue}</b>";
    }
}
