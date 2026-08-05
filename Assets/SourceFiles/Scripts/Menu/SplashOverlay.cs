using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

/// <summary>
/// First-boot splash: the launch artwork held over the freshly built menu for a beat (with a
/// small loader pulse), then faded out. The engine-init splash (Player Settings background
/// image) shows the same art, so the player sees ONE continuous splash from app tap to menu.
/// There is no real work to wait for — BuildMenu is synchronous — so the hold is deliberately
/// short; the dots exist to read as "alive", not to report progress.
///
/// Shown once per process: ReturnToMenu's scene reloads re-run ShowMenuIfNeeded and must not
/// re-splash. The menu runs at timeScale = 0, so all timing here is unscaled.
/// </summary>
public static class SplashOverlay
{
    private const string SpritePath = "Splash/splash_portrait";
    private const float HoldSeconds = 0.7f;
    private const float FadeSeconds = 0.45f;
    private const float DotPulseHz = 1.6f;

    // Above every menu surface including the simulated store sheet (9100).
    private const int SortingOrder = 12000;

    private static bool _shownThisProcess;

    public static void ShowIfFirstBoot()
    {
        if (_shownThisProcess) return;
        _shownThisProcess = true;

        Sprite art = Resources.Load<Sprite>(SpritePath);
        if (art == null) return; // art missing: boot straight to the menu, never block on it

        GameObject root = CreateOverlayCanvas("Splash", SortingOrder);

        // Solid backing behind the cover-fit art so no menu pixel shows through on any aspect.
        Image backing = CreateImage(root.transform, "Backing", null, new Color(0.05f, 0.035f, 0.03f, 1f));
        Stretch(backing.rectTransform);
        // CreateImage disables raycastTarget; re-enable on the backing or the CanvasGroup's
        // blocksRaycasts has nothing to block with and the hidden menu takes taps.
        backing.raycastTarget = true;

        Image image = CreateImage(root.transform, "Art", art, Color.white);
        Stretch(image.rectTransform);
        FitToCover(image, SpriteAspect(art, 9f / 16f));

        // Loader dots: bottom-centre, clear of the home indicator. Sprite dots (SoftPuff), not
        // text — glyph availability is then never a question on a screen with no fonts loaded.
        float bottomInset = SafeAreaBottomInset(root.GetComponent<Canvas>());
        Sprite puff = RuntimeSprites.SoftPuff();
        Color dotColor = new Color(1f, 0.9f, 0.68f, 0.9f);
        Image[] dots = new Image[3];
        for (int i = 0; i < dots.Length; i++)
        {
            dots[i] = CreateImage(root.transform, "Dot" + i, puff, dotColor);
            RectTransform rect = dots[i].rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2((i - 1) * 52f, bottomInset + 130f);
            rect.sizeDelta = new Vector2(26f, 26f);
        }

        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true; // swallow taps until the menu is actually visible

        root.AddComponent<Runner>().Init(group, dots);
    }

    private sealed class Runner : MonoBehaviour
    {
        private CanvasGroup _group;
        private Image[] _dots;
        private float _elapsed;

        public void Init(CanvasGroup group, Image[] dots)
        {
            _group = group;
            _dots = dots;
        }

        private void Update()
        {
            _elapsed += Time.unscaledDeltaTime;

            // Staggered wave: each dot trails the previous, the classic "working on it" read.
            for (int i = 0; i < _dots.Length; i++)
            {
                if (_dots[i] == null) continue;
                Color c = _dots[i].color;
                c.a = Mathf.Lerp(0.25f, 0.95f,
                    0.5f + 0.5f * Mathf.Sin((_elapsed * DotPulseHz - i * 0.18f) * 2f * Mathf.PI));
                _dots[i].color = c;
            }

            float fadeT = (_elapsed - HoldSeconds) / FadeSeconds;
            if (fadeT <= 0f) return;
            if (fadeT >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            _group.alpha = 1f - fadeT;
        }
    }
}
