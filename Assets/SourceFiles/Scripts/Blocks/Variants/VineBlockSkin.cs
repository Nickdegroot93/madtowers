using UnityEngine;

/// <summary>
/// Mossy stone with woody stems and folded leaves. The intrinsic Vine brick owns a fixed material;
/// spread from a welded neighbour draws only the plant and keeps that neighbour's chapter art.
/// Growth timing and contact direction are shared by both presentations.
/// </summary>
public sealed class VineBlockSkin : BlockVariantSkin
{
    private static readonly int GrowthId = Shader.PropertyToID("_Growth");
    private static readonly int StoneBodyId = Shader.PropertyToID("_StoneBody");
    private static readonly int RootDirId = Shader.PropertyToID("_RootDir");
    private const float GrowDuration = 0.5f;

    protected override string MaterialResource => "Vine";
    protected override bool HidesChapterArt => _ownsMaterial;
    public override bool BlocksForeignOverlays => false; // preserve Vine's original spread eligibility
    protected override string CellName => "VineCell";
    protected override int SortOrderOffset => 6;       // always above any other variant overlay (e.g. ice frost)

    private Vector2 _rootDir = new Vector2(0f, 1f);
    private bool _ownsMaterial;
    private float _growAge = -1f; // <0 = done growing

    /// <summary>Vines rooted at the bottom edge - the falling Vine block's own look.</summary>
    public void Apply()
    {
        bool promoted = IsBuilt && !_ownsMaterial;
        _ownsMaterial = true;
        if (promoted) { HideChapterArt(); SetCellsFloat(StoneBodyId, 1f); }
        Grow(new Vector2(0f, 1f));
    }

    /// <summary>Creep vines onto this block from the side facing the source. <paramref name="rootDir"/>
    /// points from the contact edge into this block (the growth direction).</summary>
    public void GrowFrom(Vector2 rootDir) => Grow(rootDir);

    private void Grow(Vector2 rootDir)
    {
        if (IsBuilt) { BuildCells(); return; } // reset reapply tint without restarting growth
        _rootDir = rootDir;
        _ = Random.value; // retain the existing RNG draw; the authored material itself is deterministic
        BuildCells();
        _growAge = 0f;
    }

    protected override void ConfigureCell(int index, int col, int row, SpriteRenderer overlay, MaterialPropertyBlock mpb)
    {
        mpb.SetFloat(StoneBodyId, _ownsMaterial ? 1f : 0f);
        mpb.SetVector(RootDirId, new Vector4(_rootDir.x, _rootDir.y, 0f, 0f));
        mpb.SetFloat(GrowthId, 0f);
    }

    private void LateUpdate()
    {
        if (_growAge < 0f) return;

        _growAge += Time.deltaTime; // scaled - a pause freezes the growth (PHYSICS.md)
        float g = Mathf.Clamp01(_growAge / GrowDuration);
        SetCellsFloat(GrowthId, g);
        if (g >= 1f) _growAge = -1f;
    }
}
