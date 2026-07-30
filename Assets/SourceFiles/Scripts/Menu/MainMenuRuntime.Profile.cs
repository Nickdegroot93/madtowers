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
//   3. ONLINE PLAY: leaderboards are live now; the rest (achievements, avatars,
//      titles & banners) stays the coming-soon promise
// No wallet, no meter, no hour pass, no lifetime stats - this page is identity and
// promises, not a dashboard. (partial of MainMenuRuntime - same class, shared statics.)
public static partial class MainMenuRuntime
{
    private static void BuildProfileScreen(Transform parent, ChapterDefinition chapter)
    {
        GameObject scroll = RuntimeUiKit.CreateScrollColumn(parent, Vector2.zero, out Transform content);
        RectTransform scrollRect = (RectTransform)scroll.transform;
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = new Vector2(60f, 236f);
        scrollRect.offsetMax = new Vector2(-60f, -344f);
        scroll.GetComponent<Image>().color = Color.clear; // the chapter backdrop IS the background

        Color accent = chapter != null ? ChapterLight(chapter) : GoldBase;

        BuildProfileIdentityCard(content, accent);
        BuildProfileUnlimitedCard(content);
        BuildProfileOnlineCard(content, accent);
    }

    // 1. Who you are - the live server identity: big avatar + name row, an honest status
    // line, then REAL buttons (change name / sign in), not a whole-card mystery tap.
    private static void BuildProfileIdentityCard(Transform content, Color accent)
    {
        bool guest = !OnlineService.IsLinked;
        RectTransform card = CreateProfileCard(content, 336f);

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
            guest ? WithAlpha(TextMuted, 0.9f) : WithAlpha(GoldBase, 0.9f),
            TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(188f, -104f), new Vector2(540f, 26f), new Vector2(0f, 1f));
        TextMeshProUGUI detail = CreateTmp(card, "Detail",
            guest ? "UNINSTALLING LOSES YOUR PROGRESS" : "YOUR PROGRESS IS SAFE ON EVERY DEVICE", 16,
            WithAlpha(TextMuted, 0.65f), TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(188f, -136f), new Vector2(540f, 24f), new Vector2(0f, 1f));

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
            status.color = g ? WithAlpha(TextMuted, 0.9f) : WithAlpha(GoldBase, 0.9f);
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
                RuntimeUiKit.AddOutline(bg.transform, GoldOutline(0.35f));
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

