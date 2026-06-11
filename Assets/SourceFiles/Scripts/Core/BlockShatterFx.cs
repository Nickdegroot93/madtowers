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

    private SpriteRenderer[] _shards;
    private Vector2[] _velocities;
    private float[] _spins;
    private float _age;

    /// <summary>Spawn a burst filling the given world-space area (e.g. the block's bounds).</summary>
    public static void Spawn(Bounds area, Color tint, int shardCount = 12)
    {
        GameObject go = new GameObject("BlockShatterFx");
        go.transform.position = area.center;
        go.AddComponent<BlockShatterFx>().Build(area, tint, Mathf.Max(4, shardCount));
    }

    private void Build(Bounds area, Color tint, int count)
    {
        _shards = new SpriteRenderer[count];
        _velocities = new Vector2[count];
        _spins = new float[count];

        for (int i = 0; i < count; i++)
        {
            GameObject shard = new GameObject("Shard");
            shard.transform.SetParent(transform, false);
            shard.transform.localPosition = new Vector3(
                Random.Range(-area.extents.x, area.extents.x),
                Random.Range(-area.extents.y, area.extents.y), 0f);
            shard.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float size = Random.Range(0.1f, 0.24f);
            shard.transform.localScale = new Vector3(size, size, 1f);

            SpriteRenderer sr = shard.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.Square();
            sr.color = Color.Lerp(tint, Color.white, Random.Range(0f, 0.5f));
            sr.sortingOrder = 60; // above blocks (0) and the limit line (50)
            _shards[i] = sr;

            _velocities[i] = new Vector2(Random.Range(-3f, 3f), Random.Range(1f, 5f));
            _spins[i] = Random.Range(-540f, 540f);
        }

        Destroy(gameObject, LifetimeSeconds + 0.05f);
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float fade = Mathf.Clamp01(1f - _age / LifetimeSeconds);

        for (int i = 0; i < _shards.Length; i++)
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
}
