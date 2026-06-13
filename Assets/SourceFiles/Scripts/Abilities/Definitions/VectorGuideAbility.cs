using UnityEngine;

/// <summary>
/// Unique passive that upgrades the placement beam with a translucent landing ghost.
/// The preview is visual-only and uses the active block's current cast result, so it
/// follows the same first-contact support rules as controlled landing.
/// </summary>
[CreateAssetMenu(fileName = "VectorGuide", menuName = "Stacking/Abilities/Vector Guide")]
public class VectorGuideAbility : PassiveAbility
{
    public override void OnAcquired(AbilityContext context, int stacks)
    {
        BlockController.SetVectorGuideEnabled(true);
    }

    public override void OnRemoved(AbilityContext context)
    {
        BlockController.SetVectorGuideEnabled(false);
    }
}
