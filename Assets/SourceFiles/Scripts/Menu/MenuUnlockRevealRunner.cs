using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Plays one unlock-reveal on the main menu: a short orient delay, the lock badge straining
/// (rattle), then a flash-covered swap from the locked look to the unlocked look with a scale
/// punch and a sparkle burst. The three-beat shape (anticipation - burst - settle) is
/// deliberate: the strain beat is what makes the pop land.
///
/// MainMenuRuntime builds the target in its LOCKED look and attaches this runner with a Spec;
/// the Rebuild callback swaps the visuals to the real (unlocked) state at the flash peak, so
/// this component never needs to know how menu widgets are constructed. The menu runs at
/// timeScale = 0, so every wait here counts unscaled time. A tap anywhere fast-forwards the
/// anticipation beats straight to the payoff (never trap the player in a reward animation).
/// </summary>
public class MenuUnlockRevealRunner : MonoBehaviour
{
    public class Spec
    {
        public float InitialDelay = 0.55f;   // let the eye land on the menu first
        public float ShakeSeconds = 0.42f;
        public RectTransform ShakeTarget;    // the lock badge that strains before breaking
        public string RattleSfx;             // played when the strain starts
        public string BurstSfx;              // played at the break
        // Swaps locked -> unlocked visuals at the break; the white flash ramps to its peak
        // within ~70ms of the swap, fast enough that the change reads as revealed-by-the-flash.
        // Returns the elements the settle beat animates.
        public Func<Result> Rebuild;
        public Color SparkleColor = Color.white;
        public int SparkleCount = 9;
        // Sparkles need a parent OUTSIDE any RectMask2D so they can fly past the card edge.
        public RectTransform SparkleLayer;
        // Optional: scroll this list so ScrollTarget is visible BEFORE the reveal plays. A
        // reveal below the fold is otherwise wasted - the player never sees it. The scroll
        // ride itself is anticipation (eyes follow the motion to the payoff).
        public ScrollRect ScrollTo;
        public RectTransform ScrollTarget;
    }

    public class Result
    {
        public RectTransform PunchTarget;    // scale-punched card
        public RectTransform FlashArea;      // rect the white flash covers (usually the card)
        public Graphic FadeIn;               // optional: halo etc, alpha 0 -> its built alpha
        public Image RadialWipe;             // optional: image revealed by a radial fill sweep
        public float RadialWipeSeconds = 0.45f;
    }

    private const float SparkleLife = 0.55f;

    private Spec _spec;
    private bool _skip;
    private readonly System.Collections.Generic.List<Image> _sparks = new System.Collections.Generic.List<Image>();

    public static void Play(GameObject host, Spec spec)
    {
        MenuUnlockRevealRunner runner = host.AddComponent<MenuUnlockRevealRunner>();
        runner._spec = spec;
        runner.StartCoroutine(runner.Run());
    }

    private void Update()
    {
        // Any tap/click fast-forwards the anticipation. Checked here (not in the waits) so a
        // tap during ANY beat registers even between yields. Pointer covers mouse, touch and pen.
        Pointer pointer = Pointer.current;
        if (pointer != null && pointer.press.wasPressedThisFrame) _skip = true;
    }

