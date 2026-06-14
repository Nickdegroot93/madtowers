using UnityEngine;

/// <summary>
/// Rare unique passive: when a LANDED block topples off the bottom of the screen, a
/// `saveChance` (20%) roll teleports it back to the FRONT of the spawn queue instead of
/// costing a life - and unlike Sacrifice, no structural blocks are destroyed. Only the
/// SHAPE returns (its variant is re-rolled when it respawns). The active piece you drive
/// into the abyss is never offered here (LossZone hands interception only landed blocks),
/// so it still takes the normal loss. On a save, RescueLift plays the beam-up animation.
/// Permanent (charges 0): always armed, 20% each time.
/// </summary>
[CreateAssetMenu(fileName = "Rebound", menuName = "Stacking/Abilities/Rebound")]
public class ReboundAbility : PassiveAbility
{
    [Range(0f, 1f)]
    [Tooltip("Chance a lost landed block is saved (teleported back to the queue) instead of costing a life.")]
    [SerializeField] private float saveChance = 0.2f;

    [Header("Rescue FX (swappable)")]
    [Tooltip("Magic burst played from every cell as the saved block dissolves (a CFXR effect).")]
    [SerializeField] private GameObject cellBurstEffect;
    [Tooltip("Size multiplier for the per-cell burst (1 = cell-sized).")]
    [SerializeField] private float effectScale = 1f;

    public override bool TryInterceptLoss(AbilityContext context, BlockController block)
    {
        if (Random.value >= saveChance) return false; // not saved this time - the normal loss proceeds

        // Only save if we can actually teleport the SHAPE back to the front of the queue -
        // otherwise let the normal loss happen rather than silently dropping a block with no
        // replacement. (Every real block has a BlockIdentity + Spawner, so this is just a guard.)
        BlockDefinition definition = block.TryGetComponent(out BlockIdentity identity) ? identity.Definition : null;
        if (context.Spawner == null || definition == null) return false;

        context.Spawner.RequeueDefinition(definition); // variant re-rolled on respawn

        // The block leaves the board now (no life, no penalty) and returns when it respawns:
        // drop it from the live total, then hand it to the beam-up rescue (which neutralises
        // it and finally destroys it). Contract: a handled block must end non-lost - the
        // rescue freezes + detaches it immediately and the loss guard is already consumed
        // upstream, so the cull sweep never re-fires on it.
        if (context.GameManager != null) context.GameManager.RemovePlacedBlock(block);
        RescueLift.Begin(block, cellBurstEffect, effectScale);
        return true;
    }
}
