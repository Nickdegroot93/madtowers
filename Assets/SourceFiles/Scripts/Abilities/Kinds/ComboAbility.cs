using UnityEngine;

/// <summary>
/// Fires when its block-pattern trigger matches on the tower. TRIGGERS and EFFECTS are
/// deliberately separate: the trigger is a shared ComboTriggerDefinition asset detected
/// once by the ComboDetector; every owned ComboAbility subscribed to that trigger fires
/// from the same match (own two abilities on one pattern - both go off). Charges follow
/// the passive convention: 0 = fires every time the pattern appears; N = consumed after
/// N fires.
/// </summary>
public abstract class ComboAbility : AbilityDefinition
{
    [Header("Combo")]
    [Tooltip("The block pattern that fires this ability. Triggers are shared assets - several abilities may subscribe to the same one.")]
    [SerializeField] private ComboTriggerDefinition trigger;

    [Tooltip("0 = fires on every pattern match. N = consumed after N fires (1 = once per run).")]
    [Min(0)]
    [SerializeField] private int charges;

    public ComboTriggerDefinition Trigger => trigger;
    public int Charges => charges;

    /// <summary>The trigger matched (and survived settle revalidation). Do not retain
    /// block references from the match beyond this call - they can be destroyed any time.</summary>
    public abstract void OnComboFired(AbilityContext context, ComboMatch match);
}
