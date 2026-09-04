using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// The Profile tab, v2 (Nick's 2026-07-20 pass killed the v1 stats/attempts clutter):
// three cards only -
//   1. identity: avatar + the live server name (auto "Builder-XXXX" until claimed,
//      BACKEND.md §3.2) with real buttons - CHANGE NAME opens the claim modal, and a
//      guest account gets a gold SIGN IN CTA that opens the shared sign-in sheet (§3.3)
//   2. MADTOWERS UNLIMITED: the one real pitch - the full game, forever (no ads,
//      unlimited attempts), one purchase
//   3. ONLINE PLAY: the big coming-soon promise, nothing else (Nick 2026-08-11 cut
//      the feature list - the leaderboard entry lives on the play screen already)
// No wallet, no meter, no hour pass, no lifetime stats - this page is identity and
// promises, not a dashboard. The one addition since (2026-09-04): a PlayStation-style
// TROPHY ROW inside the identity card - cleared levels + bronze/silver/gold counts - because
// medals are identity ("you are your trophies"), not stats. Found-counts only, never "of N"
// (the ambiguity rule). (partial of MainMenuRuntime - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private static void BuildProfileScreen(Transform parent, ChapterDefinition chapter)
    {
        GameObject scroll = RuntimeUiKit.CreateScrollColumn(parent, Vector2.zero, out Transform content);
        RectTransform scrollRect = (RectTransform)scroll.transform;
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = new Vector2(60f, 236f);
        // -240, not the sibling tabs' ~300: the identity card should sit close under the top
        // status bar (Nick 2026-08-11: the old 344 read as a dead band above the card).
        scrollRect.offsetMax = new Vector2(-60f, -240f);
        scroll.GetComponent<Image>().color = Color.clear; // the chapter backdrop IS the background

        Color accent = chapter != null ? ChapterLight(chapter) : MenuAccent;

        BuildProfileIdentityCard(content, accent);
        BuildProfileUnlimitedCard(content);
        BuildProfileOnlineCard(content, accent);
    }

    // 1. Who you are - the live server identity: big avatar + name row, an honest status
    // line, then REAL buttons (change name / sign in), not a whole-card mystery tap.
    private static void BuildProfileIdentityCard(Transform content, Color accent)
    {
        bool guest = !OnlineService.IsLinked;
        RectTransform card = CreateProfileCard(content, 336f + TrophyRowExtraHeight);

        // Avatar: a circular slot with the person glyph - still a placeholder; real avatars
        // arrive with the platform-services layer (BACKEND.md §3.6).
        Image ring = CreateImage(card, "AvatarRing", MenuSprites.CircleBadge(
            new Color(0.12f, 0.11f, 0.09f, 1f), WithAlpha(accent, 0.55f)), Color.white);
        SetRect(ring.rectTransform, new Vector2(36f, -32f), new Vector2(124f, 124f), new Vector2(0f, 1f));
        ring.rectTransform.pivot = new Vector2(0f, 1f);
        Image person = CreateImage(ring.transform, "Person", MenuSprites.Person(WithAlpha(accent, 0.85f)), Color.white);
        person.preserveAspect = true;
        SetCenteredAt(person.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(62f, 62f));

        TextMeshProUGUI name = CreateTmp(card, "Name", OnlineService.DisplayName, 42, TextPrimary,
            TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(188f, -44f), new Vector2(540f, 50f), new Vector2(0f, 1f));
        TextMeshProUGUI status = CreateTmp(card, "Status",
            guest ? "GUEST ACCOUNT" : "SIGNED IN", 19,
            guest ? WithAlpha(TextMuted, 0.9f) : WithAlpha(MenuAccent, 0.9f),
            TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(188f, -104f), new Vector2(540f, 26f), new Vector2(0f, 1f));
        TextMeshProUGUI detail = CreateTmp(card, "Detail",
            guest ? "UNINSTALLING LOSES YOUR PROGRESS" : "YOUR PROGRESS IS SAFE ON EVERY DEVICE", 20,
            WithAlpha(TextMuted, 0.65f), TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(188f, -136f), new Vector2(540f, 24f), new Vector2(0f, 1f));

        BuildTrophyRow(card, accent);

        // The profile name follows auth state (connect -> Builder-XXXX arrives; claim -> new
        // name). Menu rebuilds destroy this card constantly and state changes are rare after
        // boot, so unhook eagerly on destroy - a lazy handler-side check would leave one dead
        // closure per rebuild parked on the static event until the next change.
        TextMeshProUGUI nameButtonLabel = null; // assigned when the button row is built below
        void RefreshIdentity()
        {
            if (name == null)
            {
                OnlineService.StateChanged -= RefreshIdentity;
                return;
            }
            bool g = !OnlineService.IsLinked;
            name.text = OnlineService.DisplayName;
            status.text = g ? "GUEST ACCOUNT" : "SIGNED IN";
            status.color = g ? WithAlpha(TextMuted, 0.9f) : WithAlpha(MenuAccent, 0.9f);
            detail.text = g ? "UNINSTALLING LOSES YOUR PROGRESS" : "YOUR PROGRESS IS SAFE ON EVERY DEVICE";
            if (nameButtonLabel != null)
                nameButtonLabel.text = HasClaimedName ? "CHANGE NAME" : "CLAIM YOUR NAME";
        }
        OnlineService.StateChanged += RefreshIdentity;
        card.gameObject.AddComponent<UnhookOnDestroy>().Unhook =
            () => OnlineService.StateChanged -= RefreshIdentity;

        // Button row: CHANGE NAME (dark) + SIGN IN (gold CTA) for guests; a signed-in
        // account keeps just the full-width name button.
        const float btnH = 84f;
        const float btnY = 30f;
        const float sidePad = 36f;

        RectTransform BuildIdentityButton(string goName, string label, bool gold, float anchorMinX, float anchorMaxX, Action onClick)
        {
            Image bg;
            if (gold)
            {
                bg = CreateImage(card, goName, MenuSprites.RoundedGradient(
                    new Color(1f, 0.86f, 0.45f, 1f), new Color(0.82f, 0.58f, 0.18f, 1f)), Color.white);
            }
            else
            {
                bg = CreateImage(card, goName, RuntimeSprites.RoundedPanel(), new Color(0.13f, 0.12f, 0.10f, 1f));
                RuntimeUiKit.AddOutline(bg.transform, AccentOutline(0.35f));
            }
            bg.type = Image.Type.Sliced;
            RectTransform rt = bg.rectTransform;
            rt.anchorMin = new Vector2(anchorMinX, 0f);
            rt.anchorMax = new Vector2(anchorMaxX, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(anchorMinX <= 0f ? sidePad : 10f, btnY);
            rt.offsetMax = new Vector2(anchorMaxX >= 1f ? -sidePad : -10f, btnY + btnH);
            bg.raycastTarget = true;
            CreateTmp(bg.transform, "Label", label, 23,
                gold ? new Color(0.16f, 0.11f, 0.04f, 1f) : TextPrimary,
                TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
            Button button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(() => { SfxPlayer.Play("ui-button-click"); onClick?.Invoke(); });
            return rt;
        }

        string nameLabel = HasClaimedName ? "CHANGE NAME" : "CLAIM YOUR NAME";
        RectTransform nameButton;
        if (guest)
        {
            nameButton = BuildIdentityButton("ChangeName", nameLabel, false, 0f, 0.5f,
                () => OpenClaimNameModal(RefreshIdentity));
            BuildIdentityButton("SignIn", "SIGN IN", true, 0.5f, 1f, OpenSignInSheet);
        }
        else
        {
            nameButton = BuildIdentityButton("ChangeName", nameLabel, false, 0f, 1f,
                () => OpenClaimNameModal(RefreshIdentity));
        }
        nameButtonLabel = nameButton.GetComponentInChildren<TextMeshProUGUI>();
    }

    // ---- trophy row -------------------------------------------------------------------------
    // Under the identity text, above the buttons: a hairline, then four cells - CLEARED, then
    // one per medal tier with Nick's rendered cube and the count of levels whose HIGHEST tier
    // is that one (buckets sum to cleared - "which levels still owe me a rung" reads at a
    // glance, which cumulative PSN counting would hide). Zero counts ghost the cube. Derived at
    // read time from the campaign (MEDALS.md: tiers are never persisted); the menu rebuilds on
    // every return from a run, so no live hook is needed.
    private const float TrophyRowExtraHeight = 72f;
    private const float TrophyRowTop = 184f;       // from the card top: below the detail line

    private static void BuildTrophyRow(RectTransform card, Color accent)
    {
        const float sidePad = 36f;
        const float rowH = 62f;

        Image rule = CreateImage(card, "TrophyRule", RuntimeSprites.Square(), WithAlpha(accent, 0.28f));
        RectTransform ruleRect = rule.rectTransform;
        ruleRect.anchorMin = new Vector2(0f, 1f);
        ruleRect.anchorMax = new Vector2(1f, 1f);
        ruleRect.pivot = new Vector2(0.5f, 1f);
        ruleRect.offsetMin = new Vector2(sidePad, -TrophyRowTop - 2f);
        ruleRect.offsetMax = new Vector2(-sidePad, -TrophyRowTop);
        rule.raycastTarget = false;

        RectTransform row = CreateRect(card, "TrophyRow", new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        row.offsetMin = new Vector2(sidePad, -TrophyRowTop - 14f - rowH);
        row.offsetMax = new Vector2(-sidePad, -TrophyRowTop - 14f);

        CountMedals(out int cleared, out int[] byTier);
        int cells = 1 + LevelTiers.TierCount;
        BuildTrophyCell(row, 0, cells, "CLEARED", cleared, MenuSprites.CheckMark(
            cleared > 0 ? new Color(0.56f, 0.74f, 0.5f, 1f) : MedalStyle.Unearned), cleared > 0, true);
        for (int t = 0; t < LevelTiers.TierCount; t++)
        {
            var tier = (MedalTier)t;
            int n = byTier[t];
            BuildTrophyCell(row, 1 + t, cells, MedalStyle.DisplayName(tier).ToUpperInvariant(), n,
                MedalStyle.Sprite(tier, earned: n > 0), n > 0, false);
        }
    }

    private static void BuildTrophyCell(RectTransform row, int index, int cells, string label, int count,
        Sprite icon, bool lit, bool glyphIsCheck)
    {
        RectTransform cell = CreateRect(row, "Trophy" + label, new Vector2((float)index / cells, 0f),
            new Vector2((float)(index + 1) / cells, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // Icon + number as one centred pair; the caps label sits under the pair.
        const float iconSize = 40f;
        const float gap = 8f;
        const float numberW = 64f;
        float pairW = iconSize + gap + numberW;
        float pairLeft = -pairW * 0.5f;

        Image mark = CreateImage(cell, "Icon", icon, glyphIsCheck ? Color.white : MedalStyle.IconTint(lit));
        mark.preserveAspect = true;
        mark.raycastTarget = false;
        SetCenteredAt(mark.rectTransform, new Vector2(0.5f, 1f),
            new Vector2(pairLeft + iconSize * 0.5f, -2f - iconSize * 0.5f),
            new Vector2(glyphIsCheck ? iconSize * 0.8f : iconSize, glyphIsCheck ? iconSize * 0.8f : iconSize));

        TextMeshProUGUI number = CreateTmp(cell, "Count", count.ToString(), 32,
            lit ? TextPrimary : WithAlpha(TextMuted, 0.55f),
            TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(pairLeft + iconSize + gap, -2f - iconSize * 0.5f), new Vector2(numberW, iconSize),
            new Vector2(0.5f, 1f));
        number.rectTransform.pivot = new Vector2(0f, 0.5f);
        number.font = RuntimeUiKit.TmpDisplayFont;

        TextMeshProUGUI caption = CreateTmp(cell, "Label", label, 18,
            WithAlpha(TextMuted, lit ? 0.8f : 0.5f), TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -2f - iconSize - 4f), new Vector2(160f, 22f), new Vector2(0.5f, 1f));
        caption.characterSpacing = 4f;
    }

    /// <summary>Levels cleared across the whole campaign, and how many have each tier as their
    /// HIGHEST. Endless levels (no ladder) count as cleared only. Never persisted - derived.</summary>
    private static void CountMedals(out int cleared, out int[] byTier)
    {
        cleared = 0;
        byTier = new int[LevelTiers.TierCount];
        ChapterDefinition[] chapters = _chapters != null && _chapters.Length > 0
            ? _chapters
            : Campaign.LoadChaptersInOrder();
        for (int c = 0; c < chapters.Length; c++)
        {
            ChapterDefinition chapter = chapters[c];
            if (chapter == null) continue;
            for (int i = 0; i < chapter.Levels.Count; i++)
            {
                LevelDefinition level = chapter.Levels[i];
                if (level == null || !ProgressStore.IsLevelCompleted(level)) continue;
                cleared++;
                MedalTier? highest = LevelTiers.HighestEarned(level);
                if (highest.HasValue) byTier[(int)highest.Value]++;
            }
        }
    }

    // 2. The one thing worth advertising, built like a real store package (IAP offer
    // anatomy: hero art → title → benefit bullets → one big CTA - GameRefinery/IAP
    // merchandising playbook). Hero art is a sprite-composed placeholder until real
    // package art is made.
    private static void BuildProfileUnlimitedCard(Transform content)
    {
        const float heroH = 170f;
        RectTransform card = CreateProfileCard(content, 580f); // 4 benefit lines at the bigger 20pt size
        // The pitch card carries the accent edge - the single accent on this page.
        RuntimeUiKit.AddOutline(card, WithAlpha(MenuAccent, 0.55f));

        // Hero band: a quiet accent-tinted field with the goods spilling out of it - the big
        // coin flanked by full hearts (the warm-brown glow field was retired 2026-08-30 with
        // the rest of the gold chrome). PLACEHOLDER composition; swap for painted key art later.
        Image hero = CreateImage(card, "Hero", MenuSprites.VerticalFade(
            Color.Lerp(new Color(0.10f, 0.10f, 0.12f, 1f), MenuAccent, 0.14f),
            new Color(0.055f, 0.055f, 0.065f, 1f)), Color.white);
        SetRect(hero.rectTransform, new Vector2(0f, 0f), new Vector2(0f, heroH), new Vector2(0.5f, 1f));
        hero.rectTransform.anchorMin = new Vector2(0f, 1f);
        hero.rectTransform.anchorMax = new Vector2(1f, 1f);
        hero.rectTransform.offsetMin = new Vector2(8f, -heroH - 8f);
        hero.rectTransform.offsetMax = new Vector2(-8f, -8f);

        Sprite coin = MenuIcon("coin");
        if (coin != null)
        {
            Image bigCoin = CreateImage(hero.transform, "Coin", coin, Color.white);
            bigCoin.preserveAspect = true;
            SetCenteredAt(bigCoin.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(110f, 110f));
        }
        // Flags, not hearts: the premium pitch is unlimited ATTEMPTS (AttemptSprites).
        Sprite flag = AttemptSprites.Flag();
        if (flag != null)
        {
            for (int i = 0; i < 2; i++)
            {
                Image side = CreateImage(hero.transform, $"Flag{i}", flag, Color.white);
                side.preserveAspect = true;
                float x = i == 0 ? -110f : 110f;
                SetCenteredAt(side.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, -6f), new Vector2(64f, 64f));
                side.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 12f : -12f);
            }
        }

        TextMeshProUGUI title = CreateTmp(card, "Title", "HAZARD HEIGHTS UNLIMITED", 34, MenuAccent,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -heroH - 26f), new Vector2(720f, 42f), new Vector2(0.5f, 1f));
        title.characterSpacing = 2f;
        CreateTmp(card, "Pitch", "THE FULL GAME, FOREVER", 18, TextPrimary,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -heroH - 74f), new Vector2(720f, 24f), new Vector2(0.5f, 1f));

        // Benefits, one per line with the shared checkmark - benefits, not features. Short
        // punches only, at a size that reads (Nick 2026-08-11: 16pt was too small, and the
        // "NEVER WAIT TO PLAY" / "EVEN ON A PLANE" tails were cut - the lead words carry it).
        // The 4th line is the shared one-time-purchase microcopy (DevSupportLine - one
        // constant, every surface; only ONE "one purchase" line ever renders here, two
        // stacked 10px apart read as a collision - review 2026-08-22).
        string[] benefits = { "NO ADS, EVER", "UNLIMITED ATTEMPTS",
            "PLAY OFFLINE", DevSupportLine };
        for (int i = 0; i < benefits.Length; i++)
        {
            float y = -heroH - 116f - i * 42f;
            Image check = CreateImage(card, $"Check{i}", MenuSprites.CheckMark(MenuAccent), Color.white);
            check.preserveAspect = true;
            SetRect(check.rectTransform, new Vector2(150f, y - 4f), new Vector2(28f, 28f), new Vector2(0f, 1f));
            CreateTmp(card, $"Benefit{i}", benefits[i], 20, WithAlpha(TextPrimary, 0.92f),
                TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(188f, y), new Vector2(540f, 30f), new Vector2(0f, 1f));
        }

        // The CTA slot: full-width, 92px, rebuilt in place as ownership/state changes -
        // owned banner, live BUY button (through PremiumStore), or the dimmed COMING SOON
        // (no store provider - all device builds until Unity IAP ships, GOLIVE.md §3).
        RectTransform ctaSlot = CreateRect(card, "CtaSlot",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 26f), new Vector2(-56f, 92f));
        void RenderCta()
        {
            if (ctaSlot == null) return;
            foreach (Transform child in ctaSlot) UnityEngine.Object.Destroy(child.gameObject);
            BuildUnlimitedCta(ctaSlot, RenderCta);
        }
        RenderCta();

        // Ownership can flip from elsewhere (Settings restore, server sync-down on sign-in) -
        // same live-refresh + eager-unhook pattern as the identity card.
        void OnPremiumChanged()
        {
            if (ctaSlot == null) { PremiumStore.Changed -= OnPremiumChanged; return; }
            RenderCta();
        }
        PremiumStore.Changed += OnPremiumChanged;
        card.gameObject.AddComponent<UnhookOnDestroy>().Unhook =
            () => PremiumStore.Changed -= OnPremiumChanged;
    }

    /// <summary>One CTA render into the (cleared) slot. <paramref name="rerender"/> rebuilds
    /// the slot - used to restore the button after a cancelled/failed store exchange.</summary>
    private static void BuildUnlimitedCta(RectTransform slot, Action rerender)
    {
        if (PremiumStore.IsPremium)
        {
            // Owned: a quiet accent-edged banner in the button's slot - state, not a button.
            Image owned = CreateImage(slot, "Owned", RuntimeSprites.RoundedPanel(),
                new Color(0.12f, 0.12f, 0.14f, 1f));
            owned.type = Image.Type.Sliced;
            Stretch(owned.rectTransform);
            RuntimeUiKit.AddOutline(owned.rectTransform, WithAlpha(MenuAccent, 0.75f));
            Image check = CreateImage(owned.transform, "Check", MenuSprites.CheckMark(MenuAccent), Color.white);
            check.preserveAspect = true;
            SetCenteredAt(check.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-170f, 0f), new Vector2(34f, 34f));
            CreateTmp(owned.transform, "Label", "UNLIMITED - ACTIVE", 26, MenuAccent,
                TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(24f, 0f), new Vector2(440f, 34f), new Vector2(0.5f, 0.5f));
            return;
        }

        bool purchasable = PremiumStore.Available;
        // Chapter-accent gradient, not gold (Nick 2026-08-30: the gold CTA clashed with
        // every non-desert chapter; accent everywhere, gold = currency art only). Same
        // build as the notify sheet's YES - the menu's one primary-button treatment.
        Color ctaDarkText = new Color(0.07f, 0.06f, 0.09f, 1f);
        Image cta = CreateImage(slot, "Cta", MenuSprites.RoundedGradient(
            Color.Lerp(MenuAccent, Color.white, 0.15f), Color.Lerp(MenuAccent, Color.black, 0.22f)), Color.white);
        cta.type = Image.Type.Sliced;
        Stretch(cta.rectTransform);
        cta.raycastTarget = purchasable;

        if (!purchasable)
        {
            cta.color = new Color(0.75f, 0.75f, 0.75f, 1f); // dimmed: no store yet
            CreateTmp(cta.transform, "Label", $"GET UNLIMITED - {PremiumStore.PriceText}", 26,
                ctaDarkText, TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(0f, 10f), new Vector2(600f, 34f), new Vector2(0.5f, 0.5f));
            CreateTmp(cta.transform, "Soon", "COMING SOON", 18,
                WithAlpha(ctaDarkText, 0.75f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(0f, -22f), new Vector2(600f, 18f), new Vector2(0.5f, 0.5f));
            return;
        }

        TextMeshProUGUI label = CreateTmp(cta.transform, "Label",
            $"GET UNLIMITED - {PremiumStore.PriceText}", 26,
            ctaDarkText, TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button buy = cta.gameObject.AddComponent<Button>();
        buy.targetGraphic = cta;
        buy.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            buy.interactable = false;
            label.text = "CONTACTING STORE...";
            PremiumStore.Purchase(result =>
            {
                // Purchased/Restored re-render through PremiumStore.Changed; only the
                // no-sale outcomes need the button back. A destroyed slot skips quietly.
                if (label == null) return;
                if (result == PremiumStoreResult.Cancelled) rerender();
                else if (result == PremiumStoreResult.Failed)
                {
                    label.text = "STORE UNAVAILABLE";
                    buy.interactable = true;
                }
            });
        });
    }

    // 3. The locked door players should SEE: online play is on the way. Just the promise,
    // BIG - no feature laundry list (Nick 2026-08-11: the leaderboards/achievements/avatars/
    // one-account lines all cut; "online play coming soon and that's it").
    private static void BuildProfileOnlineCard(Transform content, Color accent)
    {
        RectTransform card = CreateProfileCard(content, 260f);

        Image lockIcon = CreateImage(card, "Lock", MenuSprites.Lock(WithAlpha(TextMuted, 0.9f)), Color.white);
        lockIcon.preserveAspect = true;
        SetRect(lockIcon.rectTransform, new Vector2(0f, -36f), new Vector2(56f, 56f), new Vector2(0.5f, 1f));
        lockIcon.rectTransform.anchoredPosition = new Vector2(0f, -36f);
        lockIcon.rectTransform.pivot = new Vector2(0.5f, 1f);

        TextMeshProUGUI title = CreateTmp(card, "Title", "ONLINE PLAY", 48, TextPrimary,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -104f), new Vector2(720f, 58f), new Vector2(0.5f, 1f));
        title.characterSpacing = 4f;

        TextMeshProUGUI soon = CreateTmp(card, "Soon", "COMING SOON", 24,
            Color.Lerp(accent, TextPrimary, 0.35f), TextAnchor.UpperCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(0f, -170f), new Vector2(720f, 32f), new Vector2(0.5f, 1f));
        soon.characterSpacing = 8f;
    }

    // ---- small shared pieces --------------------------------------------------------------

    private static RectTransform CreateProfileCard(Transform content, float height)
    {
        RectTransform card = CreateRect(content, "Card",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, height));
        Image fill = card.gameObject.AddComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = CardDark;
        RuntimeUiKit.AddOutline(card, AccentOutline(0.18f));
        LayoutElement layout = card.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        return card;
    }

    /// <summary>Runs a static-event unsubscribe when its GameObject dies - for UI that
    /// subscribes to long-lived events but is destroyed on every menu rebuild.</summary>
    private sealed class UnhookOnDestroy : MonoBehaviour
    {
        public Action Unhook;
        private void OnDestroy() => Unhook?.Invoke();
    }
}
