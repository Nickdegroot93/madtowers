using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A soft "beamed away" dissolve for a piece that is consumed rather than destroyed (Zap eats
/// the active piece to power the laser): per cell, a fading ghost square drifts upward while a
/// couple of bright shimmer streaks rise out of it. Deliberately airy and ascending - the
/// opposite read of a destruction burst. Procedural (RuntimeSprites), no prefab dependencies,
/// self-destroys; safe to call on a piece that is about to be Destroy()ed this frame.
/// </summary>
public sealed class DematerializeFx : MonoBehaviour
{
    private const float Lifetime = 0.45f;
    private static readonly Color GhostColor = new Color(0.55f, 0.85f, 1f, 0.55f);
    private static readonly Color StreakColor = new Color(0.75f, 0.95f, 1f, 0.8f);

    private readonly List<SpriteRenderer> _sprites = new List<SpriteRenderer>();
    private readonly List<Vector3> _velocities = new List<Vector3>();
    private readonly List<float> _baseAlphas = new List<float>();
    private float _age;

    public static void Spawn(BlockController block)
    {
        if (block == null) return;

        var go = new GameObject("DematerializeFx");
        var fx = go.AddComponent<DematerializeFx>();

        BoxCollider2D[] cells = block.GetComponentsInChildren<BoxCollider2D>();
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == null || cells[i].isTrigger) continue;
            Vector3 center = cells[i].bounds.center;
            float cell = Mathf.Max(cells[i].bounds.size.x, cells[i].bounds.size.y);

            fx.AddSprite(RuntimeSprites.Square(), center, new Vector3(cell * 0.92f, cell * 0.92f, 1f),
                GhostColor, new Vector3(0f, 1.6f, 0f), sortingOrder: 62);
            for (int s = 0; s < 2; s++)
            {
                Vector3 offset = new Vector3(Random.Range(-0.25f, 0.25f) * cell, Random.Range(-0.2f, 0.2f) * cell, 0f);
                fx.AddSprite(RuntimeSprites.SoftVerticalBar(0.08f), center + offset,
                    new Vector3(1f, cell * 0.9f, 1f), StreakColor,
                    new Vector3(0f, Random.Range(2.6f, 3.6f), 0f), sortingOrder: 63);
            }
        }
    }

    private void AddSprite(Sprite sprite, Vector3 position, Vector3 scale, Color color, Vector3 velocity, int sortingOrder)
    {
        var go = new GameObject("DematPart");
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        go.transform.localScale = scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = sortingOrder;
        _sprites.Add(sr);
        _velocities.Add(velocity);
        _baseAlphas.Add(color.a);
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / Lifetime);
        for (int i = 0; i < _sprites.Count; i++)
        {
            SpriteRenderer sr = _sprites[i];
            if (sr == null) continue;
            sr.transform.position += _velocities[i] * Time.deltaTime;
            Color c = sr.color;
            c.a = _baseAlphas[i] * (1f - t * t);
            sr.color = c;
        }
        if (t >= 1f) Destroy(gameObject);
    }
}
