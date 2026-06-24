using UnityEngine;

/// <summary>
/// A brick that detonates shortly after landing, deleting itself and every block touching it.
/// No blast impulse - blocks above simply lose their support and sag, so the tower is wounded,
/// not launched. It carries its own fixed, theme-independent look - an iron powder-keg casing whose
/// seams glow brighter and faster as the fuse burns (BombBlockSkin) - so it reads as explosive in any
/// chapter, even while still falling.
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

    [Header("Detonation FX (CFXR prefabs - drag to swap, see Vfx.cs)")]
    [Tooltip("Played once at the bomb's centre when it detonates (e.g. CFXR4 Explosion Orange (HDR) + Smoke).")]
    [SerializeField] private GameObject explosionEffect;
    [Tooltip("World-unit size of the explosion. CFXR reads at ~1 cell at scale 1, so a blast wants a few cells.")]
    [Min(0.1f)]
    [SerializeField] private float explosionScale = 2.5f;
    [Tooltip("A small one-shot puff burst from every cell of each destroyed neighbour (e.g. CFXR2 Debris Hit), on top of the standard shard shatter.")]
    [SerializeField] private GameObject breakPuffEffect;

    // Build the fixed iron casing as soon as the variant is applied (runs after the chapter skin).
    // Get-or-add so a re-apply can't add a second skin. (Base OnApplied is empty; see BLOCKVARIANTS.md.)
    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        if (!block.TryGetComponent(out BombBlockSkin skin))
            skin = block.gameObject.AddComponent<BombBlockSkin>();
        skin.Apply();
    }

    public override void OnLocked(BlockController block)
    {
        if (block == null) return;
        if (block.TryGetComponent(out BombBlockSkin skin)) skin.Ignite();
        block.gameObject.AddComponent<BombBlockBehaviour>()
            .Arm(fuseSeconds, touchRange, explosionEffect, explosionScale, breakPuffEffect);
    }
}
