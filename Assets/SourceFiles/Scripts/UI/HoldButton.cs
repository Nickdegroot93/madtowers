using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Pocket Cache HUD: a circular "bubble" button on the left, just above mid-height. Hidden
/// until the ability unlocks the <see cref="HoldCache"/>, then it shows the cached shape as a
/// WHITE ghost that idles with a very slight wave. Tapping banks/swaps via the cache.
///
/// Two bits of juice, both driven from the cache's events:
///   - BANK  (cache was empty): a white ghost flies from the piece's field position into the
///            bubble, then pops in - the "the block went into the pocket" read.
///   - SWAP  (cache was full): the bubble content snaps instantly to the new shape with a pop
///            (the lifted in-place respawn is the cache's job, not the button's).
///
/// Built in code like the rest of the runtime UI; registers a gesture-exclusion rect so taps
/// never steer the piece, and dims when a hold isn't currently allowed (lockout / paused / etc.).
/// </summary>
public class HoldButton : MonoBehaviour
{
    private const float Size = 132f;          // button diameter in the 1080x1920 reference space
    private const float LeftMargin = 92f;     // centre distance from the left edge
    private const float HeightAnchor = 0.56f; // a touch above mid-screen
    private const float IconInset = 30f;      // padding from the bubble to the held ghost
    private const float DimAlpha = 0.34f;
    private const float WaveAmplitude = 2.5f; // px - "alive, not distracting"
    private const float WaveSpeed = 2.1f;
    private const float FlyTime = 0.32f;
    private const float PunchSeconds = 0.45f;

    // Neutral white overlay (matches the top bar's TitleColor) so the button reads cleanly on
    // any level chapter rather than tinting it blue.
    private static readonly Color BubbleColor = new Color(0.92f, 0.97f, 1f, 0.9f);

    private HoldCache _cache;
    private GameObject _root;
    private Canvas _canvas;
    private GameObject _button;
    private RectTransform _buttonRect;
    private Image _bubble;
    private Image _heldIcon;
    private CanvasGroup _group;

    private bool _shown;
    private bool _shownUsable;
    private float _punchAge = -1f;
    private float _waveTime;
    private bool _flying;
    private BlockDefinition _flyDef;

    private readonly Dictionary<string, Sprite> _whiteGhosts = new Dictionary<string, Sprite>();
    private Func<Rect> _exclusionRect;

    private void Start()
    {
        _cache = GetComponent<HoldCache>();
        if (_cache == null) return;

        BuildButton();
        _cache.EnabledChanged += Reveal;
        _cache.HeldChanged += OnHeldChanged;
        _cache.Banked += OnBanked;

        if (_cache.IsEnabled) Reveal(); // defensive: ability acquired before this built
    }

    private void OnDestroy()
    {
        if (_cache != null)
        {
            _cache.EnabledChanged -= Reveal;
            _cache.HeldChanged -= OnHeldChanged;
            _cache.Banked -= OnBanked;
        }
        if (_exclusionRect != null) TouchGestureInput.UnregisterUiExclusionRect(_exclusionRect);

        // Generated silhouettes are HideAndDontSave (survive scene unload), so free them here or
        // they leak one texture per run - mirrors UIManager's ghost teardown. Never destroy the
        // source piece sprite (the cache stores it directly when the texture wasn't readable):
        // only our generated textures carry the HideAndDontSave flag.
        foreach (Sprite ghost in _whiteGhosts.Values)
        {
            if (ghost == null || ghost.texture == null) continue;
            if (!ghost.texture.hideFlags.HasFlag(HideFlags.HideAndDontSave)) continue;
            Destroy(ghost.texture);
            Destroy(ghost);
        }
        _whiteGhosts.Clear();
    }

