using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

/// <summary>
/// First-boot splash: the launch artwork held over the freshly built menu for a beat, then
/// faded out. The engine-init splash (Player Settings background image) shows the same art,
/// so the player sees ONE continuous splash from app tap to menu. There is no real work to
/// wait for — BuildMenu is synchronous — and no loader animation: nothing can animate during
/// the engine-init stretch, so a late-starting loader only reads as a flicker.
///
/// Shown once per process: ReturnToMenu's scene reloads re-run ShowMenuIfNeeded and must not
/// re-splash. The menu runs at timeScale = 0, so all timing here is unscaled.
/// </summary>
public static class SplashOverlay
{
    private const string SpritePath = "Splash/splash_portrait";
    private const float HoldSeconds = 0.7f;
    private const float FadeSeconds = 0.45f;

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

        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true; // swallow taps until the menu is actually visible

        root.AddComponent<Runner>().Init(group);
    }

    private sealed class Runner : MonoBehaviour
    {
        private CanvasGroup _group;
        private float _elapsed;

        public void Init(CanvasGroup group)
        {
            _group = group;
        }

        private void Update()
        {
            _elapsed += Time.unscaledDeltaTime;

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
