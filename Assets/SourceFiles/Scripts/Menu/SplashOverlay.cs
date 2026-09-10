using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

/// <summary>Holds launch art over connection, ownership and the first cloud merge.
/// Uses real time while the menu is paused. Scene reloads never restart the launch gate.</summary>
public static class SplashOverlay
{
    private const string SpritePath = "Splash/splash_portrait";
    private const float HoldSeconds = .7f;
    private const float FadeSeconds = .45f;
    private const int SortingOrder = 12000;
    private static bool _shownThisProcess;
    private static GameObject _live;
    public static bool IsVisible => _live != null;

    /// <summary>Returns true when the callback owns startup; false on subsequent menu visits.</summary>
    public static bool ShowIfFirstBoot(Action ready = null)
    {
        if (_shownThisProcess) return false;
        _shownThisProcess = true;
        GameObject root = CreateOverlayCanvas("Splash", SortingOrder);
        UnityEngine.Object.DontDestroyOnLoad(root); // also covers the transition into the introduction
        _live = root;
        Image backing = CreateImage(root.transform, "Backing", null, new Color(.05f, .035f, .03f, 1f));
        Stretch(backing.rectTransform);
        backing.raycastTarget = true;
        Sprite art = Resources.Load<Sprite>(SpritePath);
        if (art != null)
        {
            Image image = CreateImage(root.transform, "Art", art, Color.white);
            Stretch(image.rectTransform);
            FitToCover(image, SpriteAspect(art, 9f / 16f));
        }
        // Missing artwork must never bypass the connection/ownership gate.
        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true;
        root.AddComponent<Runner>().Init(group, ready);
        OnlineService.RetryConnect(); // also starts the service if runtime hook order has not done so
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        _shownThisProcess = false;
        _live = null;
    }

    private sealed class Runner : MonoBehaviour
    {
        private CanvasGroup _group;
        private Action _ready;
        private float _elapsed;
        private float _fadeElapsed;
        private bool _revealing;
        private GameObject _failure;
        private TextMeshProUGUI _status;
        private Button _restore;
        private bool _restoring;

        public void Init(CanvasGroup group, Action ready)
        {
            _group = group;
            _ready = ready;
            var safe = CreateRect(transform, "StatusSafeArea", Vector2.zero, Vector2.one,
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            _status = CreateTmp(safe, "ConnectionStatus", "Connecting…", 24, Color.white,
                TextAnchor.MiddleCenter, FontStyle.Normal, DefaultFont,
                new Vector2(0f, 120f), new Vector2(660f, 60f), new Vector2(.5f, 0f));
            _status.raycastTarget = false;
            BuildFailure();
        }

        private void Update()
        {
            if (_revealing)
            {
                _fadeElapsed += Time.unscaledDeltaTime;
                _group.alpha = 1f - Mathf.Clamp01(_fadeElapsed / FadeSeconds);
                if (_fadeElapsed >= FadeSeconds) Destroy(gameObject);
                return;
            }
            _elapsed += Time.unscaledDeltaTime;
            var decision = StartupGate.Evaluate(OnlineService.Enabled, OnlineService.IsReady,
                AttemptsSync.HasFullServerState, ProgressSync.HasSyncedThisSession,
                PremiumStore.IsPremium, _elapsed);
            bool blocked = decision == StartupGate.Decision.RetryRequired;
            _failure.SetActive(blocked);
            _status.gameObject.SetActive(!blocked);
            if (blocked)
            {
                _restore.gameObject.SetActive(PremiumStore.HasStore);
                _restore.interactable = PremiumStore.Available && !_restoring;
                return;
            }
            _status.text = OnlineService.IsReady ? "Loading your progress…" : "Connecting…";
            if (decision == StartupGate.Decision.Waiting || _elapsed < HoldSeconds) return;
            _status.text = decision == StartupGate.Decision.Offline ? "Opening your saved game" : "Ready";
            _revealing = true; // latch before invoking code that can reload the scene
            var ready = _ready;
            _ready = null;
            ready?.Invoke(); // build the final menu under opaque art, then fade on subsequent frames
        }

        private void BuildFailure()
        {
            var wash = CreateImage(transform, "ConnectionUnavailable", null, new Color(0f, 0f, 0f, .78f));
            Stretch(wash.rectTransform);
            wash.raycastTarget = true;
            _failure = wash.gameObject;
            var panel = CreateRect(wash.transform, "Panel", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), Vector2.zero, new Vector2(760f, 600f));
            ModalSafeFrame.Attach(panel);
            CreateTmp(panel, "Title", "We couldn’t connect", 44, Color.white,
                TextAnchor.MiddleCenter, FontStyle.Normal, DefaultFont,
                new Vector2(0f, -28f), new Vector2(700f, 80f), new Vector2(.5f, 1f));
            CreateTmp(panel, "Explanation", "Check your connection and try again.", 27, GameMenuStyle.BodyText,
                TextAnchor.MiddleCenter, FontStyle.Normal, DefaultFont,
                new Vector2(0f, -132f), new Vector2(660f, 80f), new Vector2(.5f, 1f));
            CreateTmp(panel, "Unlimited", "Hazard Heights Unlimited includes offline play.", 24, GameMenuStyle.BodyText,
                TextAnchor.MiddleCenter, FontStyle.Normal, DefaultFont,
                new Vector2(0f, -236f), new Vector2(640f, 90f), new Vector2(.5f, 1f));
            ActionButton(panel, "Retry", "Try again", -370f, () =>
            {
                _elapsed = 0f;
                OnlineService.RetryConnect();
            });
            _restore = ActionButton(panel, "Restore", "Restore purchases", -482f, Restore);
            _failure.SetActive(false);
        }

        private void Restore()
        {
            if (_restoring) return;
            _restoring = true;
            var label = _restore.GetComponentInChildren<TextMeshProUGUI>();
            label.text = "Checking purchases…";
            PremiumStore.Restore(result =>
            {
                if (this == null) return;
                _restoring = false;
                label.text = result == PremiumStoreResult.NothingToRestore ? "No purchase found — try again"
                    : result == PremiumStoreResult.Failed ? "Store unavailable — try again" : "Restore purchases";
            });
        }

        private static Button ActionButton(Transform panel, string name, string label, float y, Action action)
        {
            var image = CreateImage(panel, name, RuntimeSprites.RoundedPanel(), name == "Retry"
                ? new Color(.96f, .95f, .9f) : new Color(.12f, .12f, .13f));
            image.type = Image.Type.Sliced;
            SetRect(image.rectTransform, new Vector2(0f, y), new Vector2(640f, 88f), new Vector2(.5f, 1f));
            image.raycastTarget = true;
            CreateTmp(image.transform, "Label", label, 26, name == "Retry" ? Color.black : Color.white,
                TextAnchor.MiddleCenter, FontStyle.Normal, DefaultFont);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => action());
            return button;
        }
    }
}
