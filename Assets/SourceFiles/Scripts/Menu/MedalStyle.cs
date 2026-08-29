using UnityEngine;

/// <summary>
/// The one owner of how medal tiers LOOK (colors, sprite, display name) - every surface
/// (level cards, summary modal, results card, in-run toast) reads this so the three medals
/// can never drift apart. The sprite is currently a procedural placeholder (MenuSprites
/// circle badge); when Nick's real medal art lands, swap the body of <see cref="Sprite"/>
/// for a Resources.Load and nothing else changes.
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

    /// <summary>The tier's medal mark. Earned = full tier color with a lifted rim; unearned =
    /// the locked slate at low presence, so an empty slot reads as "still to do", not as art.</summary>
    public static Sprite Sprite(MedalTier tier, bool earned)
    {
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
}