    private void BuildButton()
    {
        _root = RuntimeUiKit.CreateOverlayCanvas("Hold Button", 2480);
        _canvas = _root.GetComponent<Canvas>();

        _button = new GameObject("HoldBubble", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _buttonRect = (RectTransform)_button.transform;
        _buttonRect.SetParent(_root.transform, false);
        _buttonRect.anchorMin = new Vector2(0f, HeightAnchor);
        _buttonRect.anchorMax = new Vector2(0f, HeightAnchor);
        _buttonRect.pivot = new Vector2(0.5f, 0.5f);
        _buttonRect.anchoredPosition = new Vector2(LeftMargin, 0f);
        _buttonRect.sizeDelta = new Vector2(Size, Size);

        _bubble = _button.GetComponent<Image>();
        _bubble.sprite = RuntimeSprites.Bubble();
        _bubble.color = BubbleColor;

        Button button = _button.AddComponent<Button>();
        button.targetGraphic = _bubble;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => { if (_cache.TryHold()) Punch(); });

        GameObject icon = new GameObject("Held", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = (RectTransform)icon.transform;
        iconRect.SetParent(_button.transform, false);
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(IconInset, IconInset);
        iconRect.offsetMax = new Vector2(-IconInset, -IconInset);
        _heldIcon = icon.GetComponent<Image>();
        _heldIcon.preserveAspect = true;
        _heldIcon.raycastTarget = false;
        _heldIcon.enabled = false;

        _group = _button.AddComponent<CanvasGroup>();
        _button.SetActive(false); // hidden until the ability unlocks the cache
    }

    private void Reveal()
    {
        if (_shown || _button == null) return;
        _shown = true;
        _button.SetActive(true);
        Punch(); // pop in
        SfxPlayer.Play("swoosh_01", 0.55f, 0.05f);

        // Sync the dim state up front so the button doesn't show a full-alpha, interactable frame
        // before the first Update if a hold isn't currently allowed (a tap there would dead-no-op).
        _shownUsable = _cache.CanHold;
        _group.alpha = _shownUsable ? 1f : DimAlpha;
        _group.interactable = _shownUsable;

        _exclusionRect = GetButtonScreenRect;
        TouchGestureInput.RegisterUiExclusionRect(_exclusionRect);
    }

    // Swap (instant snap) or the empty-state reset. A bank also raises this, but the flyer owns
    // the reveal in that case, so we let it set the icon on arrival.
    private void OnHeldChanged(BlockDefinition def)
    {
        if (_flying && def == _flyDef) return;
        SetHeldIcon(def);
        Punch();
    }

    private void OnBanked(Vector3 worldPos, BlockDefinition shape)
    {
        if (!_shown) return;

        // Resolve the live camera per gesture (don't cache - it can be destroyed/recreated on a
        // level reload). No camera -> can't map world to screen, so just snap the icon in.
        Camera cam = Camera.main;
        if (cam == null) { SetHeldIcon(shape); Punch(); return; }

        StartCoroutine(FlyIntoBubble(worldPos, shape, cam));
    }

    // A white ghost of the banked shape arcs from its field position into the bubble, shrinking
    // as it goes, then pops in. Overlay-canvas RectTransform.position is in screen pixels, so we
    // can tween screen-space directly. Unscaled time keeps it smooth regardless of hit-stop.
    private IEnumerator FlyIntoBubble(Vector3 worldPos, BlockDefinition shape, Camera cam)
    {
        _flying = true;
        _flyDef = shape;

        GameObject flyer = new GameObject("HoldFlyer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)flyer.transform;
        rect.SetParent(_root.transform, false);
        rect.sizeDelta = new Vector2(Size - IconInset * 2f, Size - IconInset * 2f);
        Image img = flyer.GetComponent<Image>();
        img.sprite = WhiteGhost(shape);
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.enabled = img.sprite != null;

        Vector3 start = cam.WorldToScreenPoint(worldPos);
        start.z = 0f;
        Vector3 end = _buttonRect.position; // bubble centre, screen px

        for (float t = 0f; t < FlyTime; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / FlyTime);
            float eased = 1f - (1f - u) * (1f - u); // ease-out: quick launch, gentle arrival
            rect.position = Vector3.Lerp(start, end, eased);
            float scale = Mathf.Lerp(2.1f, 1f, eased); // starts field-sized, shrinks into the bubble
            rect.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }

        Destroy(flyer);
        _flying = false;
        SetHeldIcon(shape); // bubble now owns the ghost
        Punch();
    }

    private void SetHeldIcon(BlockDefinition def)
    {
        Sprite ghost = WhiteGhost(def);
        _heldIcon.sprite = ghost;
        _heldIcon.enabled = ghost != null;
        _heldIcon.rectTransform.anchoredPosition = Vector2.zero; // wave resets to centre
    }

    private void Punch() => _punchAge = 0f;

    private void Update()
    {
        if (!_shown || _buttonRect == null) return;

        // Elastic tap/arrival punch (unscaled so UI feel never freezes on hit-stop).
        if (_punchAge >= 0f)
        {
            _punchAge += Time.unscaledDeltaTime;
            if (_punchAge >= PunchSeconds)
            {
                _punchAge = -1f;
                _buttonRect.localScale = Vector3.one;
            }
            else
            {
                float s = FxKit.Elastic(_punchAge, amplitude: 0.26f, damping: 6f, frequency: 18f);
                _buttonRect.localScale = new Vector3(s, s, 1f);
            }
        }

        // Very slight idle wave on the held ghost - alive, not distracting. Skip while paused:
        // Update runs on unscaled time, so without this the canvas would keep rebuilding (and the
        // icon visibly drift) on the pause screen where nothing should move.
        bool paused = GameManager.Instance != null && GameManager.Instance.IsGamePaused;
        if (_heldIcon != null && _heldIcon.enabled && !_flying && !paused)
        {
            _waveTime += Time.unscaledDeltaTime;
            _heldIcon.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Sin(_waveTime * WaveSpeed) * WaveAmplitude);
        }

        // Dim when a hold isn't allowed right now (lockout used, paused, win-verify, no live piece).
        bool usable = _cache.CanHold;
        if (usable != _shownUsable)
        {
            _shownUsable = usable;
            _group.alpha = usable ? 1f : DimAlpha;
            _group.interactable = usable;
        }
    }

