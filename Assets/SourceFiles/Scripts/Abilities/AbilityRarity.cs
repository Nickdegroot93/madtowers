using UnityEngine;

// Adding a rarity? Fan-out points: AbilityRarityInfo.GetColor, RarityWeightStage's
// named weight fields + GetWeight, AbilityRarityProfile.DefaultStages. The offer roll
// itself sizes its buckets from this enum and needs no change.
public enum AbilityRarity
{
    Common,
    Rare,
    Epic,
    Legendary
}

public static class AbilityRarityInfo
{
    // Rarity ODDS live in AbilityRarityProfile (offers are single-rarity, weights scale
    // with run progress); this class only owns the rarity's look.
    public static Color GetColor(AbilityRarity rarity)
    {
        switch (rarity)
        {
            case AbilityRarity.Legendary: return new Color(1f, 0.62f, 0.1f);
            case AbilityRarity.Epic: return new Color(0.72f, 0.4f, 0.96f);
            case AbilityRarity.Rare: return new Color(0.35f, 0.62f, 1f);
            default: return new Color(0.78f, 0.84f, 0.88f);
        }
    }
}
