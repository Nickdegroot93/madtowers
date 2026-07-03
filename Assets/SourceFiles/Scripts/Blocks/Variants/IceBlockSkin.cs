using UnityEngine;

/// <summary>
/// The Ice look: reuses the Freeze ability's procedural Frost material (Resources/Frost) so an ice block
/// and a frozen block read as the same substance - a translucent cyan ice pane with a glass bevel, cloudy
/// mottle, scratches and branching frost cracks (per-cell seeded pattern + quarter-turn). Born fully iced
/// (_Freeze = 1) and DEAD STILL - the deliberate contrast with Feather (warm, soft, floating). Overlay mode
/// keeps the chapter brick rendering under the translucent ice, so each ice block carries a hint of its
/// chapter colour. Slipperiness is the IceSurface physics material on the asset. See BLOCKVARIANTS.md.
/// </summary>
public sealed class IceBlockSkin : BlockVariantSkin
{
    private static readonly int FreezeId = Shader.PropertyToID("_Freeze");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int PatternId = Shader.PropertyToID("_Pattern");
    private static readonly int TurnId = Shader.PropertyToID("_Turn");
    private static readonly int BodyOpacityId = Shader.PropertyToID("_BodyOpacity");
    private static readonly int ColorPreserveId = Shader.PropertyToID("_ColorPreserve");

    protected override string MaterialResource => "Frost"; // reuse the Freeze ability's ice material
    protected override bool HidesChapterArt => false;       // keep the brick under the translucent ice (chapter-colour hint)
    protected override string CellName => "IceCell";

    private float _blockSeed;

    /// <summary>Build the ice panes. Called from IceBlockData.OnApplied.</summary>
    public void Apply()
    {
        _blockSeed = Random.value; // per-instance so crack patterns vary block to block
        BuildCells();
    }

    protected override void ConfigureCell(int index, int col, int row, SpriteRenderer overlay, MaterialPropertyBlock mpb)
    {
        float seed = (_blockSeed + index * 0.6180339f) % 1f;
        mpb.SetFloat(FreezeId, 1f);                                       // fully iced (reads as ice while falling - no crawl)
        mpb.SetFloat(SeedId, seed * 20f);
        mpb.SetFloat(PatternId, Mathf.Floor(((seed * 7.13f) % 1f) * 5f)); // one of the 5 crack patterns
        mpb.SetFloat(TurnId, Mathf.Floor(((seed * 3.71f) % 1f) * 4f));    // quarter-turn the pattern
        mpb.SetFloat(BodyOpacityId, 0.78f);                              // translucent enough for a chapter-colour hint
        mpb.SetFloat(ColorPreserveId, 0.20f);                            // ...but properly GLACIAL BLUE, unlike Freeze
                                                                         // which keeps the victim's colour recognisable
    }
}
