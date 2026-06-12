using UnityEngine;

[CreateAssetMenu(fileName = "ExtraLife", menuName = "Stacking/Abilities/Extra Life")]
public class ExtraLifePowerUp : InstantAbility
{
    [Min(1)]
    [SerializeField] private int lives = 1;

    public override void Apply(AbilityContext context)
    {
        if (context.GameManager == null) return;

        for (int i = 0; i < lives; i++)
        {
            context.GameManager.AddLife();
        }
    }
}