    private IEnumerator Run()
    {
        // One frame so the freshly built menu's layout groups have solved - scroll geometry
        // (content height, row positions) is meaningless before that.
        yield return null;

        float delay = _spec.InitialDelay;
        if (_spec.ScrollTo != null && _spec.ScrollTarget != null)
        {
            float target = ScrollTargetNormalized(_spec.ScrollTo, _spec.ScrollTarget);
            if (target >= 0f && Mathf.Abs(_spec.ScrollTo.verticalNormalizedPosition - target) > 0.01f)
            {
                yield return ScrollIntoView(_spec.ScrollTo, target, 0.45f);
                delay = Mathf.Min(delay, 0.3f); // the ride was most of the anticipation already
            }
        }

        yield return Wait(delay);

        if (!_skip && _spec.ShakeTarget != null)
        {
            if (!string.IsNullOrEmpty(_spec.RattleSfx)) SfxPlayer.Play(_spec.RattleSfx, 0.8f);
            yield return Shake(_spec.ShakeTarget, _spec.ShakeSeconds);
        }

        if (!string.IsNullOrEmpty(_spec.BurstSfx)) SfxPlayer.Play(_spec.BurstSfx);

        Result result = _spec.Rebuild != null ? _spec.Rebuild() : null;
        if (result == null)
        {
            Destroy(this);
            yield break;
        }

        // The payoff beat always plays in full - only the anticipation is skippable.
        Image flash = BuildFlash(result.FlashArea);
        Graphic fadeIn = result.FadeIn;
        Color fadeInTarget = fadeIn != null ? fadeIn.color : Color.clear;
        if (fadeIn != null) fadeIn.color = ClearOf(fadeInTarget);
        if (result.RadialWipe != null) PrepareRadialWipe(result.RadialWipe);
        SpawnSparkles(result.FlashArea);

        // The loop runs to the LONGEST payoff element - flash/punch/fade clamp at their own
        // ends. Covering SparkleLife here matters: ending earlier would kill the DriveSparkle
        // coroutines with this component and strand near-invisible spark Images until the
        // next menu rebuild.
        const float settleSeconds = 0.38f;
        float total = Mathf.Max(settleSeconds, SparkleLife + 0.1f,
            result.RadialWipe != null ? result.RadialWipeSeconds : 0f);
        float elapsed = 0f;
        while (elapsed < total)
        {
            elapsed += UnscaledDt();
            float t = Mathf.Clamp01(elapsed / settleSeconds);

            // Flash: near-instant white cap over the swap, easing away as the card settles.
            if (flash != null)
            {
                float a = elapsed < 0.07f ? Mathf.Lerp(0f, 0.85f, elapsed / 0.07f)
                    : Mathf.Lerp(0.85f, 0f, EaseOutCubic((elapsed - 0.07f) / (settleSeconds - 0.07f)));
                flash.color = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }

            // Punch: lands big, dips just under rest, relaxes - a one-bounce settle.
            if (result.PunchTarget != null)
            {
                float s = PunchScale(t);
                result.PunchTarget.localScale = new Vector3(s, s, 1f);
            }

            if (fadeIn != null)
            {
                Color c = fadeInTarget;
                c.a *= EaseOutCubic(t);
                fadeIn.color = c;
            }

            if (result.RadialWipe != null)
            {
                result.RadialWipe.fillAmount = EaseOutCubic(Mathf.Clamp01(elapsed / result.RadialWipeSeconds));
            }

            yield return null;
        }

        if (result.PunchTarget != null) result.PunchTarget.localScale = Vector3.one;
        if (fadeIn != null) fadeIn.color = fadeInTarget;
        if (result.RadialWipe != null) result.RadialWipe.fillAmount = 1f;
        if (flash != null) Destroy(flash.gameObject);
        foreach (Image spark in _sparks)
        {
            if (spark != null) Destroy(spark.gameObject); // stragglers from the same frame
        }
        Destroy(this);
    }

    // ---- beats -------------------------------------------------------------------------------

    // Where the scrollbar must sit so the target row is centred in the viewport (1 = top,
    // ScrollRect convention), or -1 when the list doesn't scroll at all.
    private static float ScrollTargetNormalized(ScrollRect scroll, RectTransform row)
    {
        if (scroll.content == null || scroll.viewport == null) return -1f;
        float scrollable = scroll.content.rect.height - scroll.viewport.rect.height;
        if (scrollable <= 1f) return -1f;
        // Rows hang from the content top (pivot y = 1), so -anchoredPosition.y is the offset
        // of the row's top edge below it.
        float rowCenter = -row.anchoredPosition.y + row.rect.height * 0.5f;
        float desired = Mathf.Clamp(rowCenter - scroll.viewport.rect.height * 0.5f, 0f, scrollable);
        return 1f - desired / scrollable;
    }

    private IEnumerator ScrollIntoView(ScrollRect scroll, float target, float seconds)
    {
        float from = scroll.verticalNormalizedPosition;
        float elapsed = 0f;
        while (elapsed < seconds && !_skip)
        {
            elapsed += UnscaledDt();
            if (scroll == null) yield break;
            scroll.verticalNormalizedPosition = Mathf.Lerp(from, target, EaseOutCubic(elapsed / seconds));
            yield return null;
        }
        if (scroll != null) scroll.verticalNormalizedPosition = target; // a skip still lands scrolled
    }

