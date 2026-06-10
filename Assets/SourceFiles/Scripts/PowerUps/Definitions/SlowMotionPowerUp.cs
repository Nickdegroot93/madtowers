using UnityEngine;

[CreateAssetMenu(fileName = "SlowMotion", menuName = "Stacking/Power Ups/Slow Motion")]
public class SlowMotionPowerUp : PowerUpDefinition
{
    [Min(1f)]
    [SerializeField] private float duration = 15f;

    public override void Apply(PowerUpContext context)
    {
        if (context.GameManager == null) return;

        context.GameManager.ApplySlowMotion(duration);
    }
}
