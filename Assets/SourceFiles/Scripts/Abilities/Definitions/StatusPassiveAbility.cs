using UnityEngine;

/// <summary>
/// Generic passive: when the chosen game event fires, apply a status effect (a reusable
/// game state). Completes the zero-code matrix with StatusComboAbility and
/// StatusConsumableAbility - "on life lost, gain 10 s of immunity" is an asset of this
/// class, no new code. Set Charges = 1 for a once-per-run version.
/// </summary>
[CreateAssetMenu(fileName = "StatusPassive", menuName = "Stacking/Abilities/Status On Event")]
public class StatusPassiveAbility : PassiveAbility
{
    private enum TriggerEvent
    {
        LifeLost,
        BlockSpawned
    }

    [Tooltip("Which game event applies the status.")]
    [SerializeField] private TriggerEvent triggerEvent = TriggerEvent.LifeLost;
    [Tooltip("The game state to apply (duration/magnitude live on the status asset).")]
    [SerializeField] private StatusEffectDefinition status;

    public override bool OnLifeLost(AbilityContext context)
    {
        if (triggerEvent != TriggerEvent.LifeLost) return false;
        return ApplyStatus(context);
    }

    public override bool OnBlockSpawned(AbilityContext context, BlockController block, BlockData data)
    {
        if (triggerEvent != TriggerEvent.BlockSpawned) return false;
        return ApplyStatus(context);
    }

    private bool ApplyStatus(AbilityContext context)
    {
        if (status == null || context.Status == null) return false;

        context.Status.Apply(status);
        return true; // counts as triggered: charges (if any) tick down
    }
}
