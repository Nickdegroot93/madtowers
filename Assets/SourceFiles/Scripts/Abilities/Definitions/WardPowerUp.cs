using UnityEngine;

/// <summary>
/// One-shot passive (charges = 1): the next hazard brick is neutralised into a plain brick of the
/// same shape, and the charge is spent. Hazards are identified by the data flag
/// <see cref="BlockData.IsHazard"/>, so a new hostile brick is warded automatically; non-hazard
/// specials (Anchor, Vine, Magma) are ignored, so the charge is never wasted on a helpful brick.
///
/// The ward does NOT fire the instant the hazard appears (Nick 2026-08-07: "I never see it working
/// in action"). It arms a <see cref="WardStrike"/> on the piece instead: the hazard falls looking
/// like a hazard for <see cref="strikeDelaySeconds"/>, THEN the ward visibly strikes it down to a
/// plain brick and the charge is spent - so the player reads the threat, sees the save, and watches
/// the icon leave the armed rail. A hazard already in the air when Ward is picked arms the same
/// strike (the acquisition catch-up in ABILITIES.md §4/§14), so a pick is never a dud either.
/// The defuse itself reuses the Sanitize path (strip the variant look in place, reset to the shape's
/// plain DefaultData).
/// </summary>
[CreateAssetMenu(fileName = "Ward", menuName = "Stacking/Abilities/Ward")]
public class WardPowerUp : PassiveAbility
{
    [Tooltip("Beat between the hazard appearing and the ward striking it. Long enough to read the hazard, short enough to feel like a save. A flick that lands sooner triggers the strike early.")]
    [Range(0.1f, 1.5f)]
    [SerializeField] private float strikeDelaySeconds = 0.45f;

    [Header("Strike FX (swappable)")]
    [Tooltip("Burst played across every cell of the brick as it is defused (a CFXR prefab - BASE prefabs only, never a variant). Null-safe: degrades to the punch + sound + the visible reset.")]
    [SerializeField] private GameObject strikeEffect;
    [Tooltip("Scale for the per-cell burst. CFXR effects are character-sized; a block cell usually wants < 1.")]
    [SerializeField] private float effectScale = 0.7f;

    // Per-run state on the CLONE (ABILITIES.md §3): the strike in flight, so two hazards in the air
    // (an Overdraw/Fission sequence) can't both claim the same charge. It is the COMPONENT, not a
    // bool, on purpose - the strike rides on the hazard brick, so if that brick leaves play (culled,
    // zapped, game over) the reference reads null through Unity's destroyed-object check and the
    // ward re-arms by itself. A bool would have to be released by a teardown callback, and anything
    // that skipped the callback would strand the ward armed-but-inert for the rest of the run.
    private WardStrike _pendingStrike;

    public override bool OnBlockSpawned(AbilityContext context, BlockController block, BlockData data)
    {
        if (_pendingStrike != null) return false;                         // already committed
        if (data == null || !data.IsHazard) return false;                 // only act on a real hazard
        if (!AbilityEffects.CanNeutralizeToPlain(context, block)) return false; // and only if it CAN be reset

        _pendingStrike = WardStrike.Arm(block, this, context, strikeDelaySeconds, strikeEffect, effectScale);

        // No charge yet: the strike pays when it lands, so the rail's icon burns away at the moment
        // the brick visibly turns plain rather than a beat earlier.
        return false;
    }

    /// <summary>The strike resolved - free the claim now rather than waiting for the component's
    /// deferred destruction, so a stacked second charge can take the very next hazard.</summary>
    internal void ClearPendingStrike(WardStrike strike)
    {
        if (_pendingStrike == strike) _pendingStrike = null;
    }

    // Only worth offering where hazards can actually drop.
    public override bool IsAvailable(AbilityContext context, int ownedStacks)
        => base.IsAvailable(context, ownedStacks) && context.LevelHazardVariantCount() >= 1;
}
