using UnityEngine;

/// <summary>
/// A very light brick (mass 0.25) - shoved around by every later landing. It carries a fixed, theme-
/// independent downy-feather look (FeatherBlockSkin) that gently floats and sways (the "light" read), and
/// it lands soft - a flutter, no slam. No special physics beyond its low mass. See BLOCKVARIANTS.md.
/// </summary>
[CreateAssetMenu(fileName = "FeatherBlockData", menuName = "Stacking/Blocks/Feather Block Variant")]
public class FeatherBlockData : BlockData
{
    // Build the downy look as soon as the variant is applied (runs after the chapter skin).
    // Get-or-add so a re-apply can't add a second skin. (Base OnApplied is empty; see BLOCKVARIANTS.md.)
    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        if (!block.TryGetComponent(out FeatherBlockSkin skin))
            skin = block.gameObject.AddComponent<FeatherBlockSkin>();
        skin.Apply();
    }

    public override void OnLocked(BlockController block)
    {
        if (block != null && block.TryGetComponent(out FeatherBlockSkin skin)) skin.PlayLandFlutter();
    }
}
