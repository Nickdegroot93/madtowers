using UnityEngine;

/// <summary>
/// The Vine look: stems + leaves (procedural Resources/Vine shader) laid OVER the kept chapter art - the
/// block keeps its real colour, vines just grow on top. Unlike the replace-mode skins it does not hide the
/// chapter renderers. The vines grow in (animated _Growth) and sway forever. Reused for the spread:
/// VineBlockBehaviour calls <see cref="GrowFrom"/> on each welded neighbour so vines creep in from the
/// contact edge. A random base seed per instance keeps blocks from cloning. See BLOCKVARIANTS.md.
/// </summary>
public sealed class VineBlockSkin : BlockVariantSkin
{
    private static readonly int GrowthId = Shader.PropertyToID("_Growth");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int RootDirId = Shader.PropertyToID("_RootDir");
    private const float GrowDuration = 0.5f;

    protected override string MaterialResource => "Vine";
    protected override bool HidesChapterArt => false; // vines sit over the chapter colour
    protected override string CellName => "VineCell";

    private Vector2 _rootDir = new Vector2(0f, 1f);
    private float _blockSeed;
    private float _growAge = -1f; // <0 = done growing

    /// <summary>Vines rooted at the bottom edge - the falling Vine block's own look.</summary>
    public void Apply() => Grow(new Vector2(0f, 1f));

    /// <summary>Creep vines onto this block from the side facing the source. <paramref name="rootDir"/>
    /// points from the contact edge into this block (the growth direction).</summary>
    public void GrowFrom(Vector2 rootDir) => Grow(rootDir);

    private void Grow(Vector2 rootDir)
    {
        if (IsBuilt) return;
        _rootDir = rootDir;
        _blockSeed = Random.value; // random base so no two vines (incl. infected neighbours) clone
        BuildCells();
        _growAge = 0f;
    }

    protected override void ConfigureCell(int index, int col, int row, SpriteRenderer overlay, MaterialPropertyBlock mpb)
    {
        mpb.SetFloat(SeedId, (_blockSeed + index * 0.6180339f) % 1f); // per-cell variation off the random base
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
