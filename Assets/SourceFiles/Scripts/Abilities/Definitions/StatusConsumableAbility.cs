using UnityEngine;

/// <summary>
/// Generic consumable: activating it applies a status effect (a reusable game state -
/// e.g. 10 s of life-loss immunity). Consumables that are "enter a state now" are just
/// assets of this class referencing different status assets - no new code.
/// </summary>
[CreateAssetMenu(fileName = "StatusConsumable", menuName = "Stacking/Abilities/Status On Use")]
public class StatusConsumableAbility : ConsumableAbility
{
    [Tooltip("The game state activating this consumable applies (duration/magnitude live on the status asset).")]
    [SerializeField] private StatusEffectDefinition status;

    public override void Activate(AbilityContext context)
    {
        if (status == null || context.Status == null) return;

        context.Status.Apply(status);
        SfxPlayer.Play("pop_01", 0.8f, 0.05f);
    }
}
