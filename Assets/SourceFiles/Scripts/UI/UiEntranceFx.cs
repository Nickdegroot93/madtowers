using UnityEngine;

/// <summary>
/// A tiny reusable UI arrival: fade in + scale up with a soft overshoot, on UNSCALED time (choice
/// panels pause the game). Attach via <see cref="Play"/>; layout groups ignore scale, so it is
/// safe on layout-managed children (the ability cards). Removes itself when done.
/// </summary>
public sealed class UiEntranceFx : MonoBehaviour
{
    private const float Duration = 0.28f;
    private CanvasGroup _group;
    private float _delay;
    private float _age;

    public static void Play(GameObject target, float delay = 0f)
    {
        if (target == null) return;
        UiEntranceFx fx = target.AddComponent<UiEntranceFx>();
        fx._delay = delay;
        fx._group = target.GetComponent<CanvasGroup>();
        if (fx._group == null) fx._group = target.AddComponent<CanvasGroup>();
        fx.Apply(0f);
    }

    private void Update()
    {
        _age += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01((_age - _delay) / Duration);
        Apply(t);
        if (t >= 1f) Destroy(this);
    }

    private void Apply(float t)
    {
        if (_group != null) _group.alpha = t;
        float overshoot = 1f + 0.06f * Mathf.Sin(t * Mathf.PI); // pop past, settle back
        transform.localScale = Vector3.one * (Mathf.Lerp(0.92f, 1f, t) * overshoot);
    }
}
