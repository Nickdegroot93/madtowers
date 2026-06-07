using UnityEngine;
using UnityEngine.Serialization;

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

    [Header("Visuals")]
    [FormerlySerializedAs("colorTint")]
    [SerializeField] private Color colorTint = Color.white;
    [SerializeField] private Sprite spriteOverride;
    [SerializeField] private Material materialOverride;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float Mass => Mathf.Max(0.01f, mass);
    public PhysicsMaterial2D PhysicsMaterial => physicsMaterial;
    public float GravityScaleMultiplier => Mathf.Max(0f, gravityScaleMultiplier);
    public Color ColorTint => colorTint;
    public Sprite SpriteOverride => spriteOverride;
    public Material MaterialOverride => materialOverride;
}
