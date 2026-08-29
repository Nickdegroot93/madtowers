using UnityEngine;

/// <summary>
/// The one owner of how medal tiers LOOK (colors, sprite, display name) - every surface
/// (level cards, summary modal, results card, in-run pills) reads this so the medals can
/// never drift apart. The mark is Nick's rendered block icon (Resources/Menu/medal_*,
/// 2026-08-29); the original procedural circle badge remains as the fallback for a tier
/// whose render hasn't landed yet. One art per tier: EARNED state is a TINT, not separate
/// art - pair every <see cref="Sprite"/> with <see cref="IconTint"/> on the Image.
/// </summary>
public static class MedalStyle
{
    // Gold is THE sanctioned reward gold (golden brick, sheen, NEW BEST pill) - one gold
    // across the whole game, never a second mint. Bronze/silver are new but sit in the same
    // warm, desaturated register as the menu palette.
    private static readonly Color BronzeColor = new Color(0.80f, 0.50f, 0.28f, 1f);
    private static readonly Color SilverColor = new Color(0.78f, 0.82f, 0.86f, 1f);

    /// <summary>Muted slate for a tier not yet earned - the menu's locked-content color.</summary>
    public static readonly Color Unearned = new Color(0.44f, 0.46f, 0.48f, 1f);

    public static Color TierColor(MedalTier tier) => tier switch
    {
        MedalTier.Bronze => BronzeColor,
        MedalTier.Silver => SilverColor,
        MedalTier.Gold => GoldenBlockDirector.GoldTint,
        _ => Unstyled(tier), // exhaustive on purpose: a new tier must get its own color, never inherit gold
    };

    public static string DisplayName(MedalTier tier) => tier switch
    {
        MedalTier.Bronze => "BRONZE",
        MedalTier.Silver => "SILVER",
        MedalTier.Gold => "GOLD",
        _ => UnstyledName(tier),
    };

    // A tier added to the enum without a style lands here: loud in the console, gold on screen
    // (the least-wrong stand-in) - so the miss is visible without breaking every medal surface.
    private static Color Unstyled(MedalTier tier)
    {
        Debug.LogError($"MedalStyle: no color for tier {tier} - add it to TierColor.");
        return GoldenBlockDirector.GoldTint;
    }

    private static string UnstyledName(MedalTier tier)
    {
        Debug.LogError($"MedalStyle: no display name for tier {tier} - add it to DisplayName.");
        return tier.ToString().ToUpperInvariant();
    }

    /// <summary>The tier's medal mark: the rendered block icon, or the procedural badge when
    /// the art is missing (fallback keeps the old earned/ghost look baked in; the real art
    /// relies on <see cref="IconTint"/> instead).</summary>
    public static Sprite Sprite(MedalTier tier, bool earned)
    {
        Sprite art = LoadMedalArt(tier);
        if (art != null) return art;

        if (!earned)
        {
            Color ghost = Unearned;
            ghost.a = 0.35f;
            return MenuSprites.CircleBadge(ghost, new Color(Unearned.r, Unearned.g, Unearned.b, 0.55f));
        }
        Color fill = TierColor(tier);
        Color rim = Color.Lerp(fill, Color.white, 0.45f);
        return MenuSprites.CircleBadge(fill, rim);
    }

    /// <summary>Image tint pairing <see cref="Sprite"/>: white when earned; unearned drops to
    /// a dark ghost so the slot reads as "still to do" without repainting the art.</summary>
    public static Color IconTint(bool earned)
        => earned ? Color.white : new Color(0.32f, 0.34f, 0.36f, 0.55f);

    // Loaded once per tier per domain; a missing file is remembered so the fallback path
    // doesn't hit Resources.Load every frame a card rebuilds.
    private static readonly Sprite[] MedalArt = new Sprite[LevelTiers.TierCount];
    private static readonly bool[] MedalArtMissing = new bool[LevelTiers.TierCount];

    private static Sprite LoadMedalArt(MedalTier tier)
    {
        int i = (int)tier;
        if (i < 0 || i >= MedalArt.Length) return null;
        if (MedalArt[i] == null && !MedalArtMissing[i])
        {
            MedalArt[i] = Resources.Load<Sprite>("Menu/medal_" + tier.ToString().ToLowerInvariant());
            MedalArtMissing[i] = MedalArt[i] == null;
        }
        return MedalArt[i];
    }
}
