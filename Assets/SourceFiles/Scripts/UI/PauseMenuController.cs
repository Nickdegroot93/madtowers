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

        SfxPlayer.Play("ui-pause", 0.9f);
        GameManager.Instance.PushPause(this);
        GameManager.Instance.RequestPhase(this, GamePhase.Paused);
        StartCoroutine(CaptureBlurThenShowMenu());
    }

    // ---- interruption auto-pause -----------------------------------------------------------
    // A call, an app switch or the Android notification shade must never cost a run: live
    // play resumes INTO the pause sheet, never into a brick that kept falling (or fell the
    // moment the OS suspended us mid-drop). Both hooks route to one gate:
    //   - OnApplicationPause(true): real suspend (home button, call, app switch).
    //   - OnApplicationFocus(false): the sneaky case - shade pull / multi-window can drop
    //     focus while the app KEEPS RENDERING AND SIMULATING, so the run would die unwatched.
    // The rule is MANUAL-PAUSE PARITY: fire exactly when the player could have tapped the
    // HUD pause button (PauseAvailable) - not just GamePhase.Playing. Discovery and
    // WinVerifying keep the world simulating (a debut modal holds spawning, not physics;
    // hold-steady verification IS live physics) and GameOver() has no phase gate, so a
    // collapse there drains lives unwatched too (review 2026-08-22). Phases that freeze
    // time push a real pause (ability draft) and exclude themselves via IsGamePaused.

    private void OnApplicationPause(bool paused)
    {
        if (paused) AutoPauseForInterruption();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) AutoPauseForInterruption();
    }

    private void AutoPauseForInterruption()
    {
        // Editor focus flips constantly (clicking any other window, MCP-driven test runs
        // with runInBackground) - this is a device behavior, not an editor one.
        if (Application.isEditor) return;
        if (!PauseAvailable) return;
        ShowPauseMenu();
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

        // The lives row adds a line for non-premium players; size the sheet for it.
        bool showLives = RunLivesUi.Applies;
        GameObject panel = RuntimeUiKit.CreateCenteredPanel(_menuCanvas.transform,
            new Vector2(560f, showLives ? 600f : 560f));
        GameMenuStyle.StylePanel(panel);
        RuntimeUiKit.CreateLabel(panel.transform, "Paused", 52, 82f, FontStyle.Bold, GameMenuStyle.Accent);
        // The meter is invisible mid-run, so a player deciding whether to restart was
        // deciding blind - restarts felt free and running dry felt random (Nick 2026-08-09).
        RunLivesUi.BuildStatusRow(panel.transform);
        GameMenuStyle.StyleButton(RuntimeUiKit.CreateButton(panel.transform, "Resume", 88f, Resume), primary: true);
        GameMenuStyle.StyleButton(RuntimeUiKit.CreateButton(panel.transform, "Restart Level", 88f, () =>
        {
            // Out of lives: a restart would only bounce to the menu after a doomed server
            // round trip. Offer the refills instead of pretending.
            if (RunLivesUi.OutOfLives) BuildOutOfLives();
            else BuildConfirm($"Restart this level?\n{RunLivesUi.RestartCostText()}", RestartLevel);
        }), primary: false);
        GameMenuStyle.StyleButton(RuntimeUiKit.CreateButton(panel.transform, "Back to Menu", 88f,
            () => BuildConfirm("Quit to the level menu?\nYour current run will be lost.", ReturnToMenu)), primary: false);
        UiEntranceFx.Play(panel, 0.02f);
    }

    /// <summary>The restart-with-zero-lives sheet: name the problem, offer the refills
    /// (ad / premium via RunLivesUi), and keep "back" one tap away. After a successful
    /// refill it returns to the pause sheet, where Restart now works normally. A heal
    /// watcher does the same when a life simply regenerates while the sheet sits open.</summary>
    private void BuildOutOfLives()
    {
        DestroyMenu();
        _menuCanvas = RuntimeUiKit.CreateOverlayCanvas("Pause Out Of Lives", 7000);
        CreateShroud(_menuCanvas.transform);

        // THIS sheet, by identity. "_menuCanvas != null" cannot tell sheets apart: a slow
        // SSV claim (~9-20s) can land after the player resumed, re-paused, and reached the
        // button-less "Restarting..." panel - rebuilding the pause menu over THAT reopens
        // the double-restart the pending panel exists to prevent (review 2026-08-11).
        GameObject sheet = _menuCanvas;

        GameObject panel = RuntimeUiKit.CreateCenteredPanel(_menuCanvas.transform, new Vector2(560f, 650f));
        GameMenuStyle.StylePanel(panel);
        RuntimeUiKit.CreateLabel(panel.transform, "Out of lives", 46, 70f, FontStyle.Bold, GameMenuStyle.Accent);
        RunLivesUi.BuildStatusRow(panel.transform);
        RuntimeUiKit.CreateLabel(panel.transform, "Restarting costs a life and you have none left.",
            26, 66f, FontStyle.Normal, GameMenuStyle.BodyText);

        int actions = RunLivesUi.BuildOutOfLivesActions(panel.transform, () =>
        {
            if (this != null && _menuCanvas == sheet) BuildMenu();
        });
        if (actions == 0)
        {
            // No ad in hand and no store: the countdown above is the honest answer.
            RuntimeUiKit.CreateLabel(panel.transform, "A life regenerates on the timer above.",
                24, 56f, FontStyle.Normal, GameMenuStyle.BodyText);
        }
        GameMenuStyle.StyleButton(RuntimeUiKit.CreateButton(panel.transform, "Keep playing this run", 88f, Resume),
            primary: actions == 0);
        GameMenuStyle.StyleButton(RuntimeUiKit.CreateButton(panel.transform, "Back to Menu", 88f,
            () => BuildConfirm("Quit to the level menu?\nYour current run will be lost.", ReturnToMenu)),
            primary: false);
        UiEntranceFx.Play(panel, 0.02f);

        // A life can arrive on its own while the sheet sits open (regen tick, late SSV
        // grant). Rebuild to the pause sheet so Restart returns without the player having
        // to leave and come back - the results screen got this watcher, this sheet didn't
        // (review 2026-08-11).
        var watcher = sheet.AddComponent<OutOfLivesHealWatcher>();
        watcher.Controller = this;
        watcher.Sheet = sheet;
    }

    /// <summary>While the pause out-of-lives sheet is open: the moment lives heal, swap back
    /// to the pause sheet (where Restart works). Self-disarms when the sheet it was born on
    /// is no longer the live one. Unscaled time - the pause menu lives at timeScale 0.</summary>
    private sealed class OutOfLivesHealWatcher : MonoBehaviour
    {
        public PauseMenuController Controller;
        public GameObject Sheet;
        private float _next;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            if (Controller == null || Controller._menuCanvas != Sheet)
            {
                enabled = false;
                return;
            }
            if (!RunLivesUi.OutOfLives) Controller.BuildMenu();
        }
    }

    private void BuildConfirm(string question, UnityEngine.Events.UnityAction onYes)
    {
        DestroyMenu();
        _menuCanvas = RuntimeUiKit.CreateOverlayCanvas("Pause Confirm", 7000);
        CreateShroud(_menuCanvas.transform);

        GameObject panel = RuntimeUiKit.CreateCenteredPanel(_menuCanvas.transform, new Vector2(560f, 500f));
        GameMenuStyle.StylePanel(panel);
        RuntimeUiKit.CreateLabel(panel.transform, "Are you sure?", 46, 70f, FontStyle.Bold, GameMenuStyle.Accent);
        RuntimeUiKit.CreateLabel(panel.transform, question, 28, 92f, FontStyle.Normal, GameMenuStyle.BodyText);
        GameMenuStyle.StyleButton(RuntimeUiKit.CreateButton(panel.transform, "Yes", 88f, onYes), primary: false);
        GameMenuStyle.StyleButton(RuntimeUiKit.CreateButton(panel.transform, "No, keep playing", 88f, BuildMenu), primary: true);
        UiEntranceFx.Play(panel, 0.02f);
    }

    private void Resume()
    {
        SfxPlayer.Play("ui-resume", 0.9f);
        DestroyMenu();
        ReleaseBlur();
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PopPause(this);
            GameManager.Instance.ReleasePhase(this);
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PopPause(this);
            GameManager.Instance.ReleasePhase(this);
        }
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
        // YES is the point of no return: the abandon report below consumes the server run
        // synchronously, but the fresh start_run grant is an async round trip - so the
        // confirm sheet must die FIRST, or a "No, keep playing" tapped during the wait
        // would resume a run whose finish the server already accepted (review 2026-08-01).
        // The pending panel is deliberately button-less; the grant's landing decides what
        // happens next (reload, or back to the menu on a denial - same as Try Again).
        BuildPending("Restarting...");
        // Abandoning still reports the run (bests, server finish, XP - XP.md). The retry is
        // then a NEW run and must win its own start_run grant, so route through RestartGame
        // like the game-over Try Again - a bare scene reload here would orphan the retry
        // from the server (the abandon consumed the run_id: no score/refund/XP at its end).
        if (LevelRuntimeController.Active != null) LevelRuntimeController.Active.ReportAbandonedRun();
        if (GameManager.Instance != null)
        {
            GameManager.Instance.RestartGame();
            return;
        }
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void BuildPending(string message)
    {
        DestroyMenu();
        _menuCanvas = RuntimeUiKit.CreateOverlayCanvas("Pause Pending", 7000);
        CreateShroud(_menuCanvas.transform);
        GameObject panel = RuntimeUiKit.CreateCenteredPanel(_menuCanvas.transform, new Vector2(560f, 200f));
        GameMenuStyle.StylePanel(panel);
        RuntimeUiKit.CreateLabel(panel.transform, message, 36, 96f, FontStyle.Bold, GameMenuStyle.Accent);
    }

    private void ReturnToMenu()
    {
        SfxPlayer.Play("ui-leave-game");
        if (LevelRuntimeController.Active != null) LevelRuntimeController.Active.ReportAbandonedRun();
        MainMenuRuntime.ReturnToMenu();
    }

    private void DestroyMenu()
    {
        if (_menuCanvas != null) Destroy(_menuCanvas);
        _menuCanvas = null;
    }
}
