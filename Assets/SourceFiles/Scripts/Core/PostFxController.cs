using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// One global post-processing stack applied over EVERY chapter - the cross-chapter "same
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
    // Death drain (DeathBeatFx.SetDrain 1): colour bleeds out, the vignette closes in.
    private const float DrainSaturation = -65f;
    private const float DrainVignetteIntensity = 0.42f;

    private static Vignette _vignette;
    private static ColorAdjustments _color;

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
        _vignette = vignette;

        Bloom bloom = profile.Add<Bloom>();
        bloom.intensity.Override(BloomIntensity);
        bloom.threshold.Override(BloomThreshold);

        ColorAdjustments color = profile.Add<ColorAdjustments>();
        color.saturation.Override(ExtraSaturation);
        color.contrast.Override(ExtraContrast);
        _color = color;

        Volume volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0f;
        volume.profile = profile;

        ApplyPostProcessing();
        SceneManager.sceneLoaded += OnSceneLoaded;
        // Live toggle from the Graphics settings; idempotent re-bind (static survives fast-playmode).
        SettingsService.Changed -= ApplyPostProcessing;
        SettingsService.Changed += ApplyPostProcessing;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SettingsService.Changed -= ApplyPostProcessing;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyPostProcessing(); // each scene load brings a fresh camera
        SetDrain(0f);          // a death drain never outlives its run
    }

    /// <summary>0 = the normal grade, 1 = the dead scene (grey, closed vignette). Held until the
    /// next scene load resets it.</summary>
    public static void SetDrain(float t)
    {
        t = Mathf.Clamp01(t);
        if (_color != null) _color.saturation.Override(Mathf.Lerp(ExtraSaturation, DrainSaturation, t));
        if (_vignette != null) _vignette.intensity.Override(Mathf.Lerp(VignetteIntensity, DrainVignetteIntensity, t));
    }

    // Whole post stack (vignette + bloom + grading) follows the user's Visual Effects setting -
    // turning it off is the cheap GPU/battery win on mobile. The Volume stays built; we just
    // toggle the camera's renderPostProcessing.
    private static void ApplyPostProcessing()
    {
        Camera camera = Camera.main;
        if (camera == null) return;

        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        if (data != null) data.renderPostProcessing = SettingsService.VisualEffects;
    }
}
