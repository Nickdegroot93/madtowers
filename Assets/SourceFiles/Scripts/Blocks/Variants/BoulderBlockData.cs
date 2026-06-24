using UnityEngine;

/// <summary>
/// A very heavy brick (mass 4) that strains everything below it. It carries its own fixed,
/// theme-independent look - a dark cracked-basalt slab built by BoulderBlockSkin - so it is instantly
/// recognisable in any chapter, and lands with a heavy slam. No freeze, no special physics beyond its
/// mass; the weight is the gameplay, the slam is the feel.
/// </summary>
[CreateAssetMenu(fileName = "BoulderBlockData", menuName = "Stacking/Blocks/Boulder Block Variant")]
public class BoulderBlockData : BlockData
{
    // Build the fixed rock look as soon as the variant is applied (runs after the chapter skin).
    // Get-or-add so a re-apply can't add a second skin. (Base OnApplied is empty; see BLOCKVARIANTS.md.)
    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        if (!block.TryGetComponent(out BoulderBlockSkin skin))
            skin = block.gameObject.AddComponent<BoulderBlockSkin>();
        skin.Apply();
    }

    public override void OnLocked(BlockController block)
    {
        if (block != null && block.TryGetComponent(out BoulderBlockSkin skin)) skin.PlayLandImpact();
    }
}
