using UnityEngine;

[CreateAssetMenu(fileName = "SlowMotion", menuName = "Stacking/Abilities/Slow Motion")]
public class SlowMotionPowerUp : InstantAbility
{
    [Min(1f)]
    [SerializeField] private float duration = 15f;

    public override void Apply(AbilityContext context)
    {
        if (context.GameManager == null) return;

        context.GameManager.ApplySlowMotion(duration);
    }
}
