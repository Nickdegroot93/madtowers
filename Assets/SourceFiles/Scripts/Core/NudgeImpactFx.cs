using UnityEngine;

/// <summary>
/// The failed-nudge counterpart of DashWindFx: where the wind sells "cutting through
/// air", this sells "bounced off something solid". A tight burst of short debris
/// streaks kicks BACK from the edge the piece slammed into, with strong vertical
/// scatter and a fast fade. Purely visual (no colliders), no assets, self-destroys.
/// </summary>
public sealed class NudgeImpactFx : MonoBehaviour
{
    private const float LifetimeSeconds = 0.22f;
    private const int StreakCount = 8;
    private const float ShrinkPerSecond = 1.8f; // debris shortens as it dies (wind stretches)

    private SpriteRenderer[] _streaks;
    private Vector2[] _velocities;
    private float _age;

    /// <summary>
    /// Spawn at the edge the piece slammed into. pieceArea = the piece's world bounds;
    /// attemptedDirection: -1 the piece dashed left, +1 right.
    /// </summary>
    public static void Spawn(Bounds pieceArea, int attemptedDirection)
    {
        GameObject go = new GameObject("NudgeImpactFx");
        // sit just outside the LEADING edge - the point of impact
        go.transform.position = new Vector3(
            pieceArea.center.x + attemptedDirection * (pieceArea.extents.x + 0.1f),
            pieceArea.center.y, 0f);
        go.AddComponent<NudgeImpactFx>().Build(pieceArea, attemptedDirection);
    }

    private void Build(Bounds area, int direction)
    {
        _streaks = new SpriteRenderer[StreakCount];
        _velocities = new Vector2[StreakCount];

        for (int i = 0; i < StreakCount; i++)
        {
            GameObject streak = new GameObject("Debris");
            streak.transform.SetParent(transform, false);
            streak.transform.localPosition = new Vector3(
                Random.Range(-0.08f, 0.08f),
                Random.Range(-area.extents.y, area.extents.y), 0f);
            // much shorter than the dash wind: chips, not airflow
            streak.transform.localScale = new Vector3(Random.Range(0.25f, 0.5f), 1f, 1f);

            SpriteRenderer sr = streak.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.WindStreak();
            sr.color = new Color(1f, 0.95f, 0.85f, 0.65f); // warm dust, denser than wind
            sr.sortingOrder = 40; // above blocks (0), below the laser line (50)
            _streaks[i] = sr;

            // ricochet: back against the attempted dash, fanned out vertically
            _velocities[i] = new Vector2(-direction * Random.Range(1.5f, 4f), Random.Range(-2.5f, 2.5f));
        }

        Destroy(gameObject, LifetimeSeconds + 0.05f);
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float fade = Mathf.Clamp01(1f - _age / LifetimeSeconds);

        for (int i = 0; i < _streaks.Length; i++)
        {
            SpriteRenderer sr = _streaks[i];
            if (sr == null) continue;

            sr.transform.localPosition += (Vector3)(_velocities[i] * Time.deltaTime);
            Vector3 scale = sr.transform.localScale;
            scale.x = Mathf.Max(0.05f, scale.x - ShrinkPerSecond * Time.deltaTime);
            sr.transform.localScale = scale;

            Color c = sr.color;
            c.a = 0.65f * fade;
            sr.color = c;
        }
    }
}
