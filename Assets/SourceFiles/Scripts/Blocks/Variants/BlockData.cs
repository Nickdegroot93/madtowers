using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// One brick variant = one asset in Assets/Data/Blocks/. Stats live in the serialized fields;
/// behaviour lives in the virtual hooks - subclass this (see SturdyBlockData) when a variant
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

    [Header("Shape")]
    [Tooltip("Uniform scale of the whole piece. 2 = double-size brick that fits nowhere.")]
    [SerializeField] private float sizeScale = 1f;

    [Header("Visuals")]
    [FormerlySerializedAs("colorTint")]
    [SerializeField] private Color colorTint = Color.white;
    [SerializeField] private Sprite spriteOverride;
    [SerializeField] private Material materialOverride;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float Mass => Mathf.Max(0.01f, mass);
    public PhysicsMaterial2D PhysicsMaterial => physicsMaterial;
    public float GravityScaleMultiplier => Mathf.Max(0f, gravityScaleMultiplier);
    public float SizeScale => Mathf.Max(0.1f, sizeScale);
    public Color ColorTint => colorTint;
    public Sprite SpriteOverride => spriteOverride;
    public Material MaterialOverride => materialOverride;

    /// <summary>Called once when the variant is assigned to a freshly spawned piece.</summary>
    public virtual void OnApplied(BlockController block) { }

    /// <summary>Called when the piece lands and control hands off to physics.</summary>
    public virtual void OnLocked(BlockController block) { }
}
