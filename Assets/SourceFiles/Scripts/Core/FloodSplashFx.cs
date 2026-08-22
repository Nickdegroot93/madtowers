using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The water twin of LifeLossFx: a brick swallowed by the Flood splashes AT THE WATERLINE
/// instead of puffing fog-mist (Nick 2026-08-22: "white smoke underwater looks weird").
/// Droplets arc up and fall back under gravity, a foam streak spreads flat along the
/// surface - flat tones, hard shapes, matching the cartoon water (LEVELS.md flood look).
/// Purely cosmetic, procedural, self-destroys. Other modes keep LifeLossFx untouched.
/// </summary>
public sealed class FloodSplashFx : MonoBehaviour
{
    private const float Lifetime = 0.6f;
    private const float DropletGravity = 11f;
    // Near-white with a cold touch - reads as foam against every chapter's water bias.
    private static readonly Color DropletColor = new Color(0.88f, 0.96f, 1f, 0.95f);
    private static readonly Color StreakColor = new Color(0.92f, 0.98f, 1f, 0.8f);

    private readonly List<SpriteRenderer> _drops = new List<SpriteRenderer>();
    private readonly List<Vector3> _velocities = new List<Vector3>();
    private SpriteRenderer _streak;
    private float _age;

    /// <summary>Splash at the flood surface above <paramref name="x"/>. Callers gate on the
    /// flood being active (RisingFloodModifier.FloodSurfaceY is -inf otherwise).</summary>
    public static void Play(float x)
    {
        float surfaceY = RisingFloodModifier.FloodSurfaceY;
        if (float.IsNegativeInfinity(surfaceY)) return;

        // The waterline can sit below the camera frame on a tall tower - surface the
        // splash at the visible bottom edge so the player always SEES the loss moment
        // (the LifeLossFx clamp, same reasoning).
        float y = surfaceY;
        Camera cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            y = Mathf.Max(y, cam.transform.position.y - cam.orthographicSize + 1.1f);
        }

        var go = new GameObject("FloodSplashFx");
        var fx = go.AddComponent<FloodSplashFx>();
        Vector3 center = new Vector3(x, y, 0f);

        for (int i = 0; i < 7; i++)
        {
            float angle = Mathf.Lerp(35f, 145f, i / 6f) * Mathf.Deg2Rad; // fountain out of the surface
            float speed = Random.Range(2.5f, 4.5f);
            var drop = new GameObject("Droplet");
            drop.transform.SetParent(go.transform, false);
            drop.transform.position = center + new Vector3(Random.Range(-0.35f, 0.35f), 0f, 0f);
            float size = Random.Range(0.14f, 0.32f);
            drop.transform.localScale = new Vector3(size, size, 1f);
            var sr = drop.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.SoftBlob();
            sr.color = DropletColor;
            sr.sortingOrder = 46; // the LifeLossFx layer - above the water quad (30)
            fx._drops.Add(sr);
            fx._velocities.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed);
        }

        var streak = new GameObject("FoamStreak");
        streak.transform.SetParent(go.transform, false);
        streak.transform.position = center;
        streak.transform.localScale = new Vector3(1.1f, 0.22f, 1f);
        fx._streak = streak.AddComponent<SpriteRenderer>();
        fx._streak.sprite = RuntimeSprites.SoftBlob();
        fx._streak.color = StreakColor;
        fx._streak.sortingOrder = 46;

        ImpactFx.ImpactPunch(0.015f, 0.08f, 0.1f);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        _age += dt;
        float t = Mathf.Clamp01(_age / Lifetime);

        for (int i = 0; i < _drops.Count; i++)
        {
            SpriteRenderer sr = _drops[i];
            if (sr == null) continue;
            _velocities[i] += Vector3.down * (DropletGravity * dt);
            sr.transform.position += _velocities[i] * dt;
            // A droplet falling back under the surface is home - vanish, don't fade midair.
            bool underwater = sr.transform.position.y < RisingFloodModifier.FloodSurfaceY - 0.05f
                              && _velocities[i].y < 0f;
            Color c = sr.color;
            c.a = underwater ? 0f : DropletColor.a * (1f - t * t);
            sr.color = c;
        }

        if (_streak != null)
        {
            _streak.transform.localScale = new Vector3(1.1f + 1.6f * t, 0.22f * (1f - 0.4f * t), 1f);
            Color c = _streak.color;
            c.a = StreakColor.a * (1f - t);
            _streak.color = c;
        }

        if (t >= 1f) Destroy(gameObject);
    }
}
