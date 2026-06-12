using UnityEngine;

/// <summary>One rung of a rarity profile: from this fraction of the level target
/// onward, offers roll their rarity with these weights (ratios matter, not sums).</summary>
[System.Serializable]
public sealed class RarityWeightStage
{
    [Tooltip("Applies once run progress (score or height vs the level target) reaches this fraction. Keep stages sorted ascending; 0 is the base stage.")]
    [Range(0f, 1f)]
    public float progressThreshold;
    [Min(0f)] public float common = 100f;
    [Min(0f)] public float rare = 40f;
    [Min(0f)] public float epic = 15f;
    [Min(0f)] public float legendary = 5f;

    public float GetWeight(AbilityRarity rarity)
    {
        switch (rarity)
        {
            case AbilityRarity.Legendary: return legendary;
            case AbilityRarity.Epic: return epic;
            case AbilityRarity.Rare: return rare;
            default: return common;
        }
    }
}

/// <summary>
/// How likely each rarity is to be offered, as a function of run progress. Every offer
/// is SINGLE-RARITY (all three cards share it - a mixed common/legendary offer is a
/// non-choice), so the profile decides the offer's rarity, then cards sample uniformly
/// within it.
///
/// Levels without a profile use the built-in defaults below: base odds early, a bump
/// past 50% of the target, a bigger one past 80% - the closer to the goal, the spicier
/// the offers. A level can override with its own asset for anything from gentle
/// retuning to a "legendaries only" gimmick (weights 0/0/0/1).
/// </summary>
[CreateAssetMenu(fileName = "RarityProfile", menuName = "Stacking/Abilities/Rarity Profile")]
public class AbilityRarityProfile : ScriptableObject
{
    [Tooltip("The stage with the highest threshold <= current progress wins; the lowest-threshold stage is the base. Order doesn't matter. Include a 0-threshold stage so early offers are covered.")]
    [SerializeField] private RarityWeightStage[] stages;

    // Code-owned defaults (used when a level has no profile): base -> +50% -> +80%.
    private static readonly RarityWeightStage[] DefaultStages =
    {
        new RarityWeightStage { progressThreshold = 0f, common = 100f, rare = 40f, epic = 15f, legendary = 5f },
        new RarityWeightStage { progressThreshold = 0.5f, common = 70f, rare = 40f, epic = 25f, legendary = 12f },
        new RarityWeightStage { progressThreshold = 0.8f, common = 45f, rare = 35f, epic = 30f, legendary = 25f }
    };

    /// <summary>Stage for the given progress, from this profile's stages (or the
    /// built-in defaults when the profile has none authored).</summary>
    public RarityWeightStage ResolveStage(float progress)
    {
        return ResolveFrom(stages != null && stages.Length > 0 ? stages : DefaultStages, progress);
    }

    /// <summary>Profile-or-default resolution: null profile = built-in defaults.</summary>
    public static RarityWeightStage Resolve(AbilityRarityProfile profile, float progress)
    {
        return profile != null ? profile.ResolveStage(progress) : ResolveFrom(DefaultStages, progress);
    }

    private static RarityWeightStage ResolveFrom(RarityWeightStage[] source, float progress)
    {
        // Highest threshold <= progress wins; before any stage applies (an authored
        // profile whose lowest stage starts above 0), the LOWEST-threshold stage acts
        // as the base. Order in the array never matters; null entries are skipped.
        RarityWeightStage best = null;
        RarityWeightStage lowest = null;
        for (int i = 0; i < source.Length; i++)
        {
            RarityWeightStage stage = source[i];
            if (stage == null) continue;

            if (lowest == null || stage.progressThreshold < lowest.progressThreshold) lowest = stage;
            if (stage.progressThreshold <= progress &&
                (best == null || stage.progressThreshold >= best.progressThreshold))
            {
                best = stage;
            }
        }
        return best ?? lowest ?? DefaultStages[0];
    }
}
