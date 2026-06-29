using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Quick one-shot shatter burst for a destroyed block: a handful of small tinted shards
/// fly out, tumble, fall and fade over about half a second. Purely visual (no colliders),
/// no assets, self-destroys. Reusable by anything that removes a block - laser zaps,
/// bombs, future destruction effects.
/// </summary>
public sealed class BlockShatterFx : MonoBehaviour
{
    private const float LifetimeSeconds = 0.5f;
    private const float Gravity = 14f;
    private static readonly Stack<BlockShatterFx> Pool = new Stack<BlockShatterFx>();

    private SpriteRenderer[] _shards;
    private Vector2[] _velocities;
    private float[] _spins;
    private int _activeShardCount;
    private float _age;

    /// <summary>Spawn a burst filling the given world-space area (e.g. the block's bounds).</summary>
    public static void Spawn(Bounds area, Color tint, int shardCount = 12)
    {
        BlockShatterFx fx = Get();
        fx.transform.position = area.center;
        fx.Build(area, tint, Mathf.Max(4, shardCount));
    }

    private static BlockShatterFx Get()
    {
        while (Pool.Count > 0)
        {
            BlockShatterFx pooled = Pool.Pop();
            if (pooled == null) continue;
            pooled.gameObject.SetActive(true);
            return pooled;
        }

        GameObject go = new GameObject("BlockShatterFx");
        return go.AddComponent<BlockShatterFx>();
    }

    private void Build(Bounds area, Color tint, int count)
    {
        _age = 0f;
        _activeShardCount = count;
        EnsureCapacity(count);

        for (int i = 0; i < count; i++)
        {
            SpriteRenderer sr = _shards[i];
            Transform shard = sr.transform;
            shard.gameObject.SetActive(true);
            shard.localPosition = new Vector3(
                Random.Range(-area.extents.x, area.extents.x),
                Random.Range(-area.extents.y, area.extents.y), 0f);
            shard.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float size = Random.Range(0.1f, 0.24f);
            shard.localScale = new Vector3(size, size, 1f);

            sr.color = Color.Lerp(tint, Color.white, Random.Range(0f, 0.5f));

            _velocities[i] = new Vector2(Random.Range(-3f, 3f), Random.Range(1f, 5f));
            _spins[i] = Random.Range(-540f, 540f);
        }

        for (int i = count; i < _shards.Length; i++)
        {
            if (_shards[i] != null) _shards[i].gameObject.SetActive(false);
        }
    }

    private void EnsureCapacity(int count)
    {
        if (_shards != null && _shards.Length >= count) return;

        int oldCount = _shards != null ? _shards.Length : 0;
        SpriteRenderer[] shards = new SpriteRenderer[count];
        Vector2[] velocities = new Vector2[count];
        float[] spins = new float[count];

        for (int i = 0; i < oldCount; i++)
        {
            shards[i] = _shards[i];
            velocities[i] = _velocities[i];
            spins[i] = _spins[i];
        }

        for (int i = oldCount; i < count; i++)
        {
            GameObject shard = new GameObject("Shard");
            shard.transform.SetParent(transform, false);

            SpriteRenderer sr = shard.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.Square();
            sr.sortingOrder = 60; // above blocks (0) and the limit line (50)
            shards[i] = sr;
        }

        _shards = shards;
        _velocities = velocities;
        _spins = spins;
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

        for (int i = 0; i < _activeShardCount; i++)
        {
            SpriteRenderer sr = _shards[i];
            if (sr == null) continue;

            _velocities[i].y -= Gravity * Time.deltaTime;
            sr.transform.localPosition += (Vector3)(_velocities[i] * Time.deltaTime);
            sr.transform.Rotate(0f, 0f, _spins[i] * Time.deltaTime);

            Color c = sr.color;
            c.a = fade;
            sr.color = c;
        }
    }

    private void Release()
    {
        for (int i = 0; i < _activeShardCount; i++)
        {
            if (_shards[i] != null) _shards[i].gameObject.SetActive(false);
        }
        _activeShardCount = 0;
        gameObject.SetActive(false);
        Pool.Push(this);
    }
}