    private const float GhostBrightness = 0.6f; // <1 pushes faces toward white; the dips (cell seams) stay as lines

    // White rendering of the shape that KEEPS the cell seams: luminance normalised by the
    // piece's brightest pixel (hue-independent across chapters), then gamma-lifted so the cell
    // faces read white while the embossed seam/outline dips survive as soft lines. The block's
    // colour is dropped entirely - "displayed in white, not the block's colour". Cached per shape+skin.
    private Sprite WhiteGhost(BlockDefinition def)
    {
        if (def == null) return null;
        string shape = ChapterSkins.ExtractShapeToken(def.DisplayName);
        if (string.IsNullOrEmpty(shape)) return null;

        string key = $"{ChapterSkins.Folder}:{shape}";
        if (_whiteGhosts.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        Sprite source = ChapterSkins.LoadPiece(shape);
        Sprite ghost = source;
        if (source != null && source.texture.isReadable)
        {
            Texture2D src = source.texture;
            Texture2D tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Color[] pixels = src.GetPixels();

            float maxLum = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 0.05f) continue;
                float l = pixels[i].r * 0.299f + pixels[i].g * 0.587f + pixels[i].b * 0.114f;
                if (l > maxLum) maxLum = l;
            }
            if (maxLum < 0.001f) maxLum = 1f;

            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                float rel = Mathf.Clamp01((c.r * 0.299f + c.g * 0.587f + c.b * 0.114f) / maxLum);
                float v = Mathf.Pow(rel, GhostBrightness);
                pixels[i] = new Color(v, v, v, c.a);
            }
            tex.SetPixels(pixels);
            tex.Apply();

            ghost = Sprite.Create(tex, new Rect(0, 0, src.width, src.height), new Vector2(0.5f, 0.5f), 256f);
            ghost.hideFlags = HideFlags.HideAndDontSave;
        }

        _whiteGhosts[key] = ghost;
        return ghost;
    }

    // Raw screen-pixel rect over the bubble so taps never also steer the piece. Recomputed per
    // query (survives resolution changes); RectTransform.position is already screen px on overlay.
    private Rect GetButtonScreenRect()
    {
        if (_buttonRect == null) return Rect.zero;
        float scale = _canvas != null ? _canvas.scaleFactor : 1f;
        float side = Size * scale;
        Vector3 center = _buttonRect.position;
        return new Rect(center.x - side * 0.5f, center.y - side * 0.5f, side, side);
    }
}
