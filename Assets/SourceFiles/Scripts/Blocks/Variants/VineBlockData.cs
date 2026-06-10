using UnityEngine;

/// <summary>
/// A brick that grows onto whatever it lands against: shortly after placement it welds itself
/// to every block (or static platform) it touches. A local, earned version of Cement Tower -
/// the welded cluster still moves as live physics, it just can't come apart at those seams
/// unless the joint's break force is exceeded.
/// </summary>
[CreateAssetMenu(fileName = "VineBlockData", menuName = "Stacking/Blocks/Vine Block Variant")]
public class VineBlockData : BlockData
{
    [Tooltip("Seconds after landing before the vine grabs hold - gives the brick a beat to seat first.")]
    [Range(0.1f, 2f)]
    [SerializeField] private float attachDelaySeconds = 0.4f;
    [Tooltip("Force needed to tear a vine weld apart. Roughly: a resting block exerts ~10 per unit of mass on its support.")]
    [Min(10f)]
    [SerializeField] private float breakForce = 150f;
    [Tooltip("How far beyond its own surface (world units) a neighbour counts as 'touching'.")]
    [Range(0.05f, 0.4f)]
    [SerializeField] private float touchRange = 0.15f;

    public override void OnLocked(BlockController block)
    {
        block.gameObject.AddComponent<VineBlockBehaviour>().Attach(attachDelaySeconds, breakForce, touchRange);
    }
}
