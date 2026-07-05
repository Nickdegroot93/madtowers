using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime UGUI renderer for ability cards: the offer picker's three cards, the Vault's
/// collection cards, and the shared detail panel. Cards are neon glass slabs drawn from
/// procedural sprites (no authored frame art): a rounded vertical-gradient body tinted by
/// the rarity, wrapped in a bright NEON RING with a real outer bloom, heavy Archivo Black
/// display type, a solid type chip, the icon on a glowing white tile, and a ghost DETAILS
/// pill. AbilityChoiceController owns scheduling, pick routing, and modal flow.
///
/// Rarity is never written as a word - the colour of the neon edge carries it (the body
/// stays near-black at every tier), escalating from restraint to spectacle:
///   Common     faint silver ring
///   Rare       bright blue ring
///   Epic       hot violet ring + extra halo + a slow shine sweep
///   Legendary  gold ring, breathing halo, fast warm sweep
/// </summary>
public static class AbilityCardView
{
    // Offer-panel geometry, shared with AbilityChoiceController so the reserved row height
    // always matches the built cards.
    public const float PanelWidth = 1000f;
    public const float CardRowSpacing = 24f;
    public const float CardHeight = 500f;

    private static readonly Color BodyColor = new Color(0.85f, 0.89f, 0.94f, 1f);
    private static readonly Color LockedColor = new Color(0.45f, 0.46f, 0.50f, 1f);
    private static readonly Color PillDark = new Color(0.045f, 0.05f, 0.065f, 0.92f);

    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    // Card text is display-first: titles/chips/buttons speak Archivo Black, descriptions Inter.
    private static TextMeshProUGUI Display(TextMeshProUGUI tmp)
    {
        tmp.font = RuntimeUiKit.TmpDisplayFont;
        return tmp;
    }

    // ---- rarity tiers -------------------------------------------------------------------------

    private struct TierStyle
    {
        public float RingAlpha;      // neon edge strength - where the rarity colour lives
        public float TopLerp;        // body top = Lerp(accent, black, TopLerp); the body stays
                                     // NEAR-BLACK at every tier - only the tint whisper varies
        public float HaloAlpha;      // extra outer bloom beyond the ring's own (0 = none)
        public float IconGlow;       // soft accent glow behind the icon tile
        public bool Shine;           // periodic light sweep across the card
        public bool Pulse;           // the halo breathes (legendary only)
        public float ShinePause;     // seconds between sweeps
    }

    private static TierStyle GetTier(AbilityRarity rarity)
    {
        switch (rarity)
        {
            case AbilityRarity.Legendary:
                return new TierStyle { RingAlpha = 1f, TopLerp = 0.78f, HaloAlpha = 0.22f, IconGlow = 0.30f, Shine = true, Pulse = true, ShinePause = 2.0f };
            case AbilityRarity.Epic:
                return new TierStyle { RingAlpha = 1f, TopLerp = 0.80f, HaloAlpha = 0.14f, IconGlow = 0.26f, Shine = true, ShinePause = 3.6f };
            case AbilityRarity.Rare:
                return new TierStyle { RingAlpha = 0.85f, TopLerp = 0.84f, IconGlow = 0.22f };
            default:
                return new TierStyle { RingAlpha = 0.35f, TopLerp = 0.90f, IconGlow = 0.10f };
        }
    }

    private static Color ShineColor(AbilityRarity rarity, Color accent) =>
        rarity == AbilityRarity.Legendary
            ? new Color(1f, 0.95f, 0.75f, 0.26f)
            : WithAlpha(Color.Lerp(accent, Color.white, 0.55f), 0.16f);

    // ---- "CHOOSE AN ABILITY" header -------------------------------------------------------------

