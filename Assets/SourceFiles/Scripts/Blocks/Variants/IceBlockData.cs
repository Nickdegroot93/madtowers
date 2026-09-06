using UnityEngine;

/// <summary>
/// A slippery brick (low-friction IceSurface material on the asset) that carries the Freeze ability's frost
/// look (IceBlockSkin), so an ice block and a frozen block read as the same substance. Hook-dependent
/// placements release with a small outward slip; the look is born fully iced and still. See BLOCKVARIANTS.md.
/// </summary>
[CreateAssetMenu(fileName = "IceBlockData", menuName = "Stacking/Blocks/Ice Block Variant")]
public class IceBlockData : BlockData
{
    // Start sliding at half a cell per second. Without this, released S/Z hooks can
    // catch against the ledge and sleep in a shallow lean even with IceSurface friction.
    // Called once, only after a verified ice hook becomes Dynamic; physics owns the rest.
    internal void SlipOffHook(BlockController block, int direction)
    {
        Rigidbody2D body = block.GetComponent<Rigidbody2D>();
        body.linearVelocity += Vector2.right * (direction * 0.5f * block.GridSpacing);
    }

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
