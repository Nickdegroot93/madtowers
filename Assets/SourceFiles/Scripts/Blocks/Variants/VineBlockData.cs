using UnityEngine;

/// <summary>
/// A brick that grows onto whatever it lands against: shortly after placement it welds itself
/// to every block (or static platform) it touches. A local, earned version of Freeze -
/// the welded cluster still moves as live physics, it just can't come apart at those seams
/// unless the joint's break force is exceeded.
/// </summary>
[CreateAssetMenu(fileName = "VineBlockData", menuName = "Stacking/Blocks/Vine Block Variant")]
public class VineBlockData : BlockData
{
    [Tooltip("Seconds after landing before the vine welds. 0 = weld instantly on landing (no settle, so it can't tilt before gluing).")]
    [Range(0f, 2f)]
    [SerializeField] private float attachDelaySeconds = 0f;
    [Tooltip("Force needed to tear a vine weld apart. Roughly: a resting block exerts ~10 per unit of mass on its support.")]
    [Min(10f)]
    [SerializeField] private float breakForce = 150f;
    [Tooltip("How far beyond its own surface (world units) a neighbour counts as 'touching'.")]
    [Range(0.05f, 0.4f)]
    [SerializeField] private float touchRange = 0.15f;

    // Build the vine overlay as soon as the variant is applied (after the chapter skin), so a falling
    // Vine brick already wears its vines over the chapter colour. Get-or-add avoids a duplicate skin.
    // The spread onto welded neighbours is handled in VineBlockBehaviour.
    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        if (!block.TryGetComponent(out VineBlockSkin skin))
            skin = block.gameObject.AddComponent<VineBlockSkin>();
        skin.Apply();
    }

    public override void OnLocked(BlockController block)
    {
        block.gameObject.AddComponent<VineBlockBehaviour>().Attach(attachDelaySeconds, breakForce, touchRange);
    }
}
