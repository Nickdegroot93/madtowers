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
            if (block != null && block.HasLanded && !block.IsFrozenInPlace && IsVisible(block)) return true;
        }
        return false;
    }

    public override void Activate(AbilityContext context)
    {
        ExtractTargetingSession.BeginSuspension(anchorVariant);
    }

    private static bool IsVisible(BlockController block)
    {
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic) return true;
        if (!block.TryGetWorldBounds(out Bounds bounds)) return false;

        Vector3 min = camera.WorldToViewportPoint(bounds.min);
        Vector3 max = camera.WorldToViewportPoint(bounds.max);
        return max.x >= 0f && min.x <= 1f && max.y >= 0f && min.y <= 1f;
    }
}
