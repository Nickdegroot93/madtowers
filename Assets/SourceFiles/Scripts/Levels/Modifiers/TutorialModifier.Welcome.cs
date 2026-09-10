using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public partial class TutorialModifier
{
    private CanvasGroup _welcome;
    private RectTransform _welcomePanel;
    private GameManager _welcomeGameManager;
    private UIManager _welcomeHud;
    private float _welcomeTime;
    private bool _skipWelcome;
    private const float WelcomeFadeSeconds = .45f;

    private void BuildWelcome()
    {
        var root = RuntimeUiKit.CreateRect(_overlayRoot.transform, "Welcome", Vector2.zero,
            Vector2.one, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        _welcome = root.gameObject.AddComponent<CanvasGroup>();
        _welcome.alpha = 0f;
        var wash = RuntimeUiKit.CreateImage(root, "SceneryWash", null, new Color(.015f, .025f, .03f, .32f));
        RuntimeUiKit.Stretch(wash.rectTransform);
        wash.raycastTarget = true;

        var safe = RuntimeUiKit.CreateRect(root, "SafeArea", Vector2.zero, Vector2.one,
            new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        safe.gameObject.AddComponent<SafeAreaFitter>();
        _welcomePanel = RuntimeUiKit.CreateRect(safe, "WelcomePanel", new Vector2(.06f, .5f),
            new Vector2(.94f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(0f, 680f));
        var backing = _welcomePanel.gameObject.AddComponent<Image>();
        backing.sprite = RuntimeSprites.RoundedPanel();
        backing.type = Image.Type.Sliced;
        backing.color = new Color(.025f, .04f, .045f, .97f);

        ChapterOrnaments.Dress(_welcomePanel, ChapterArtwork.ActiveChapter);

        var eyebrow = WelcomeText("Eyebrow", "YOUR FIRST TOWER", 23f, Secondary, 44f, 40f, true);
        eyebrow.characterSpacing = 4f;
        WelcomeText("Title", "Welcome to\nHazard Heights", 64f, Accent, 110f, 184f, true);
        WelcomeText("Description", "Every great tower starts with one brick.\nLet’s learn the controls.",
            32f, Secondary, 312f, 96f, false);
        WelcomeButton("LetsBuild", "Let’s build", 450f, 100f, primary: true, skip: false);
        WelcomeButton("SkipTutorial", "Skip tutorial", 565f, 76f, primary: false, skip: true);
    }

    private TextMeshProUGUI WelcomeText(string name, string text, float size, Color color,
        float top, float height, bool bold)
    {
        var rect = RuntimeUiKit.CreateRect(_welcomePanel, name, new Vector2(0f, 1f), Vector2.one,
            new Vector2(.5f, 1f), Vector2.zero, Vector2.zero);
        rect.offsetMin = new Vector2(44f, -top - height);
        rect.offsetMax = new Vector2(-44f, -top);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        HudVisualStyle.Text(label, bold);
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        return label;
    }

    private void WelcomeButton(string name, string text, float top, float height, bool primary, bool skip)
    {
        var rect = RuntimeUiKit.CreateRect(_welcomePanel, name, new Vector2(.08f, 1f),
            new Vector2(.92f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -top), new Vector2(0f, height));
        var fill = rect.gameObject.AddComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = primary ? Accent : Color.clear;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        var colors = button.colors;
        colors.highlightedColor = new Color(.9f, .94f, .92f);
        colors.pressedColor = new Color(.7f, .78f, .75f);
        colors.fadeDuration = .12f;
        button.colors = colors;
        var label = RuntimeUiKit.CreateTmp(rect, "Label", text, primary ? 36 : 30,
            primary ? new Color(.035f, .05f, .06f) : Secondary, TextAnchor.MiddleCenter,
            primary ? FontStyle.Bold : FontStyle.Normal, RuntimeUiKit.TitleFont);
        HudVisualStyle.Text(label, primary);
        label.color = primary ? new Color(.035f, .05f, .06f) : Secondary;
        button.onClick.AddListener(() => ChooseWelcome(skip));
    }

    private void ChooseWelcome(bool skip)
    {
        if (_phase != Phase.Welcome) return;
        _skipWelcome = skip;
        _phase = Phase.WelcomeExit;
        _welcomeTime = 0f;
        _welcome.interactable = false;
        SfxPlayer.Play("pop_01", .55f);
        Haptics.Light();
    }

    private void UpdateWelcome(float deltaTime)
    {
        _welcomeTime += deltaTime;
        // Modifiers can start before the scene HUD has awakened. Bind once it becomes available.
        if (_welcomeHud != UIManager.Instance && UIManager.Instance != null)
        {
            _welcomeHud = UIManager.Instance;
            _welcomeHud.SetTutorialPresentation(welcoming: true, teaching: true);
        }
        // Fit the authored height on short/landscape safe areas. Width still stretches.
        if (_welcomePanel != null)
        {
            var safe = (RectTransform)_welcomePanel.parent;
            _welcomePanel.localScale = Vector3.one * Mathf.Min(1f, Mathf.Max(.1f, (safe.rect.height - 64f) / 680f));
        }
        if (_phase == Phase.Welcome)
        {
            _welcome.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_welcomeTime / .65f));
            _welcomePanel.anchoredPosition = new Vector2(0f, (1f - _welcome.alpha) * -18f);
            return;
        }

        // A quick choice never cuts the pan short. Keep the welcome readable until it ends.
        if (CameraIntroGate.IsPlaying) { _welcomeTime = 0f; return; }
        float t = Mathf.Clamp01(_welcomeTime / WelcomeFadeSeconds);
        _welcome.alpha = 1f - Mathf.SmoothStep(0f, 1f, t);
        _welcomePanel.anchoredPosition = new Vector2(0f, -12f * t);
        // Consume the full button gesture before enabling gameplay, including keyboard submit.
        if (t < 1f || IsPointerDown() || (Keyboard.current != null &&
            (Keyboard.current.enterKey.isPressed || Keyboard.current.spaceKey.isPressed))) return;

        _welcome.gameObject.SetActive(false);
        _groupVisible = true;
        _group.alpha = 0f;
        UIManager.Instance?.SetTutorialPresentation(welcoming: false, teaching: !_skipWelcome);
        if (_skipWelcome)
        {
            BeginCoda(earned: false);
            ReleaseWelcomeHold();
        }
        else
        {
            _phase = Phase.AwaitPiece;
            SetInputGate(AllowedThrough(0));
            ApplyStepVisuals();
            _skipRoot.SetActive(true);
            if (_piece != null && !_piece.HasLanded) BeginPreRoll(_piece);
            ReleaseWelcomeHold();
        }
    }

    private void ReleaseWelcomeHold()
    {
        if (_welcomeGameManager == null) return;
        _welcomeGameManager.SetSpawnSuspended(this, false);
        _welcomeGameManager = null;
    }
}
