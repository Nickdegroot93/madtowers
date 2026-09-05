using UnityEngine;

/// <summary>
/// Fixed glacial ice with cloudy depth, bright fractures and trapped air. This hazard uses its own
/// shader; Freeze keeps the original Frost material and its colour-preserving ability visuals.
/// The pane is born fully frozen and remains still, retaining the existing landing squash parent.
/// </summary>
public sealed class IceBlockSkin : BlockVariantSkin
{
    protected override string MaterialResource => "Ice"; // isolated from the Freeze ability
    protected override bool HidesChapterArt => true;
    public override bool BlocksForeignOverlays => false; // preserve original Vine eligibility
    protected override string CellName => "IceCell";
    protected override bool CellsFollowPieceSkin => true;    // ice panes squash WITH the chapter art on landing - a rigid pane over a squashing brick reads as two layers

    public void Apply()
    {
        _ = Random.value; // retain the old RNG draw without randomising the fixed material
        BuildCells();
    }
}
