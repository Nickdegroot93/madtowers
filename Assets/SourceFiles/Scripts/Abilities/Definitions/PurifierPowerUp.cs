using UnityEngine;

/// <summary>
/// Epic passive that drastically cuts the spawn rate of EVERY listed hazard brick at once - one
/// pick does the job of many single-variant suppressors. Offered only in levels that actually
/// feature at least <see cref="minHazardTypesInLevel"/> of these hazards (a Maw-only level wouldn't
/// warrant it), checked via the level's spawn tables - not a live on-screen count, which fluctuates.
/// Reuses the multiplicative ReduceVariantChance registry, so it scales each hazard's ambient rate
/// down and never below zero; it no-ops on a hazard the level can't spawn.
/// </summary>
[CreateAssetMenu(fileName = "Purifier", menuName = "Stacking/Abilities/Purifier")]
public class PurifierPowerUp : PassiveAbility
{
    [Tooltip("The hazard variants this suppresses AND counts toward the availability threshold.")]
    [SerializeField] private BlockData[] hazardVariants;
    [Tooltip("Fraction each listed hazard's spawn chance is reduced by. 0.6 = -60% (relative).")]
    [Range(0f, 1f)]
    [SerializeField] private float reductionPerStack = 0.6f;
    [Tooltip("Only offered when the level can spawn at least this many of the listed hazards.")]
    [Min(1)]
    [SerializeField] private int minHazardTypesInLevel = 3;

    public override void OnAcquired(AbilityContext context, int stacks) => ReduceAll(context);
    public override void OnStackAdded(AbilityContext context, int stacks) => ReduceAll(context);

    private void ReduceAll(AbilityContext context)
    {
        if (context.Spawner == null || hazardVariants == null) return;
        for (int i = 0; i < hazardVariants.Length; i++)
            if (hazardVariants[i] != null) context.Spawner.ReduceVariantChance(hazardVariants[i], reductionPerStack);
    }

    public override bool IsAvailable(AbilityContext context, int ownedStacks)
    {
        if (!base.IsAvailable(context, ownedStacks)) return false;

        int present = 0;
        if (hazardVariants != null)
        {
            for (int i = 0; i < hazardVariants.Length; i++)
                if (hazardVariants[i] != null && context.LevelHasVariant(hazardVariants[i])) present++;
        }
        return present >= minHazardTypesInLevel;
    }
}
