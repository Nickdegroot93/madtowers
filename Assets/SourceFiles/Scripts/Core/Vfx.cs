using UnityEngine;

/// <summary>
/// One-shot effect spawner: drop an authored effect prefab (e.g. a Cartoon FX
/// CFXR prefab) into the world at a position. The reusable bridge between
/// data-authored ability FX and the scene - every ability references its effect
/// prefabs as serialized fields and plays them through here, so swapping a look is
/// a drag-and-drop in the Inspector, never a code change. Null-safe (an unassigned
/// effect simply plays nothing). CFXR prefabs self-destroy via their CFXR_Effect
/// component, so we only Instantiate.
/// </summary>
public static class Vfx
{
    public static GameObject Spawn(GameObject prefab, Vector3 position, float scale = 1f)
    {
        if (prefab == null) return null;

        position.z = 0f; // keep effects on the gameplay plane (2D ortho)
        GameObject instance = Object.Instantiate(prefab, position, Quaternion.identity);
        if (!Mathf.Approximately(scale, 1f))
        {
            instance.transform.localScale *= scale;
        }
        return instance;
    }
}
