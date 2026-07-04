using UnityEngine;

/// <summary>
/// Consumable (Slo-Mo): on activate, the next N blocks fall slower (normal descent only -
/// fast drops still go full speed, so a player who chooses to drop fast is never fought).
/// Block-count based, not a timescale effect, so it follows the player's pace and never
/// touches the game clock. Shares the slow window on AbilityRuntime with Recovery.
/// </summary>
[CreateAssetMenu(fileName = "SlowWindow", menuName = "Stacking/Abilities/Slow Window")]
public class SlowWindowConsumable : ConsumableAbility
{
    [Range(0.1f, 1f)]
    [SerializeField] private float slowFactor = 0.5f;
    [Min(1)]
    [SerializeField] private int blocks = 5;

    public override bool CanActivate(AbilityContext context) => context.Runtime != null;

    public override void Activate(AbilityContext context)
    {
        context.Runtime.GrantSlowWindow(blocks, slowFactor);
        SfxPlayer.Play("slowmo_engage", 0.8f, 0.03f);
    }
}
