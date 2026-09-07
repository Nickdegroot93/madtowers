using UnityEngine;

/// <summary>A preallocated cosmetic remnant; never extends a zone's gameplay lifetime.</summary>
public sealed class VoidScarFx : MonoBehaviour
{
    private Material _material;
    private SpriteRenderer _sprite;
    private float _age, _phase;

    public static VoidScarFx Prepare(Rect rect, Material material, Sprite sprite, int order)
    {
        var go = new GameObject("Void closing scar");
        go.transform.position = rect.center;
        go.transform.localScale = new Vector3(rect.width + .5f, rect.height + .5f, 1f);
        var fx = go.AddComponent<VoidScarFx>();
        fx._material = material;
        fx._sprite = go.AddComponent<SpriteRenderer>();
        fx._sprite.sprite = sprite;
        fx._sprite.sharedMaterial = material;
        fx._sprite.sortingOrder = order;
        fx._sprite.enabled = false;
        fx.enabled = false;
        return fx;
    }

    public void Close(float phase)
    {
        _phase = phase;
        _sprite.enabled = true;
        enabled = true;
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float u = Mathf.Clamp01(_age / .24f);
        _material.SetFloat("_Open", 1f - u * u);
        _material.SetFloat("_Hunger", 0f);
        _material.SetFloat("_Phase", _phase + _age);
        _material.SetFloat("_Closing", Mathf.SmoothStep(0, 1, u));
        _sprite.color = new Color(1, 1, 1, 1f - Mathf.Clamp01((_age - .24f) / .22f));
        if (_age >= .46f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
        SurfaceSceneFx.Release(flood: false);
    }
}
