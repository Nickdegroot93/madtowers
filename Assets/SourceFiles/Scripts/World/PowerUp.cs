using UnityEngine;

public enum PowerUpType
{
    SlowMotion,
    ExtraLife
}

public class PowerUp : MonoBehaviour
{
    [SerializeField] private PowerUpType _type;
    [SerializeField] private float _slowMotionDuration = 10f;
    public PowerUpType Type => _type;

    private bool _collected;
    public event System.Action<PowerUp> Collected;

    public void Initialize(PowerUpType type, float slowMotionDuration = 10f)
    {
        _type = type;
        _slowMotionDuration = slowMotionDuration;
        _collected = false;
    }

    // Collection is now driven by BlockController when a block lands on top of
    // the power-up (BlockController.CollectOverlappingPowerUps), so a block must
    // actually settle over the target rather than just pass through it.
    public void Collect()
    {
        if (GameManager.Instance == null) return;
        if (_collected) return;
        _collected = true;

        switch (_type)
        {
            case PowerUpType.SlowMotion:
                GameManager.Instance.ApplySlowMotion(_slowMotionDuration);
                break;
            case PowerUpType.ExtraLife:
                GameManager.Instance.AddLife();
                break;
        }

        Debug.Log($"Power-up {_type} collected!");
        if (Collected != null) Collected(this);
        else Destroy(gameObject);
    }
}
