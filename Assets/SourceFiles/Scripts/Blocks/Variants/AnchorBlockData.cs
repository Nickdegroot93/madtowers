using UnityEngine;

/// <summary>
/// A brick that freezes exactly where it lands, however badly it is placed - the player can
/// anchor a tower or build their own platforms with it.
/// </summary>
[CreateAssetMenu(fileName = "AnchorBlockData", menuName = "Stacking/Blocks/Anchor Block Variant")]
public class AnchorBlockData : BlockData
{
    public override void OnLocked(BlockController block)
    {
        block.FreezeInPlace();
    }
}
