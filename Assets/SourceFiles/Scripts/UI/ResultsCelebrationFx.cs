using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tier-celebration burst behind the results card (MEDALS.md §9): a slowly rotating ray
/// fan plus a one-shot confetti burst of tumbling paper pieces - all plain UI Images on the
/// card's own overlay canvas, deliberately NOT a world-space ParticleSystem: Shuriken cannot
/// render over a Screen Space - Overlay canvas at all, and it would freeze at the
/// timeScale-0 pause the victory card opens under. Everything runs on UNSCALED time (the
/// CoinHud flight precedent). Attach to a full-screen layer sibling-ordered BEHIND the card
/// panel; the burst origin follows the badge so it stays glued through layout settling.
/// </summary>
public sealed class ResultsCelebrationFx : MonoBehaviour
{
    private const int ConfettiCount = 40;
    private const float RayDegreesPerSecond = 22f;   // handoff §4
    private const float RaySize = 460f;
    private const float RayFadeInSeconds = 0.5f;
    private const float RayAlpha = 0.20f;
    private const float Gravity = 520f;              // canvas px/s² - floaty fall, not a drop
    private const float PieceFadeTail = 0.30f;       // fade over the last 30% of a piece's life

    private struct Piece
    {
        public RectTransform Rect;
        public Image Image;
        public Vector2 Velocity;
        public float Spin;      // deg/s, ±180 - the paper tumble
        public float Life;
        public float Age;
    }

    private RectTransform _follow;
    private MedalTier _tier;
    private float _startDelay;
    private Image _rays;
    private float _rayAge;
    private bool _burstFired;
    private readonly List<Piece> _pieces = new List<Piece>(ConfettiCount);

    /// <summary>Build the fx on <paramref name="layer"/> (a full-screen rect BEHIND the card
    /// panel), bursting from <paramref name="follow"/> (the badge) after
    /// <paramref name="startDelay"/> unscaled seconds - the badge pop-in moment.</summary>
    public static ResultsCelebrationFx Attach(RectTransform layer, RectTransform follow,
        MedalTier tier, float startDelay)
    {
        ResultsCelebrationFx fx = layer.gameObject.AddComponent<ResultsCelebrationFx>();
        fx._follow = follow;
        fx._tier = tier;
        fx._startDelay = startDelay;

        GameObject rays = new GameObject("Rays", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)rays.transform;
        rect.SetParent(layer, false);
        rect.sizeDelta = new Vector2(RaySize, RaySize);
        fx._rays = rays.GetComponent<Image>();
        fx._rays.sprite = MedalStyle.RayBurstSprite();
        fx._rays.raycastTarget = false;
        Color tint = MedalStyle.TierColor(tier);
        tint.a = 0f;
        fx._rays.color = tint;
        return fx;
    }

    /// <summary>Fire now: the card's tap-to-skip fast-forward jumps every element to rest, and
    /// a burst still waiting on its start delay would erupt over the finished card.</summary>
    public void SkipDelay() => _startDelay = 0f;

    private void Update()
    {
        if (_startDelay > 0f)
        {
            _startDelay -= Time.unscaledDeltaTime;
            if (_startDelay > 0f) return;
        }

        // Clamped so a hitch (app refocus) can't teleport pieces off-screen in one step.
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        if (_rays != null)
        {
            // Track the badge only until the burst (the card's ContentSizeFitter settles in
            // the first frames; after that the badge only ever scales, never moves) - a
            // world->local conversion per frame forever bought nothing.
            if (!_burstFired && _follow != null) _rays.rectTransform.position = _follow.position;
            _rayAge += dt;
            Color tint = _rays.color;
            tint.a = RayAlpha * Mathf.Clamp01(_rayAge / RayFadeInSeconds);
            _rays.color = tint;
            _rays.rectTransform.Rotate(Vector3.forward, RayDegreesPerSecond * dt);
        }

        if (!_burstFired)
        {
            _burstFired = true;
            SpawnBurst();
        }

        TickPieces(dt);
    }

    private void SpawnBurst()
    {
        MedalStyle.ConfettiColors(_tier, out Color a, out Color b);
        Vector3 origin = _follow != null ? _follow.position : transform.position;

        for (int i = 0; i < ConfettiCount; i++)
        {
            GameObject piece = new GameObject("Confetti", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = (RectTransform)piece.transform;
            rect.SetParent(transform, false);
            rect.position = origin;
            rect.sizeDelta = new Vector2(Random.Range(10f, 20f), Random.Range(13f, 25f));
            rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            Image image = piece.GetComponent<Image>();
            image.raycastTarget = false; // no sprite: a plain tinted quad reads as paper
            Color color = Color.Lerp(a, b, Random.value);
            image.color = Color.Lerp(color, Color.white, Random.Range(0f, 0.25f)); // value jitter

            // A wide upward fan (the radial "explosion" before gravity takes over).
            float angle = (90f + Random.Range(-70f, 70f)) * Mathf.Deg2Rad;
            float speed = Random.Range(280f, 680f);
            _pieces.Add(new Piece
            {
                Rect = rect,
                Image = image,
                Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed,
                Spin = Random.Range(-180f, 180f),
                Life = Random.Range(2f, 3f),
            });
        }
    }

    private void TickPieces(float dt)
    {
        for (int i = _pieces.Count - 1; i >= 0; i--)
        {
            Piece piece = _pieces[i];
            piece.Age += dt;
            if (piece.Age >= piece.Life || piece.Rect == null)
            {
                if (piece.Rect != null) Destroy(piece.Rect.gameObject);
                _pieces.RemoveAt(i);
                continue;
            }

            piece.Velocity.y -= Gravity * dt;
            piece.Rect.localPosition += (Vector3)(piece.Velocity * dt);
            piece.Rect.Rotate(Vector3.forward, piece.Spin * dt);

            float t = piece.Age / piece.Life;
            if (t > 1f - PieceFadeTail)
            {
                Color color = piece.Image.color; // RGB never changes after spawn; only alpha fades
                color.a = (1f - t) / PieceFadeTail;
                piece.Image.color = color;
            }
            _pieces[i] = piece;
        }
    }
}
