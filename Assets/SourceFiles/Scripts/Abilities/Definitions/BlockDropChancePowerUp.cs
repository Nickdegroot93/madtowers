using UnityEngine;

/// <summary>
/// Unique passive that introduces an OUT-OF-BAG brick to the run: once owned, the brick
/// starts dropping at a low per-spawn chance via the Spawner's injection roll. It is never
/// added to the authored bag (asset data stays immutable) - it rides the same
/// definition-chance override the supply abilities use, just for a shape the level doesn't
/// normally have. Marking the brick spawnable also unlocks any later chance-boost ability
/// targeting it (a BlockDefinitionChancePowerUp whose IsAvailable is gated on
/// CanSpawnDefinition - false until this ability introduces the brick).
/// </summary>
[CreateAssetMenu(fileName = "BlockDropChance", menuName = "Stacking/Abilities/Block Drop Chance")]
public class BlockDropChancePowerUp : PassiveAbility
{
    [Tooltip("The out-of-bag brick this ability adds to the run.")]
    [SerializeField] private BlockDefinition definition;

    [Tooltip("Per-spawn chance the brick drops. 0.05 ~= one every ~20 pieces (about a third the rate of a normal shape in a 7-bag).")]
    [Range(0f, 1f)]
    [SerializeField] private float dropChance = 0.05f;

    public override void OnAcquired(AbilityContext context, int stacks)
    {
        if (context.Spawner != null && definition != null)
        {
            context.Spawner.AddInjectedDefinition(definition, dropChance);
        }
    }
}
