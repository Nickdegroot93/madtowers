using UnityEngine;

/// <summary>
/// A brick that detonates shortly after landing, deleting itself and every block touching it.
/// No blast impulse - blocks above simply lose their support and sag, so the tower is wounded,
/// not launched.
/// </summary>
[CreateAssetMenu(fileName = "BombBlockData", menuName = "Stacking/Blocks/Bomb Block Variant")]
public class BombBlockData : BlockData
{
    [Tooltip("Seconds between landing and detonation.")]
    [Min(0.2f)]
    [SerializeField] private float fuseSeconds = 1f;
    [Tooltip("How far beyond its own surface (world units) a block counts as 'touching'. Covers the small collider clearance between adjacent bricks.")]
    [Range(0.05f, 0.4f)]
    [SerializeField] private float touchRange = 0.15f;

    public override void OnLocked(BlockController block)
    {
        block.gameObject.AddComponent<BombBlockBehaviour>().Arm(fuseSeconds, touchRange);
    }
}
