using UnityEngine;

/// <summary>
/// The Anchor look: a fixed, theme-independent slab of riveted gunmetal (procedural Resources/Anchor
/// shader), replacing the chapter art. Idle is dead-still; the signature is a "clamp-down" on lock -
/// a rivet/rim glint (_LockFlash) plus a short metallic settle. See BLOCKVARIANTS.md.
/// </summary>
public sealed class AnchorBlockSkin : BlockVariantSkin
{
    private static readonly int LockFlashId = Shader.PropertyToID("_LockFlash");
    private const float FlashDuration = 0.45f;

    protected override string MaterialResource => "Anchor";
    protected override string CellName => "AnchorCell";

    private float _flashAge = -1f; // <0 = idle

    /// <summary>Build the metal look. Called from AnchorBlockData.OnApplied.</summary>
    public void Apply() => BuildCells();

    /// <summary>Play the clamp-down beat. Called from AnchorBlockData.OnLocked after the brick freezes.</summary>
    public void PlayLockFlash() => _flashAge = 0f;

    private void LateUpdate()
    {
        if (_flashAge < 0f) return;

        _flashAge += Time.deltaTime; // scaled - a pause freezes the beat (PHYSICS.md)
        float t = _flashAge / FlashDuration;
        if (t >= 1f)
        {
            SetCellsFloat(LockFlashId, 0f);
            ResetCellScales();
            _flashAge = -1f;
            return;
        }

        float ease = 1f - t;
        SetCellsFloat(LockFlashId, ease * ease); // glint eases out

        // Damped metallic settle (volume-preserving squash, cosmetic only).
        float k = Mathf.Exp(-_flashAge * 9f) * Mathf.Sin(_flashAge * 38f) * 0.12f;
        for (int i = 0; i < Cells.Count; i++)
        {
            if (Cells[i] == null) continue;
            Vector3 b = BaseScales[i];
            Cells[i].transform.localScale = new Vector3(b.x * (1f + k), b.y * (1f - k), b.z);
        }
    }
}
