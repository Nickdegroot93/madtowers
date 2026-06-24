using UnityEngine;

/// <summary>
/// A brick that mirrors left/right steering while it falls (the inversion itself lives on the
/// <c>invertHorizontalControls</c> flag, read by the controller - this subclass only adds the look).
/// Unlike the material variants, "dizzy" isn't a substance, so its look is an optical cue rather than
/// a material: each cell wears a swirling pink-marble vortex (the kept chapter brick stays solid behind
/// it). The vortex churns and periodically REVERSES direction - the on-block metaphor for "left becomes
/// right", which also closes the clarity gap BLOCKVARIANTS.md flags for Dizzy.
/// </summary>
[CreateAssetMenu(fileName = "DizzyBlockData", menuName = "Stacking/Blocks/Dizzy Block Variant")]
public class DizzyBlockData : BlockData
{
    // Build the vortex overlay as soon as the variant is applied (after the chapter skin), so a falling
    // Dizzy brick already wears it over the chapter colour. Get-or-add avoids a duplicate skin.
    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        if (!block.TryGetComponent(out DizzyBlockSkin skin))
            skin = block.gameObject.AddComponent<DizzyBlockSkin>();
        skin.Apply();
    }
}
