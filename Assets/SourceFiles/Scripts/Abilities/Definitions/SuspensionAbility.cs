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

        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block != null && block.HasLanded && !block.IsFrozenInPlace && BlockQuery.IsOnScreen(block)) return true;
        }
        return false;
    }

    public override void Activate(AbilityContext context)
    {
        ExtractTargetingSession.BeginSuspension(anchorVariant);
    }
}
