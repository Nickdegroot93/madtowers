using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// In-game pause: a small button top-right pauses the game behind a near-opaque shroud -
/// deliberately hiding the tower, so pausing can't be used as a free "stop and study the
/// board" tool. Offers Resume, plus Restart and Back-to-menu behind an are-you-sure step
/// (both throw away the current run). Added to the GameManager's object at runtime, same
/// pattern as LevelRuntimeController.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    private GameObject _menuCanvas;
    private RenderTexture _blurTexture;

    /// <summary>True while live play should show a pause affordance - the HUD's pause
    /// button (UIManager top bar) drives its visibility from this.</summary>
    public static bool PauseAvailable =>
        GameManager.Instance != null
        && !GameManager.Instance.isGameOver
        && !GameManager.Instance.IsGamePaused
        && !LevelSelectionState.IsSelectionPending;

    /// <summary>Open the pause menu; the button itself lives in the HUD top bar.</summary>
    public void ShowPauseMenu()
    {
        if (GameManager.Instance == null || GameManager.Instance.isGameOver ||
            GameManager.Instance.IsGamePaused) return;

        GameManager.Instance.SetGamePaused(true);
        StartCoroutine(CaptureBlurThenShowMenu());
    }

    // The shroud is a real blur: capture the frozen frame once, then downscale through a
    // render-texture chain - each bilinear resample is a cheap strong blur pass, and the
    // result is a static texture with zero per-frame cost. Rendering keeps running while
    // timeScale is 0, so WaitForEndOfFrame still fires.
    private System.Collections.IEnumerator CaptureBlurThenShowMenu()
    {
        yield return null;                    // let the HUD hide its pause button first
        yield return new WaitForEndOfFrame(); // grab the fully rendered still frame

        int width = Mathf.Max(16, Screen.width);
        int height = Mathf.Max(16, Screen.height);
        RenderTexture full = RenderTexture.GetTemporary(width, height, 0);
        RenderTexture quarter = RenderTexture.GetTemporary(width / 4, height / 4, 0);
        RenderTexture eighth = RenderTexture.GetTemporary(width / 8, height / 8, 0);
        RenderTexture sixteenth = RenderTexture.GetTemporary(width / 16, height / 16, 0);

        ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
        Graphics.Blit(full, quarter);
        Graphics.Blit(quarter, eighth);
        Graphics.Blit(eighth, sixteenth);
        Graphics.Blit(sixteenth, eighth); // round trip softens further

        RenderTexture.ReleaseTemporary(full);
        RenderTexture.ReleaseTemporary(quarter);
        RenderTexture.ReleaseTemporary(sixteenth);
        _blurTexture = eighth; // held while the pause UI is open

        BuildMenu();
    }

    private void CreateShroud(Transform canvasRoot)
    {
        if (_blurTexture != null)
        {
            GameObject blur = new GameObject("BlurShroud");
            blur.transform.SetParent(canvasRoot, false);
            RectTransform rect = blur.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            RawImage image = blur.AddComponent<RawImage>();
            image.texture = _blurTexture;
            // Back-buffer captures follow the platform's UV convention (Metal/DX start at top).
            image.uvRect = SystemInfo.graphicsUVStartsAtTop
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);
        }

        // Dim tint on top of the blur for menu readability (and a heavy fallback if the
        // capture ever fails).
        RuntimeUiKit.CreateBackdrop(canvasRoot,
            new Color(0.03f, 0.045f, 0.06f, _blurTexture != null ? 0.55f : 0.985f));
    }

    private void BuildMenu()
    {
        DestroyMenu();
        _menuCanvas = RuntimeUiKit.CreateOverlayCanvas("Pause Menu", 7000);
        CreateShroud(_menuCanvas.transform);

        GameObject panel = RuntimeUiKit.CreateCenteredPanel(_menuCanvas.transform, new Vector2(560f, 480f));
        RuntimeUiKit.CreateLabel(panel.transform, "Paused", 52, 82f, FontStyle.Bold, RuntimeUiKit.TitleColor);
        RuntimeUiKit.CreateButton(panel.transform, "Resume", 88f, Resume);
        RuntimeUiKit.CreateButton(panel.transform, "Restart Level", 88f,
            () => BuildConfirm("Restart this level?\nYour current run will be lost.", RestartLevel));
        RuntimeUiKit.CreateButton(panel.transform, "Back to Menu", 88f,
            () => BuildConfirm("Quit to the level menu?\nYour current run will be lost.", ReturnToMenu));
    }

    private void BuildConfirm(string question, UnityEngine.Events.UnityAction onYes)
    {
        DestroyMenu();
        _menuCanvas = RuntimeUiKit.CreateOverlayCanvas("Pause Confirm", 7000);
        CreateShroud(_menuCanvas.transform);

        GameObject panel = RuntimeUiKit.CreateCenteredPanel(_menuCanvas.transform, new Vector2(560f, 430f));
        RuntimeUiKit.CreateLabel(panel.transform, "Are you sure?", 46, 70f, FontStyle.Bold, RuntimeUiKit.TitleColor);
        RuntimeUiKit.CreateLabel(panel.transform, question, 28, 92f, FontStyle.Normal,
            new Color(0.78f, 0.85f, 0.9f, 1f));
        RuntimeUiKit.CreateButton(panel.transform, "Yes", 88f, onYes);
        RuntimeUiKit.CreateButton(panel.transform, "No, keep playing", 88f, BuildMenu);
    }

    private void Resume()
    {
        DestroyMenu();
        ReleaseBlur();
        if (GameManager.Instance != null) GameManager.Instance.SetGamePaused(false);
    }

    private void OnDestroy()
    {
        ReleaseBlur(); // restart / back-to-menu paths end in a scene unload
    }

    private void ReleaseBlur()
    {
        if (_blurTexture == null) return;
        RenderTexture.ReleaseTemporary(_blurTexture);
        _blurTexture = null;
    }

    private void RestartLevel()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void ReturnToMenu()
    {
        LevelSelectRuntimeMenu.ReturnToMenu();
    }

    private void DestroyMenu()
    {
        if (_menuCanvas != null) Destroy(_menuCanvas);
        _menuCanvas = null;
    }
}
