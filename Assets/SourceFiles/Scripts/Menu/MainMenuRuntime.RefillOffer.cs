using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

/// <summary>
/// The refill offer: what opens when the player taps the lives chip (anywhere on it,
/// not just the "+"). Two options, deliberately ordered as a pitch (Nick 2026-08-09):
/// Hazard Heights Unlimited first - the same offer anatomy as the Profile page's card,
/// sharing its CTA builder so store states can never drift - then "watch an ad, +2
/// lives" with the day's remaining watches. Before this, tapping "+" started an ad
/// with no warning at all, which read as broken (or worse, hostile).
///
/// Never opens for premium players: their chip shows the infinity glyph and there is
/// nothing here to sell them.
/// </summary>
public static partial class MainMenuRuntime
{
    private const float RefillPanelW = 700f;
    private const float RefillPanelH = 700f;

    // Contextual permission ask (approved 2026-08-11): the one moment a notification is
    // a favor is standing at an empty meter, so the "want a ping when lives are full?"
    // row lives HERE, not in a boot dialog. Extra panel height when the row shows: the
    // row itself plus the same 26px breathing gap the ad row keeps to the panel edge.
    private const float NotifyAskRowH = 70f;
    private const float NotifyAskExtra = NotifyAskRowH + 26f;

    private static GameObject _refillOverlay;

    /// <summary>Open the offer (tap-outside and X both dismiss). Safe to call from any
    /// chip tap: no-ops when it is already up or when the player owns Unlimited.</summary>
    private static void OpenRefillOffer()
    {
        if (_refillOverlay != null) return;
        if (PremiumStore.IsPremium) return;

        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Refill Offer", 5900);
        _refillOverlay = overlay;

        // Backdrop: dims the menu and is itself the dismiss surface.
        Image backdrop = RuntimeUiKit.CreateImage(overlay.transform, "Backdrop", null,
            new Color(0.02f, 0.02f, 0.04f, 0f));   // pop-in animates to 0.82
        RuntimeUiKit.Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        Button dismiss = backdrop.gameObject.AddComponent<Button>();
        dismiss.targetGraphic = backdrop;
        dismiss.transition = Selectable.Transition.None;
        dismiss.onClick.AddListener(CloseRefillOffer);

        // Panel: near-black body, the accent lives in the edges (taste contract).
        Image panel = RuntimeUiKit.CreateImage(overlay.transform, "Panel",
            RuntimeSprites.RoundedPanel(), GameMenuStyle.PanelColor);
        panel.type = Image.Type.Sliced;
        // Standing at an EMPTY meter with the OS never yet asked: grow the panel and
        // append the notification ask. Any other state keeps the tuned 700 layout.
        bool showNotifyAsk = NotificationScheduler.CanOfferContextualAsk
            && AttemptsService.MeterActive && AttemptsService.Count <= 0;
        RuntimeUiKit.SetRect(panel.rectTransform, new Vector2(0f, 0f),
            new Vector2(RefillPanelW, RefillPanelH + (showNotifyAsk ? NotifyAskExtra : 0f)),
            new Vector2(0.5f, 0.5f));
        panel.raycastTarget = true;   // swallow taps so only the backdrop dismisses

        BuildRefillContent(panel.rectTransform);
        if (showNotifyAsk) BuildNotifyAskRow(panel.rectTransform);

        // Pop-in on unscaled time (the menu runs at timeScale = 0): scale + backdrop
        // fade. Small and quick - liveliness, not a celebration.
        RefillOfferPop pop = overlay.AddComponent<RefillOfferPop>();
        pop.Panel = panel.rectTransform;
        pop.Backdrop = backdrop;
    }

    private static void CloseRefillOffer()
    {
        if (_refillOverlay == null) return;
        SfxPlayer.Play("ui-button-click", 0.7f);
        UnityEngine.Object.Destroy(_refillOverlay);
        _refillOverlay = null;
    }

