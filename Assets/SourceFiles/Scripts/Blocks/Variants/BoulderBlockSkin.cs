using UnityEngine;

/// <summary>
/// The Boulder look: a fixed, theme-independent chunk of dark cracked basalt (procedural Resources/Boulder
/// shader), replacing the chapter art. No idle motion (dead-still reads as heavy); the personality is the
/// landing SLAM - a hit-stop + camera kick plus a hard squash that settles fast. See BLOCKVARIANTS.md.
/// </summary>
public sealed class BoulderBlockSkin : BlockVariantSkin
{
    private const float ImpactDuration = 0.5f;

    protected override string MaterialResource => "Boulder";
    protected override string CellName => "BoulderCell";

    private float _impactAge = -1f; // <0 = idle

    /// <summary>Build the rock look. Called from BoulderBlockData.OnApplied.</summary>
    public void Apply() => BuildCells();

    /// <summary>The heavy landing slam. Called from BoulderBlockData.OnLocked.</summary>
    public void PlayLandImpact()
    {
        _impactAge = 0f;
        ImpactFx.ImpactPunch(0.045f, 0.16f, 0.18f); // weight: brief freeze + camera kick
    }

    private void LateUpdate()
    {
        if (_impactAge < 0f) return;

        _impactAge += Time.deltaTime; // scaled - a pause freezes the beat (PHYSICS.md)
        if (_impactAge >= ImpactDuration)
        {
            ResetCellScales();
            _impactAge = -1f;
            return;
        }

        // Hard compress on contact, settling fast with little bounce (heavy, not springy).
        float k = Mathf.Exp(-_impactAge * 10f) * Mathf.Cos(_impactAge * 22f) * 0.18f;
        for (int i = 0; i < Cells.Count; i++)
        {
            if (Cells[i] == null) continue;
            Vector3 b = BaseScales[i];
            Cells[i].transform.localScale = new Vector3(b.x * (1f + k * 0.6f), b.y * (1f - k), b.z);
        }
    }
}
