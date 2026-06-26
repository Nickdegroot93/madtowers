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
            // Mirror ExtractTargetingSession.CanTarget: maws can't be extracted, so a screen of only maws
            // leaves the charge unusable rather than no-op'ing on activation.
            if (block != null && block.HasLanded
                && block.GetComponent<MawBlockSkin>() == null && BlockQuery.IsOnScreen(block)) return true;
        }
        return false;
    }

    public override void Activate(AbilityContext context)
    {
        ExtractTargetingSession.Begin();
    }
}
