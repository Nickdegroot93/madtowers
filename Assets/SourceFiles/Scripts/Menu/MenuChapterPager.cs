using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the chapter-to-chapter transition on the main menu. A swipe (or the Next Chapter
/// card) slides the current chapter's content root and its background as one motion, while
/// the incoming chapter - rendered into an off-screen neighbour panel and its background
/// parented into the same background track - slides in from the opposite side. The screen
/// chrome (top bar, bottom nav, dimming overlay) stays put, so the chapter scene reads as a
/// single page sliding underneath it. On release the gesture either completes the page turn
/// or springs back - decided by fling velocity first (a flick commits or cancels from anywhere),
/// distance travelled otherwise - and the settle spring inherits the finger's release velocity.
///
/// Note on parallax: with a distinct full-screen background per chapter a "slower" background
/// layer cannot be done without the incoming image either peeking early or snapping at the
/// end, so foreground and background travel together 1:1 - one smooth motion.
///
/// MainMenuRuntime owns the menu; this component only animates and reports back through the
/// delegates handed to <see cref="Configure"/> (re-supplied on every rebuild so references
/// never go stale). It lives on the menu canvas so it can run the settle coroutines.
/// </summary>
public class MenuChapterPager : MonoBehaviour
{
    private const float CommitFraction = 0.22f;   // travel past this fraction of a screen = commit
    private const float ResistFactor = 0.18f;     // rubber-band stiffness when there is nowhere to go
    private const float Deadzone = 4f;            // pixels of slack before a direction is chosen

    // Settle physics: a critically damped spring (SmoothDamp) seeded with the finger's release
    // velocity, so the page keeps the exact speed it had when the finger lifted - no visible
    // velocity step at release, and a hard flick lands faster than a gentle push.
    private const float FlickVelocity = 700f;      // canvas units/s that counts as a deliberate flick
    private const float CommitSmoothTime = 0.09f;
    private const float CancelSmoothTime = 0.11f;
    private const float MaxSettleSeconds = 0.6f;   // failsafe so a bad frame can never strand the page
    private const float MaxCarryVelocity = 9000f;
    private const float VelocitySmoothing = 0.05f; // exp. time constant for the drag-velocity estimate
    // A frame hitch (page build, GC) must slow the animation down, not fast-forward it: clamp the
    // per-step dt so one long frame advances the spring by at most a 30fps step.
    private const float MaxStepSeconds = 1f / 30f;

    // Context, refreshed by Configure on every BuildMenu.
    private RectTransform _content;
    private RectTransform _bgTrack;
    private RectTransform _contentParent;
    private int _chapterCount;
    private Func<int, int> _resolveTarget;
    private Action<RectTransform, int> _buildContent;
    private Func<int, RectTransform> _buildBackground;
    private Action<int> _commit;
    private Action<int, float> _blend;
    private bool _configured;
    private float _scaleFactor = 1f;

    // Live gesture state.
    private bool _busy;          // a settle coroutine owns the layers right now
    private bool _panning;
    private bool _hasNeighbor;
    private int _chapterDelta;   // +1 = next (enters from right), -1 = previous (from left)
    private int _targetIndex;
    private float _width;        // screen width in canvas units
    private RectTransform _neighbor;
    private RectTransform _neighborBg;

    // Drag-velocity estimate (canvas units/s, sign = finger direction), smoothed over the last
    // ~50ms of pointer samples so one noisy event can't decide a flick.
    private float _velocity;
    private float _lastDx;
    private float _lastSampleTime;

    public bool Busy => _busy;

    public void Configure(RectTransform content, RectTransform bgTrack, RectTransform contentParent,
        int chapterCount,
        Func<int, int> resolveTarget, Action<RectTransform, int> buildContent,
        Func<int, RectTransform> buildBackground, Action<int> commit,
        Action<int, float> blend = null)
    {
        _content = content;
        _bgTrack = bgTrack;
        _contentParent = contentParent;
        _chapterCount = chapterCount;
        _resolveTarget = resolveTarget;
        _buildContent = buildContent;
        _buildBackground = buildBackground;
        _commit = commit;
        _blend = blend;
        _configured = true;
        RefreshMetrics();
    }

    private void RefreshMetrics()
    {
        if (_contentParent != null) _width = Mathf.Max(1f, _contentParent.rect.width);
        Canvas canvas = _contentParent != null ? _contentParent.GetComponentInParent<Canvas>() : null;
        _scaleFactor = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
    }

    public void BeginPan()
    {
        if (!_configured || _busy) return;
        _panning = true;
        _hasNeighbor = false;
        _chapterDelta = 0;
        _velocity = 0f;
        _lastDx = 0f;
        _lastSampleTime = Time.unscaledTime;
        RefreshMetrics();
    }

    public void PanMove(float pixelDx)
    {
        if (!_panning || _busy) return;

        float dx = pixelDx / _scaleFactor;
        SampleVelocity(dx);
        int desiredDelta = dx < -Deadzone ? 1 : (dx > Deadzone ? -1 : 0);

        if (!_hasNeighbor)
        {
            if (desiredDelta == 0) { ApplyPan(0f); return; }
            int target = _resolveTarget(desiredDelta);
            if (target < 0) { ApplyResist(dx); return; }   // nowhere to go: rubber-band
            BuildNeighbor(desiredDelta, target);
        }
        else if (desiredDelta != 0 && desiredDelta != _chapterDelta && NearZero())
        {
            // Reversed back across the origin: drop this neighbour, build the other side.
            TearDownNeighbor(true);
            int target = _resolveTarget(desiredDelta);
            if (target < 0) { ApplyResist(dx); return; }
            BuildNeighbor(desiredDelta, target);
        }

        float commitTarget = -_chapterDelta * _width;
        float t = Mathf.Clamp(dx, Mathf.Min(0f, commitTarget), Mathf.Max(0f, commitTarget));
        ApplyPan(t);
    }

    public void EndPan(float pixelDx)
    {
        if (!_panning) return;
        _panning = false;
        if (_busy) return;

        if (!_hasNeighbor)
        {
            StartCoroutine(SpringBackResist());
            return;
        }

        float dx = pixelDx / _scaleFactor;
        // A finger that paused before lifting has no fling, whatever it did earlier in the
        // drag: the release sample spans the whole pause, so its near-zero instantaneous
        // velocity blends the estimate down hard (long dt = heavy blend weight).
        SampleVelocity(dx);

        float commitTarget = -_chapterDelta * _width;
        float t = Mathf.Clamp(dx, Mathf.Min(0f, commitTarget), Mathf.Max(0f, commitTarget));

        // Flick beats distance: a deliberate fling toward the neighbour commits from anywhere,
        // a fling back cancels even past the distance threshold; otherwise distance decides.
        float towardTarget = Mathf.Sign(commitTarget) * _velocity;
        bool commit;
        if (towardTarget > FlickVelocity) commit = true;
        else if (towardTarget < -FlickVelocity) commit = false;
        else commit = Mathf.Abs(t) / _width >= CommitFraction;

        StartCoroutine(Settle(commit ? commitTarget : 0f, commit));
    }

    private void SampleVelocity(float dx)
    {
        float now = Time.unscaledTime;
        float dt = now - _lastSampleTime;
        if (dt <= 0.0001f) return;

        float instantaneous = (dx - _lastDx) / dt;
        float blend = 1f - Mathf.Exp(-dt / VelocitySmoothing);
        _velocity = Mathf.Lerp(_velocity, instantaneous, blend);
        _lastDx = dx;
        _lastSampleTime = now;
    }

    /// <summary>Programmatic transition (the Next Chapter card). Slides from rest to the
    /// target chapter, entering from the side implied by <paramref name="chapterDelta"/>.</summary>
    public void AnimateToChapter(int targetIndex, int chapterDelta)
    {
        if (!_configured || _busy || _panning) return;
        if (targetIndex < 0 || targetIndex >= _chapterCount) return;
        RefreshMetrics();
        BuildNeighbor(chapterDelta, targetIndex);
        ApplyPan(0f);
        // Seed the spring as if a flick had just released: the page leaves immediately and
        // confidently instead of creeping out of the gate, then eases into place.
        _velocity = -chapterDelta * _width / 0.30f;
        StartCoroutine(Settle(-chapterDelta * _width, true));
    }

    private void BuildNeighbor(int chapterDelta, int targetIndex)
    {
        _chapterDelta = chapterDelta;
        _targetIndex = targetIndex;

        RectTransform panel = new GameObject("NeighborChapterContent", typeof(RectTransform))
            .GetComponent<RectTransform>();
        panel.SetParent(_contentParent, false);
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.one;
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;

        CanvasGroup cg = panel.gameObject.AddComponent<CanvasGroup>();
        cg.interactable = false;
        cg.blocksRaycasts = false;            // never steals taps from the live screen
        _buildContent(panel, targetIndex);
        _neighbor = panel;

        // The neighbour background is a child of the background track; it sits at its full
        // off-screen offset and rides the track when the track pans (so it moves 1:1 with the
        // foreground neighbour - no separate parallax math, no early peek).
        _neighborBg = _buildBackground(targetIndex);
        if (_neighborBg != null) SetX(_neighborBg, chapterDelta * _width);

        _hasNeighbor = true;
        ApplyPan(0f);
    }

    private void TearDownNeighbor(bool resetLayers)
    {
        if (_hasNeighbor) _blend?.Invoke(_targetIndex, 0f);
        if (_neighbor != null) Destroy(_neighbor.gameObject);
        if (_neighborBg != null) Destroy(_neighborBg.gameObject);
        _neighbor = null;
        _neighborBg = null;
        _hasNeighbor = false;
        _chapterDelta = 0;
        if (resetLayers)
        {
            SetX(_content, 0f);
            SetX(_bgTrack, 0f);
        }
    }

    private bool NearZero()
    {
        float x = _content != null ? _content.anchoredPosition.x : 0f;
        return Mathf.Abs(x) < _width * 0.04f;
    }

    // Slides the foreground content and the background track by the same offset, so the whole
    // chapter scene moves as one. The neighbour content rides alongside; the neighbour
    // background rides the track as a child. Reports the travelled fraction so the fixed
    // chrome (top bar, bottom nav) can cross-fade its chapter tint in step with the page.
    private void ApplyPan(float t)
    {
        SetX(_content, t);
        if (_neighbor != null) SetX(_neighbor, _chapterDelta * _width + t);
        SetX(_bgTrack, t);
        if (_hasNeighbor) _blend?.Invoke(_targetIndex, Mathf.Clamp01(Mathf.Abs(t) / _width));
    }

    private void ApplyResist(float dx)
    {
        float x = dx * ResistFactor;
        SetX(_content, x);
        SetX(_bgTrack, x);
    }

    private static void SetX(RectTransform rt, float x)
    {
        if (rt == null) return;
        Vector2 p = rt.anchoredPosition;
        p.x = x;
        rt.anchoredPosition = p;
    }

    private IEnumerator SpringBackResist()
    {
        _busy = true;
        float x = _content != null ? _content.anchoredPosition.x : 0f;
        // The rubber-band moved at ResistFactor of the finger, so it springs back from the
        // same fraction of the release velocity.
        float vel = Mathf.Clamp(_velocity * ResistFactor, -MaxCarryVelocity, MaxCarryVelocity);
        float elapsed = 0f;
        while (elapsed < MaxSettleSeconds)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStepSeconds);
            elapsed += dt;
            x = Mathf.SmoothDamp(x, 0f, ref vel, CancelSmoothTime, Mathf.Infinity, dt);
            if (Mathf.Abs(x) < 0.5f) break;
            SetX(_content, x);
            SetX(_bgTrack, x);
            yield return null;
        }
        SetX(_content, 0f);
        SetX(_bgTrack, 0f);
        _busy = false;
    }

    private IEnumerator Settle(float targetT, bool commit)
    {
        _busy = true;
        if (commit) SfxPlayer.Play("ui-page-swipe", 0.8f, 0.05f);

        float x = _content != null ? _content.anchoredPosition.x : 0f;
        float vel = Mathf.Clamp(_velocity, -MaxCarryVelocity, MaxCarryVelocity);
        float smoothTime = commit ? CommitSmoothTime : CancelSmoothTime;
        float elapsed = 0f;
        while (elapsed < MaxSettleSeconds)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStepSeconds);
            elapsed += dt;
            x = Mathf.SmoothDamp(x, targetT, ref vel, smoothTime, Mathf.Infinity, dt);
            if (Mathf.Abs(targetT - x) < 0.5f) break;
            ApplyPan(x);
            yield return null;
        }
        ApplyPan(targetT);

        if (!commit)
        {
            TearDownNeighbor(true);
            _busy = false;
            yield break;
        }

        // The neighbour is now centred. Drop the transient panels and rebuild the settled
        // state at the new chapter; the rebuilt root/background appear at the same place the
        // neighbour occupied, so the hand-off is seamless. (Destroy is deferred to frame end,
        // which is fine - the rebuilt content draws over the dying neighbour for one frame.)
        int target = _targetIndex;
        if (_neighbor != null) Destroy(_neighbor.gameObject);
        _neighbor = null;
        _neighborBg = null;                 // cleared by the background rebuild
        _hasNeighbor = false;
        _busy = false;
        _commit?.Invoke(target);
    }
}
