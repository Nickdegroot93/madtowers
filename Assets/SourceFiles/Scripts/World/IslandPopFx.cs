using UnityEngine;

/// <summary>
/// Scale-in "materialize" animation for a support island's VISUAL when a rising laser
/// line reveals it on screen (off-screen spawns stay silent and skip this entirely).
/// Lives on the visual child only - the cell's collider is full-size from frame one,
/// so physics never sees a half-grown island. Plays one pop sound when the (optionally
/// delayed) animation actually starts; disables itself when settled at scale 1.
/// </summary>
public sealed class IslandPopFx : MonoBehaviour
{
    private const float DurationSeconds = 0.3f;
    private const float Overshoot = 1.18f; // brief bulge past full size reads as a "pop"

    private float _delay;
    private float _age;
    private bool _started;
    private bool _withSound;

    /// <summary>
    /// Start (or restart) the pop; delay staggers band reveals bottom-to-top. A multi-cell
    /// cluster animates every cell but passes withSound on only one, so it pops as one.
    /// </summary>
    public void Play(float delay, bool withSound)
    {
        _delay = Mathf.Max(0f, delay);
        _age = 0f;
        _started = false;
        _withSound = withSound;
        transform.localScale = Vector3.zero;
        enabled = true;
    }

    /// <summary>Pooled reuse without animation: full size immediately, component idle.</summary>
    public void Skip()
    {
        transform.localScale = Vector3.one;
        enabled = false;
    }

    private void Update()
    {
        _age += Time.deltaTime;
        if (_age < _delay) return;

        if (!_started)
        {
            _started = true;
            if (_withSound) SfxPlayer.Play("pop_01", 0.5f, 0.12f);
        }

        float t = Mathf.Clamp01((_age - _delay) / DurationSeconds);
        // grow fast, overshoot mid-way, settle back to exactly 1
        float scale = t < 0.6f
            ? Mathf.SmoothStep(0f, Overshoot, t / 0.6f)
            : Mathf.SmoothStep(Overshoot, 1f, (t - 0.6f) / 0.4f);
        transform.localScale = new Vector3(scale, scale, 1f);

        if (t >= 1f) enabled = false;
    }
}
