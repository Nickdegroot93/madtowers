using UnityEngine;

/// <summary>
/// Passive: after losing a life, the next N blocks fall slower (normal descent only -
/// fast drops are unaffected), giving a clean window to recover rhythm. Count-based, not a
/// timer, so it follows the player's pace. The slow window itself lives on AbilityRuntime
/// (shared with the Slo-Mo consumable); this just grants it on life loss.
/// </summary>
[CreateAssetMenu(fileName = "RecoveryWindow", menuName = "Stacking/Abilities/Recovery Window")]
public class RecoveryWindowAbility : PassiveAbility
{
    [Range(0.1f, 1f)]
    [SerializeField] private float slowFactor = 0.5f;
    [Min(1)]
    [SerializeField] private int blocksPerTrigger = 3;

    public override bool OnLifeLost(AbilityContext context)
    {
        if (context.Runtime != null) context.Runtime.GrantSlowWindow(blocksPerTrigger, slowFactor);
        return false; // permanent passive: granting the window never consumes it
    }
}
