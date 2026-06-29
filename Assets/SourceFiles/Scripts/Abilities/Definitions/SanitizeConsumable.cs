using UnityEngine;

/// <summary>
/// Consumable that DEFUSES the active falling piece: if it's a special/hazard brick (Maw, Vortex,
/// Magma, ...) its variant LOOK is stripped IN PLACE (same GameObject - so rotation, position and
/// fall progress are preserved, no shift) and the piece is reset to its shape's plain DefaultData
/// so it counts and locks as a normal brick. Usable only while the falling piece carries a
/// non-default variant, and only OFFERED on levels that can actually spawn a hazard brick
/// (so it never appears as dead weight on a no-hazard level).
/// </summary>
[CreateAssetMenu(fileName = "Sanitize", menuName = "Stacking/Abilities/Sanitize")]
public class SanitizeConsumable : ConsumableAbility
{
    [Header("Transform FX (swappable)")]
    [Tooltip("Plays on the piece as it's defused (a CFXR cleanse/poof effect). Null-safe.")]
    [SerializeField] private GameObject transformEffect;
    [Tooltip("Scale for the effect - CFXR effects are character-sized, a block usually wants < 1.")]
    [SerializeField] private float transformScale = 0.6f;

    // Offer gate: only ever OFFERED on levels whose spawn tables can produce a hazard brick
    // (Bomb, Vortex, Maw, Ice, Locked, Tremor - the BlockData.IsHazard set). Without something
    // worth defusing the pick is dead weight, so it self-excludes exactly like Ward/Purifier.
    // (CanActivate below is the separate "can I use it on THIS piece right now" check.)
    public override bool IsAvailable(AbilityContext context, int ownedStacks)
        => base.IsAvailable(context, ownedStacks) && context.LevelHazardVariantCount() >= 1;

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
        // Defuse in place via the shared helper (strip look + reset to plain DefaultData). FX only
        // on a real neutralise.
        if (!AbilityEffects.NeutralizeToPlain(context, active)) return;

        Vfx.Spawn(transformEffect, active.transform.position, transformScale); // null-safe
        SfxPlayer.Play("transmute", 0.9f);
    }
}
