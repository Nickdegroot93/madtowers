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
        Color chapterTint = chapter != null ? chapter.MenuAccentSecondaryColor : MenuAccent;
        Sprite statBackground = chapter != null ? chapter.MenuBackgroundImage : null;

        RectTransform bar = CreateRect(parent, "TopStatusBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -34f), new Vector2(-48f, 122f));
        Image barImage = bar.gameObject.AddComponent<Image>();
        barImage.sprite = RuntimeSprites.RoundedPanel();
        barImage.type = Image.Type.Sliced;
        barImage.color = WithAlpha(Color.Lerp(chapterTint, TextPrimary, 0.18f), 0.07f);
        // Register the chapter-tinted pieces for the swipe cross-fade (see OnChapterBlend).
        _topBarWashImage = barImage;
        _chromeFrostBlurs.Clear();
        AddFrostedGlass(bar, statBackground, TopBarFrostWash, chapterBlend: true);
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
        TextMeshProUGUI levelText = CreateTmp(badge.transform, "LevelText", profile.PlayerLevel.ToString(), 30, TextPrimary,
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

        // The server identity (auto "Builder-XXXX" until claimed, BACKEND.md §3.2); falls
        // back to the placeholder while offline/disabled. TopBarLive re-renders it when the
        // online state or profile changes (name claim, boot completing after the bar built).
        // 26pt with a 20pt autosize floor: the old 18/14 was unreadable on a phone (Nick
        // 2026-08-01) - names are identity, not fine print. The taller line box (34px) plus
        // stack padding+spacing+bar still lands exactly on the column's 82px.
        TextMeshProUGUI playerName = CreateTmp(profileColumn, "PlayerName", OnlineService.DisplayName, 26, TextPrimary,
            TextAnchor.MiddleLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont,
            Vector2.zero, new Vector2(0f, 34f), new Vector2(0f, 1f));
        AutoSize(playerName, 20, 26);
        playerName.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        // Tapping your name opens the claim flow (BACKEND.md §3.4 prompt #2). The whole
        // profile column is the target so the tap area clears the 64px contract.
        Image nameTapCatcher = profileColumn.gameObject.AddComponent<Image>();
        nameTapCatcher.color = Color.clear;
        Button nameTap = profileColumn.gameObject.AddComponent<Button>();
        nameTap.transition = Selectable.Transition.None;
        nameTap.onClick.AddListener(() =>
        {
            if (!OnlineService.Enabled) return;
            SfxPlayer.Play("ui-button-click");
            OpenClaimNameModal(null);
        });

        Image expTrack = CreateImage(profileColumn, "ExpTrack", RuntimeSprites.RoundedPanel(),
            new Color(0.02f, 0.019f, 0.017f, 0.36f));
        expTrack.type = Image.Type.Sliced;
        LayoutElement expLayout = expTrack.gameObject.AddComponent<LayoutElement>();
        expLayout.preferredWidth = 195f;
        expLayout.preferredHeight = 7f;
        expLayout.flexibleWidth = 0f;
        Image expFill = CreateImage(expTrack.transform, "ExpFill", RuntimeSprites.RoundedPanel(),
            chapter != null ? ChapterLight(chapter) : new Color(1f, 0.72f, 0.32f, 1f));
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

        // No "+" on the wallet: coins are earned in play, never bought (SHOP.md), so an add
        // affordance would be a lie (Nick 2026-08-01 - it did nothing).
        BuildCurrencyCard(bar, statBackground, "$", profile.Coins.ToString("N0", CultureInfo.InvariantCulture), null,
            addButton: false);

        // The attempts chip (SHOP.md §7): real meter once the meta systems unlock, absent
        // before that (soft landing). PREMIUM outranks everything (Nick 2026-07-30): the
        // chip stays, showing flag + ∞ with no "+" - unlimited is true online AND offline,
        // so it even outranks the OFFLINE chip (the modal carries the unranked warning).
        // While the online layer is enabled but unreachable (free players) the chip says
        // OFFLINE - campaign runs can't start (BACKEND.md §5.1) and the top bar admits it.
        RectTransform attemptsCard = null;
        GameObject adRefillSlot = null;
        if (PremiumStore.IsPremium && AttemptsService.MetaEnabled)
        {
            attemptsCard = BuildCurrencyCard(bar, statBackground, null, "∞", null, addButton: false);
            TextMeshProUGUI infinity = FindTmp(attemptsCard, "Primary");
            infinity.enableAutoSizing = false;   // the glyph is the whole message - let it be big
            infinity.fontSize = 44f;
            // ∞ is an x-height glyph (visual centre ~0.27em vs the line centre ~0.35em), so
            // Middle alignment renders it visibly LOW next to the dead-centred icon at this
            // size - lift the box to put the loops back on the icon's midline (Nick 2026-08-01).
            infinity.rectTransform.anchoredPosition += new Vector2(0f, 4f);
        }
        else if (AttemptsService.OnlineBlocked)
        {
            attemptsCard = BuildCurrencyCard(bar, statBackground, null, "OFFLINE", null, addButton: false);
        }
        else if (AttemptsService.MeterActive)
        {
            System.TimeSpan regen = AttemptsService.NextRegenIn;
            bool showTimer = AttemptsService.Count < AttemptsService.MaxAttempts;
            attemptsCard = BuildCurrencyCard(bar, statBackground, null,
                $"{AttemptsService.Count}/{AttemptsService.MaxAttempts}",
                showTimer ? $"{(int)regen.TotalMinutes:00}:{regen.Seconds:00}" : null);
            adRefillSlot = WireAdRefillPlus(attemptsCard);

            // The WHOLE chip opens the refill offer, not just the "+" (Nick 2026-08-09):
            // the card is the natural "I want more lives" tap target, and the modal is
            // where the choice (Unlimited vs. an ad) is actually explained.
            Image cardHit = attemptsCard.gameObject.GetComponent<Image>()
                ?? attemptsCard.gameObject.AddComponent<Image>();
            if (cardHit.sprite == null && cardHit.color == default) cardHit.color = Color.clear;
            cardHit.raycastTarget = true;
            Button cardButton = attemptsCard.gameObject.GetComponent<Button>()
                ?? attemptsCard.gameObject.AddComponent<Button>();
            cardButton.targetGraphic = cardHit;
            cardButton.transition = Selectable.Transition.None;
            cardButton.onClick.AddListener(() =>
            {
                SfxPlayer.Play("ui-button-click");
                OpenRefillOffer();
            });
        }

        // Live refresh: name + meter numbers change underneath the bar (boot completing,
        // name claim, regen ticking). The bar is otherwise a build-time snapshot.
        TopBarLive live = bar.gameObject.AddComponent<TopBarLive>();
        live.PlayerName = playerName;
        live.LevelText = levelText;
        live.ExpFill = expFillRect;
        live.AttemptsPrimary = attemptsCard != null ? FindTmp(attemptsCard, "Primary") : null;
        live.AttemptsSecondary = attemptsCard != null ? FindTmp(attemptsCard, "Secondary") : null;
        live.AdRefillSlot = adRefillSlot;
        live.BuiltMode = ChipMode();
    }

    /// <summary>The meter chip's "+": opens the refill offer, exactly like tapping the
    /// chip itself (Nick 2026-08-09 - it used to START AN AD outright, which read as
    /// broken: no explanation, no choice, just a video). The modal is where Unlimited is
    /// pitched and the ad is chosen knowingly. The plus still only SHOWS while a watch
    /// could pay out (meter below max, showable non-rate-limited ad - TopBarLive
    /// re-evaluates every tick), because it advertises the ad option specifically.</summary>
    private static GameObject WireAdRefillPlus(RectTransform attemptsCard)
    {
        Transform slot = attemptsCard.Find("AddSlot");
        if (slot == null) return null;

        Image hit = slot.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Button button = slot.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            OpenRefillOffer();
        });

        slot.gameObject.SetActive(AdRefillPlusShouldShow());
        return slot.gameObject;
    }

    private static bool AdRefillPlusShouldShow() =>
        AttemptsService.Count < AttemptsService.MaxAttempts && AttemptsService.AdRefillAvailable;

    private static TextMeshProUGUI FindTmp(RectTransform card, string name)
    {
        Transform child = card.Find(name);
        return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
    }

    // Which attempts chip the bar would build right now (hidden / OFFLINE / meter / ∞). A
    // live bar whose mode drifts from what it built rebuilds the menu section once.
    private static int ChipMode() =>
        PremiumStore.IsPremium && AttemptsService.MetaEnabled ? 3
        : AttemptsService.OnlineBlocked ? 1
        : AttemptsService.MeterActive ? 2 : 0;

    /// <summary>Keeps the built top bar truthful between rebuilds: re-renders the player
    /// name on online-state changes, ticks the regen countdown once a second (unscaled -
    /// the menu runs at timeScale 0), and triggers one full rebuild when the chip MODE
    /// changes (offline chip appearing/vanishing needs different children, not new text).</summary>
    private sealed class TopBarLive : MonoBehaviour
    {
        public TextMeshProUGUI PlayerName;
        public TextMeshProUGUI LevelText;
        public RectTransform ExpFill;
        public TextMeshProUGUI AttemptsPrimary;
        public TextMeshProUGUI AttemptsSecondary;
        public GameObject AdRefillSlot;
        public int BuiltMode;

        private float _nextTick;
        private bool _consumed;

        private void OnEnable()
        {
            OnlineService.StateChanged += HandleChanged;
            AttemptsSync.Changed += HandleChanged;
            // A purchase/restore flips the chip to ∞ without any online event (the local
            // entitlement is the trigger) - the bar must hear about it directly.
            PremiumStore.Changed += HandleChanged;
            // XP verdicts land after the bar built (a queued finish_run retry, the boot
            // profile answering late) - keep the badge and bar truthful (XP.md).
            XpSystem.Changed += HandleChanged;
        }

        private void OnDisable()
        {
            OnlineService.StateChanged -= HandleChanged;
            AttemptsSync.Changed -= HandleChanged;
            PremiumStore.Changed -= HandleChanged;
            XpSystem.Changed -= HandleChanged;
        }

        private void HandleChanged()
        {
            if (_consumed) return;
            if (PlayerName != null) PlayerName.text = OnlineService.DisplayName;
            if (LevelText != null) LevelText.text = XpSystem.Level.ToString();
            if (ExpFill != null) ExpFill.anchorMax = new Vector2(Mathf.Clamp01(XpSystem.Fraction01), 1f);
            if (ChipMode() != BuiltMode)
            {
                // One-shot: the rebuild replaces this bar (and this component) wholesale.
                _consumed = true;
                BuildMenu();
            }
        }

        private void Update()
        {
            if (_consumed || AttemptsPrimary == null) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 1f;
            if (BuiltMode != 2) return;   // OFFLINE chip has nothing to tick

            int count = AttemptsService.Count;
            AttemptsPrimary.text = $"{count}/{AttemptsService.MaxAttempts}";
            // The ad-refill "+" tracks the same tick: gone at 5/5 or when no ad can pay out
            // (no SDK, rate-limited, one already showing), back as spending makes room.
            if (AdRefillSlot != null && AdRefillSlot.activeSelf != AdRefillPlusShouldShow())
                AdRefillSlot.SetActive(AdRefillPlusShouldShow());
            if (AttemptsSecondary == null) return;
            System.TimeSpan regen = AttemptsService.NextRegenIn;
            bool full = count >= AttemptsService.MaxAttempts;
            AttemptsSecondary.text = full
                ? "" : $"{(int)regen.TotalMinutes:00}:{regen.Seconds:00}";
            // An ad refill can fill the meter while the bar is alive: the timer line goes
            // empty, so re-centre the count the way a full-at-build card lays out (78,0) -
            // otherwise "5/5" keeps the two-line offset and floats high over a dead gap.
            AttemptsPrimary.rectTransform.anchoredPosition = new Vector2(78f, full ? 0f : 12f);
        }
    }

    // Turns a freshly-built card (root = RoundedPanel fill) into a frosted-glass panel: a blurred
    // copy of the chapter background, clipped to the card's rounded silhouette and kept aligned to
    // the screen as the card scrolls/swipes, under a dark wash for legibility. Call right after the
    // fill image and BEFORE adding content so content draws on top. No-op without a background
    // (the card keeps its plain darkened fill).
    private static void AddFrostedGlass(RectTransform card, Sprite background, float washAlpha, float blurScale = 2f,
        bool chapterBlend = false)
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
        // Top-bar chrome frosts show the CHAPTER's backdrop, so a swipe cross-fades them;
        // frosted cards inside the sliding page content just ride the page and never blend.
        if (chapterBlend) _chromeFrostBlurs.Add(blur);

        // Dark wash over the blur so text stays readable against bright backgrounds.
        Image wash = CreateImage(frame, "Wash", RuntimeSprites.RoundedPanel(), new Color(0.03f, 0.028f, 0.025f, washAlpha));
        wash.type = Image.Type.Sliced;
        Stretch(wash.rectTransform);
    }

    private static RectTransform BuildCurrencyCard(Transform parent, Sprite background, string coinGlyph, string primary, string secondary,
        bool addButton = true)
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
        AddFrostedGlass(card, background, CurrencyCardFrostWash, chapterBlend: true);
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
            // The summit flag, NOT the heart: attempts and run lives are different resources
            // and never share a glyph (AttemptSprites - Nick 2026-08-30).
            Image flag = CreateImage(card, "Flag", AttemptSprites.Flag(), Color.white);
            flag.preserveAspect = true;
            SetRect(flag.rectTransform, new Vector2(18f, 0f), new Vector2(50f, 50f), new Vector2(0f, 0.5f));
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
        // fixed left offset so the "+" keeps an even margin from the edge - independent of the
        // card's laid-out width. Only the attempts meter chip requests it (the ad-refill
        // entry, wired + visibility-managed by WireAdRefillPlus); wallet/OFFLINE/∞ chips
        // have nothing to add to. Grouped under one "AddSlot" so it toggles atomically.
        if (addButton)
        {
            RectTransform slot = CreateRect(card, "AddSlot",
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(64f, 0f));
            Image divider = CreateImage(slot, "Divider", RuntimeSprites.Square(), WithAlpha(TextPrimary, 0.28f));
            SetCenteredAt(divider.rectTransform, new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(1.5f, 38f));
            TextMeshProUGUI plus = CreateTmp(slot, "Plus", "+", 32, TextPrimary, TextAnchor.MiddleCenter,
                FontStyle.Normal, RuntimeUiKit.DefaultFont);
            SetCenteredAt(plus.rectTransform, new Vector2(1f, 0.5f), new Vector2(-26f, 0f), new Vector2(44f, 44f));
            plus.raycastTarget = false; // the slot itself is the tap target
        }
        return card;
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
