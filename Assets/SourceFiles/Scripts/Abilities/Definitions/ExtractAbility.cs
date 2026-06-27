using UnityEngine;

/// <summary>
/// Consumable targeting ability: pause the run, present the visible landed tower as selectable
/// floating proxies, then delete the chosen placed piece and resume.
/// </summary>
[CreateAssetMenu(fileName = "Extract", menuName = "Stacking/Abilities/Extract")]
public class ExtractAbility : ConsumableAbility
{
    public override bool CanActivate(AbilityContext context)
    {
        if (ExtractTargetingSession.IsActive) return false;
        return ExtractTargetingSession.HasAnyTargetable(excludeFrozen: false);
    }

    public override void Activate(AbilityContext context)
    {
        ExtractTargetingSession.Begin();
    }
}
