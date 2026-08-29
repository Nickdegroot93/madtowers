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
    // DATA.md rule 2: these names are the stable save/analytics ids (start_run loadout json,
    // RunSuppliesState) - never rename a shipped one.
    SlowDescent,
    ScarceHazards,
    QuickStudy,
    StockedSloMo,
    StockedZap,
    LowTide,
    VoidWard,
    PocketCache,
    StockedVine,
}

public static class SupplyCatalog
{
    public const int MaxBoostsPerRun = 2;

    /// <summary>Escalating per-pip life prices (1st/2nd/3rd) - a single life stays a
    /// light-touch buy, the full buffer an occasional decision (SHOP.md §4).</summary>
    public static readonly int[] LifePipPrices = { 40, 60, 90 };

    public const float SlowDescentSpeedScale = 0.9f;  // never deeper - below ×0.9 the run stops resembling the level
    public const float ScarceHazardsReduction = 0.3f; // fraction removed from every hazard chance (0.5 read as OP - Nick 2026-08-29)
    public const int QuickStudyAtBlocks = 3;
    public const float LowTideFloodScale = 0.85f;     // flood rise speed multiplier (~17% more time to the goal)

    public sealed class BoostInfo
    {
        public BoostId Id;
        public string DisplayName;
        public string Blurb;   // one line, shown on the tray card
        public int Price;
        /// <summary>Asset name of the consumable this boost pre-grants (Stocked boosts only).</summary>
        public string ConsumableAssetName;
    }

    // LIST ORDER IS PICKER ORDER (irrelevant boosts are hidden, so the visible subset keeps
    // this ranking - Nick 2026-08-29): TYPE-SPECIFIC first (they exist only on their game
    // type and are that type's headline), then CONDITIONAL (only shown where their trigger
    // exists, so they're always pointed when visible), then the basics. Never sort by price
    // or name.
    public static readonly IReadOnlyList<BoostInfo> Boosts = new[]
    {
        // -- type-specific --
        new BoostInfo { Id = BoostId.LowTide, DisplayName = "LOW TIDE",
            Blurb = "The flood rises 15% slower, all run long.", Price = 70 },
        new BoostInfo { Id = BoostId.VoidWard, DisplayName = "VOID WARD",
            Blurb = "The first block the void grabs is spared.", Price = 60 },
        // -- conditional --
        new BoostInfo { Id = BoostId.ScarceHazards, DisplayName = "SCARCE HAZARDS",
            Blurb = "Hostile bricks show up 30% less often.", Price = 60 },
        new BoostInfo { Id = BoostId.QuickStudy, DisplayName = "QUICK STUDY",
            Blurb = "Your first ability choice arrives after 3 blocks.", Price = 30 },
        // -- basics --
        new BoostInfo { Id = BoostId.SlowDescent, DisplayName = "SLOW DESCENT",
            Blurb = "Blocks fall 10% slower, all run long.", Price = 20 }, // repriced 80->20: x0.9 played as comfort, not power (Nick 2026-08-29)
        new BoostInfo { Id = BoostId.PocketCache, DisplayName = "POCKET CACHE",
            Blurb = "Unlocks the hold pocket - bank a block, swap it back anytime.", Price = 140 },
        new BoostInfo { Id = BoostId.StockedSloMo, DisplayName = "STOCKED: SLO-MO",
            Blurb = "Start the run holding one Slo-Mo charge.", Price = 30,
            ConsumableAssetName = "SloMo" },
        new BoostInfo { Id = BoostId.StockedZap, DisplayName = "STOCKED: ZAP",
            Blurb = "Start the run holding one Zap charge.", Price = 40,
            ConsumableAssetName = "Zap" },
        new BoostInfo { Id = BoostId.StockedVine, DisplayName = "STOCKED: VINE",
            Blurb = "Start holding one Vine charge - the next 2 bricks turn to vine.", Price = 60,
            ConsumableAssetName = "VineBrick" },
    };

    /// <summary>Stable save/analytics id (DATA.md rule 2) - the enum name, never its ordinal.</summary>
    public static string StableId(BoostId id) => id.ToString();

    /// <summary>Total price of <paramref name="lives"/> purchased pips, the first landing
    /// in slot <paramref name="firstSlot"/> (0-based). Free lives (authored or type-granted,
    /// SHOP.md §3.1) occupy the cheap slots, so purchases pay the later, dearer prices -
    /// the escalation prices the pip, not the purchase order.</summary>
    public static int PriceForLives(int lives, int firstSlot = 0)
    {
        int total = 0;
        for (int i = 0; i < lives; i++)
        {
            int slot = firstSlot + i;
            if (slot < LifePipPrices.Length) total += LifePipPrices[slot];
        }
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
            case BoostId.LowTide:
                // Only the Flood has a clock today (no timed goal levels shipped) - the boost
                // slows the ONE pacing dial (RisingFloodModifier consumes it at level start).
                return HasModifier<RisingFloodModifier>(level);
            case BoostId.VoidWard:
                return HasModifier<VoidZoneModifier>(level);
            case BoostId.PocketCache:
                // The hold pocket is an ability in boost clothing (PocketCacheAbility grants
                // the same thing) - the wave-mode ability ban applies (Nick 2026-08-29).
                return level.TargetType != LevelTargetType.ClearWaves;
            case BoostId.StockedSloMo:
            case BoostId.StockedZap:
            case BoostId.StockedVine:
                // Wave mode runs WITHOUT abilities (HeightLimitWavesModifier.BansAbility,
                // Nick 2026-08-24) - stocked consumables ARE abilities, so the tray must
                // not sell them into the one mode they'd cheese. SlowDescent stays: a
                // fall-speed comfort is a boost, not an ability.
                if (level.TargetType == LevelTargetType.ClearWaves) return false;
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

    private static bool HasModifier<T>(LevelDefinition level) where T : LevelModifier
    {
        System.Collections.Generic.IReadOnlyList<LevelModifier> modifiers =
            level != null ? level.Modifiers : null;
        for (int i = 0; modifiers != null && i < modifiers.Count; i++)
        {
            if (modifiers[i] is T) return true;
        }
        return false;
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
