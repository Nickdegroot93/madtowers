using UnityEngine;

public sealed class SacrificeLaserFlash : MonoBehaviour
{
    private const float Lifetime = 0.24f;
    private const float LineLength = 90f;

    private SpriteRenderer _renderer;
    private Color _color;
    private float _age;

    public void Play(Color color, float y, float x)
    {
        _color = color;
        transform.position = new Vector3(x, y, 0f);
        _renderer = gameObject.AddComponent<SpriteRenderer>();
        _renderer.sprite = RuntimeSprites.SoftHorizontalBar(0.65f);
        _renderer.sortingOrder = 61;
        _renderer.transform.localScale = new Vector3(LineLength / _renderer.sprite.bounds.size.x, 1f, 1f);
        Destroy(gameObject, Lifetime + 0.05f);
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / Lifetime);
        Color color = Color.Lerp(_color, Color.white, 0.45f);
        color.a = 1f - t;
        if (_renderer != null)
        {
            _renderer.color = color;
            _renderer.transform.localScale = new Vector3(
                Mathf.Lerp(LineLength, LineLength + 14f, t) / _renderer.sprite.bounds.size.x,
                Mathf.Lerp(1f, 1.65f, t),
                1f);
        }
    }
}
