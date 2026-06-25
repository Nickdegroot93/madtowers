using UnityEngine;

/// <summary>
/// Zap (Common consumable): the active falling piece vanishes and a vertical laser stabs down its
/// column from the top of the screen to the first block beneath it. The beam charges from a wide
/// glow to a thin needle over 3 seconds, then the targeted block detonates - dynamic tower blocks
/// only, statics/floor/frozen are zap-proof (a wasted shot reads as a soft dud). All the timing and
/// the beam live in <see cref="ZapSession"/>; this asset just guards activation and kicks it off.
/// Not a block variant - nothing falls; the laser does the work.
/// </summary>
[CreateAssetMenu(fileName = "Zap", menuName = "Stacking/Abilities/Zap")]
public class ZapAbility : ConsumableAbility
{
    [Header("Beam look (HDR-bright so it glows through bloom)")]
    [Tooltip("Core beam color - a bright blue reads best and blooms for free.")]
    [SerializeField] private Color beamColor = new Color(0.30f, 0.70f, 1f, 1f);
    [Tooltip("Accent the filaments blend toward (cyan/white-hot) for the slick layered look.")]
    [SerializeField] private Color accentColor = new Color(0.55f, 0.95f, 1f, 1f);

    [Header("Detonation FX (swappable CFXR, base prefabs only)")]
    [Tooltip("Plays per cell on the block the beam destroys (and a small puff as the piece vanishes).")]
    [SerializeField] private GameObject detonateEffect;
    [SerializeField] private float detonateScale = 1f;

    // The blanket consumable gate (AbilityRuntime.ConsumablesUsable) already refuses while any choice
    // session - including a Zap - is running, so this only needs the active-piece checks.
    public override bool CanActivate(AbilityContext context)
    {
        if (context == null || context.Spawner == null) return false;

        BlockController active = BlockController.ActiveControlled;
        if (active == null || active.HasLanded) return false;

        Camera cam = Camera.main;
        if (cam != null && cam.orthographic && active.transform.position.y < LossZone.CullY(cam)) return false;

        return true;
    }

    public override void Activate(AbilityContext context)
    {
        BlockController active = BlockController.ActiveControlled;
        if (active != null)
        {
            ImpactFx.BurstFromEveryCell(active, detonateEffect, detonateScale * 0.6f); // piece blinks out
            SfxPlayer.Play("swoosh_01", 0.7f, 0.04f);
        }

        ZapSession.Begin(context.Spawner, detonateEffect, detonateScale, beamColor, accentColor);
    }
}
