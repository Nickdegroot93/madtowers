using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Magma look: a fixed, theme-independent molten slab (procedural Resources/Lava shader), replacing the
/// chapter art. Every cell is the same charcoal crust riven by glowing molten veins; cells alternate a HOT
/// and a COOL cast across the piece (via the per-cell _StoneColor heat tint - hot cells run brighter veins)
/// and _Seed desyncs each cell's vein layout. The piece gently wobbles, out of phase per cell, so it
/// bubbles. When the magma melts (MagmaMelt) the resulting 1x1 cells use the chapter's own skin - only the
/// molten block itself is theme-locked. See BLOCKVARIANTS.md.
/// </summary>
public sealed class MagmaBlockSkin : BlockVariantSkin
{
    private static readonly int StoneColorId = Shader.PropertyToID("_StoneColor");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");

    // Bright fire orange-red (the shader adds bloom glow, so it reads molten not bloody) + clean dark stone.
    private static readonly Color RedStone = new Color(0.93f, 0.18f, 0.08f, 1f);
    private static readonly Color BlackStone = new Color(0.12f, 0.10f, 0.11f, 1f);

    private const float WobbleAmp = 0.07f;
    private const float WobbleSpeed = 5.5f;

    protected override string MaterialResource => "Lava";
    protected override string CellName => "MagmaCell";

    private readonly List<float> _phases = new List<float>();

    /// <summary>Build the molten look. Called from MagmaBlockData.OnApplied.</summary>
    public void Apply()
    {
        _phases.Clear();
        BuildCells();
    }

    protected override void ConfigureCell(int index, int col, int row, SpriteRenderer overlay, MaterialPropertyBlock mpb)
    {
        bool hot = ((col + row) & 1) == 0;
        mpb.SetColor(StoneColorId, hot ? RedStone : BlackStone);
        mpb.SetFloat(SeedId, (index * 0.6180339f) % 1f); // desync each cell's vein layout
        _phases.Add(col * 1.7f + row * 0.9f); // slight per-cell phase so it bubbles, not in lockstep
    }

    // The molten "alive" wobble: a volume-preserving squash/stretch per cell, gently out of phase.
    private void LateUpdate()
    {
        for (int i = 0; i < Cells.Count; i++)
        {
            if (Cells[i] == null) continue;
            float w = Mathf.Sin(Time.time * WobbleSpeed + _phases[i]) * WobbleAmp;
            Vector3 b = BaseScales[i];
            Cells[i].transform.localScale = new Vector3(b.x * (1f + w), b.y * (1f - w), b.z);
        }
    }
}