    private static void BuildRefillContent(RectTransform panel)
    {
        TextMeshProUGUI title = CreateTmp(panel, "Title", "MORE ATTEMPTS", 34, TextPrimary,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -26f), new Vector2(RefillPanelW - 120f, 42f), new Vector2(0.5f, 1f));
        title.characterSpacing = 4f;

        // X: functional escape hatch, 64px target (tap-outside also works).
        Button close = CreateGhostButton(panel, "Close", new Vector2(-10f, -10f), new Vector2(64f, 64f));
        CreateTmp(close.transform, "X", "×", 40, WithAlpha(TextMuted, 0.9f),
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        close.onClick.AddListener(CloseRefillOffer);

        // ---- Option 1: the pitch. Gold edge = the single accent, same as Profile. ----
        Image offer = RuntimeUiKit.CreateImage(panel, "Unlimited",
            RuntimeSprites.RoundedPanel(), new Color(0.09f, 0.075f, 0.05f, 1f));
        offer.type = Image.Type.Sliced;
        RectTransform offerRect = offer.rectTransform;
        offerRect.anchorMin = new Vector2(0f, 1f);
        offerRect.anchorMax = new Vector2(1f, 1f);
        offerRect.pivot = new Vector2(0.5f, 1f);
        offerRect.offsetMin = new Vector2(24f, -84f - 356f);
        offerRect.offsetMax = new Vector2(-24f, -84f);
        RuntimeUiKit.AddOutline(offerRect, WithAlpha(MenuAccent, 0.55f));

        // Hero band: warm glow with full hearts spilling toward an infinity - the goods,
        // not an ornament. Mirrors the Profile hero's composition language.
        Image glow = CreateImage(offer.transform, "Glow", MenuSprites.VerticalFade(
            new Color(0.24f, 0.16f, 0.05f, 1f), new Color(0.09f, 0.075f, 0.05f, 0f)), Color.white);
        RectTransform glowRect = glow.rectTransform;
        glowRect.anchorMin = new Vector2(0f, 1f);
        glowRect.anchorMax = new Vector2(1f, 1f);
        glowRect.pivot = new Vector2(0.5f, 1f);
        glowRect.offsetMin = new Vector2(6f, -96f);
        glowRect.offsetMax = new Vector2(-6f, -6f);
        glow.raycastTarget = false;

        TextMeshProUGUI infinity = CreateTmp(glow.transform, "Infinity", "∞", 64, MenuAccent,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, 2f), new Vector2(120f, 90f), new Vector2(0.5f, 0.5f));
        infinity.enableAutoSizing = false;
        // Flags, not hearts: the ∞ being pitched is unlimited ATTEMPTS (AttemptSprites).
        Sprite flag = AttemptSprites.Flag();
        if (flag != null)
        {
            for (int i = 0; i < 2; i++)
            {
                Image side = CreateImage(glow.transform, $"Flag{i}", flag, Color.white);
                side.preserveAspect = true;
                side.raycastTarget = false;
                float x = i == 0 ? -96f : 96f;
                SetCenteredAt(side.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, -4f), new Vector2(52f, 52f));
                side.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 12f : -12f);
            }
        }

        TextMeshProUGUI offerTitle = CreateTmp(offer.transform, "Title", "HAZARD HEIGHTS UNLIMITED",
            26, MenuAccent, TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -104f), new Vector2(RefillPanelW - 100f, 34f), new Vector2(0.5f, 1f));
        offerTitle.characterSpacing = 2f;
        CreateTmp(offer.transform, "Pitch", "UNLIMITED ATTEMPTS - NEVER WAIT TO PLAY AGAIN", 18,
            WithAlpha(TextPrimary, 0.92f), TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -142f), new Vector2(RefillPanelW - 100f, 22f), new Vector2(0.5f, 1f));
        CreateTmp(offer.transform, "Pitch2", "NO ADS  ·  PLAY OFFLINE  ·  YOURS FOREVER", 18,
            WithAlpha(TextMuted, 0.95f), TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -170f), new Vector2(RefillPanelW - 100f, 20f), new Vector2(0.5f, 1f));
        // The DEVLETTER.md beat-2 microcopy: one line of who's behind the price, on the
        // surface that already converts - never a new popup (SHOP.md §7.2 restraint).
        CreateTmp(offer.transform, "DevLine", DevSupportLine, 18,
            WithAlpha(TextMuted, 0.8f), TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -196f), new Vector2(RefillPanelW - 100f, 20f), new Vector2(0.5f, 1f));

        // CTA: the Profile card's builder, verbatim - owned banner, live BUY, or the
        // dimmed COMING SOON all render here exactly as they do there.
        RectTransform ctaSlot = CreateRect(offer.transform, "CtaSlot",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 22f), new Vector2(-44f, 92f));
        void RenderCta()
        {
            if (ctaSlot == null) return;
            foreach (Transform child in ctaSlot) UnityEngine.Object.Destroy(child.gameObject);
            BuildUnlimitedCta(ctaSlot, RenderCta);
        }
        RenderCta();

        // A purchase mid-modal removes the reason this modal exists.
        void OnPremiumChanged()
        {
            PremiumStore.Changed -= OnPremiumChanged;
            if (PremiumStore.IsPremium) CloseRefillOffer();
        }
        PremiumStore.Changed += OnPremiumChanged;
        panel.gameObject.AddComponent<UnhookOnDestroy>().Unhook =
            () => PremiumStore.Changed -= OnPremiumChanged;

        // ---- the quiet hinge between the two options ----
        // Vertically CENTERED in the gap between the offer card and the ad row (it sat
        // 32px below the card but 50px above the row - Nick clocked it immediately).
        // Panel 700: offer ends -440; ad row's top edge is -(700 - 118) = -582; the gap
        // is 142, so a 24-tall label starts at -440 - (142 - 24) / 2 = -499.
        CreateTmp(panel, "Or", "OR", 18, WithAlpha(TextMuted, 0.8f),
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -499f), new Vector2(120f, 24f), new Vector2(0.5f, 1f));

        BuildWatchAdOption(panel);

        // The ad row is LIVE, not a snapshot. The dominant real-world case: the modal is
        // opened seconds after boot, before the first ad has finished loading - a static
        // row then shows "NO AD READY" forever even though the ad lands moments later,
        // and an inert row shaped like a button reads as a broken button (Nick's phone,
        // 2026-08-09, twice). The watcher rebuilds the row whenever its state changes.
        RefillOfferLive live = panel.gameObject.AddComponent<RefillOfferLive>();
        live.Panel = panel;
    }

    /// <summary>Rebuilds the WATCH AD row when what it says stops being true: the ad
    /// finishing its load, the meter filling from regen, the budget changing. Checks
    /// twice a second on unscaled time (the menu runs at timeScale 0).</summary>
    private sealed class RefillOfferLive : MonoBehaviour
    {
        public RectTransform Panel;

        private float _nextTick;
        private (bool ready, bool full, int left) _rendered;

        private void Start() => _rendered = CurrentState();

        private static (bool, bool, int) CurrentState()
        {
            bool full = AttemptsService.Count >= AttemptsService.MaxAttempts;
            return (AttemptsService.AdRefillAvailable && !full, full, AttemptsService.AdGrantsRemaining);
        }

        private void Update()
        {
            if (Panel == null) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.5f;

            var now = CurrentState();
            if (now == _rendered) return;
            _rendered = now;

            Transform old = Panel.Find("WatchAd");
            if (old != null)
            {
                // Destroy is deferred: rename first, or next tick's Find could grab the
                // dying row instead of the fresh one (the deferred-destroy Find trap).
                old.name = "WatchAdRetired";
                Destroy(old.gameObject);
            }
            BuildWatchAdOption(Panel);
        }
    }

    /// <summary>Option 2: the ad. Visually SECONDARY to the pitch above - dark body,
    /// thin edge, no gold - and always explicit about what a watch buys and how many
    /// are left today, because a surprise ad is what this whole modal replaces.</summary>
    private static void BuildWatchAdOption(RectTransform panel)
    {
        bool full = AttemptsService.Count >= AttemptsService.MaxAttempts;
        int left = AttemptsService.AdGrantsRemaining;
        bool ready = AttemptsService.AdRefillAvailable && !full;

        // Inert states are deliberately FLAT - a body barely above the panel and no edge
        // at all. A dimmed-but-button-shaped card still reads as a button that is broken
        // (Nick tapped one repeatedly on device); only the tappable state may look
        // tappable.
        Image row = RuntimeUiKit.CreateImage(panel, "WatchAd",
            RuntimeSprites.RoundedPanel(),
            ready ? new Color(0.08f, 0.08f, 0.10f, 1f) : new Color(0.07f, 0.065f, 0.085f, 0.55f));
        row.type = Image.Type.Sliced;
        RectTransform rowRect = row.rectTransform;
        rowRect.anchorMin = new Vector2(0f, 0f);
        rowRect.anchorMax = new Vector2(1f, 0f);
        rowRect.pivot = new Vector2(0.5f, 0f);
        // When the notification ask row occupies the panel's bottom band, ride above it
        // by exactly the added height - every top-relative position (offer card, OR
        // label) then keeps its tuned distance to this row.
        float lift = panel.sizeDelta.y - RefillPanelH;
        rowRect.offsetMin = new Vector2(24f, 26f + lift);
        rowRect.offsetMax = new Vector2(-24f, 26f + 92f + lift);
        if (ready) RuntimeUiKit.AddOutline(rowRect, WithAlpha(GameMenuStyle.Accent, 0.5f));

        string label;
        string sub;
        if (full)
        {
            label = "ATTEMPTS ARE FULL";
            sub = "COME BACK AFTER YOUR NEXT RUN";
        }
        else if (left == 0)
        {
            label = "WATCH AD  +2 ATTEMPTS";
            sub = "DAILY LIMIT REACHED - MORE TOMORROW";
        }
        else if (!ready)
        {
            label = "WATCH AD  +2 ATTEMPTS";
            sub = "NO AD READY - TRY AGAIN IN A MOMENT";
        }
        else
        {
            label = "WATCH AD  +2 ATTEMPTS";
            sub = left == AttemptsService.GrantsUnknown ? "FREE - SPONSORED VIDEO"
                : left == 1 ? "LAST ONE TODAY"
                : $"{left} LEFT TODAY";
        }

        CreateTmp(row.transform, "Label", label, 24,
            ready ? TextPrimary : WithAlpha(TextMuted, 0.9f),
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, 12f), new Vector2(RefillPanelW - 100f, 32f), new Vector2(0.5f, 0.5f));
        CreateTmp(row.transform, "Sub", sub, 18, WithAlpha(TextMuted, 0.9f),
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -18f), new Vector2(RefillPanelW - 100f, 20f), new Vector2(0.5f, 0.5f));

        if (!ready) return;

        // CreateImage defaults raycastTarget to FALSE (RuntimeUiKit hygiene) - without
        // this the Button below exists but never receives a tap: the click falls through
        // to the panel, which swallows it, and nothing happens at all. Device-found
        // 2026-08-09; the backdrop and X worked only because theirs are set explicitly.
        row.raycastTarget = true;

        Button watch = row.gameObject.AddComponent<Button>();
        watch.targetGraphic = row;
        watch.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            // The modal's job is done: the player chose the ad with full knowledge.
            CloseRefillOffer();
            RewardedAds.Show(earned =>
            {
                if (earned) AttemptsService.RequestAdRefill(null);
            });
        });
    }

    /// <summary>The contextual permission ask: one tappable row at the panel's bottom.
    /// A tap fires the REAL OS dialog (the only place outside Settings that does), and
    /// the row then speaks the verdict itself - it never turns into a dead button, and
    /// CanOfferContextualAsk goes false either way, so the row never appears again.</summary>
    private static void BuildNotifyAskRow(RectTransform panel)
    {
        Image row = RuntimeUiKit.CreateImage(panel, "NotifyAsk",
            RuntimeSprites.RoundedPanel(), new Color(0.08f, 0.08f, 0.10f, 1f));
        row.type = Image.Type.Sliced;
        RectTransform rowRect = row.rectTransform;
        rowRect.anchorMin = new Vector2(0f, 0f);
        rowRect.anchorMax = new Vector2(1f, 0f);
        rowRect.pivot = new Vector2(0.5f, 0f);
        rowRect.offsetMin = new Vector2(24f, 26f);
        rowRect.offsetMax = new Vector2(-24f, 26f + NotifyAskRowH);
        RuntimeUiKit.AddOutline(rowRect, WithAlpha(GameMenuStyle.Accent, 0.4f));
        row.raycastTarget = true;   // CreateImage defaults to false; see BuildWatchAdOption

        Image bell = CreateImage(row.transform, "Bell",
            MenuSprites.Bell(WithAlpha(GameMenuStyle.Accent, 0.9f)), Color.white);
        bell.preserveAspect = true;
        SetCenteredAt(bell.rectTransform, new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(34f, 34f));

        TextMeshProUGUI label = CreateTmp(row.transform, "Label",
            "GET A PING WHEN ATTEMPTS ARE FULL?", 19, TextPrimary,
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, 9f), new Vector2(RefillPanelW - 160f, 26f), new Vector2(0.5f, 0.5f));
        TextMeshProUGUI sub = CreateTmp(row.transform, "Sub",
            "ONE TAP - JUST THE REFILL, NO SPAM", 18, WithAlpha(TextMuted, 0.9f),
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -15f), new Vector2(RefillPanelW - 160f, 18f), new Vector2(0.5f, 0.5f));

        Button ask = row.gameObject.AddComponent<Button>();
        ask.targetGraphic = row;
        ask.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            ask.interactable = false;
            label.text = "...";
            NotificationScheduler.RequestPermission(granted =>
            {
                if (label == null) return;   // modal closed while the OS dialog was up
                if (granted)
                {
                    label.text = "YOU'LL GET A PING WHEN ATTEMPTS ARE FULL";
                    sub.text = "MANAGE IN SETTINGS - ALERTS";
                }
                else
                {
                    label.text = "NOTIFICATIONS ARE BLOCKED";
                    sub.text = "ENABLE THEM IN YOUR PHONE'S SETTINGS";
                }
            });
        });
    }

    /// <summary>The one-time "out of lives - want a ping?" sheet, popped automatically
    /// the FIRST time the meter blocks play (Nick 2026-08-12: the ask must find the
    /// player, not wait to be found). OS rules make on-by-default impossible - both
    /// stores require an explicit grant via the system dialog - so this is the moment
    /// we ask: the one time a notification is a favor. Shows once ever (flag set on
    /// open, crash-proof); declining softly keeps the OS never-asked, so the refill
    /// modal's passive row and Settings remain as quiet second chances.</summary>
    private static void MaybeOfferNotificationPrompt()
    {
        if (NotificationScheduler.SoftAskShown) return;
        if (!NotificationScheduler.CanOfferContextualAsk) return;
        NotificationScheduler.MarkSoftAskShown();

        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Notify Ask", 5850);

        Image backdrop = CreateImage(overlay.transform, "Backdrop", null, new Color(0f, 0f, 0f, 0.72f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        Button dismiss = backdrop.gameObject.AddComponent<Button>();
        dismiss.transition = Selectable.Transition.None;
        dismiss.onClick.AddListener(() => UnityEngine.Object.Destroy(overlay));

        Color accent = GameMenuStyle.Accent;
        Image panel = CreateImage(overlay.transform, "Panel", RuntimeSprites.RoundedPanel(),
            GameMenuStyle.PanelColor);
        panel.type = Image.Type.Sliced;
        SetRect(panel.rectTransform, Vector2.zero, new Vector2(640f, 440f), new Vector2(0.5f, 0.5f));
        panel.raycastTarget = true;

        Image bell = CreateImage(panel.transform, "Bell", MenuSprites.Bell(WithAlpha(accent, 0.9f)), Color.white);
        bell.preserveAspect = true;
        SetCenteredAt(bell.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(56f, 56f));

        TextMeshProUGUI title = CreateTmp(panel.transform, "Title", "OUT OF ATTEMPTS", 32, TextPrimary,
            TextAnchor.UpperCenter, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(0f, -108f), new Vector2(560f, 42f), new Vector2(0.5f, 1f));
        title.characterSpacing = 3f;
        TextMeshProUGUI body = CreateTmp(panel.transform, "Body",
            "Want a ping the moment they're back to full?", 22, WithAlpha(TextPrimary, 0.9f),
            TextAnchor.UpperCenter, FontStyle.Normal, RuntimeUiKit.TitleFont,
            new Vector2(0f, -158f), new Vector2(520f, 64f), new Vector2(0.5f, 1f));
        body.textWrappingMode = TextWrappingModes.Normal;

        // YES: the bright, easy choice - only this fires the real OS dialog.
        Image yesBg = CreateImage(panel.transform, "Yes", MenuSprites.RoundedGradient(
            Color.Lerp(accent, Color.white, 0.15f), Color.Lerp(accent, Color.black, 0.2f)), Color.white);
        yesBg.type = Image.Type.Sliced;
        SetRect(yesBg.rectTransform, new Vector2(0f, 118f), new Vector2(540f, 92f), new Vector2(0.5f, 0f));
        yesBg.raycastTarget = true;
        TextMeshProUGUI yesLabel = CreateTmp(yesBg.transform, "Label", "YES, NOTIFY ME", 25,
            new Color(0.08f, 0.07f, 0.10f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button yes = yesBg.gameObject.AddComponent<Button>();
        yes.targetGraphic = yesBg;
        yes.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            yes.interactable = false;
            dismiss.interactable = false;
            yesLabel.text = "...";
            NotificationScheduler.RequestPermission(_ =>
            {
                // Either verdict: the sheet's job is done (scheduling happens at the
                // next app background; a denial is spoken by the Settings tab, not here).
                if (overlay != null) UnityEngine.Object.Destroy(overlay);
            });
        });

        // The quiet no: flat and unbordered, same weight as tap-outside.
        Image noBg = CreateImage(panel.transform, "No", RuntimeSprites.RoundedPanel(),
            new Color(0.10f, 0.095f, 0.12f, 1f));
        noBg.type = Image.Type.Sliced;
        SetRect(noBg.rectTransform, new Vector2(0f, 30f), new Vector2(540f, 72f), new Vector2(0.5f, 0f));
        noBg.raycastTarget = true;
        CreateTmp(noBg.transform, "Label", "NOT NOW", 21, WithAlpha(TextMuted, 0.9f),
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button no = noBg.gameObject.AddComponent<Button>();
        no.targetGraphic = noBg;
        no.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click", 0.7f);
            UnityEngine.Object.Destroy(overlay);
        });
    }

    /// <summary>Invisible-but-tappable button anchored to the panel's top right.</summary>
    private static Button CreateGhostButton(RectTransform parent, string name, Vector2 pos, Vector2 size)
    {
        Image hit = RuntimeUiKit.CreateImage(parent, name, null, Color.clear);
        RectTransform rect = hit.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        hit.raycastTarget = true;
        Button button = hit.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        return button;
    }

    /// <summary>Pop-in: panel scales 0.92 -> 1 with an ease-out while the backdrop fades
    /// in, all on unscaled time (menu timeScale is 0). ~0.16s - alive, not showy.</summary>
    private sealed class RefillOfferPop : MonoBehaviour
    {
        public RectTransform Panel;
        public Image Backdrop;

        private const float Duration = 0.16f;
        private float _t;

        private void Start()
        {
            if (Panel != null) Panel.localScale = Vector3.one * 0.92f;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / Duration);
            float ease = 1f - (1f - k) * (1f - k) * (1f - k);
            if (Panel != null) Panel.localScale = Vector3.one * Mathf.LerpUnclamped(0.92f, 1f, ease);
            if (Backdrop != null)
            {
                Color c = Backdrop.color;
                c.a = 0.82f * ease;
                Backdrop.color = c;
            }
            if (k >= 1f) enabled = false;
        }
    }
}
