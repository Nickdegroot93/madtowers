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

    // Whether this block has already been resolved by the loss system. Two LossZone
    // detectors (the camera-relative sweep and the fixed backstop trigger below the floor)
    // can both catch the same falling piece - a piece driven straight off-screen hits both -
    // so the life charge must be guarded to fire exactly once, the same way the count -1 is.
    private bool _lossConsumed;

    // Per-INSTANCE opt-out of the placement count, for fragments that are the debris of a
    // placement rather than placements in their own right. It cannot live on the variant:
    // the Pip is a real playable block (the Pip ability drops one, Fission shatters a piece
    // into several the player then places by hand) and must count normally there. Only the
    // pips a Magma block melts into are suppressed - one magma placement is one block, not
    // four, or a 7-block puzzle wave clears in two placements (Nick 2026-08-09).
    public bool SuppressPlacedCount { get; private set; }

    public void SuppressPlacementCount() => SuppressPlacedCount = true;

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

    /// <summary>Returns true at most ONCE - for the first loss detector to resolve this
    /// block. Later detectors get false and must skip, so a single block can only ever
    /// cost one life.</summary>
    public bool TryConsumeLoss()
    {
        if (_lossConsumed) return false;
        _lossConsumed = true;
        return true;
    }
}
