using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The "a block was lost to the fog" moment: a burst of mist puffs where the block sank (clamped
/// to the visible bottom edge, since the actual cull line sits inside the fog / below the view)
/// plus a small camera punch. The puffs are TORN FROM THE FOG (2026-09-04): they take the
/// chapter's lit fog colour (a near-white mist read as generic smoke over a green or purple
/// bank), sit flat like the bank's wisps, and the fog itself heaves where the block went under
/// (FloorTerrain.SplashAt) so mist and bank read as one material. The life-lost SOUND and the
/// heart animation are driven separately
/// off GameEvents.LifeLost, so an immune loss (Brace) still splashes but stays silent. Purely
/// cosmetic, procedural, self-destroys.
/// </summary>
public sealed class LifeLossFx : MonoBehaviour
{
    private const float Lifetime = 0.55f;
    // Fallback only (no terrain alive, e.g. demos): the lit fog colour is used when it exists.
    private static readonly Color FallbackMistColor = new Color(0.85f, 0.88f, 0.92f, 0.55f);
    private const float MistAlpha = 0.5f;

    private readonly List<SpriteRenderer> _puffs = new List<SpriteRenderer>();
    private readonly List<Vector3> _velocities = new List<Vector3>();
    private readonly List<float> _baseAlphas = new List<float>();
    private float _age;

    public static void Play(Vector3 blockPosition)
    {
        Camera cam = Camera.main;
        float y = blockPosition.y;
        if (cam != null && cam.orthographic)
        {
            // The death itself happens inside/below the fog - surface the splash at the screen's
            // bottom edge so the player always SEES the moment.
            float visibleFloor = cam.transform.position.y - cam.orthographicSize + 1.1f;
            y = Mathf.Max(y, visibleFloor);
        }

        var go = new GameObject("LifeLossFx");
        var fx = go.AddComponent<LifeLossFx>();
        Vector3 center = new Vector3(blockPosition.x, y, 0f);

        Color mist = FallbackMistColor;
        FloorTerrain terrain = FloorTerrain.Live;
        if (terrain != null)
        {
            // Lit haze pulled a touch toward the rim: the top of the bank, thrown upward.
            Color lit = Color.Lerp(terrain.Fog.Light, terrain.Fog.Rim, 0.3f);
            mist = new Color(lit.r, lit.g, lit.b, MistAlpha);
            FloorTerrain.SplashAt(center.x);
        }

        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.Lerp(20f, 160f, i / 5f) * Mathf.Deg2Rad; // fan upward out of the fog
            float speed = Random.Range(1.6f, 3.0f);
            var puff = new GameObject("MistPuff");
            puff.transform.SetParent(go.transform, false);
            puff.transform.position = center + new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-0.15f, 0.15f), 0f);
            float size = Random.Range(0.6f, 1.3f);
            puff.transform.localScale = new Vector3(size, size * 0.55f, 1f); // flat, like the bank's wisps
            var sr = puff.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.SoftBlob();
            sr.color = mist;
            sr.sortingOrder = 46; // just above the front fog band
            fx._puffs.Add(sr);
            fx._velocities.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed);
            fx._baseAlphas.Add(mist.a * Random.Range(0.7f, 1f));
        }

        ImpactFx.ImpactPunch(0.02f, 0.10f, 0.12f);
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / Lifetime);
        for (int i = 0; i < _puffs.Count; i++)
        {
            SpriteRenderer sr = _puffs[i];
            if (sr == null) continue;
            sr.transform.position += _velocities[i] * Time.deltaTime * (1f - t * 0.6f);
            sr.transform.localScale *= 1f + 0.9f * Time.deltaTime;
            Color c = sr.color;
            c.a = _baseAlphas[i] * (1f - t * t);
            sr.color = c;
        }
        if (t >= 1f) Destroy(gameObject);
    }
}
