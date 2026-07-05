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
    [Tooltip("Untick for bricks that cannot be rotated while falling (Locked brick).")]
    [SerializeField] private bool canRotate = true;
    [Tooltip("Tick to mirror left/right steering for this brick (Vortex brick).")]
    [SerializeField] private bool invertHorizontalControls = false;

    [Header("Visuals")]
    [FormerlySerializedAs("colorTint")]
    [SerializeField] private Color colorTint = Color.white;
    [SerializeField] private Sprite spriteOverride;
    [SerializeField] private Material materialOverride;

    [Header("Scoring & loss (independent - any combination is valid)")]
    [Tooltip("Does placing this piece count toward the live block total (+1 placed, -1 when it leaves)? Untick for pieces that aren't 'real' blocks, e.g. a projectile-style piece.")]
    [SerializeField] private bool countsAsPlacedBlock = true;
    [Tooltip("Does this piece cost a life when it falls off the bottom? Untick for pieces that should never punish a drop, e.g. a future 'free' block that still counts when placed but is safe to lose.")]
    [SerializeField] private bool costsLifeWhenLost = true;

    [Header("Classification")]
    [Tooltip("Is this a HOSTILE/hazard variant (Maw, Vortex, Bomb, Tremor, Ice, Locked)? Drives the " +
             "'all hazards' abilities - Ward neutralises the next hazard, Purifier suppresses every hazard - " +
             "so they pick up a new hostile brick automatically. Leave false for normal/helpful variants.")]
    [SerializeField] private bool isHazard;

    [Header("Vault")]
    [Tooltip("One line for the Vault card, e.g. 'Detonates after landing, dropping its neighbours'.")]
    [SerializeField] private string behaviourSummary = "";
    [Tooltip("The Vault detail / debut-modal copy: 2-4 sentences on what the brick does and how to play it.")]
    [SerializeField, TextArea(2, 5)] private string vaultDescription = "";

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public bool CountsAsPlacedBlock => countsAsPlacedBlock;
    public bool CostsLifeWhenLost => costsLifeWhenLost;
    /// <summary>True for hostile/hazard variants - the single source of truth for the "all hazards"
    /// abilities (Ward, Purifier), so a new hazard variant participates the moment it sets this.</summary>
    public bool IsHazard => isHazard;
    public float Mass => Mathf.Max(0.01f, mass);
    public PhysicsMaterial2D PhysicsMaterial => physicsMaterial;
    public float GravityScaleMultiplier => Mathf.Max(0f, gravityScaleMultiplier);
    public bool CanRotate => canRotate;
    public bool InvertHorizontalControls => invertHorizontalControls;
    public Color ColorTint => colorTint;
    public Sprite SpriteOverride => spriteOverride;
    public Material MaterialOverride => materialOverride;
    /// <summary>One-line Vault card blurb; empty when unauthored (the UI hides the line).</summary>
    public string BehaviourSummary => behaviourSummary ?? "";
    /// <summary>The Vault/debut description; empty when unauthored (callers fall back or hide).</summary>
    public string VaultDescription => vaultDescription ?? "";

    // Whether a live block costs a life when it falls off, resolved via its BlockIdentity.
    // A block with no variant data is a normal block (costs a life). The counting side is
    // NOT re-derived here - it's recorded on the block at lock (BlockIdentity.Counted) so
    // the -1 fires exactly once; only the life decision needs a per-loss lookup.
    public static bool CostsLife(BlockController block)
    {
        BlockData data = block != null && block.TryGetComponent(out BlockIdentity identity) ? identity.Variant : null;
        return data == null || data.costsLifeWhenLost;
    }

    /// <summary>Called once when the variant is assigned to a freshly spawned piece (after the chapter
    /// skin is built). Variants with a procedural look (Anchor, Boulder, Vine, Magma) override this to
    /// add their <c>…BlockSkin</c> component; see BLOCKVARIANTS.md.</summary>
    public virtual void OnApplied(BlockController block) { }

    /// <summary>Called when the piece lands and control hands off to physics.</summary>
    public virtual void OnLocked(BlockController block) { }

    /// <summary>Called when the player presses rotate on a falling piece that <see cref="CanRotate"/>
    /// forbids (Locked) - the moment the rotation is refused. <paramref name="direction"/> is -1 for a
    /// left press, +1 for right. Lets a variant play an on-block "no" cue (Locked's gear strains against
    /// its chain); see BLOCKVARIANTS.md. Purely cosmetic - the rotation itself stays blocked.</summary>
    public virtual void OnRotationDenied(BlockController block, int direction) { }
}
