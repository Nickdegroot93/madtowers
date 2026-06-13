using UnityEngine;

/// <summary>
/// The Bullet projectile's variant: a normal falling piece in every way (steering,
/// fast drop, flick) until first contact - then it destroys whatever dynamic tower
/// block it landed on, and itself, in one burst. Floors, islands and frozen
/// (cemented/anchored) blocks are bulletproof: the shot is wasted.
///
/// The two impact looks are authored prefab fields (Cartoon FX CFXR prefabs) - swap
/// the effect by dragging a different prefab onto the slot, no code change.
/// </summary>
[CreateAssetMenu(fileName = "BulletData", menuName = "Stacking/Blocks/Bullet")]
public class BulletBlockData : BlockData
{
    [Header("Bullet impact FX (swappable)")]
    [Tooltip("Plays on the block the bullet destroys (a hit/break effect).")]
    [SerializeField] private GameObject impactEffect;
    [Tooltip("Plays when the bullet hits something bulletproof - the dud (small dust).")]
    [SerializeField] private GameObject wastedEffect;
    [Tooltip("Size multiplier. The effect already scales to the block it hits (a long I-piece bursts bigger than a square); 1 ≈ the effect spans the block, raise for a punchier burst.")]
    [SerializeField] private float effectScale = 1f;

    public GameObject ImpactEffect => impactEffect;
    public GameObject WastedEffect => wastedEffect;
    public float EffectScale => effectScale;

    public override void OnLocked(BlockController block)
    {
        BulletImpact.Run(block, this);
    }
}
