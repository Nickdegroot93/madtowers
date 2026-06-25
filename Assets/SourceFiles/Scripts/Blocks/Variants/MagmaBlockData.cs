using UnityEngine;

/// <summary>
/// The Magma Block variant: a molten, wobbling piece that falls and lands like any
/// other brick, but the instant it locks it MELTS - each of its cells liquefies, flows
/// straight down into the empty gap beneath that column, and solidifies into a solid
/// stone Pip. A tetromino lands as four counting stone cells that conform to whatever
/// terrain is underneath (BLOCKS.md: the Fission counting model - the magma itself never
/// counts, each stone cell does).
///
/// All the heavy lifting is reuse: the melt rides the normal controlled-piece landing
/// path (one auto-dropped Pip per cell, gravity finds the gap), so PHYSICS.md is honoured
/// without a single new physics rule. This class is just the two hooks - dress the falling
/// piece as molten magma (OnApplied), kick off the melt when it lands (OnLocked).
/// </summary>
[CreateAssetMenu(fileName = "Magma", menuName = "Stacking/Blocks/Magma")]
public class MagmaBlockData : BlockData
{
    [Header("Melt")]
    [Tooltip("The 1x1 stone brick each cell solidifies into (Block_Pip's BlockDefinition). A normal counting brick - the magma tetromino becomes four placements.")]
    [SerializeField] private BlockDefinition stoneCell;

    [Header("Melt FX (swappable)")]
    [Tooltip("Authored CFXR burst played as each cell solidifies into stone (a base prefab - never a variant; see ABILITIES.md). Null-safe - degrades to the squish + colour fuse.")]
    [SerializeField] private GameObject solidifyEffect;
    [Tooltip("Per-cell scale for the solidify burst (CFXR effects read at ~1 cell at scale 1).")]
    [SerializeField] private float solidifyEffectScale = 0.6f;

    [Header("Molten look")]
    [Tooltip("Colour a falling cell glows while molten, before it cools to the level's normal 1x1 look on landing.")]
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
