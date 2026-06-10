using UnityEngine;

[CreateAssetMenu(fileName = "ExtraLife", menuName = "Stacking/Power Ups/Extra Life")]
public class ExtraLifePowerUp : PowerUpDefinition
{
    [Min(1)]
    [SerializeField] private int lives = 1;

    public override void Apply(PowerUpContext context)
    {
        if (context.GameManager == null) return;

        for (int i = 0; i < lives; i++)
        {
            context.GameManager.AddLife();
        }
    }
}