    /// <summary>Offer header: a letter-spaced rarity-tinted overline flanked by fading bars and
    /// diamond points, over the display-face title. `accent` is the offer's rarity colour
    /// (offers are single-rarity, so the tint is meaningful).</summary>
    public static void CreateHeader(Transform parent, Color accent)
    {
        GameObject header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(parent, false);
        LayoutElement headerElement = header.AddComponent<LayoutElement>();
        headerElement.preferredHeight = 148f;
        headerElement.flexibleHeight = 0f;

        TextMeshProUGUI overline = Display(RuntimeUiKit.CreateTmp(header.transform, "Overline",
            "MILESTONE REWARD", 19, WithAlpha(Color.Lerp(accent, Color.white, 0.3f), 0.95f),
            TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.TitleFont,
            new Vector2(0f, -28f), new Vector2(560f, 30f), new Vector2(0.5f, 1f)));
        overline.characterSpacing = 10f;

        CreateHeaderFlourish(header.transform, accent, leftSide: true);
        CreateHeaderFlourish(header.transform, accent, leftSide: false);

        TextMeshProUGUI title = Display(RuntimeUiKit.CreateTmp(header.transform, "Title",
            "CHOOSE AN ABILITY", 44, RuntimeUiKit.TitleColor,
            TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.TitleFont,
            new Vector2(0f, -66f), new Vector2(920f, 72f), new Vector2(0.5f, 1f)));
        title.characterSpacing = 5f;
    }

