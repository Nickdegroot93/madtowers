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

    // Whether this block's placement was counted into the live block total. Recorded
    // once at lock (the only place a +1 happens) so the matching -1 fires exactly once
    // when it leaves - the count never depends on re-deriving "does it count + landed?"
    // at each destroy site, and a double-remove is a no-op instead of a hidden clamp.
    private bool _countedAsPlaced;

    public void Assign(BlockDefinition definition, BlockData variant)
    {
        Definition = definition;
        Variant = variant;
    }

    public void MarkCountedAsPlaced() => _countedAsPlaced = true;

    /// <summary>Returns true at most ONCE - the first call after the block was counted.
    /// The caller decrements the live total only on a true result.</summary>
    public bool TryConsumeCounted()
    {
        if (!_countedAsPlaced) return false;
        _countedAsPlaced = false;
        return true;
    }
}
