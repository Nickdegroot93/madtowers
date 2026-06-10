using UnityEngine;

public enum PowerUpRarity
{
    Common,
    Rare,
    Epic,
    Legendary
}

public static class PowerUpRarityInfo
{
    // Relative chance of appearing in a choice roll. Tune freely; only ratios matter.
    public static int GetRollWeight(PowerUpRarity rarity)
    {
        switch (rarity)
        {
            case PowerUpRarity.Legendary: return 5;
            case PowerUpRarity.Epic: return 15;
            case PowerUpRarity.Rare: return 40;
            default: return 100;
        }
    }

    public static Color GetColor(PowerUpRarity rarity)
    {
        switch (rarity)
        {
            case PowerUpRarity.Legendary: return new Color(1f, 0.62f, 0.1f);
            case PowerUpRarity.Epic: return new Color(0.72f, 0.4f, 0.96f);
            case PowerUpRarity.Rare: return new Color(0.35f, 0.62f, 1f);
            default: return new Color(0.78f, 0.84f, 0.88f);
        }
    }
}