    // A soft bar fading toward the screen edge, tipped by a small diamond pointing at the
    // overline - sits on the overline's row, left or right of the text.
    private static void CreateHeaderFlourish(Transform parent, Color accent, bool leftSide)
    {
        RectTransform rect = RuntimeUiKit.CreateRect(parent, leftSide ? "FlourishL" : "FlourishR",
            new Vector2(leftSide ? 0.03f : 0.72f, 1f), new Vector2(leftSide ? 0.28f : 0.97f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(0f, 30f));

        Image bar = RuntimeUiKit.CreateImage(rect, "Bar", RuntimeSprites.SoftHorizontalBar(0.1f),
            WithAlpha(accent, 0.5f));
        RectTransform barRect = bar.rectTransform;
        barRect.anchorMin = new Vector2(0f, 0.5f);
        barRect.anchorMax = new Vector2(1f, 0.5f);
        barRect.offsetMin = new Vector2(leftSide ? 0f : 22f, -2f);
        barRect.offsetMax = new Vector2(leftSide ? -22f : 0f, 2f);

        Image diamond = RuntimeUiKit.CreateImage(rect, "Diamond", RuntimeSprites.Diamond(),
            Color.Lerp(accent, Color.white, 0.25f));
        RectTransform diamondRect = diamond.rectTransform;
        diamondRect.anchorMin = diamondRect.anchorMax = new Vector2(leftSide ? 1f : 0f, 0.5f);
        diamondRect.sizeDelta = new Vector2(13f, 13f);
    }

    // ---- shared card pieces ---------------------------------------------------------------------

    // The gradient body and neon ring live on a PADDED canvas (room for the outer bloom), so
    // both stretch CardSpritePad past the card rect on every side.
    private static Image AddPaddedSprite(Transform root, string name, Sprite sprite, Color color, float extra = 0f)
    {
        Image image = RuntimeUiKit.CreateImage(root, name, sprite, color);
        image.type = Image.Type.Sliced;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        float pad = RuntimeSprites.CardSpritePad + extra;
        rect.offsetMin = new Vector2(-pad, -pad);
        rect.offsetMax = new Vector2(pad, pad);
        return image;
    }

    // Body + halo + ring. No ornaments - the card is a clean near-black slab and the neon
    // edge carries the rarity. Content goes on top; the shine sweep (tier.Shine) must be
    // attached AFTER content so it sweeps over everything. Returns the body Image (the pick
    // button's target graphic).
    private static Image BuildCardChrome(Transform root, Color accent, TierStyle tier, bool discovered)
    {
        Color top = discovered
            ? WithAlpha(Color.Lerp(accent, Color.black, tier.TopLerp), 0.985f)
            : new Color(0.075f, 0.08f, 0.09f, 0.98f);
        Color bottom = discovered
            ? WithAlpha(Color.Lerp(accent, Color.black, 0.94f), 0.985f)
            : new Color(0.04f, 0.045f, 0.055f, 0.98f);
        Image body = AddPaddedSprite(root, "Body", RuntimeSprites.CardGradient(top, bottom), Color.white);

        if (discovered && tier.HaloAlpha > 0f)
        {
            Image halo = AddPaddedSprite(root, "Halo", MenuSprites.GlowFrame(),
                WithAlpha(accent, tier.HaloAlpha), extra: 10f);
            if (tier.Pulse) halo.gameObject.AddComponent<UiGlowPulse>();
        }

        // Locked cards keep a NEUTRAL ring - the rarity colour is part of the discovery reward.
        AddPaddedSprite(root, "Ring", RuntimeSprites.CardNeonRing(), discovered
            ? WithAlpha(Color.Lerp(accent, Color.white, 0.15f), tier.RingAlpha)
            : WithAlpha(LockedColor, 0.25f));

        return body;
    }

    private static void AddTitle(Transform root, string text, float top, float height, float maxSize, Color color)
    {
        RectTransform rect = RuntimeUiKit.CreateRect(root, "Title",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        rect.offsetMin = new Vector2(16f, top - height);
        rect.offsetMax = new Vector2(-16f, top);

        TextMeshProUGUI title = Display(RuntimeUiKit.CreateTmp(rect, "Text", text.ToUpperInvariant(),
            (int)maxSize, color, TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.TitleFont));
        title.characterSpacing = 2f;
        title.textWrappingMode = TextWrappingModes.Normal;
        title.overflowMode = TextOverflowModes.Truncate;
        RuntimeUiKit.AutoSize(title, 14f, maxSize);
    }

    // Type badge (PASSIVE / INSTANT / CONSUMABLE): a solid pill tinted by the DERIVED ability
    // type (see ABILITIES.md - kind colour is a second information axis beside rarity).
    private static void AddTypeChip(Transform root, AbilityType type, float centerY, float scale = 1f)
    {
        Color typeColor = AbilityTypeInfo.GetColor(type);
        Image pill = RuntimeUiKit.CreateImage(root, "TypeChip", RuntimeSprites.RoundedPanel(),
            WithAlpha(Color.Lerp(typeColor, Color.black, 0.58f), 0.95f));
        pill.type = Image.Type.Sliced;
        pill.pixelsPerUnitMultiplier = 1.4f;
        RuntimeUiKit.SetRect(pill.rectTransform, new Vector2(0f, centerY),
            new Vector2(160f * scale, 34f * scale), new Vector2(0.5f, 1f));

        TextMeshProUGUI label = Display(RuntimeUiKit.CreateTmp(pill.transform, "Label",
            AbilityTypeInfo.GetLabel(type), Mathf.RoundToInt(14f * scale),
            Color.Lerp(typeColor, Color.white, 0.6f), TextAnchor.MiddleCenter,
            FontStyle.Normal, RuntimeUiKit.TitleFont));
        label.characterSpacing = 3f;
        RuntimeUiKit.AutoSize(label, 9f, 14f * scale);
    }

    // The icon on a light rounded tile lifted by a soft accent glow (authored glyphs are drawn
    // against white). Locked entries flip to a near-black silhouette tease.
    private static RectTransform AddIconTile(Transform root, Sprite icon, Color accent, float top,
        float size, bool discovered, float glowAlpha)
    {
        RectTransform holder = RuntimeUiKit.CreateRect(root, "IconSlot",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, top), new Vector2(size, size));

        if (discovered && glowAlpha > 0f)
        {
            Image glow = RuntimeUiKit.CreateImage(holder, "Glow", MenuSprites.GlowFrame(),
                WithAlpha(accent, glowAlpha));
            glow.type = Image.Type.Sliced;
            RectTransform glowRect = glow.rectTransform;
            glowRect.anchorMin = Vector2.zero;
            glowRect.anchorMax = Vector2.one;
            glowRect.offsetMin = new Vector2(-12f, -12f);
            glowRect.offsetMax = new Vector2(12f, 12f);
        }

        Image tile = RuntimeUiKit.CreateImage(holder, "IconTile", RuntimeSprites.RoundedPanel(),
            discovered ? new Color(0.97f, 0.97f, 0.98f, 1f) : new Color(0.03f, 0.03f, 0.035f, 1f));
        tile.type = Image.Type.Sliced;
        RuntimeUiKit.Stretch(tile.rectTransform);

        RuntimeUiKit.AddOutline(tile.transform,
            discovered ? WithAlpha(accent, 0.5f) : WithAlpha(LockedColor, 0.15f));

        Image glyph = RuntimeUiKit.CreateImage(tile.transform, "Glyph",
            icon != null ? icon : RuntimeSprites.AbilityGlyph(),
            discovered
                ? (icon != null ? Color.white : Color.Lerp(accent, Color.white, 0.3f))
                : new Color(0.05f, 0.05f, 0.06f, 0.9f));
        glyph.preserveAspect = true;
        RectTransform glyphRect = glyph.rectTransform;
        glyphRect.anchorMin = Vector2.zero;
        glyphRect.anchorMax = Vector2.one;
        float inset = size * 0.11f;
        glyphRect.offsetMin = new Vector2(inset, inset);
        glyphRect.offsetMax = new Vector2(-inset, -inset);
        return holder;
    }

    // "OWNED ×N" - a small gold tag riding the icon tile's top-right corner.
    private static void AddOwnedBadge(RectTransform tile, int stacks)
    {
        Image pill = RuntimeUiKit.CreateImage(tile, "Owned", RuntimeSprites.RoundedPanel(),
            new Color(1f, 0.9f, 0.68f, 1f));
        pill.type = Image.Type.Sliced;
        pill.pixelsPerUnitMultiplier = 3f;
        RectTransform rect = pill.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.72f, 0.5f);
        rect.anchoredPosition = new Vector2(6f, 4f);
        rect.sizeDelta = new Vector2(104f, 30f);

        TextMeshProUGUI label = Display(RuntimeUiKit.CreateTmp(pill.transform, "Label",
            $"OWNED ×{stacks}", 13, new Color(0.16f, 0.13f, 0.05f, 1f), TextAnchor.MiddleCenter,
            FontStyle.Normal, RuntimeUiKit.TitleFont));
        label.characterSpacing = 1f;
    }

