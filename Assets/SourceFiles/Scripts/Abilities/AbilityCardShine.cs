using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The higher-rarity cards' shine: a tilted soft light band that sweeps across the card
/// every few seconds. <see cref="Attach"/> builds its own rounded clip child (the band is
/// wider than the card and must clip to the card's corner radius), so the card's own glows
/// that intentionally bleed past the edge stay unclipped. Unscaled time - the picker is
/// open while the game is paused.
/// </summary>
public class AbilityCardShine : MonoBehaviour
{
    private const float SweepSeconds = 1.1f;
    private const float BandWidth = 70f;
    private const float TiltDegrees = 18f;

    private Color _bandColor = new Color(1f, 0.95f, 0.75f, 0.28f);
    private float _pauseSeconds = 2.4f;

    private RectTransform _band;
    private RectTransform _card;
    private float _cycle;

    /// <summary>Add a shine sweep to a neon card. `bandColor` carries the sweep's tint and
    /// strength (alpha); `pauseSeconds` is the rest between sweeps. The clip stencil is the
    /// card body sprite itself (same padded canvas + corner radius), so the sweep hugs the
    /// exact card silhouette.</summary>
    public static void Attach(Transform cardRoot, Color bandColor, float pauseSeconds)
    {
        GameObject clip = new GameObject("ShineClip", typeof(RectTransform));
        clip.transform.SetParent(cardRoot, false);
        RectTransform clipRect = (RectTransform)clip.transform;
        clipRect.anchorMin = Vector2.zero;
        clipRect.anchorMax = Vector2.one;
        float pad = RuntimeSprites.CardSpritePad;
        clipRect.offsetMin = new Vector2(-pad, -pad);
        clipRect.offsetMax = new Vector2(pad, pad);
        clipRect.pivot = new Vector2(0.5f, 0.5f);

        Image stencil = clip.AddComponent<Image>();
        stencil.sprite = RuntimeSprites.CardGradient(Color.white, Color.white);
        stencil.type = Image.Type.Sliced;
        stencil.raycastTarget = false;
        Mask mask = clip.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        AbilityCardShine shine = clip.AddComponent<AbilityCardShine>();
        shine._bandColor = bandColor;
        shine._pauseSeconds = pauseSeconds;
    }

    private void Start()
    {
        _card = (RectTransform)transform;

        GameObject bandObject = new GameObject("Shine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _band = (RectTransform)bandObject.transform;
        _band.SetParent(_card, false);
        _band.anchorMin = new Vector2(0f, 0.5f);
        _band.anchorMax = new Vector2(0f, 0.5f);
        _band.pivot = new Vector2(0.5f, 0.5f);
        _band.localEulerAngles = new Vector3(0f, 0f, TiltDegrees);

        Image image = bandObject.GetComponent<Image>();
        image.sprite = RuntimeSprites.SoftHorizontalBar(0.1f);
        image.raycastTarget = false;
        image.color = _bandColor;

        // The band must be tall enough to cross the tilted card corner to corner; layout
        // groups must never try to place it.
        LayoutElement layout = bandObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
    }

    private void Update()
    {
        if (_band == null) return;

        _cycle += Time.unscaledDeltaTime;
        float total = SweepSeconds + _pauseSeconds;
        if (_cycle > total) _cycle -= total;

        float t = Mathf.Clamp01(_cycle / SweepSeconds);
        float height = _card.rect.height;
        _band.sizeDelta = new Vector2(BandWidth, height * 1.6f);

        // Sweep left edge -> right edge (anchored to the card's left side).
        float x = Mathf.Lerp(-BandWidth, _card.rect.width + BandWidth, t);
        _band.anchoredPosition = new Vector2(x, 0f);

        // Invisible during the pause between sweeps.
        _band.gameObject.SetActive(t < 1f || _cycle <= SweepSeconds);
    }
}

/// <summary>
/// A slow breathing pulse on an Image's alpha (the legendary card halo). Unscaled time,
/// same reason as the shine.
/// </summary>
public sealed class UiGlowPulse : MonoBehaviour
{
    private Image _image;
    private float _baseAlpha;
    private float _age;

    private void Awake()
    {
        _image = GetComponent<Image>();
        if (_image != null) _baseAlpha = _image.color.a;
    }

    private void Update()
    {
        if (_image == null) return;
        _age += Time.unscaledDeltaTime;
        Color color = _image.color;
        color.a = _baseAlpha * (0.75f + 0.25f * Mathf.Sin(_age * 2.4f));
        _image.color = color;
    }
}
