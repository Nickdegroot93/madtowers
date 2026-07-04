using UnityEngine;

/// <summary>
/// Common consumable that trades the active falling piece for the front of the next-piece queue.
/// The old active shape becomes the new previewed next piece.
/// </summary>
[CreateAssetMenu(fileName = "Flip", menuName = "Stacking/Abilities/Flip")]
public class FlipAbility : ConsumableAbility
{
    public override bool CanActivate(AbilityContext context)
    {
        return context != null &&
               context.Spawner != null &&
               context.Spawner.CanSwapActiveWithNextQueued();
    }

    public override void Activate(AbilityContext context)
    {
        if (context == null || context.Spawner == null) return;

        if (context.Spawner.SwapActiveWithNextQueued())
        {
            SfxPlayer.Play("flip_swap", 0.72f, 0.05f);
        }
    }
}
