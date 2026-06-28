using UnityEngine;

/// <summary>
/// Epic passive that drastically cuts the spawn rate of EVERY hazard brick at once - one pick does
/// the job of many single-variant suppressors. Hazards are identified by the data flag
/// <see cref="BlockData.IsHazard"/> (not a hand-maintained list), so a new hostile brick is covered
/// automatically. Offered only in levels whose spawn tables feature at least
/// <see cref="minHazardTypesInLevel"/> distinct hazards (a single-hazard level wouldn't warrant it).
/// Reuses the multiplicative chance registry, so it scales each hazard's ambient rate down, never
/// below zero, and no-ops on a hazard the level can't spawn.
/// </summary>
[CreateAssetMenu(fileName = "Purifier", menuName = "Stacking/Abilities/Purifier")]
public class PurifierPowerUp : PassiveAbility
{
    [Tooltip("Fraction each hazard's spawn chance is reduced by. 0.6 = -60% (relative).")]
    [Range(0f, 1f)]
    [SerializeField] private float reductionPerStack = 0.6f;
    [Tooltip("Only offered when the level can spawn at least this many distinct hazards.")]
    [Min(1)]
    [SerializeField] private int minHazardTypesInLevel = 3;

    public override void OnAcquired(AbilityContext context, int stacks) => Reduce(context);
    public override void OnStackAdded(AbilityContext context, int stacks) => Reduce(context);

    private void Reduce(AbilityContext context)
    {
        if (context.Spawner != null) context.Spawner.ReduceHazardChances(reductionPerStack);
    }

    public override bool IsAvailable(AbilityContext context, int ownedStacks)
        => base.IsAvailable(context, ownedStacks) && context.LevelHazardVariantCount() >= minHazardTypesInLevel;
}
