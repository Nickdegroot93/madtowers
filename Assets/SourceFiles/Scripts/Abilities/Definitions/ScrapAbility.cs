using UnityEngine;

/// <summary>
/// Consumable that removes the latest counted placed block, even if physics has already
/// started pulling it away from the tower. Used as a quick undo for risky placements.
/// </summary>
[CreateAssetMenu(fileName = "Scrap", menuName = "Stacking/Abilities/Scrap")]
public class ScrapAbility : ConsumableAbility
{
    [SerializeField] private Color vaporColor = new Color(0.35f, 0.62f, 1f, 1f);

    public override bool CanActivate(AbilityContext context)
    {
        return context != null &&
               context.GameManager != null &&
               context.GameManager.LastPlacedBlock != null;
    }

    public override void Activate(AbilityContext context)
    {
        BlockController target = context != null && context.GameManager != null
            ? context.GameManager.LastPlacedBlock
            : null;
        if (target == null) return;

        ImpactFx.DestroyBlockWithShatter(target, vaporColor);
        ImpactFx.ImpactPunch(0.035f, 0.07f, 0.12f);
    }
}
