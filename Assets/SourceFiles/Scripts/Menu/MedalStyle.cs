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

    // ---- Celebration visuals (results-card redesign, Nick 2026-08-29) -----------------------
    // Chip gradient, hero-number gradient and confetti palette are per-tier DATA here, code-
    // owned like every style value (no ScriptableObject configs - serialized defaults go
    // stale). The chip and ray sprites are procedural, generated once and cached.

    /// <summary>Dark text on the tier chip's gradient.</summary>
    public static readonly Color ChipText = new Color(0.10f, 0.08f, 0.06f, 1f);

    /// <summary>Light end of the tier gradients (chip left, hero-number right).</summary>
    public static Color TierLight(MedalTier tier) => tier switch
    {
        MedalTier.Bronze => new Color(0.90f, 0.60f, 0.36f, 1f),
        MedalTier.Silver => new Color(0.91f, 0.93f, 0.96f, 1f),
        MedalTier.Gold => new Color(1f, 0.85f, 0.45f, 1f),   // #FFD873
        _ => Unstyled(tier),
    };

    /// <summary>Deep end of the tier chip gradient.</summary>
    public static Color TierDeep(MedalTier tier) => tier switch
    {
        MedalTier.Bronze => new Color(0.66f, 0.40f, 0.21f, 1f),
        MedalTier.Silver => new Color(0.58f, 0.63f, 0.70f, 1f),
        MedalTier.Gold => new Color(0.94f, 0.65f, 0.18f, 1f), // #F0A62E
        _ => Unstyled(tier),
    };

    /// <summary>Cream anchor of the hero-number gradient (#FFF6DF).</summary>
    public static readonly Color HeroCream = new Color(1f, 0.965f, 0.875f, 1f);

    /// <summary>The confetti palette: pieces pick a random blend of TierLight and this
    /// partner. Gold carries the handoff's coral; bronze/silver stay in their own register.
    /// Exhaustive like every per-tier table on this class - a new tier must bring its own.</summary>
    public static void ConfettiColors(MedalTier tier, out Color a, out Color b)
    {
        a = TierLight(tier);
        b = tier switch
        {
            MedalTier.Bronze => new Color(1f, 0.74f, 0.47f, 1f),
            MedalTier.Silver => new Color(0.72f, 0.82f, 0.95f, 1f),
            MedalTier.Gold => new Color(1f, 0.55f, 0.46f, 1f), // coral #FF8B75
            _ => Unstyled(tier),
        };
    }

    /// <summary>The chip's on-screen size. The texture is generated at EXACTLY this size so
    /// the rounded caps never stretch - size the Image from here, never a literal.</summary>
    public static readonly Vector2 ChipSize = new Vector2(340f, 44f);

    /// <summary>The "{TIER} TIER REACHED" chip background: a horizontal light-to-deep gradient
    /// capsule at <see cref="ChipSize"/>.</summary>
    public static Sprite ChipSprite(MedalTier tier)
    {
        int i = (int)tier;
        if (i < 0 || i >= ChipSprites.Length) return null;
        if (ChipSprites[i] != null) return ChipSprites[i];

        int W = (int)ChipSize.x, H = (int)ChipSize.y;
        float radius = H * 0.5f;
        Texture2D tex = NewSpriteTexture(W, H);
        Color left = TierLight(tier), right = TierDeep(tier);
        Color[] pixels = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float dy = Mathf.Abs(y - (H - 1) * 0.5f);
            for (int x = 0; x < W; x++)
            {
                // Capsule alpha: distance from the horizontal center segment, 1px AA edge.
                float dx = x < radius ? radius - x : x > W - 1 - radius ? x - (W - 1 - radius) : 0f;
                Color c = Color.Lerp(left, right, x / (float)(W - 1));
                c.a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                pixels[y * W + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        ChipSprites[i] = FinishSprite(tex, W, H);
        return ChipSprites[i];
    }

    /// <summary>The rotating ray fan behind the celebration badge: soft radial wedges fading
    /// with distance, hollow at the center so the badge sits clean. White - tint per tier via
    /// the Image color.</summary>
    public static Sprite RayBurstSprite()
    {
        if (_rayBurst != null) return _rayBurst;

        const int S = 256;
        const float Rays = 12f;
        Texture2D tex = NewSpriteTexture(S, S);
        float half = (S - 1) * 0.5f;
        Color[] pixels = new Color[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float vx = x - half, vy = y - half;
                float dist = Mathf.Sqrt(vx * vx + vy * vy) / half;
                float alpha = 0f;
                if (dist <= 1f)
                {
                    float wedge = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(Mathf.Atan2(vy, vx) * Rays), 3f);
                    alpha = wedge * Mathf.Pow(1f - dist, 1.6f) * Mathf.Clamp01(dist / 0.10f);
                }
                pixels[y * S + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        _rayBurst = FinishSprite(tex, S, S);
        return _rayBurst;
    }

    // Same texture/sprite hygiene as RuntimeSprites/MenuSprites' generators: clamped,
    // bilinear, and hidden from the hierarchy/save (a bare DontSave texture leaked both).
    private static Texture2D NewSpriteTexture(int width, int height) => new Texture2D(
        width, height, TextureFormat.RGBA32, false)
    {
        hideFlags = HideFlags.HideAndDontSave,
        wrapMode = TextureWrapMode.Clamp,
        filterMode = FilterMode.Bilinear,
    };

    private static Sprite FinishSprite(Texture2D tex, int width, int height)
    {
        Sprite sprite = UnityEngine.Sprite.Create(tex, new Rect(0, 0, width, height),
            new Vector2(0.5f, 0.5f), 100f);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static readonly Sprite[] ChipSprites = new Sprite[LevelTiers.TierCount];
    private static Sprite _rayBurst;
}
