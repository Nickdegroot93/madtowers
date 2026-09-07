using UnityEngine;

/// <summary>The departed pyramid is scenery only: no controller, joints, colliders,
/// ledger membership or height contribution. Scaled time freezes flight on pause.</summary>
public sealed class PyramidDepartureFx : MonoBehaviour
{
    private Material _material;
    private SpriteRenderer _sprite;
    private Vector3 _from;
    private float _age;
    private const float Duration = 1.15f;

    public static void Play(SpriteRenderer source, Transform effectParent = null, bool playSound = true)
    {
        var go = new GameObject("Departing cyber pyramid");
        go.layer = source.gameObject.layer;
        go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        go.transform.localScale = source.transform.lossyScale;
        if (effectParent != null) go.transform.SetParent(effectParent, true);
        var fx = go.AddComponent<PyramidDepartureFx>();
        fx._from = go.transform.position;
        fx._material = new Material(source.sharedMaterial);
        fx._material.SetFloat("_Ignition", 1);
        fx._sprite = go.AddComponent<SpriteRenderer>();
        fx._sprite.sprite = source.sprite;
        fx._sprite.sharedMaterial = fx._material;
        fx._sprite.sortingLayerID = source.sortingLayerID;
        fx._sprite.sortingOrder = -2; // departure passes behind the live tower
        if (playSound) SfxPlayer.Play("rescue_beam", .65f, 0f);
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float u = Mathf.Clamp01(_age / Duration);
        transform.position = _from + Vector3.up * (1.2f * u + 13f * u * u);
        _material.SetFloat("_Phase", _material.GetFloat("_Phase") + Time.deltaTime);
        _sprite.color = new Color(1, 1, 1, 1f - Mathf.SmoothStep(0, 1, (u - .55f) / .45f));
        if (u >= 1) Destroy(gameObject);
    }

    private void OnDestroy() { if (_material != null) Destroy(_material); }
}
