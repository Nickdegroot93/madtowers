using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// One global post-processing stack applied over EVERY theme - the cross-theme "same
/// game" glue and the cheap answer to "make it all look a bit better":
///   - vignette: soft darkened corners focus the eye on the tower
///   - bloom: bright elements (laser, sun, glow) bleed light slightly
///   - color grading: a touch more saturation and contrast - richer, less flat
/// Built entirely in code (no volume-profile assets to maintain); self-installs and
/// re-attaches to the camera on every scene load. Tune the constants below.
/// </summary>
public class PostFxController : MonoBehaviour
{
    private const float VignetteIntensity = 0.22f;
    private const float VignetteSmoothness = 0.45f;
    private const float BloomIntensity = 0.35f;
    private const float BloomThreshold = 0.9f;
    private const float ExtraSaturation = 8f;
    private const float ExtraContrast = 6f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        GameObject host = new GameObject("PostFx");
        DontDestroyOnLoad(host);
        host.AddComponent<PostFxController>();
    }

    private void Start()
    {
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.hideFlags = HideFlags.HideAndDontSave;

        Vignette vignette = profile.Add<Vignette>();
        vignette.intensity.Override(VignetteIntensity);
        vignette.smoothness.Override(VignetteSmoothness);

        Bloom bloom = profile.Add<Bloom>();
        bloom.intensity.Override(BloomIntensity);
        bloom.threshold.Override(BloomThreshold);

        ColorAdjustments color = profile.Add<ColorAdjustments>();
        color.saturation.Override(ExtraSaturation);
        color.contrast.Override(ExtraContrast);

        Volume volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0f;
        volume.profile = profile;

        EnablePostProcessingOnCamera();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnablePostProcessingOnCamera(); // each scene load brings a fresh camera
    }

    private static void EnablePostProcessingOnCamera()
    {
        Camera camera = Camera.main;
        if (camera == null) return;

        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        if (data != null) data.renderPostProcessing = true;
    }
}
