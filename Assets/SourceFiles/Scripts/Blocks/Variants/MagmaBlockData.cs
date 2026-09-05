using UnityEngine;

/// <summary>
/// The Magma variant melts on locking. Connected cells with equal fall distance stay
/// joined as rigid fragments; clearance comes from the lowest cell in each original
/// column. Fragments reuse Pip geometry and normal controlled descent, preserving
/// total per-cell mass and counting the original Magma as one placement.
/// MagmaMelt owns the hand-off; the skin cools into fixed basalt on landing.
/// </summary>
[CreateAssetMenu(fileName = "Magma", menuName = "Stacking/Blocks/Magma")]
public class MagmaBlockData : BlockData
{
    [Header("Melt")]
    [Tooltip("Pip definition supplying each fragment's cell geometry and normal brick data. Connected equal-drop cells join; only the first fragment counts the original placement.")]
    [SerializeField] private BlockDefinition stoneCell;

    [Header("Melt FX (swappable)")]
    [Tooltip("Authored CFXR burst played as each cell solidifies into stone (a base prefab - never a variant; see ABILITIES.md). Null-safe - retains the splat and material cooling.")]
    [SerializeField] private GameObject solidifyEffect;
    [Tooltip("Per-cell scale for the solidify burst (CFXR effects read at ~1 cell at scale 1).")]
    [SerializeField] private float solidifyEffectScale = 0.6f;

    [Header("Molten look")]
    [Tooltip("Legacy molten colour setting retained for serialized compatibility; the fixed magma shader owns fragment heat and cooling.")]
    [SerializeField] private Color moltenColor = new Color(1f, 0.52f, 0.14f, 1f);

    public BlockDefinition StoneCell => stoneCell;
    public GameObject SolidifyEffect => solidifyEffect;
    public float SolidifyEffectScale => Mathf.Max(0.05f, solidifyEffectScale);
    public Color MoltenColor => moltenColor;

    // Dress the falling piece with the fixed, theme-independent magma look (the procedural Lava
    // shader). Visual-only - the material swap never touches physics (PHYSICS.md).
    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        // Get-or-add (the shared variant convention) so a re-apply can't stack a second skin.
        if (!block.TryGetComponent(out MagmaBlockSkin skin))
            skin = block.gameObject.AddComponent<MagmaBlockSkin>();
        skin.Apply();
    }

    // Landed -> melt. MagmaMelt owns the decision (guards, cell capture, hand-off).
    public override void OnLocked(BlockController block)
    {
        MagmaMelt.Run(block, this);
    }
}
