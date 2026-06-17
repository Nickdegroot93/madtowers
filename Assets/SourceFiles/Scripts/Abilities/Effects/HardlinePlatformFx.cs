using UnityEngine;

/// <summary>
/// Takes over a block caught by Hardline: neutralise physics immediately, ease it into the
/// chosen platform pose, then leave it as a Static body so it becomes real stackable terrain.
/// </summary>
public sealed class HardlinePlatformFx : MonoBehaviour
{
    private BlockController _block;
    private Rigidbody2D _body;
    private Vector3 _startPosition;
    private Vector3 _targetPosition;
    private float _startAngle;
    private float _targetAngle;
    private float _duration;
    private float _elapsed;

    public static void Begin(BlockController block, Vector3 targetPosition, float targetAngle, float duration)
    {
        if (block == null) return;

        HardlinePlatformFx existing = block.GetComponent<HardlinePlatformFx>();
        if (existing != null) Destroy(existing);

        HardlinePlatformFx fx = block.gameObject.AddComponent<HardlinePlatformFx>();
        fx.Configure(block, targetPosition, targetAngle, duration);
    }

    private void Configure(BlockController block, Vector3 targetPosition, float targetAngle, float duration)
    {
        _block = block;
        _body = block.GetComponent<Rigidbody2D>();
        _startPosition = block.transform.position;
        _targetPosition = targetPosition;
        _startAngle = block.transform.eulerAngles.z;
        _targetAngle = targetAngle;
        _duration = Mathf.Max(0.02f, duration);

        if (_body != null)
        {
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;
            _body.gravityScale = 0f;
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.constraints = RigidbodyConstraints2D.None;
        }

        BlockController.InvalidateReachGeometry();
    }

    private void Update()
    {
        if (_block == null)
        {
            Destroy(this);
            return;
        }

        _elapsed += Time.deltaTime;
        float u = Mathf.Clamp01(_elapsed / _duration);
        float eased = EaseOutBackSoft(u);

        transform.position = Vector3.LerpUnclamped(_startPosition, _targetPosition, eased);
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(_startAngle, _targetAngle, eased));

        if (u >= 1f) Complete();
    }

    private void Complete()
    {
        transform.position = _targetPosition;
        transform.rotation = Quaternion.Euler(0f, 0f, _targetAngle);

        if (_body != null)
        {
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;
            _body.constraints = RigidbodyConstraints2D.None;
            _body.bodyType = RigidbodyType2D.Static;
        }

        BlockController.InvalidateReachGeometry();
        Destroy(this);
    }

    private static float EaseOutBackSoft(float t)
    {
        float inv = t - 1f;
        return 1f + inv * inv * ((1.25f + 1f) * inv + 1.25f);
    }
}
