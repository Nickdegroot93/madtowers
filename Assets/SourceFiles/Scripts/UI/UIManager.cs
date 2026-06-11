using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("HUD Elements")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI heightText;
    [SerializeField] private TextMeshProUGUI livesText;     // legacy; hidden, replaced by heart icons
    [SerializeField] private TextMeshProUGUI nextBlockText; // legacy; hidden, replaced by the ghost preview

    [Header("Game Over UI")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI finalScoreText;

    [Header("HUD Style")]
    [SerializeField] private bool restyleHud = true;

    // Style values are code-owned (not serialized) so tweaks always take effect —
    // serialized defaults go stale in Unity's import caches (see memory/PHYSICS.md §2).
    private static readonly Color HudTextColor = new Color(0.95f, 0.98f, 1f, 1f);
    private static readonly Color HeartColor = new Color(0.93f, 0.29f, 0.34f, 1f);
    private static readonly Color NextPreviewTint = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color NextPanelColor = new Color(0.04f, 0.07f, 0.1f, 0.6f);
    private static readonly Color NextLabelColor = new Color(0.62f, 0.72f, 0.82f, 0.85f);
    private const float HeartSize = 44f;
    private const float HeartGap = 10f;
    private const int MaxHearts = 12;

    private Spawner _spawner;
    private RectTransform _heartsContainer;
    private Image[] _hearts = System.Array.Empty<Image>();
    private GameObject _nextPanel;
    private Image _nextPreview;
    private readonly System.Collections.Generic.Dictionary<string, Sprite> _ghostSprites =
        new System.Collections.Generic.Dictionary<string, Sprite>();

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
        if (scoreText != null) scoreText.text = score.ToString();
    }

    private void HandleHeightChanged(float height)
    {
        if (heightText != null) heightText.text = $"{height:F1}m";
    }

    private void HandleLivesChanged(int lives)
    {
        EnsureHearts();
        if (_heartsContainer == null) return;

        for (int i = 0; i < _hearts.Length; i++)
        {
            if (_hearts[i] != null) _hearts[i].enabled = i < lives;
        }
    }

    private void HandleNextBlockChanged(string blockName)
    {
        EnsureNextPreview();
        if (_nextPreview == null) return;

        string shape = ThemeSkins.ExtractShapeToken(blockName);
        Sprite ghost = string.IsNullOrEmpty(shape) ? null : GetGhostSprite(shape);

        _nextPreview.sprite = ghost;
        _nextPreview.enabled = ghost != null;
        if (_nextPanel != null) _nextPanel.SetActive(ghost != null);
    }

    // Desaturated copy of the piece sprite so the preview reads as "coming up", not as a
    // brick already in play. Cached per shape and skin folder.
    private Sprite GetGhostSprite(string shape)
    {
        string cacheKey = $"{ThemeSkins.Folder}:{shape}";
        if (_ghostSprites.TryGetValue(cacheKey, out Sprite cached)) return cached;

        Sprite source = ThemeSkins.LoadPiece(shape);
        Sprite ghost = source;
        if (source != null && source.texture.isReadable)
        {
            Texture2D src = source.texture;
            Texture2D tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Color[] pixels = src.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                float gray = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                pixels[i] = Color.Lerp(new Color(gray, gray, gray, c.a), c, 0.18f);
            }
            tex.SetPixels(pixels);
            tex.Apply();

            ghost = Sprite.Create(tex, new Rect(0, 0, src.width, src.height), new Vector2(0.5f, 0.5f), 256f);
            ghost.hideFlags = HideFlags.HideAndDontSave;
        }

        _ghostSprites[cacheKey] = ghost;
        return ghost;
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
        if (!restyleHud) return;

        // Big bold numbers, no labels, no card chrome - readable at a glance.
        PlaceCornerText(scoreText, new Vector2(0f, 1f), new Vector2(28f, -16f), TextAlignmentOptions.TopLeft);
        PlaceCornerText(heightText, new Vector2(1f, 1f), new Vector2(-28f, -16f), TextAlignmentOptions.TopRight);

        if (livesText != null) livesText.gameObject.SetActive(false);
        if (nextBlockText != null) nextBlockText.gameObject.SetActive(false);

        StyleGameOverText();
    }

    private void PlaceCornerText(TextMeshProUGUI text, Vector2 corner, Vector2 offset, TextAlignmentOptions alignment)
    {
        if (text == null) return;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = corner;
        rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = offset;
        rect.sizeDelta = new Vector2(360f, 96f);

        text.color = HudTextColor;
        text.alignment = alignment;
        text.fontStyle = FontStyles.Bold;
        text.enableAutoSizing = true;
        text.fontSizeMin = 28f;
        text.fontSizeMax = 60f;
        text.raycastTarget = false;
    }

    private RectTransform HudRoot()
    {
        return scoreText != null ? scoreText.rectTransform.parent as RectTransform : null;
    }

    private void EnsureHearts()
    {
        if (_heartsContainer != null || HudRoot() == null) return;

        GameObject container = new GameObject("Hearts", typeof(RectTransform));
        _heartsContainer = (RectTransform)container.transform;
        _heartsContainer.SetParent(HudRoot(), false);
        _heartsContainer.anchorMin = Vector2.zero;
        _heartsContainer.anchorMax = Vector2.zero;
        _heartsContainer.pivot = Vector2.zero;
        _heartsContainer.anchoredPosition = new Vector2(24f, 22f);
        _heartsContainer.sizeDelta = new Vector2(MaxHearts * (HeartSize + HeartGap), HeartSize);

        _hearts = new Image[MaxHearts];
        for (int i = 0; i < MaxHearts; i++)
        {
            GameObject heart = new GameObject($"Heart{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform heartRect = (RectTransform)heart.transform;
            heartRect.SetParent(_heartsContainer, false);
            heartRect.anchorMin = Vector2.zero;
            heartRect.anchorMax = Vector2.zero;
            heartRect.pivot = Vector2.zero;
            heartRect.anchoredPosition = new Vector2(i * (HeartSize + HeartGap), 0f);
            heartRect.sizeDelta = new Vector2(HeartSize, HeartSize);

            Image image = heart.GetComponent<Image>();
            image.sprite = RuntimeSprites.Heart();
            image.color = HeartColor;
            image.raycastTarget = false;
            image.enabled = false;
            _hearts[i] = image;
        }
    }

    // A small rounded panel top-center with a "NEXT" caption and the desaturated ghost
    // of the coming piece inside it.
    private void EnsureNextPreview()
    {
        if (_nextPreview != null || HudRoot() == null) return;

        _nextPanel = new GameObject("NextPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform panelRect = (RectTransform)_nextPanel.transform;
        panelRect.SetParent(HudRoot(), false);
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -12f);
        panelRect.sizeDelta = new Vector2(224f, 132f);

        Image panelImage = _nextPanel.GetComponent<Image>();
        panelImage.sprite = RuntimeSprites.RoundedPanel();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = NextPanelColor;
        panelImage.raycastTarget = false;

        if (scoreText != null)
        {
            GameObject label = new GameObject("NextLabel", typeof(RectTransform));
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.SetParent(panelRect, false);
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -7f);
            labelRect.sizeDelta = new Vector2(0f, 22f);

            TextMeshProUGUI labelText = label.AddComponent<TextMeshProUGUI>();
            labelText.font = scoreText.font;
            labelText.text = "NEXT";
            labelText.fontSize = 17f;
            labelText.characterSpacing = 18f;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.color = NextLabelColor;
            labelText.raycastTarget = false;
        }

        GameObject preview = new GameObject("NextPiecePreview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)preview.transform;
        rect.SetParent(panelRect, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(18f, 12f);
        rect.offsetMax = new Vector2(-18f, -34f);

        _nextPreview = preview.GetComponent<Image>();
        _nextPreview.preserveAspect = true;
        _nextPreview.raycastTarget = false;
        _nextPreview.color = NextPreviewTint;
        _nextPreview.enabled = false;
        _nextPanel.SetActive(false);
    }

    private void StyleGameOverText()
    {
        if (finalScoreText == null) return;

        finalScoreText.color = HudTextColor;
        finalScoreText.enableAutoSizing = true;
        finalScoreText.fontSizeMin = 22f;
        finalScoreText.fontSizeMax = 38f;
    }
}
