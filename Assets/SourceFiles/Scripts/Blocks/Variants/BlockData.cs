using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// One brick variant = one asset in Assets/Data/Blocks/. Stats live in the serialized fields;
/// behaviour lives in the virtual hooks - subclass this (see AnchorBlockData) when a variant
/// needs to do something, and override the hook for the moment it should act.
/// </summary>
[CreateAssetMenu(fileName = "BlockData", menuName = "Stacking/Blocks/Block Variant")]
public class BlockData : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string displayName = "Normal";

    [Header("Physics")]
    [FormerlySerializedAs("mass")]
    [SerializeField] private float mass = 1.0f;
    [FormerlySerializedAs("physMaterial")]
    [SerializeField] private PhysicsMaterial2D physicsMaterial;
    [SerializeField] private float gravityScaleMultiplier = 1f;

    [Header("Control")]
    [Tooltip("Untick for bricks that cannot be rotated while falling (Stubborn brick).")]
    [SerializeField] private bool canRotate = true;
    [Tooltip("Tick to mirror left/right steering for this brick (Dizzy brick).")]
    [SerializeField] private bool invertHorizontalControls = false;

    [Header("Visuals")]
    [FormerlySerializedAs("colorTint")]
    [SerializeField] private Color colorTint = Color.white;
    [SerializeField] private Sprite spriteOverride;
    [SerializeField] private Material materialOverride;

    [Header("Scoring & loss (independent - any combination is valid)")]
    [Tooltip("Does placing this piece count toward the live block total (+1 placed, -1 when it leaves)? Untick for pieces that aren't 'real' blocks, e.g. the Bullet projectile.")]
    [SerializeField] private bool countsAsPlacedBlock = true;
    [Tooltip("Does this piece cost a life when it falls off the bottom? Untick for pieces that should never punish a drop, e.g. the Bullet, or a future 'free' block that still counts when placed but is safe to lose.")]
    [SerializeField] private bool costsLifeWhenLost = true;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public bool CountsAsPlacedBlock => countsAsPlacedBlock;
    public bool CostsLifeWhenLost => costsLifeWhenLost;
    public float Mass => Mathf.Max(0.01f, mass);
    public PhysicsMaterial2D PhysicsMaterial => physicsMaterial;
    public float GravityScaleMultiplier => Mathf.Max(0f, gravityScaleMultiplier);
    public bool CanRotate => canRotate;
    public bool InvertHorizontalControls => invertHorizontalControls;
    public Color ColorTint => colorTint;
    public Sprite SpriteOverride => spriteOverride;
    public Material MaterialOverride => materialOverride;

    // Whether a live block costs a life when it falls off, resolved via its BlockIdentity.
    // A block with no variant data is a normal block (costs a life). The counting side is
    // NOT re-derived here - it's recorded on the block at lock (BlockIdentity.Counted) so
    // the -1 fires exactly once; only the life decision needs a per-loss lookup.
    public static bool CostsLife(BlockController block)
    {
        BlockData data = block != null && block.TryGetComponent(out BlockIdentity identity) ? identity.Variant : null;
        return data == null || data.costsLifeWhenLost;
    }

    /// <summary>Called once when the variant is assigned to a freshly spawned piece.</summary>
    public virtual void OnApplied(BlockController block) { }

    /// <summary>Called when the piece lands and control hands off to physics.</summary>
    public virtual void OnLocked(BlockController block) { }
}
