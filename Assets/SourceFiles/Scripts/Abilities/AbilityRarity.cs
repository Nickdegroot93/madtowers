using UnityEngine;

public enum AbilityRarity
{
    Common,
    Rare,
    Epic,
    Legendary
}

public static class AbilityRarityInfo
{
    // Relative chance of appearing in a choice roll. Tune freely; only ratios matter.
    public static int GetRollWeight(AbilityRarity rarity)
    {
        switch (rarity)
        {
            case AbilityRarity.Legendary: return 5;
            case AbilityRarity.Epic: return 15;
            case AbilityRarity.Rare: return 40;
            default: return 100;
        }
    }

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
