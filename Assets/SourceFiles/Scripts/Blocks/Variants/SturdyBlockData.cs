using UnityEngine;

/// <summary>
/// A brick that freezes exactly where it lands, however badly it is placed - the player can
/// build their own platforms with it.
/// </summary>
[CreateAssetMenu(fileName = "SturdyBlockData", menuName = "Stacking/Blocks/Sturdy Block Variant")]
public class SturdyBlockData : BlockData
{
    public override void OnLocked(BlockController block)
    {
        block.MakeSturdy();
    }
}