    private IEnumerator Wait(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds && !_skip)
        {
            elapsed += UnscaledDt();
            yield return null;
        }
    }

    // The lock strains: quick rotation rattle with a rising envelope plus a slight swell, ending
    // back at rest so the swap frame starts clean.
    private IEnumerator Shake(RectTransform target, float seconds)
    {
        float elapsed = 0f;
        Quaternion baseRotation = target.localRotation;
        Vector3 baseScale = target.localScale;
        while (elapsed < seconds && !_skip)
        {
            elapsed += UnscaledDt();
            float t = Mathf.Clamp01(elapsed / seconds);
            if (target == null) yield break;
            float envelope = Mathf.Sin(t * Mathf.PI); // in and back out, never a hard stop
            float angle = Mathf.Sin(elapsed * 58f) * 11f * envelope;
            target.localRotation = baseRotation * Quaternion.Euler(0f, 0f, angle);
            float swell = 1f + 0.09f * envelope;
            target.localScale = baseScale * swell;
            yield return null;
        }
        if (target != null)
        {
            target.localRotation = baseRotation;
            target.localScale = baseScale;
        }
    }

    private Image BuildFlash(RectTransform area)
    {
        if (area == null) return null;
        Image flash = RuntimeUiKit.CreateImage(area, "UnlockFlash", RuntimeSprites.RoundedPanel(),
            new Color(1f, 1f, 1f, 0f));
        flash.type = Image.Type.Sliced;
        RectTransform rect = flash.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return flash;
    }

    private void PrepareRadialWipe(Image image)
    {
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Radial360;
        image.fillOrigin = (int)Image.Origin360.Top;
        image.fillClockwise = true;
        image.fillAmount = 0f;
    }

    // A modest burst of chapter-tinted sparks flying out of the card - the reward accent.
    // Each spark is its own coroutine so they ride the same unscaled clock as everything else.
    private void SpawnSparkles(RectTransform origin)
    {
        RectTransform layer = _spec.SparkleLayer;
        if (layer == null || origin == null) return;

        Vector3 world = origin.TransformPoint(origin.rect.center);
        Sprite sprite = MenuSprites.Sparkle(_spec.SparkleColor); // one baked sprite, shared by all sparks
        for (int i = 0; i < _spec.SparkleCount; i++)
        {
            Image spark = RuntimeUiKit.CreateImage(layer, "UnlockSpark", sprite, Color.white);
            _sparks.Add(spark);
            spark.rectTransform.position = world;
            float size = UnityEngine.Random.Range(28f, 48f);
            spark.rectTransform.sizeDelta = new Vector2(size, size);
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float speed = UnityEngine.Random.Range(220f, 460f);
            Vector2 velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
            StartCoroutine(DriveSparkle(spark, velocity));
        }
    }

    private IEnumerator DriveSparkle(Image spark, Vector2 velocity)
    {
        const float life = SparkleLife;
        float elapsed = 0f;
        float spin = UnityEngine.Random.Range(-260f, 260f);
        while (elapsed < life)
        {
            float dt = UnscaledDt();
            elapsed += dt;
            if (spark == null) yield break;
            velocity *= Mathf.Exp(-2.6f * dt); // air drag: fast exit, gentle end
            spark.rectTransform.anchoredPosition += velocity * dt;
            spark.rectTransform.Rotate(0f, 0f, spin * dt);
            float t = Mathf.Clamp01(elapsed / life);
            spark.color = new Color(1f, 1f, 1f, 1f - EaseOutCubic(t));
            yield return null;
        }
        if (spark != null) Destroy(spark.gameObject);
    }

    // ---- math --------------------------------------------------------------------------------

    // A frame hitch must slow the reveal, not fast-forward it (same rule as MenuChapterPager).
    private static float UnscaledDt() => Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    // 1.13 at the swap, a whisper under 1.0 on the way down, rest at 1 - one soft bounce.
    private static float PunchScale(float t)
    {
        if (t >= 1f) return 1f;
        return 1f + 0.13f * Mathf.Exp(-4.2f * t) * Mathf.Cos(t * Mathf.PI * 1.6f);
    }

    private static Color ClearOf(Color color)
    {
        color.a = 0f;
        return color;
    }
}
