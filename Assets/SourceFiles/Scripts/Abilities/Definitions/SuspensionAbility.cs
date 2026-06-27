using UnityEngine;

/// <summary>
/// Rare consumable targeting ability: select one visible placed block and permanently freeze it
/// at its current world coordinates, turning it into an anchor-like static body.
/// </summary>
[CreateAssetMenu(fileName = "Suspension", menuName = "Stacking/Abilities/Suspension")]
public class SuspensionAbility : ConsumableAbility
{
    [Tooltip("The Anchor block variant the frozen block is converted into, so it adopts the " +
             "shared anchor look (tint/skin) instead of staying visually identical.")]
    [SerializeField] private BlockData anchorVariant;

    public override bool CanActivate(AbilityContext context)
    {
        if (ExtractTargetingSession.IsActive) return false;
        // excludeFrozen: a held charge can't be wasted on an existing anchor/Freeze/previous target.
        return ExtractTargetingSession.HasAnyTargetable(excludeFrozen: true);
    }

    public override void Activate(AbilityContext context)
    {
        ExtractTargetingSession.BeginSuspension(anchorVariant);
    }
}
