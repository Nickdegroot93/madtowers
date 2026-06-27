using UnityEngine;

/// <summary>
/// Passive that banks a pool of power-up REROLLS. While you hold any, a Reroll button appears
/// under the choice cards (AbilityChoiceController); each click redraws the three cards and
/// spends one. Charges are banked across offers - click once now, the rest wait for a later
/// offer. The count lives on AbilityRuntime (run state), not on this instance, so it survives
/// regardless of this ability's own lifetime.
/// </summary>
[CreateAssetMenu(fileName = "Reroll", menuName = "Stacking/Abilities/Reroll")]
public class RerollPowerUp : PassiveAbility
{
    [Tooltip("How many power-up rerolls this grants, banked across future offers.")]
    [Min(1)]
    [SerializeField] private int rerollCharges = 3;

    public override void OnAcquired(AbilityContext context, int stacks)
    {
        context.Runtime?.GrantRerollCharges(rerollCharges);
    }
}
