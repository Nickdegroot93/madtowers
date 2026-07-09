using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tier-0 landing feedback (JUICE.md): a burst of soft dust puffs squirting sideways from
/// under the block's bottom edge the moment it touches down. Count and kick scale with the
/// landing hardness, so a soft-steered placement breathes a little dust while a flick slams
/// a cloud out of both sides. Purely visual (no colliders), no assets, pooled, self-releases.
/// </summary>
public sealed class LandingDustFx : MonoBehaviour
{
    private const float LifetimeSeconds = 0.45f;
    private const int MaxPuffs = 14;
    private const float GrowPerSecond = 1.6f; // dust clouds expand as they thin out
    private static readonly Stack<LandingDustFx> Pool = new Stack<LandingDustFx>();

    private SpriteRenderer[] _puffs;
    private Vector2[] _velocities;
    private float[] _baseAlphas;
    private int _activeCount;
    private float _age;

    /// <summary>Spawn under the piece's bottom edge. pieceArea = world bounds at touchdown;
    /// hardness01 = 0 soft steered landing .. 1 full flick slam.</summary>
    public static void Spawn(Bounds pieceArea, float hardness01)
    {
        if (!SettingsService.VisualEffects) return;

        LandingDustFx fx = Get();
        fx.transform.position = new Vector3(pieceArea.center.x, pieceArea.min.y + 0.05f, 0f);
        fx.Build(pieceArea, Mathf.Clamp01(hardness01));
    }

    private static LandingDustFx Get()
    {
        while (Pool.Count > 0)
        {
            LandingDustFx pooled = Pool.Pop();
            if (pooled == null) continue;
            pooled.gameObject.SetActive(true);
            return pooled;
        }

        GameObject go = new GameObject("LandingDustFx");
        return go.AddComponent<LandingDustFx>();
    }

    private void Build(Bounds area, float hardness)
    {
        _age = 0f;
        EnsureBuilt();

        _activeCount = Mathf.Clamp(5 + Mathf.RoundToInt(9f * hardness), 1, MaxPuffs);
        float kick = 0.9f + 2.2f * hardness;

        for (int i = 0; i < MaxPuffs; i++)
        {
            SpriteRenderer sr = _puffs[i];
            if (i >= _activeCount)
            {
                sr.gameObject.SetActive(false);
                continue;
            }

            Transform puff = sr.transform;
            puff.gameObject.SetActive(true);
            // Seed along the bottom edge; the outer puffs carry the sideways squirt, the
            // inner ones mostly rise, so the cloud reads as pushed out from UNDER the block.
            float across = Random.Range(-1f, 1f); // -1 left corner .. 1 right corner
            puff.localPosition = new Vector3(across * area.extents.x, Random.Range(-0.03f, 0.08f), 0f);
            puff.localScale = Vector3.one * Random.Range(0.22f, 0.4f);

            float side = Mathf.Sign(across == 0f ? Random.Range(-1f, 1f) : across);
            _velocities[i] = new Vector2(
                side * Mathf.Abs(across) * Random.Range(0.5f, 1f) * kick,
                Random.Range(0.15f, 0.55f) * (0.6f + 0.8f * (1f - Mathf.Abs(across))));

            _baseAlphas[i] = Random.Range(0.3f, 0.5f) * (0.55f + 0.45f * hardness);
            sr.color = new Color(1f, 0.95f, 0.85f, _baseAlphas[i]); // warm dust (NudgeImpactFx family)
        }
    }

    private void EnsureBuilt()
    {
        if (_puffs != null) return;

        _puffs = new SpriteRenderer[MaxPuffs];
        _velocities = new Vector2[MaxPuffs];
        _baseAlphas = new float[MaxPuffs];
        for (int i = 0; i < MaxPuffs; i++)
        {
            GameObject puff = new GameObject("Dust");
            puff.transform.SetParent(transform, false);

            SpriteRenderer sr = puff.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.SoftPuff();
            sr.sortingOrder = 30; // above blocks (0), below nudge debris (40) and the laser line (50)
            _puffs[i] = sr;
        }
    }

    private void Update()
    {
        _age += Time.deltaTime;
        if (_age >= LifetimeSeconds + 0.05f)
        {
            Release();
            return;
        }

        float fade = Mathf.Clamp01(1f - _age / LifetimeSeconds);

        for (int i = 0; i < _activeCount; i++)
        {
            SpriteRenderer sr = _puffs[i];
            if (sr == null) continue;

            // Puffs decelerate as the squirt loses its push - dust drifts, chips fly.
            _velocities[i] *= 1f - 2.5f * Time.deltaTime;
            sr.transform.localPosition += (Vector3)(_velocities[i] * Time.deltaTime);
            sr.transform.localScale += Vector3.one * (GrowPerSecond * Time.deltaTime * fade * 0.35f);

            Color c = sr.color;
            c.a = _baseAlphas[i] * fade * fade;
            sr.color = c;
        }
    }

    private void Release()
    {
        for (int i = 0; i < _puffs.Length; i++)
        {
            if (_puffs[i] != null) _puffs[i].gameObject.SetActive(false);
        }
        gameObject.SetActive(false);
        Pool.Push(this);
    }
}
