using UnityEngine;

/// <summary>
/// Consumable that turns the current active piece into a short three-piece draft. The
/// player chooses the first two drop order entries; the final held choice drops by itself.
/// </summary>
[CreateAssetMenu(fileName = "Overdraw", menuName = "Stacking/Abilities/Overdraw")]
public class OverdrawAbility : ConsumableAbility
{
    [SerializeField, Min(2)] private int choiceCount = 3;

    [Header("Activation FX (optional)")]
    [Tooltip("Bursts from every cell of the current piece as it leaves play.")]
    [SerializeField] private GameObject vanishEffect;
    [SerializeField] private float vanishScale = 0.55f;

    public override bool CanActivate(AbilityContext context)
    {
        // No running-session check needed: AbilityRuntime's ConsumablesUsable blanket gate already
        // blocks activation while any active-piece session owns the field (see ConsumableAbility).
        if (context == null || context.Spawner == null || context.Config == null) return false;
        if (context.Config.BlockBag == null || context.Config.BlockBag.Count == 0) return false;

        BlockController active = BlockController.ActiveControlled;
        if (active == null || active.HasLanded || active != context.Spawner.currentBlock) return false;

        Camera camera = Camera.main;
        if (camera != null && camera.orthographic && active.transform.position.y < LossZone.CullY(camera)) return false;

        return true;
    }

    public override void Activate(AbilityContext context)
    {
        BlockController active = BlockController.ActiveControlled;
        if (active == null || context == null || context.Spawner == null) return;

        ImpactFx.BurstFromEveryCell(active, vanishEffect, vanishScale);
        ImpactFx.ImpactPunch(0.045f, 0.08f, 0.12f);
        SfxPlayer.Play("swoosh_01", 0.65f, 0.05f);

        OverdrawSession.Begin(context.Spawner, choiceCount);
    }
}
