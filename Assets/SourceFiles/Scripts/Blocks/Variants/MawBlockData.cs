using UnityEngine;

/// <summary>
/// The Maw: a monster brick that DEVOURS anything placed on it. It falls dormant; the moment it lands the
/// mouth opens and it begins eating - every block that comes to rest on its top is shattered and costs a
/// LIFE, forever. There is no defusing it: you must build AROUND a maw, never on it. A brutal, high-skill
/// hazard. Look = MawBlockSkin (fleshy gnashing jaws); eating = MawBlockBehaviour. See BLOCKVARIANTS.md.
/// </summary>
[CreateAssetMenu(fileName = "MawBlockData", menuName = "Stacking/Blocks/Maw Block Variant")]
public class MawBlockData : BlockData
{
    [Header("Devour FX (swappable)")]
    [Tooltip("One-shot effect played on a block as it's devoured (e.g. CFXR2 Souls Escape - the soul flees). Null-safe.")]
    [SerializeField] private GameObject eatEffect;
    [Tooltip("World-unit scale for the devour effect (CFXR effects read at ~1 cell at scale 1).")]
    [SerializeField] private float eatEffectScale = 0.7f;

    // Build the dormant fleshy look as soon as the variant is applied (after the chapter skin).
    // Get-or-add so a re-apply can't add a second skin. (Base OnApplied is empty; see BLOCKVARIANTS.md.)
    public override void OnApplied(BlockController block)
    {
        if (block == null) return;
        if (!block.TryGetComponent(out MawBlockSkin skin))
            skin = block.gameObject.AddComponent<MawBlockSkin>();
        skin.Apply();
    }

    // On landing the mouth opens and the maw starts eating anything that rests on its top - forever.
    public override void OnLocked(BlockController block)
    {
        if (block == null) return;
        block.TryGetComponent(out MawBlockSkin skin);
        if (skin != null) skin.Activate();
        block.gameObject.AddComponent<MawBlockBehaviour>().Begin(skin, eatEffect, Mathf.Max(0.05f, eatEffectScale));
    }
}
