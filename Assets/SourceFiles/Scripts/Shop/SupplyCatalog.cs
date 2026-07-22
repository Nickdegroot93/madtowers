using System.Collections.Generic;

/// <summary>
/// The run-supplies catalog (SHOP.md §3): what can be bought for a run and what it costs.
/// Code-owned config on purpose - a static class, not a ScriptableObject, so price tuning
/// is one edit here with no serialized-default staleness. Effects are applied by
/// RunSuppliesApplier / the systems that consume RunSuppliesState; this class only
/// describes, prices, and gates relevance per level.
/// </summary>
public enum BoostId
{
    SlowDescent,
    ScarceHazards,
    QuickStudy,
    StockedSloMo,
    StockedZap,
}

public static class SupplyCatalog
{
    public const int MaxBoostsPerRun = 2;

    /// <summary>Escalating per-pip life prices (1st/2nd/3rd) - a single life stays a
    /// light-touch buy, the full buffer an occasional decision (SHOP.md §4).</summary>
    public static readonly int[] LifePipPrices = { 40, 60, 90 };

    public const float SlowDescentSpeedScale = 0.9f;  // never deeper - below ×0.9 the run stops resembling the level
    public const float ScarceHazardsReduction = 0.5f; // fraction removed from every hazard chance
    public const int QuickStudyAtBlocks = 3;

    public sealed class BoostInfo
    {
        public BoostId Id;
        public string DisplayName;
        public string Blurb;   // one line, shown on the tray card
        public int Price;
        /// <summary>Asset name of the consumable this boost pre-grants (Stocked boosts only).</summary>
        public string ConsumableAssetName;
    }

    public static readonly IReadOnlyList<BoostInfo> Boosts = new[]
    {
        new BoostInfo { Id = BoostId.SlowDescent, DisplayName = "SLOW DESCENT",
            Blurb = "Blocks fall 10% slower, all run long.", Price = 80 },
        new BoostInfo { Id = BoostId.ScarceHazards, DisplayName = "SCARCE HAZARDS",
            Blurb = "Hostile bricks show up half as often.", Price = 60 },
        new BoostInfo { Id = BoostId.QuickStudy, DisplayName = "QUICK STUDY",
            Blurb = "Your first ability choice arrives after 3 blocks.", Price = 30 },
        new BoostInfo { Id = BoostId.StockedSloMo, DisplayName = "STOCKED: SLO-MO",
            Blurb = "Start the run holding one Slo-Mo charge.", Price = 30,
            ConsumableAssetName = "SloMo" },
        new BoostInfo { Id = BoostId.StockedZap, DisplayName = "STOCKED: ZAP",
            Blurb = "Start the run holding one Zap charge.", Price = 40,
            ConsumableAssetName = "Zap" },
    };

    /// <summary>Stable save/analytics id (DATA.md rule 2) - the enum name, never its ordinal.</summary>
    public static string StableId(BoostId id) => id.ToString();

    public static int PriceForLives(int lives)
    {
        int total = 0;
        for (int i = 0; i < lives && i < LifePipPrices.Length; i++) total += LifePipPrices[i];
        return total;
    }

    /// <summary>Whether a boost can do anything on this level. Irrelevant boosts are not shown
    /// at all (SHOP.md §3.2) - no greyed-out cards.</summary>
    public static bool IsRelevant(BoostInfo boost, LevelDefinition level)
    {
        GameModeConfig config = level != null ? level.GameModeConfig : null;
        if (config == null) return false;

        switch (boost.Id)
        {
            case BoostId.SlowDescent:
                return true;
            case BoostId.ScarceHazards:
                return LevelHasHazards(config);
            case BoostId.QuickStudy:
                // Only meaningful when the level runs the ability draft at a cadence the boost
                // actually beats.
                return config.PowerUpChoiceEveryBlocks > QuickStudyAtBlocks
                    && config.PowerUpChoicePool != null && config.PowerUpChoicePool.Count > 0;
            case BoostId.StockedSloMo:
            case BoostId.StockedZap:
                return FindConsumable(boost.ConsumableAssetName) != null;
            default:
                return false;
        }
    }

    /// <summary>Resolve a Stocked boost's consumable by asset name via the catalog. Null when
    /// the asset is missing or isn't a consumable (the boost is then simply not offered).</summary>
    public static ConsumableAbility FindConsumable(string assetName)
    {
        if (string.IsNullOrEmpty(assetName) || !ContentCatalog.IsAvailable) return null;

        List<AbilityDefinition> all = ContentCatalog.AllAbilities();
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] != null && all[i].name == assetName) return all[i] as ConsumableAbility;
        }
        return null;
    }

    public static BoostInfo Info(BoostId id)
    {
        for (int i = 0; i < Boosts.Count; i++)
        {
            if (Boosts[i].Id == id) return Boosts[i];
        }
        return null;
    }

    private static bool LevelHasHazards(GameModeConfig config)
    {
        IReadOnlyList<AmbientBlockVariantChance> ambient = config.AmbientBlockVariantChances;
        if (ambient == null) return false;
        for (int i = 0; i < ambient.Count; i++)
        {
            AmbientBlockVariantChance entry = ambient[i];
            if (entry != null && entry.Variant != null && entry.Variant.IsHazard && entry.ChancePerBlock > 0f)
                return true;
        }
        return false;
    }
}
