using UnityEngine;

/// <summary>
/// One-shot passive (set Charges = 1 on the asset): the first landed block that would
/// fall off the screen is destroyed in a shatter instead of charging a life. With
/// charges exhausted the ability disappears from the inventory.
/// </summary>
[CreateAssetMenu(fileName = "SacrificialSafety", menuName = "Stacking/Abilities/Sacrificial Safety")]
public class SacrificialSafetyAbility : PassiveAbility
{
    public override bool TryInterceptLoss(AbilityContext context, BlockController block)
    {
        // Contract: a handled block must end non-lost - destroyed counts.
        AbilityEffects.DestroyBlockWithShatter(block, new Color(0.95f, 0.9f, 0.6f));
        return true;
    }
}
