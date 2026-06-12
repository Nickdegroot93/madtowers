using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The legendary cards' shine: a tilted soft light band that sweeps across the card
/// every few seconds. Added to a card root that has a RectMask2D (the band is wider
/// than the card and must clip at its edges). Unscaled time - the picker is open while
/// the game is paused.
/// </summary>
public class AbilityCardShine : MonoBehaviour
{
    private const float SweepSeconds = 1.1f;
    private const float PauseSeconds = 2.4f;
    private const float BandWidth = 70f;
    private const float TiltDegrees = 18f;
    private static readonly Color BandColor = new Color(1f, 0.95f, 0.75f, 0.28f);

    private RectTransform _band;
    private RectTransform _card;
    private float _cycle;

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
        image.color = BandColor;

        // The band must be tall enough to cross the tilted card corner to corner; layout
        // groups must never try to place it.
        LayoutElement layout = bandObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
    }

    private void Update()
    {
        if (_band == null) return;

        _cycle += Time.unscaledDeltaTime;
        float total = SweepSeconds + PauseSeconds;
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
