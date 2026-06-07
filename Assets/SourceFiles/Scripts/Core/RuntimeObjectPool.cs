using System.Collections.Generic;
using UnityEngine;

public sealed class RuntimeObjectPool
{
    private readonly Dictionary<GameObject, Queue<GameObject>> _pools = new Dictionary<GameObject, Queue<GameObject>>();

    public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (prefab == null) return null;

        if (!_pools.TryGetValue(prefab, out Queue<GameObject> pool))
        {
            pool = new Queue<GameObject>();
            _pools.Add(prefab, pool);
        }

        GameObject instance = pool.Count > 0 ? pool.Dequeue() : Object.Instantiate(prefab);
        instance.transform.SetParent(parent);
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.SetActive(true);
        return instance;
    }

    public void Release(GameObject prefab, GameObject instance)
    {
        if (prefab == null || instance == null)
        {
            if (instance != null) Object.Destroy(instance);
            return;
        }

        if (!_pools.TryGetValue(prefab, out Queue<GameObject> pool))
        {
            pool = new Queue<GameObject>();
            _pools.Add(prefab, pool);
        }

        instance.SetActive(false);
        pool.Enqueue(instance);
    }
}
