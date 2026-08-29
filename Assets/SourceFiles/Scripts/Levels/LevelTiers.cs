using UnityEngine;

/// <summary>
/// The ONE owner of medal-tier math. Tiers are never persisted as booleans: they DERIVE, at read
/// time, from the level's stored bestVerifiedValue (the highest value that ever survived a
/// hold-steady verification, in target units) against the CURRENT thresholds. That makes later
/// threshold recalibration retroactive - lowering a threshold upgrades every player already above
/// it, with no migration - while raw (never-held) scores can never award a tier.
///
/// Thresholds are formula-derived from the authored bronze target; a level can override silver/gold
/// individually (LevelDefinition). ClearWaves levels step in whole waves (+1/+2) instead of
/// multiplying, because wave difficulty is owned by the wave engine's quota curve.
/// </summary>
public static class LevelTiers
{
    /// <summary>The ladder's top rung. Every loop and terminal check derives from this pair -
    /// adding a tier (platinum) means: extend the enum, update these, give MedalStyle a color +
    /// name, give Threshold a rule, and re-check the two fixed-height medal layouts
    /// (LevelSummary modal, RunResultsScreen row). Nothing else hard-codes the count.</summary>
    public const MedalTier MaxTier = MedalTier.Gold;
    public const int TierCount = (int)MaxTier + 1;

    private const float SilverMultiplier = 1.25f;
    private const float GoldMultiplier = 1.6f;

    // Verified values are recorded as exactly the earned threshold (each rung banks its own
    // hold), so equality is the common case; the epsilon only absorbs float noise, never
    // lowers a threshold meaningfully.
    private const float Epsilon = 0.0001f;

    /// <summary>False = the level has no goal (Endless free play) and the whole ladder is dormant.</summary>
    public static bool HasTiers(LevelDefinition level)
        => level != null && level.TargetType != LevelTargetType.Endless;

    /// <summary>The tier's goal in target units (blocks / meters / waves). Bronze is exactly the
    /// authored target - completion semantics are untouched by the medal system. MONOTONE by
    /// construction: each rung is clamped to at least the rung below it, so a partial per-level
    /// override can never invert the ladder (silver 40 with formula-gold 32 would let one hold
    /// earn gold while silver stays locked); LevelDefinition.OnValidate flags the authoring.</summary>
    public static float Threshold(LevelDefinition level, MedalTier tier)
    {
        if (level == null) return float.MaxValue;

        float bronze = level.TargetValue;
        if (tier == MedalTier.Bronze) return bronze;

        // Waves step in whole waves (monotone by construction); the wave engine freezes its
        // quota/density growth past the bronze wave (WaveSolver) so these "overtime" waves
        // stay feasible.
        if (level.TargetType == LevelTargetType.ClearWaves)
            return Mathf.RoundToInt(bronze) + (int)tier;

        float overridden = tier == MedalTier.Silver ? level.SilverTargetOverride : level.GoldTargetOverride;
        float raw = overridden > 0f
            ? overridden
            : Mathf.Ceil(bronze * (tier == MedalTier.Silver ? SilverMultiplier : GoldMultiplier));
        return Mathf.Max(raw, Threshold(level, tier - 1));
    }

    /// <summary>Is the tier earned? Derived: best verified value (stored, or the live run's
    /// <paramref name="sessionVerifiedValue"/> for Custom Game levels that have no store identity)
    /// reached the tier's CURRENT threshold. Legacy rule: levels completed before the medal system
    /// existed have bestVerifiedValue 0 but must read as bronze - completion IS bronze.</summary>
    public static bool IsEarned(LevelDefinition level, MedalTier tier, float sessionVerifiedValue = 0f)
    {
        if (!HasTiers(level)) return false;
        if (tier == MedalTier.Bronze && ProgressStore.IsLevelCompleted(level)) return true;

        float verified = Mathf.Max(ProgressStore.BestVerifiedValue(level), sessionVerifiedValue);
        return verified >= Threshold(level, tier) - Epsilon;
    }

    /// <summary>The next rung to arm, or null when the ladder is done (all earned) or dormant.</summary>
    public static MedalTier? LowestUnearned(LevelDefinition level, float sessionVerifiedValue = 0f)
    {
        if (!HasTiers(level)) return null;
        for (MedalTier tier = MedalTier.Bronze; tier <= MaxTier; tier++)
        {
            if (!IsEarned(level, tier, sessionVerifiedValue)) return tier;
        }
        return null;
    }

    /// <summary>The best medal to show for the level, or null when none is earned yet.</summary>
    public static MedalTier? HighestEarned(LevelDefinition level, float sessionVerifiedValue = 0f)
    {
        if (!HasTiers(level)) return null;
        for (MedalTier tier = MaxTier; tier >= MedalTier.Bronze; tier--)
        {
            if (IsEarned(level, tier, sessionVerifiedValue)) return tier;
        }
        return null;
    }
}
