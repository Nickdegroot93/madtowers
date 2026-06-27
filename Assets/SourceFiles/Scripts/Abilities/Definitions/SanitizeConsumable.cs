using UnityEngine;

/// <summary>
/// Consumable that DEFUSES the active falling piece: if it's a special/hazard brick (Maw, Vortex,
/// Magma, ...) its variant LOOK is stripped IN PLACE (same GameObject - so rotation, position and
/// fall progress are preserved, no shift) and the piece is reset to its shape's plain DefaultData
/// so it counts and locks as a normal brick. Usable only while the falling piece carries a
/// non-default variant.
/// </summary>
[CreateAssetMenu(fileName = "Sanitize", menuName = "Stacking/Abilities/Sanitize")]
public class SanitizeConsumable : ConsumableAbility
{
    [Header("Transform FX (swappable)")]
    [Tooltip("Plays on the piece as it's defused (a CFXR cleanse/poof effect). Null-safe.")]
    [SerializeField] private GameObject transformEffect;
    [Tooltip("Scale for the effect - CFXR effects are character-sized, a block usually wants < 1.")]
    [SerializeField] private float transformScale = 0.6f;

    // Shares the active-piece/loss-line guard; adds the Sanitize-specific condition that the
    // piece is actually a non-default variant (nothing to defuse on a plain brick).
    public override bool CanActivate(AbilityContext context)
    {
        if (!AbilityEffects.ActivePieceCanTransform(context)) return false;

        BlockController active = BlockController.ActiveControlled;
        return active != null
            && active.TryGetComponent(out BlockIdentity id)
            && id.Definition != null
            && id.Variant != null
            && id.Variant != id.Definition.DefaultData;
    }

    public override void Activate(AbilityContext context)
    {
        BlockController active = BlockController.ActiveControlled;
        if (active == null || context.Spawner == null) return;
        if (!active.TryGetComponent(out BlockIdentity id) || id.Definition == null) return;

        // Strip the hazard LOOK in place (same GameObject - rotation/position/fall progress kept),
        // then reset the piece to its shape's plain DefaultData so it counts and locks as a normal
        // brick. ApplyVariantToNextBlock transforms the in-air piece and re-syncs its identity +
        // the active-piece accounting cache (BLOCKS.md), exactly like the other transmutes.
        active.StripVariantSkins();
        context.Spawner.ApplyVariantToNextBlock(id.Definition.DefaultData);

        Vfx.Spawn(transformEffect, active.transform.position, transformScale); // null-safe
        SfxPlayer.Play("transmute", 0.9f);
    }
}
