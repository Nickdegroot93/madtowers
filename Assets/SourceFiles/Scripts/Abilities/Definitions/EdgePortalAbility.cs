using UnityEngine;

/// <summary>
/// Unique passive that lets active falling pieces wrap across the current camera's
/// horizontal edges while they are still under player control.
/// </summary>
[CreateAssetMenu(fileName = "EdgePortal", menuName = "Stacking/Abilities/Edge Portal")]
public class EdgePortalAbility : PassiveAbility
{
    public override void OnAcquired(AbilityContext context, int stacks)
    {
        BlockController.SetEdgePortalEnabled(true);
    }

    public override void OnRemoved(AbilityContext context)
    {
        BlockController.SetEdgePortalEnabled(false);
    }
}
