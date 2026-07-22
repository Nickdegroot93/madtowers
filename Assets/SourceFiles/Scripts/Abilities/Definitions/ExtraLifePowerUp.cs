using UnityEngine;

[CreateAssetMenu(fileName = "ExtraLife", menuName = "Stacking/Abilities/Extra Life")]
public class ExtraLifePowerUp : InstantAbility
{
    [Min(1)]
    [SerializeField] private int lives = 1;

    // At the 3-life ceiling (RunState.MaxLives, SHOP.md §3.1) the pickup would be a dead
    // card - don't offer it. AddLife also clamps, so this is presentation, not the guard.
    public override bool IsAvailable(AbilityContext context, int ownedStacks)
    {
        if (!base.IsAvailable(context, ownedStacks)) return false;
        return context.GameManager == null
            || context.GameManager.CurrentRunResult.Lives < RunState.MaxLives;
    }

    public override void Apply(AbilityContext context)
    {
        if (context.GameManager == null) return;

        for (int i = 0; i < lives; i++)
        {
            context.GameManager.AddLife();
        }
    }
}