    // 2. The one thing worth advertising, built like a real store package (IAP offer
    // anatomy: hero art → title → benefit bullets → one big CTA - GameRefinery/IAP
    // merchandising playbook). Hero art is a sprite-composed placeholder until real
    // package art is made.
    private static void BuildProfileUnlimitedCard(Transform content)
    {
        const float heroH = 170f;
        RectTransform card = CreateProfileCard(content, 560f); // 4 benefit lines since offline play joined the pitch
        // The pitch card carries the gold edge - the single accent on this page.
        RuntimeUiKit.AddOutline(card, WithAlpha(GoldBase, 0.55f));

        // Hero band: a warm glow field with the goods spilling out of it - the big coin
        // flanked by full hearts. PLACEHOLDER composition; swap for painted key art later.
        Image hero = CreateImage(card, "Hero", MenuSprites.VerticalFade(
            new Color(0.24f, 0.16f, 0.05f, 1f), new Color(0.07f, 0.06f, 0.05f, 1f)), Color.white);
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
        Sprite heart = HeartSprites.Full();
        if (heart != null)
        {
            for (int i = 0; i < 2; i++)
            {
                Image side = CreateImage(hero.transform, $"Heart{i}", heart, Color.white);
                side.preserveAspect = true;
                float x = i == 0 ? -110f : 110f;
                SetCenteredAt(side.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, -6f), new Vector2(64f, 64f));
                side.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 12f : -12f);
            }
        }

        TextMeshProUGUI title = CreateTmp(card, "Title", "MADTOWERS UNLIMITED", 34, GoldBase,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -heroH - 26f), new Vector2(720f, 42f), new Vector2(0.5f, 1f));
        title.characterSpacing = 2f;
        CreateTmp(card, "Pitch", "THE FULL GAME, FOREVER", 17, TextPrimary,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -heroH - 74f), new Vector2(720f, 24f), new Vector2(0.5f, 1f));

        // Benefits, one per line with the shared checkmark - benefits, not features.
        string[] benefits = { "NO ADS, EVER", "UNLIMITED LIVES - NEVER WAIT TO PLAY",
            "PLAY OFFLINE - EVEN ON A PLANE", "ONE PURCHASE, YOURS FOREVER" };
        for (int i = 0; i < benefits.Length; i++)
        {
            float y = -heroH - 116f - i * 36f;
            Image check = CreateImage(card, $"Check{i}", MenuSprites.CheckMark(GoldBase), Color.white);
            check.preserveAspect = true;
            SetRect(check.rectTransform, new Vector2(150f, y - 2f), new Vector2(24f, 24f), new Vector2(0f, 1f));
            CreateTmp(card, $"Benefit{i}", benefits[i], 16, WithAlpha(TextPrimary, 0.92f),
                TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(188f, y), new Vector2(540f, 24f), new Vector2(0f, 1f));
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
            // Owned: a quiet gold-edged banner in the button's slot - state, not a button.
            Image owned = CreateImage(slot, "Owned", RuntimeSprites.RoundedPanel(),
                new Color(0.13f, 0.11f, 0.06f, 1f));
            owned.type = Image.Type.Sliced;
            Stretch(owned.rectTransform);
            RuntimeUiKit.AddOutline(owned.rectTransform, WithAlpha(GoldBase, 0.75f));
            Image check = CreateImage(owned.transform, "Check", MenuSprites.CheckMark(GoldBase), Color.white);
            check.preserveAspect = true;
            SetCenteredAt(check.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-170f, 0f), new Vector2(34f, 34f));
            CreateTmp(owned.transform, "Label", "UNLIMITED - ACTIVE", 26, GoldBase,
                TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(24f, 0f), new Vector2(440f, 34f), new Vector2(0.5f, 0.5f));
            return;
        }

        bool purchasable = PremiumStore.Available;
        Image cta = CreateImage(slot, "Cta", MenuSprites.RoundedGradient(
            new Color(1f, 0.86f, 0.45f, 1f), new Color(0.82f, 0.58f, 0.18f, 1f)), Color.white);
        cta.type = Image.Type.Sliced;
        Stretch(cta.rectTransform);
        cta.raycastTarget = purchasable;

        if (!purchasable)
        {
            cta.color = new Color(0.75f, 0.75f, 0.75f, 1f); // dimmed: no store yet
            CreateTmp(cta.transform, "Label", $"GET UNLIMITED - {PremiumStore.PriceText}", 26,
                new Color(0.16f, 0.11f, 0.04f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(0f, 10f), new Vector2(600f, 34f), new Vector2(0.5f, 0.5f));
            CreateTmp(cta.transform, "Soon", "COMING SOON", 13,
                new Color(0.24f, 0.17f, 0.07f, 0.9f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(0f, -22f), new Vector2(600f, 18f), new Vector2(0.5f, 0.5f));
            return;
        }

        TextMeshProUGUI label = CreateTmp(cta.transform, "Label",
            $"GET UNLIMITED - {PremiumStore.PriceText}", 26,
            new Color(0.16f, 0.11f, 0.04f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
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

    // 3. The locked door players should SEE: online play is on the way.
    private static void BuildProfileOnlineCard(Transform content, Color accent)
    {
        RectTransform card = CreateProfileCard(content, 330f);

        Image lockIcon = CreateImage(card, "Lock", MenuSprites.Lock(WithAlpha(TextMuted, 0.9f)), Color.white);
        lockIcon.preserveAspect = true;
        SetRect(lockIcon.rectTransform, new Vector2(0f, -40f), new Vector2(56f, 56f), new Vector2(0.5f, 1f));
        lockIcon.rectTransform.anchoredPosition = new Vector2(0f, -40f);
        lockIcon.rectTransform.pivot = new Vector2(0.5f, 1f);

        TextMeshProUGUI title = CreateTmp(card, "Title", "ONLINE PLAY", 40, TextPrimary,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -112f), new Vector2(720f, 50f), new Vector2(0.5f, 1f));
        title.characterSpacing = 4f;

        TextMeshProUGUI soon = CreateTmp(card, "Soon", "COMING SOON", 18,
            Color.Lerp(accent, TextPrimary, 0.35f), TextAnchor.UpperCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(0f, -166f), new Vector2(720f, 26f), new Vector2(0.5f, 1f));
        soon.characterSpacing = 8f;

        // Leaderboards shipped with the server phase - say so; the rest stays promised.
        CreateTmp(card, "Live", "LEADERBOARDS - LIVE NOW", 15,
            WithAlpha(GoldBase, 0.9f), TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -214f), new Vector2(720f, 22f), new Vector2(0.5f, 1f));
        CreateTmp(card, "Features",
            "ACHIEVEMENTS   -   PROFILES & AVATARS   -   TITLES & BANNERS", 15,
            WithAlpha(TextMuted, 0.8f), TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -244f), new Vector2(720f, 26f), new Vector2(0.5f, 1f));

        // Nick's direction (2026-07-20): online won't be a side feature - accounts become
        // the standard, with lives and leaderboards living on the server.
        CreateTmp(card, "Account", "ONE ACCOUNT, EVERY DEVICE - LIVES & SCORES CHECKED ONLINE", 13,
            WithAlpha(TextMuted, 0.55f), TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -286f), new Vector2(720f, 20f), new Vector2(0.5f, 1f));
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
        RuntimeUiKit.AddOutline(card, GoldOutline(0.18f));
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
