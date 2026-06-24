using UnityEngine;

/// <summary>
/// A brick that freezes exactly where it lands, however badly it is placed - the player can
/// anchor a tower or build their own platforms with it.
/// </summary>
[CreateAssetMenu(fileName = "AnchorBlockData", menuName = "Stacking/Blocks/Anchor Block Variant")]
public class AnchorBlockData : BlockData
{
    // Build the fixed gunmetal look as soon as the variant is applied (runs after the chapter skin).
    // Get-or-add so a re-apply (e.g. Suspension turning a landed block into an anchor) can't add a
    // second skin. (Base OnApplied is empty; see BLOCKVARIANTS.md.)
    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        if (!block.TryGetComponent(out AnchorBlockSkin skin))
            skin = block.gameObject.AddComponent<AnchorBlockSkin>();
        skin.Apply();
    }

    public override void OnLocked(BlockController block)
    {
        block.FreezeInPlace();
        if (block != null && block.TryGetComponent(out AnchorBlockSkin skin)) skin.PlayLockFlash();
    }
}
