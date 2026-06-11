using UnityEngine;

/// <summary>
/// Quick one-shot air rush for a nudge dash: a few soft streaks burst from the side the
/// piece is dashing away from and rush in the opposite direction, stretching and fading
/// in about a quarter second. Purely visual (no colliders), no assets, self-destroys.
/// Sibling of BlockShatterFx.
/// </summary>
public sealed class DashWindFx : MonoBehaviour
{
    private const float LifetimeSeconds = 0.28f;
    private const int StreakCount = 6;
    private const float StretchPerSecond = 2.5f; // streaks lengthen as they shoot away

    private SpriteRenderer[] _streaks;
    private Vector2[] _velocities;
    private float _age;

    /// <summary>
    /// Spawn behind a piece that just dashed. pieceArea = the piece's world bounds at the
    /// moment of the dash (it hasn't visually moved yet); movedDirection: -1 left, +1 right.
    /// </summary>
    public static void Spawn(Bounds pieceArea, int movedDirection)
    {
        GameObject go = new GameObject("DashWindFx");
        // sit just outside the trailing edge, so the wind separates from the piece
        go.transform.position = new Vector3(
            pieceArea.center.x - movedDirection * (pieceArea.extents.x + 0.15f),
            pieceArea.center.y, 0f);
        go.AddComponent<DashWindFx>().Build(pieceArea, movedDirection);
    }

    private void Build(Bounds area, int direction)
    {
        _streaks = new SpriteRenderer[StreakCount];
        _velocities = new Vector2[StreakCount];

        for (int i = 0; i < StreakCount; i++)
        {
            GameObject streak = new GameObject("Streak");
            streak.transform.SetParent(transform, false);
            streak.transform.localPosition = new Vector3(
                Random.Range(-0.15f, 0.15f),
                Random.Range(-area.extents.y, area.extents.y), 0f);
            streak.transform.localScale = new Vector3(Random.Range(0.7f, 1.4f), 1f, 1f);

            SpriteRenderer sr = streak.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.WindStreak();
            sr.color = new Color(1f, 1f, 1f, 0.5f);
            sr.sortingOrder = 40; // above blocks (0), below the laser line (50)
            _streaks[i] = sr;

            // rush opposite to the dash, with a little vertical scatter
            _velocities[i] = new Vector2(-direction * Random.Range(5f, 9f), Random.Range(-0.8f, 0.8f));
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
            scale.x += StretchPerSecond * Time.deltaTime;
            sr.transform.localScale = scale;

            Color c = sr.color;
            c.a = 0.5f * fade;
            sr.color = c;
        }
    }
}
