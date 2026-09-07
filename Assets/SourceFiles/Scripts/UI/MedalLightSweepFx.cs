using UnityEngine;
using UnityEngine.UI;

/// <summary>A single reflected-light pass across the medal's own opaque pixels.</summary>
public sealed class MedalLightSweepFx : MonoBehaviour
{
    private Image _image;
    private Material _material;
    private Material _original;
    private float _age;
    private float _delay;

    public static MedalLightSweepFx Attach(Image image, float delay)
    {
        var shader = Resources.Load<Shader>("MedalLightSweep");
        if (shader == null) return null;
        var fx = image.gameObject.AddComponent<MedalLightSweepFx>();
        fx._image = image;
        fx._original = image.material;
        fx._material = new Material(shader);
        fx._material.SetFloat("_Sweep", -1f);
        image.material = fx._material;
        fx._delay = delay;
        return fx;
    }

    private void Update()
    {
        _age += Time.unscaledDeltaTime;
        _material.SetFloat("_Sweep", Mathf.Lerp(-.4f, 1.5f, Mathf.Clamp01((_age - _delay) / .55f)));
        if (_age >= _delay + .55f) Finish();
    }

    public void Finish()
    {
        // A skip tap can finish the sweep before its Update in the same frame.
        // Destroy is deferred, so stop ticking before releasing the material.
        enabled = false;
        if (_material == null) return;
        if (_image != null) _image.material = _original;
        if (_material != null) Destroy(_material);
        _material = null;
        Destroy(this);
    }

    private void OnDestroy()
    {
        if (_image != null && _image.material == _material) _image.material = _original;
        if (_material != null) Destroy(_material);
    }
}
