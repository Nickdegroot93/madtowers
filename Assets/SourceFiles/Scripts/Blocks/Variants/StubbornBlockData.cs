using UnityEngine;

/// <summary>
/// A brick that refuses to rotate while it falls (the lock itself lives on the <c>canRotate=false</c> flag,
/// read by BlockController - this subclass only adds the look and the "no" cue). Like Dizzy, "stubborn" is a
/// trait, not a material, so its look is a character cue: each cell wears a rusted iron gear bound by a chain
/// and a locking pin, drawn over the kept chapter brick. A gear is rotation made physical, so a chained,
/// pinned gear reads unambiguously as "rotation locked" (and stays distinct from Anchor's freeze). When the
/// player tries to rotate, the gear lurches against its chain and snaps back - the on-block cue that closes
/// the clarity gap BLOCKVARIANTS.md flags for Stubborn.
/// </summary>
[CreateAssetMenu(fileName = "StubbornBlockData", menuName = "Stacking/Blocks/Stubborn Block Variant")]
public class StubbornBlockData : BlockData
{
    // Build the gear/chain overlay as soon as the variant is applied (after the chapter skin), so a falling
    // Stubborn brick already wears it over the chapter colour. Get-or-add avoids a duplicate skin.
    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        if (!block.TryGetComponent(out StubbornBlockSkin skin))
            skin = block.gameObject.AddComponent<StubbornBlockSkin>();
        skin.Apply();
    }

    // The player pressed rotate and CanRotate said no: strain the gear against its chain in that direction.
    public override void OnRotationDenied(BlockController block, int direction)
    {
        if (block != null && block.TryGetComponent(out StubbornBlockSkin skin)) skin.PlayRefuse(direction);
    }

    // On lock, the brick must stop flinching (no transform writes on a landed body, PHYSICS.md I1).
    public override void OnLocked(BlockController block)
    {
        if (block != null && block.TryGetComponent(out StubbornBlockSkin skin)) skin.OnLocked();
    }
}