    private static void AddDescription(Transform root, string text, float top, float bottom, float maxSize)
    {
        RectTransform rect = RuntimeUiKit.CreateRect(root, "Description",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        rect.offsetMin = new Vector2(22f, bottom);
        rect.offsetMax = new Vector2(-22f, top);

        TextMeshProUGUI body = RuntimeUiKit.CreateTmp(rect, "Text", text, (int)maxSize, BodyColor,
            TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.DefaultFont);
        body.font = RuntimeUiKit.TmpTitleFont; // Inter reads cleaner than the built-in body face
        body.textWrappingMode = TextWrappingModes.Normal;
        body.overflowMode = TextOverflowModes.Truncate;
        RuntimeUiKit.AutoSize(body, 16f, maxSize);
    }

    // DETAILS: a dark pill with a bright accent outline (the mockups' ghost button), spanning
    // the card width at a comfortable mobile touch height. A nested Button whose raycast
    // target absorbs the tap, so opening details never also picks the card.
    private static void AddDetailsButton(Transform root, Color accent, Action onDetails)
    {
        GameObject go = new GameObject("Details", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(root, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.offsetMin = new Vector2(26f, 18f);
        rect.offsetMax = new Vector2(-26f, 18f + 64f);

        Image pill = go.GetComponent<Image>();
        pill.sprite = RuntimeSprites.RoundedPanel();
        pill.type = Image.Type.Sliced;
        pill.color = PillDark;
        pill.raycastTarget = true;

        RuntimeUiKit.AddOutline(go.transform, WithAlpha(Color.Lerp(accent, Color.white, 0.35f), 0.9f));

        Button button = go.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.targetGraphic = pill;
        button.onClick.AddListener(() => onDetails?.Invoke());

        TextMeshProUGUI label = Display(RuntimeUiKit.CreateTmp(go.transform, "Label", "DETAILS", 19,
            Color.Lerp(accent, Color.white, 0.7f), TextAnchor.MiddleCenter, FontStyle.Normal,
            RuntimeUiKit.TitleFont));
        label.characterSpacing = 4f;
    }

    // ---- offer card -----------------------------------------------------------------------------

    /// <summary>One tappable offer card (the whole card picks; DETAILS opens the detail panel).
    /// Sized by the offer row: width splits evenly, height is the row's CardHeight.</summary>
    public static void Create(Transform parent, AbilityDefinition definition, AbilityRuntime runtime,
        Action<AbilityDefinition> onPick, Action<AbilityDefinition> onDetails)
    {
        Color accent = AbilityRarityInfo.GetColor(definition.Rarity);
        TierStyle tier = GetTier(definition.Rarity);
        int stacks = runtime != null ? runtime.GetOwnedStacks(definition) : 0;

        GameObject cardObject = new GameObject(definition.DisplayName, typeof(RectTransform));
        cardObject.transform.SetParent(parent, false);

        // Equal card widths no matter the content: identical preferred + flexible weights.
        LayoutElement cardElement = cardObject.AddComponent<LayoutElement>();
        cardElement.preferredWidth = 10f;
        cardElement.flexibleWidth = 1f;

        Image body = BuildCardChrome(cardObject.transform, accent, tier, discovered: true);
        body.raycastTarget = true;

        Button button = cardObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.colorMultiplier = 1f;
        button.colors = colors;
        button.targetGraphic = body;
        AbilityDefinition picked = definition;
        button.onClick.AddListener(() => onPick?.Invoke(picked));

        AddTitle(cardObject.transform, definition.DisplayName, -28f, 62f, 29f, RuntimeUiKit.TitleColor);
        AddTypeChip(cardObject.transform, definition.Type, -100f);
        RectTransform tile = AddIconTile(cardObject.transform, definition.Icon, accent, -146f, 158f,
            true, tier.IconGlow);
        if (stacks > 0) AddOwnedBadge(tile, stacks);
        AddDescription(cardObject.transform, definition.ShortDescriptionFor(stacks), -324f, -402f, 24f);
        AbilityDefinition detailDef = definition;
        AddDetailsButton(cardObject.transform, accent, () => onDetails?.Invoke(detailDef));

        if (tier.Shine) AbilityCardShine.Attach(cardObject.transform, ShineColor(definition.Rarity, accent), tier.ShinePause);
    }

    // ---- collection card (Vault) ------------------------------------------------------------------

    /// <summary>
    /// The Vault's collection card: the same neon glass dressing as an offer card, minus the
    /// offer-only chrome (no pick handler, no DETAILS sub-button, no Owned tag). Grid cards drop
    /// the description (unreadable at grid size - the whole card opens the detail view);
    /// <paramref name="large"/> re-adds it for the detail modal. An undiscovered ability renders
    /// as a SILHOUETTE tease: darkened glass, near-black icon shadow, "???" - the name and rarity
    /// colour stay part of the reward. Fills its parent rect; returns the card root.
    /// </summary>
    public static GameObject CreateCollectionCard(Transform parent, AbilityDefinition definition,
        bool discovered, bool large)
    {
        Color accent = AbilityRarityInfo.GetColor(definition.Rarity);
        TierStyle tier = GetTier(definition.Rarity);

        GameObject cardObject = new GameObject(definition.name, typeof(RectTransform));
        cardObject.transform.SetParent(parent, false);
        RuntimeUiKit.Stretch((RectTransform)cardObject.transform);

        BuildCardChrome(cardObject.transform, accent, tier, discovered);

        if (large)
        {
            AddTitle(cardObject.transform, discovered ? definition.DisplayName : "???",
                -36f, 80f, 38f, discovered ? RuntimeUiKit.TitleColor : LockedColor);
            if (discovered) AddTypeChip(cardObject.transform, definition.Type, -132f, 1.15f);
            AddIconTile(cardObject.transform, definition.Icon, accent, -190f, 240f, discovered, tier.IconGlow);
            if (discovered)
                AddDescription(cardObject.transform, definition.ShortDescriptionFor(0), -450f, -602f, 24f);
        }
        else
        {
            AddTitle(cardObject.transform, discovered ? definition.DisplayName : "???",
                -26f, 66f, 26f, discovered ? RuntimeUiKit.TitleColor : LockedColor);
            if (discovered)
            {
                AddTypeChip(cardObject.transform, definition.Type, -102f, 0.92f);
            }
            else
            {
                Image lockIcon = RuntimeUiKit.CreateImage(cardObject.transform, "Lock",
                    MenuSprites.Lock(LockedColor), Color.white);
                lockIcon.preserveAspect = true;
                RuntimeUiKit.SetRect(lockIcon.rectTransform, new Vector2(0f, -102f),
                    new Vector2(34f, 34f), new Vector2(0.5f, 1f));
            }
            AddIconTile(cardObject.transform, definition.Icon, accent, -152f, 194f, discovered, tier.IconGlow);
        }

        if (discovered && tier.Shine)
            AbilityCardShine.Attach(cardObject.transform, ShineColor(definition.Rarity, accent), tier.ShinePause);

        return cardObject;
    }

    // ---- detail panel (offer's DETAILS view) -------------------------------------------------------

    /// <summary>The full-presentation detail view: rarity chrome, big icon, title, type chip,
    /// LONG description, and Choose/Back. Built on the caller's modal canvas.</summary>
    public static void CreateDetailPanel(Transform parent, AbilityDefinition definition, int stacks,
        Action onChoose, Action onBack)
    {
        Color accent = AbilityRarityInfo.GetColor(definition.Rarity);
        TierStyle tier = GetTier(definition.Rarity);

        RectTransform panel = RuntimeUiKit.CreateRect(parent, "DetailPanel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(720f, 1020f));
        Image body = BuildCardChrome(panel, accent, tier, discovered: true);
        body.raycastTarget = true;

        AddTitle(panel, definition.DisplayName, -38f, 84f, 42f, RuntimeUiKit.TitleColor);
        AddTypeChip(panel, definition.Type, -134f, 1.1f);
        RectTransform tile = AddIconTile(panel, definition.Icon, accent, -188f, 216f, true, tier.IconGlow);
        if (stacks > 0) AddOwnedBadge(tile, stacks);

        RectTransform bodyRect = RuntimeUiKit.CreateRect(panel, "LongDescription",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        bodyRect.offsetMin = new Vector2(46f, -770f);
        bodyRect.offsetMax = new Vector2(-46f, -444f);
        TextMeshProUGUI longDesc = RuntimeUiKit.CreateTmp(bodyRect, "Text", definition.LongDescription, 26,
            BodyColor, TextAnchor.UpperCenter, FontStyle.Normal, RuntimeUiKit.DefaultFont);
        longDesc.font = RuntimeUiKit.TmpTitleFont;
        longDesc.textWrappingMode = TextWrappingModes.Normal;
        longDesc.overflowMode = TextOverflowModes.Truncate;
        RuntimeUiKit.AutoSize(longDesc, 18f, 26f);

        CreateActionButton(panel, $"CHOOSE {definition.DisplayName.ToUpperInvariant()}",
            new Vector2(0f, 134f), new Vector2(560f, 92f), primary: true, accent, onChoose);
        CreateActionButton(panel, "BACK",
            new Vector2(0f, 44f), new Vector2(560f, 70f), primary: false, accent, onBack);

        if (tier.Shine) AbilityCardShine.Attach(panel, ShineColor(definition.Rarity, accent), tier.ShinePause);
    }

    // A hand-anchored rounded button: primary = filled with the accent (dark label);
    // ghost = dark pill with a bright outline (accent label).
    private static void CreateActionButton(Transform parent, string label, Vector2 bottomOffset,
        Vector2 size, bool primary, Color accent, Action onClick)
    {
        GameObject go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = bottomOffset;
        rect.sizeDelta = size;

        Image image = go.GetComponent<Image>();
        image.sprite = RuntimeSprites.RoundedPanel();
        image.type = Image.Type.Sliced;
        image.color = Color.white;
        image.raycastTarget = true;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        ApplyButtonPalette(button, primary, accent);
        if (!primary) RuntimeUiKit.AddOutline(go.transform, WithAlpha(Color.Lerp(accent, Color.white, 0.35f), 0.9f));
        button.onClick.AddListener(() => onClick?.Invoke());

        TextMeshProUGUI text = Display(RuntimeUiKit.CreateTmp(go.transform, "Label", label,
            primary ? 25 : 21,
            primary ? Color.Lerp(accent, Color.black, 0.82f) : Color.Lerp(accent, Color.white, 0.7f),
            TextAnchor.MiddleCenter, FontStyle.Normal, RuntimeUiKit.TitleFont));
        text.characterSpacing = 2f;
        RuntimeUiKit.AutoSize(text, 15f, primary ? 25f : 21f);
    }

    // The fill lives in the ColorBlock (image stays white) so Button tinting works, same
    // pattern as GameMenuStyle.StyleButton.
    private static void ApplyButtonPalette(Button button, bool primary, Color accent)
    {
        Color fill = primary
            ? WithAlpha(Color.Lerp(accent, Color.white, 0.1f), 1f)
            : PillDark;
        ColorBlock colors = button.colors;
        colors.normalColor = fill;
        colors.highlightedColor = primary ? Color.Lerp(fill, Color.white, 0.1f) : new Color(0.14f, 0.15f, 0.18f, 0.95f);
        colors.pressedColor = primary ? Color.Lerp(fill, Color.black, 0.2f) : new Color(0.02f, 0.025f, 0.035f, 0.95f);
        colors.selectedColor = colors.highlightedColor;
        colors.colorMultiplier = 1f;
        button.colors = colors;
    }

    // ---- restyle helpers for kit-built panels/buttons (swap dialog, reroll) -------------------------

    /// <summary>Give a kit panel (CreateCenteredPanel, drawBackground:false) the card look:
    /// gradient glass + the neon ring, tinted by `accent`.</summary>
    public static void StyleModalPanel(GameObject panel, Color accent)
    {
        Image body = AddPaddedSprite(panel.transform, "Body",
            RuntimeSprites.CardGradient(
                WithAlpha(Color.Lerp(accent, Color.black, 0.72f), 0.98f),
                WithAlpha(Color.Lerp(accent, Color.black, 0.92f), 0.98f)), Color.white);
        body.raycastTarget = true;
        body.transform.SetAsFirstSibling();
        Image ring = AddPaddedSprite(panel.transform, "Ring", RuntimeSprites.CardNeonRing(),
            WithAlpha(Color.Lerp(accent, Color.white, 0.2f), 0.7f));
        ring.transform.SetSiblingIndex(1);
        // Keep the chrome out of the panel's vertical layout flow.
        body.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        ring.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
    }

    /// <summary>Restyle a kit button (legacy Text child) as a filled accent primary.</summary>
    public static void StylePrimaryButton(Button button, Color accent)
    {
        StyleKitButton(button, primary: true, accent);
    }

    /// <summary>Restyle a kit button (legacy Text child) as a ghost pill: dark fill,
    /// bright outline, accent label.</summary>
    public static void StyleGhostButton(Button button, Color accent)
    {
        StyleKitButton(button, primary: false, accent);
    }

    private static void StyleKitButton(Button button, bool primary, Color accent)
    {
        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = RuntimeSprites.RoundedPanel();
            image.type = Image.Type.Sliced;
            image.color = Color.white;
        }
        ApplyButtonPalette(button, primary, accent);
        if (!primary) RuntimeUiKit.AddOutline(button.transform, WithAlpha(Color.Lerp(accent, Color.white, 0.35f), 0.9f));

        Text label = button.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.font = RuntimeUiKit.TitleFont;
            label.fontSize = 26;
            label.fontStyle = FontStyle.Bold;
            label.color = primary
                ? Color.Lerp(accent, Color.black, 0.82f)
                : Color.Lerp(accent, Color.white, 0.7f);
        }
    }
}
