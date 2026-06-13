using UnityEngine;

/// <summary>
/// Stackable passive: future spawns have a small extra chance to be a specific
/// shape definition (for example, more straight I-pieces). The first pickup can
/// have a stronger delta than later stacks; the Spawner accumulates the run-local
/// bias internally.
/// </summary>
[CreateAssetMenu(fileName = "BlockDefinitionChance", menuName = "Stacking/Abilities/Block Definition Chance")]
public class BlockDefinitionChancePowerUp : PassiveAbility
{
    [SerializeField] private BlockDefinition definition;
    [Range(0f, 1f)]
    [SerializeField] private float firstStackChance = 0.08f;
    [Range(0f, 1f)]
    [SerializeField] private float additionalStackChance = 0.05f;

    public override bool IsAvailable(AbilityContext context, int ownedStacks)
    {
        if (!base.IsAvailable(context, ownedStacks)) return false;
        return context.Spawner != null && context.Spawner.CanSpawnDefinition(definition);
    }

    public override void OnAcquired(AbilityContext context, int stacks)
    {
        AddChance(context, firstStackChance);
    }

    public override void OnStackAdded(AbilityContext context, int stacks)
    {
        AddChance(context, additionalStackChance);
    }

    private void AddChance(AbilityContext context, float chance)
    {
        if (context.Spawner == null) return;

        context.Spawner.AddDefinitionChance(definition, chance);
    }
}
