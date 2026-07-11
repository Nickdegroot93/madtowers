using UnityEngine;

/// <summary>
/// A brick with a LOAD-BEARING LIMIT: it stacks like normal structure, but it reads the real
/// physical weight resting on it (the summed mass of the stack it supports - so a Boulder
/// crushes it outright and Feathers barely register) and crumbles when the load exceeds its limit
/// (~3 normal bricks). The damage is continuous and ratchets: cracks grow fluidly with the
/// worst load it has carried, sand trickles from them while under load, and near the limit
/// the whole brick shivers - the player must SEE "one more thing and it bursts". The crumble
/// destroys the brick through the standard flow (no life charged - the collapse above is the
/// punishment). Fixed sandstone look in every chapter (SandstoneBlockSkin). See BLOCKVARIANTS.md.
/// </summary>
[CreateAssetMenu(fileName = "SandstoneBlockData", menuName = "Stacking/Blocks/Sandstone Block Variant")]
public class SandstoneBlockData : BlockData
{
    [Header("Load limit")]
    [Tooltip("Sustained load (in normal-brick weights) at which the brick crumbles. 3 = two bricks sit fine, the third breaks it; one Boulder (mass 4) is instant death.")]
    [Range(1f, 8f)]
    [SerializeField] private float breakLoadBrickWeights = 3f;
    [Tooltip("Seconds of smoothing on the load reading - landing impacts and Tremor jolts must not read as standing weight.")]
    [Range(0.1f, 1.5f)]
    [SerializeField] private float loadSmoothingSeconds = 0.35f;

    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        if (!block.TryGetComponent(out SandstoneBlockSkin skin))
            skin = block.gameObject.AddComponent<SandstoneBlockSkin>();
        skin.Apply();
    }

    public override void OnLocked(BlockController block)
    {
        if (block == null) return;
        if (block.GetComponent<SandstoneBlockBehaviour>() != null) return;
        block.gameObject.AddComponent<SandstoneBlockBehaviour>()
            .Arm(breakLoadBrickWeights, loadSmoothingSeconds);
    }
}
