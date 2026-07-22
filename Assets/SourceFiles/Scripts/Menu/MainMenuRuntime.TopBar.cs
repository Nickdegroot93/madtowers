using System.Collections.Generic;
using System.Globalization;
using Coffee.UIEffects;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// The top status & currency bar, frosted-glass helper, and menu icon cache.
// (partial of MainMenuRuntime, split from the main file for readability - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private static void BuildTopStatusBar(Transform parent, ChapterDefinition chapter)
    {
        PlayerProfileStore.Snapshot profile = PlayerProfileStore.Current;
        Color chapterTint = chapter != null ? chapter.MenuAccentSecondaryColor : GoldBase;
        Sprite statBackground = chapter != null ? chapter.MenuBackgroundImage : null;

        RectTransform bar = CreateRect(parent, "TopStatusBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -34f), new Vector2(-48f, 122f));
        Image barImage = bar.gameObject.AddComponent<Image>();
        barImage.sprite = RuntimeSprites.RoundedPanel();
        barImage.type = Image.Type.Sliced;
        barImage.color = WithAlpha(Color.Lerp(chapterTint, TextPrimary, 0.18f), 0.07f);
        AddFrostedGlass(bar, statBackground, TopBarFrostWash);
        RuntimeUiKit.AddOutline(bar, GlassBorder);

        HorizontalLayoutGroup layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 14, 14);
        layout.spacing = 24f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        Image badge = CreateImage(bar, "LevelBadge",
            MenuSprites.PointHexBadge(
                new Color(0.10f, 0.095f, 0.085f, 0.62f),
                new Color(0.035f, 0.032f, 0.028f, 0.72f),
                GlassBorder),
            Color.white);
        LayoutElement badgeLayout = badge.gameObject.AddComponent<LayoutElement>();
        badgeLayout.preferredWidth = 82f;
        badgeLayout.preferredHeight = 82f;
        CreateTmp(badge.transform, "LevelText", profile.PlayerLevel.ToString(), 30, TextPrimary,
            TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.DefaultFont);

        RectTransform profileColumn = CreateRect(bar, "ProfileInfo",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        LayoutElement profileLayout = profileColumn.gameObject.AddComponent<LayoutElement>();
        profileLayout.minWidth = 210f;
        profileLayout.preferredWidth = 210f;
        profileLayout.preferredHeight = 82f;

        VerticalLayoutGroup profileStack = profileColumn.gameObject.AddComponent<VerticalLayoutGroup>();
        profileStack.padding = new RectOffset(0, 0, 16, 17);
        profileStack.spacing = 8f;
        profileStack.childAlignment = TextAnchor.MiddleLeft;
        profileStack.childControlWidth = true;
        profileStack.childControlHeight = true;
        profileStack.childForceExpandWidth = true;
        profileStack.childForceExpandHeight = false;

        TextMeshProUGUI playerName = CreateTmp(profileColumn, "PlayerName", profile.PlayerName, 18, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont,
            Vector2.zero, new Vector2(0f, 27f), new Vector2(0f, 1f));
        AutoSize(playerName, 14, 18);
        playerName.gameObject.AddComponent<LayoutElement>().preferredHeight = 27f;

        Image expTrack = CreateImage(profileColumn, "ExpTrack", RuntimeSprites.RoundedPanel(),
            new Color(0.02f, 0.019f, 0.017f, 0.36f));
        expTrack.type = Image.Type.Sliced;
        LayoutElement expLayout = expTrack.gameObject.AddComponent<LayoutElement>();
        expLayout.preferredWidth = 195f;
        expLayout.preferredHeight = 7f;
        expLayout.flexibleWidth = 0f;
        Image expFill = CreateImage(expTrack.transform, "ExpFill", RuntimeSprites.RoundedPanel(),
            new Color(1f, 0.72f, 0.32f, 1f));
        expFill.type = Image.Type.Sliced;
        RectTransform expFillRect = expFill.rectTransform;
        expFillRect.anchorMin = new Vector2(0f, 0f);
        expFillRect.anchorMax = new Vector2(Mathf.Clamp01(profile.Experience01), 1f);
        expFillRect.offsetMin = Vector2.zero;
        expFillRect.offsetMax = Vector2.zero;

        RectTransform spacer = CreateRect(bar, "StatusSpacer",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        LayoutElement spacerLayout = spacer.gameObject.AddComponent<LayoutElement>();
        spacerLayout.minWidth = 24f;
        spacerLayout.flexibleWidth = 1f;

        BuildCurrencyCard(bar, statBackground, "$", profile.Coins.ToString("N0", CultureInfo.InvariantCulture), null);

        // The attempts chip (SHOP.md §7): real meter once the meta systems unlock, absent
        // before that (soft landing) and absent for premium (the meter doesn't exist for them).
        if (AttemptsService.MeterActive)
        {
            System.TimeSpan regen = AttemptsService.NextRegenIn;
            bool showTimer = AttemptsService.Count < AttemptsService.MaxAttempts;
            BuildCurrencyCard(bar, statBackground, null,
                $"{AttemptsService.Count}/{AttemptsService.MaxAttempts}",
                showTimer ? $"{(int)regen.TotalMinutes:00}:{regen.Seconds:00}" : null);
        }
    }

    // Turns a freshly-built card (root = RoundedPanel fill) into a frosted-glass panel: a blurred
    // copy of the chapter background, clipped to the card's rounded silhouette and kept aligned to
    // the screen as the card scrolls/swipes, under a dark wash for legibility. Call right after the
    // fill image and BEFORE adding content so content draws on top. No-op without a background
    // (the card keeps its plain darkened fill).
    private static void AddFrostedGlass(RectTransform card, Sprite background, float washAlpha, float blurScale = 2f)
    {
        if (background == null) return;

        // Rounded clip frame, ignored by layout groups (e.g. the top bar's HorizontalLayoutGroup)
        // so it never counts as a layout item.
        RectTransform frame = CreateRect(card, "FrostedGlass",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        MakeRoundedMask(frame);
        frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        // Blurred backdrop: the chapter background, screen-locked (so each card shows the slice
        // behind it) and blurred via UIEffect (which blurs the element's own texture).
        Image blur = CreateImage(frame, "Blur", background, Color.white);
        blur.gameObject.AddComponent<MenuFrostedBackdrop>();
        UIEffect blurFx = blur.gameObject.AddComponent<UIEffect>();
        blurFx.samplingFilter = SamplingFilter.BlurFast;
        blurFx.samplingScale = blurScale;

        // Dark wash over the blur so text stays readable against bright backgrounds.
        Image wash = CreateImage(frame, "Wash", RuntimeSprites.RoundedPanel(), new Color(0.03f, 0.028f, 0.025f, washAlpha));
        wash.type = Image.Type.Sliced;
        Stretch(wash.rectTransform);
    }

    private static void BuildCurrencyCard(Transform parent, Sprite background, string coinGlyph, string primary, string secondary)
    {
        RectTransform card = CreateRect(parent, "StatusCard",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(232f, 70f));
        LayoutElement cardLayout = card.gameObject.AddComponent<LayoutElement>();
        cardLayout.preferredWidth = 232f;
        cardLayout.preferredHeight = 70f;
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.sprite = RuntimeSprites.RoundedPanel();
        cardImage.type = Image.Type.Sliced;
        cardImage.color = new Color(0.02f, 0.018f, 0.016f, 0.68f);
        AddFrostedGlass(card, background, CurrencyCardFrostWash);
        RuntimeUiKit.AddOutline(card, GlassBorder);

        if (!string.IsNullOrEmpty(coinGlyph))
        {
            Sprite coinIcon = MenuIcon("coin");
            if (coinIcon != null)
            {
                Image coin = CreateImage(card, "Coin", coinIcon, Color.white);
                coin.preserveAspect = true;
                SetRect(coin.rectTransform, new Vector2(18f, 0f), new Vector2(48f, 48f), new Vector2(0f, 0.5f));
            }
            else
            {
                // Fallback if the coin art is missing: the procedural golden bubble with a "$".
                Image coin = CreateImage(card, "Coin", RuntimeSprites.Bubble(), new Color(1f, 0.7f, 0.16f, 1f));
                SetRect(coin.rectTransform, new Vector2(18f, 0f), new Vector2(46f, 46f), new Vector2(0f, 0.5f));
                CreateTmp(coin.transform, "CoinGlyph", coinGlyph, 20, new Color(0.28f, 0.15f, 0.02f, 1f),
                    TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.DefaultFont);
            }
        }
        else
        {
            Sprite heartIcon = HeartSprites.Full();
            if (heartIcon != null)
            {
                Image heart = CreateImage(card, "Heart", heartIcon, Color.white);
                heart.preserveAspect = true;
                SetRect(heart.rectTransform, new Vector2(18f, 0f), new Vector2(50f, 50f), new Vector2(0f, 0.5f));
            }
            else
            {
                // Fallback if the heart art is missing: the procedural heart sprite.
                Image heart = CreateImage(card, "Heart", RuntimeSprites.Heart(), new Color(1f, 0.22f, 0.15f, 1f));
                SetRect(heart.rectTransform, new Vector2(18f, 0f), new Vector2(50f, 50f), new Vector2(0f, 0.5f));
            }
        }

        Vector2 primaryPosition = string.IsNullOrEmpty(secondary) ? new Vector2(78f, 0f) : new Vector2(78f, 12f);
        TextMeshProUGUI primaryText = CreateTmp(card, "Primary", primary, 23, TextPrimary, TextAnchor.MiddleLeft,
            FontStyle.Normal, RuntimeUiKit.DefaultFont, primaryPosition, new Vector2(96f, 34f), new Vector2(0f, 0.5f));
        AutoSize(primaryText, 16, 23);
        if (!string.IsNullOrEmpty(secondary))
        {
            CreateTmp(card, "Secondary", secondary, 17, TextMuted, TextAnchor.MiddleLeft,
                FontStyle.Normal, RuntimeUiKit.DefaultFont, new Vector2(78f, -14f), new Vector2(96f, 24f), new Vector2(0f, 0.5f));
        }

        // Divider + add button, pinned to the card's RIGHT edge (pivot-centred) rather than a
        // fixed left offset. Both cards then match exactly and the "+" keeps an even margin from
        // the edge instead of overflowing it - independent of the card's laid-out width.
        Image divider = CreateImage(card, "Divider", RuntimeSprites.Square(), WithAlpha(TextPrimary, 0.28f));
        SetCenteredAt(divider.rectTransform, new Vector2(1f, 0.5f), new Vector2(-52f, 0f), new Vector2(1.5f, 38f));
        TextMeshProUGUI plus = CreateTmp(card, "Plus", "+", 32, TextPrimary, TextAnchor.MiddleCenter,
            FontStyle.Normal, RuntimeUiKit.DefaultFont);
        SetCenteredAt(plus.rectTransform, new Vector2(1f, 0.5f), new Vector2(-26f, 0f), new Vector2(44f, 44f));
    }

    // Cached menu icons loaded by short name from Resources/Menu (e.g. "coin", "heart"). Drop a
    // transparent PNG into Assets/Resources/Menu and it imports as a sprite automatically (see
    // MenuArtImportSettings); a missing file returns null so call sites can fall back.
    private static readonly Dictionary<string, Sprite> MenuIconCache = new Dictionary<string, Sprite>();

    private static Sprite MenuIcon(string name)
    {
        if (!MenuIconCache.TryGetValue(name, out Sprite sprite))
        {
            sprite = Resources.Load<Sprite>($"Menu/{name}");
            MenuIconCache[name] = sprite;
        }
        return sprite;
    }

}
