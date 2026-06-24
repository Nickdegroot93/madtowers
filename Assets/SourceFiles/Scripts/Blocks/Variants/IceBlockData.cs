using UnityEngine;

/// <summary>
/// A slippery brick (low-friction IceSurface material on the asset) that carries the Freeze ability's frost
/// look (IceBlockSkin), so an ice block and a frozen block read as the same substance. No special behaviour
/// beyond its slipperiness; the look is born fully iced and still. See BLOCKVARIANTS.md.
/// </summary>
[CreateAssetMenu(fileName = "IceBlockData", menuName = "Stacking/Blocks/Ice Block Variant")]
public class IceBlockData : BlockData
{
    // Build the frost look as soon as the variant is applied (runs after the chapter skin).
    // Get-or-add so a re-apply can't add a second skin. (Base OnApplied is empty; see BLOCKVARIANTS.md.)
    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        if (!block.TryGetComponent(out IceBlockSkin skin))
            skin = block.gameObject.AddComponent<IceBlockSkin>();
        skin.Apply();
    }
}
