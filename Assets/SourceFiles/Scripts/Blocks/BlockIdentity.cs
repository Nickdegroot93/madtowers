using UnityEngine;

/// <summary>
/// What this block IS, attached by the Spawner at spawn time: the shape definition it
/// was drawn from and the variant it rolled. Exists because BlockController stores
/// neither (shape was only ever derivable by parsing the GameObject name - a display
/// hack, not a contract). Combo triggers match against Definition by reference.
/// Plain data carrier - no behaviour, no physics.
/// </summary>
public sealed class BlockIdentity : MonoBehaviour
{
    public BlockDefinition Definition { get; private set; }
    public BlockData Variant { get; private set; }

    public void Assign(BlockDefinition definition, BlockData variant)
    {
        Definition = definition;
        Variant = variant;
    }
}
