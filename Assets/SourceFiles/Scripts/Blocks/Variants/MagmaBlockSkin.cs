using UnityEngine;

/// <summary>Fixed weathered basalt with molten plate seams. Cooled fragments retain the same material.</summary>
public sealed class MagmaBlockSkin : BlockVariantSkin
{
    private static readonly int HeatId = Shader.PropertyToID("_Heat");
    private static readonly int EdgesId = Shader.PropertyToID("_Edges");
    private static readonly int CracksId = Shader.PropertyToID("_MagmaCracks");
    protected override string MaterialResource => "Lava";
    protected override string CellName => "MagmaCell";

    public void Apply()
    {
        BuildCells();
        SetHeat(1f);
        var mpb = new MaterialPropertyBlock();
        float spacing = GetComponent<BlockController>() is BlockController block ? block.GridSpacing : 1f;
        for (int i = 0; i < Cells.Count; i++)
        {
            Vector4 edges = Vector4.one;
            for (int j = 0; j < Cells.Count; j++)
            {
                if (i == j) continue;
                Vector3 delta = BasePositions[j] - BasePositions[i];
                if (Mathf.Abs(delta.y) < .01f)
                {
                    if (Mathf.Abs(delta.x + spacing) < .01f) edges.x = 0;
                    if (Mathf.Abs(delta.x - spacing) < .01f) edges.y = 0;
                }
                if (Mathf.Abs(delta.x) < .01f)
                {
                    if (Mathf.Abs(delta.y + spacing) < .01f) edges.z = 0;
                    if (Mathf.Abs(delta.y - spacing) < .01f) edges.w = 0;
                }
            }
            Cells[i].GetPropertyBlock(mpb);
            mpb.SetVector(EdgesId, edges);
            Cells[i].SetPropertyBlock(mpb);
        }
    }

    public void SetHeat(float heat) => SetCellsFloat(HeatId, Mathf.Clamp01(heat));

    protected override void ConfigureCell(int index, int col, int row, SpriteRenderer overlay, MaterialPropertyBlock mpb)
    {
        mpb.SetTexture(CracksId, Resources.Load<Texture2D>("MagmaCracks"));
        mpb.SetFloat(HeatId, 1f);
    }
}
