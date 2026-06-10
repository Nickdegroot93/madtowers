using UnityEngine;

/// <summary>
/// A brick that jolts the whole tower the moment it lands - survive the shake.
/// </summary>
[CreateAssetMenu(fileName = "TremorBlockData", menuName = "Stacking/Blocks/Tremor Block Variant")]
public class TremorBlockData : BlockData
{
    [Tooltip("Horizontal velocity jolt applied to every landed block, alternating direction per block.")]
    [Range(0f, 3f)]
    [SerializeField] private float joltSpeed = 0.7f;

    public override void OnLocked(BlockController block)
    {
        var blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController other = blocks[i];
            if (other == null || other == block || !other.HasLanded) continue;

            other.ApplyJolt(new Vector2((i % 2 == 0 ? 1f : -1f) * joltSpeed, 0f));
        }
    }
}
