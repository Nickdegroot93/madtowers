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

        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController block = blocks[i];
            if (block != null && block.HasLanded && IsVisible(block)) return true;
        }
        return false;
    }

    public override void Activate(AbilityContext context)
    {
        ExtractTargetingSession.Begin();
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
