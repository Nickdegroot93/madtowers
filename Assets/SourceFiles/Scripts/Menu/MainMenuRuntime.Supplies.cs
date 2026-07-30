using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// The level modal's SUPPLIES section (SHOP.md §9.1): two full-width card rows - RUN LIVES
// with a big +/- stepper, and BOOSTS opening the tray - plus one status line (running total,
// or the out-of-attempts block with the Hour Pass). Mobile-first: every tap target is a
// ≥80px card-row button, prices ride ON the buttons, and nothing is display-only except the
// heart pips themselves. Charging happens at run start (RunSuppliesApplier) - closing the
// modal always costs nothing.
// (partial of MainMenuRuntime, split from LevelSummary for readability - same class.)
public static partial class MainMenuRuntime
{
    private sealed class SupplySelection
    {
        public int Lives;                            // 0..3, purchased pips
        public readonly List<BoostId> Boosts = new();  // ≤ SupplyCatalog.MaxBoostsPerRun

        public bool Boosted => Lives > 0 || Boosts.Count > 0;

        public int Total()
        {
            int total = SupplyCatalog.PriceForLives(Lives);
            for (int i = 0; i < Boosts.Count; i++)
            {
                SupplyCatalog.BoostInfo info = SupplyCatalog.Info(Boosts[i]);
                if (info != null) total += info.Price;
            }
            return total;
        }

        public RunSuppliesState.Loadout ToLoadout()
        {
            if (!Boosted) return null;
            var loadout = new RunSuppliesState.Loadout { Lives = Lives, TotalPrice = Total() };
            loadout.Boosts.AddRange(Boosts);
            return loadout;
        }

        /// <summary>The loadout as jsonb for start_run (the boosted board's honesty badge,
        /// SHOP.md §5) - null for a clean run, matching ToLoadout.</summary>
        public string ToLoadoutJson()
        {
            if (!Boosted) return null;
            var sb = new System.Text.StringBuilder(96);
            sb.Append("{\"lives\":").Append(Lives).Append(",\"boosts\":[");
            for (int i = 0; i < Boosts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Boosts[i]).Append('"');
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }

    // One modal's worth of supplies UI state: rebuilt-in-place roots + the pieces the
    // refreshers touch. Local to the open modal; dies with the overlay.
    private sealed class SuppliesUi
    {
        public LevelDefinition Level;
        public SupplySelection Selection;
        public RectTransform Panel;         // the modal panel (section parent)
        public GameObject SectionRoot;      // destroyed + rebuilt on every change
        public GameObject TrayOverlay;      // non-null while the boost tray is open
        public Image PlayBg;
        public TextMeshProUGUI PlayLabel;
        public GameObject PlayOutline;      // gold edge, boosted only
        public Button PlayButton;
        public bool StartPending;           // a start_run grant is in flight - freeze the button
        public float SectionTop;            // y of the section inside the panel
        public float ContentW;
        public float Pad;
    }

    private const float SupplyRowH = 104f;      // the two card rows - full-width tap targets
    private const float SupplyRowGap = 14f;
    private const float SupplyStatusH = 88f;    // total line / out-of-attempts row
    /// <summary>Extra modal height the section needs (LevelSummary adds this to H).</summary>
    private const float SuppliesSectionHeight = 2f * SupplyRowH + 2f * SupplyRowGap + SupplyStatusH + 26f;
    /// <summary>The level modal's full height once supplies are on - shared with the boost
    /// picker, which must be EXACTLY this tall: a shorter overlay panel lets the modal underneath
    /// peek out above and below it, which reads as broken borders (Nick, 2026-07-29).</summary>
    private const float ModalHeightWithSupplies = 768f + SuppliesSectionHeight;

    private static string CoinText(int amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

    // ---- the section -------------------------------------------------------------------------

    private static SuppliesUi BuildSuppliesSection(RectTransform panel, LevelDefinition level,
        float pad, float contentW, float sectionTop)
    {
        var ui = new SuppliesUi
        {
            Level = level,
            Selection = new SupplySelection(),
            Panel = panel,
            SectionTop = sectionTop,
            ContentW = contentW,
            Pad = pad,
        };
        RefreshSuppliesSection(ui);
        return ui;
    }

    private static void RefreshSuppliesSection(SuppliesUi ui)
    {
        if (ui.SectionRoot != null) UnityEngine.Object.Destroy(ui.SectionRoot);

        RectTransform section = CreateRect(ui.Panel, "Supplies",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(ui.Pad, ui.SectionTop), new Vector2(ui.ContentW, SuppliesSectionHeight));
        ui.SectionRoot = section.gameObject;

        int wallet = PlayerProfileStore.Coins;

        // The wallet can shrink while a loadout is picked (Hour Pass). Trim the selection
        // back inside the balance so the quote on screen is always the charge at run start.
        while (ui.Selection.Total() > wallet)
        {
            if (ui.Selection.Boosts.Count > 0) ui.Selection.Boosts.RemoveAt(ui.Selection.Boosts.Count - 1);
            else if (ui.Selection.Lives > 0) ui.Selection.Lives--;
            else break;
        }

        TextMeshProUGUI header = CreateTmp(section, "Header", "SUPPLIES - THIS RUN ONLY", 16,
            WithAlpha(TextMuted, 0.9f), TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(4f, 0f), new Vector2(500f, 22f), new Vector2(0f, 1f));
        header.characterSpacing = 3f;

        BuildLivesRow(ui, section, wallet, -26f);
        BuildBoostsRow(ui, section, -26f - SupplyRowH - SupplyRowGap);
        TextMeshProUGUI countdown = BuildStatusRow(ui, section, wallet, -26f - 2f * (SupplyRowH + SupplyRowGap));
        RefreshPlayButton(ui);

        // Keeps the section honest between taps: ticks the out-of-attempts countdown once a
        // second (the old build-time snapshot froze until the next interaction) and rebuilds
        // when the online state or the server meter changes. Dies with the section.
        SuppliesLive live = section.gameObject.AddComponent<SuppliesLive>();
        live.Ui = ui;
        live.Countdown = countdown;
    }

    /// <summary>Per-section live updater (menu runs at timeScale=0 - unscaled time only).
    /// Event handlers fire a single rebuild and retire; the rebuilt section brings a fresh
    /// instance, so a deferred-destroyed one can never refresh twice.</summary>
    private sealed class SuppliesLive : MonoBehaviour
    {
        public SuppliesUi Ui;
        public TextMeshProUGUI Countdown;   // null unless the out-of-attempts row is up

        private float _nextTick;
        private bool _consumed;

        private void OnEnable()
        {
            OnlineService.StateChanged += HandleChanged;
            AttemptsSync.Changed += HandleChanged;
        }

        private void OnDisable()
        {
            OnlineService.StateChanged -= HandleChanged;
            AttemptsSync.Changed -= HandleChanged;
        }

        private void HandleChanged()
        {
            if (_consumed || Ui == null || Ui.StartPending) return;
            _consumed = true;
            RefreshSuppliesSection(Ui);
        }

        private void Update()
        {
            if (_consumed || Countdown == null) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 1f;

            if (AttemptsService.CanStartRun)
            {
                // The meter healed while the modal sat open - flip back to the normal row.
                HandleChanged();
                return;
            }
            TimeSpan regen = AttemptsService.NextRegenIn;
            Countdown.text = $"OUT OF ATTEMPTS - NEXT IN {(int)regen.TotalMinutes:00}:{regen.Seconds:00}";
        }
    }

    // ---- RUN LIVES: hearts + one big +/- stepper ----------------------------------------------
    // (A tappable-hearts version was tried 2026-07-29 and reverted same day - Nick preferred the
    // stepper; the hearts stay display-only.)

    private static void BuildLivesRow(SuppliesUi ui, RectTransform section, int wallet, float y)
    {
        RectTransform row = CreateSupplyCard(section, "LivesRow", y, ui.ContentW);

        // Text-only left edge, same x as the BOOSTS row below - the pips ARE the heart imagery,
        // a leading icon on the label read as a duplicate (Nick, 2026-07-22).
        CreateTmp(row, "Label", "RUN LIVES", 21, TextPrimary, TextAnchor.UpperLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(24f, -20f), new Vector2(210f, 28f), new Vector2(0f, 1f));
        CreateTmp(row, "Blurb", "MAX 3", 14,
            WithAlpha(TextMuted, 0.8f), TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(24f, -52f), new Vector2(210f, 20f), new Vector2(0f, 1f));

        // Heart pips: the current pick, big and readable (display - the buttons do the work).
        // Full/empty are the two-state heart art; while the dedicated empty socket asset is
        // pending (HeartSprites), an unfilled pip is the full art dimmed.
        // Fixed x so they don't jump when the [-] button appears; the rightmost pip stops
        // short of the [-] slot (left edge at ContentW - 340).
        for (int i = 0; i < RunState.MaxLives; i++)
        {
            bool filled = ui.Selection.Lives > i;
            Sprite pipSprite = filled ? HeartSprites.Full() : HeartSprites.Empty();
            Color pipColor = filled || HeartSprites.HasDedicatedEmpty
                ? Color.white : new Color(1f, 1f, 1f, 0.16f);
            Image pip = CreateImage(row, $"Pip{i}", pipSprite, pipColor);
            pip.preserveAspect = true;
            SetRect(pip.rectTransform, new Vector2(280f + i * 56f, 0f), new Vector2(48f, 48f), new Vector2(0f, 0.5f));
        }

        // The stepper: [-] appears once something is picked; [+] carries the NEXT pip's price.
        if (ui.Selection.Lives > 0)
        {
            Button minus = CreateSupplyButton(row, "Minus", "-", 84f,
                new Vector2(-256f, 0f), enabled: true, gold: false);
            minus.onClick.AddListener(() =>
            {
                SfxPlayer.Play("ui-button-click");
                ui.Selection.Lives--;
                RefreshSuppliesSection(ui);
            });
        }

        if (ui.Selection.Lives < RunState.MaxLives)
        {
            int price = SupplyCatalog.LifePipPrices[ui.Selection.Lives];
            bool affordable = ui.Selection.Total() + price <= wallet;
            Button plus = CreateSupplyButton(row, "Plus", "+ LIFE", 220f,
                new Vector2(-24f, 0f), affordable, gold: true, price: price);
            if (affordable)
            {
                plus.onClick.AddListener(() =>
                {
                    SfxPlayer.Play("ui-button-click");
                    ui.Selection.Lives++;
                    RefreshSuppliesSection(ui);
                });
            }
        }
        else
        {
            CreateTmp(row, "Max", "MAX", 18, WithAlpha(TextMuted, 0.7f), TextAnchor.MiddleRight,
                FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(-24f, 0f), new Vector2(200f, 30f), new Vector2(1f, 0.5f));
        }
    }

    // ---- BOOSTS: one big row, the picker does the choosing --------------------------------------

    private static void BuildBoostsRow(SuppliesUi ui, RectTransform section, float y)
    {
        RectTransform row = CreateSupplyCard(section, "BoostsRow", y, ui.ContentW);

        CreateTmp(row, "Label", "BOOSTS", 22, TextPrimary, TextAnchor.UpperLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(24f, -24f), new Vector2(300f, 30f), new Vector2(0f, 1f));

        // Picked boosts render as gold pill CHIPS, not a text list - the equipped state should
        // look like equipment, and the chips echo the picker cards the player just chose from.
        if (ui.Selection.Boosts.Count == 0)
        {
            CreateTmp(row, "Picked", "NONE PICKED", 16, WithAlpha(TextMuted, 0.9f),
                TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(24f, -60f), new Vector2(ui.ContentW - 300f, 24f), new Vector2(0f, 1f));
        }
        else
        {
            float chipX = 24f;
            for (int i = 0; i < ui.Selection.Boosts.Count; i++)
            {
                SupplyCatalog.BoostInfo info = SupplyCatalog.Info(ui.Selection.Boosts[i]);
                if (info == null) continue;
                string name = info.DisplayName.ToUpperInvariant();
                float chipW = name.Length * 10.5f + 40f;
                Image chip = CreateImage(row, $"Chip{i}", RuntimeSprites.RoundedPanel(),
                    new Color(0.17f, 0.14f, 0.07f, 1f));
                chip.type = Image.Type.Sliced;
                chip.pixelsPerUnitMultiplier = 1.6f;
                SetRect(chip.rectTransform, new Vector2(chipX, -56f), new Vector2(chipW, 40f), new Vector2(0f, 1f));
                RuntimeUiKit.AddOutline(chip.rectTransform, WithAlpha(GoldBase, 0.6f));
                TextMeshProUGUI chipText = CreateTmp(chip.transform, "Name", name, 16, GoldBase,
                    TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
                chipText.characterSpacing = 1f;
                AutoSize(chipText, 11f, 16f);
                chipX += chipW + 12f;
            }
        }

        Button choose = CreateSupplyButton(row, "Choose",
            ui.Selection.Boosts.Count > 0 ? "CHANGE" : "CHOOSE", 220f,
            new Vector2(-24f, 0f), enabled: true, gold: ui.Selection.Boosts.Count == 0);
        choose.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            OpenBoostTray(ui);
        });
    }

    // ---- the status row: running total, or the out-of-attempts block ---------------------------

    private static TextMeshProUGUI BuildStatusRow(SuppliesUi ui, RectTransform section, int wallet, float y)
    {
        RectTransform zone = CreateRect(section, "Status",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, y), new Vector2(ui.ContentW, SupplyStatusH));

        // Campaign runs need the server's start_run grant (BACKEND.md §5.1): when the online
        // layer can't reach it, say so honestly instead of showing meter numbers we can't
        // vouch for. RETRY kicks the reconnect; the SuppliesLive watcher rebuilds on Ready.
        if (AttemptsService.OnlineBlocked)
        {
            CreateTmp(zone, "Offline", "YOU'RE OFFLINE - CONNECTION\nNEEDED FOR RANKED LEVELS",
                17, LockedColor, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(4f, 0f), new Vector2(ui.ContentW - 210f, 52f), new Vector2(0f, 0.5f));
            Button retry = CreateSupplyButton(zone, "Retry", "RETRY", 170f,
                new Vector2(0f, 0f), enabled: true, gold: true);
            retry.onClick.AddListener(() =>
            {
                SfxPlayer.Play("ui-button-click");
                OnlineService.RetryConnect();
            });
            return null;
        }

        // The meter only speaks up when it actually blocks play (its home is the top bar).
        // (The Hour Pass buy that used to sit here was cut by Nick 2026-07-20 - waiting and
        // the rewarded ad are the only refills.) The WATCH AD button is the game's single ad
        // placement (SHOP.md §7): opt-in, explicit "+2" copy, and only rendered when an ad
        // can actually show AND pay out (no SDK / rate-limited = the row is countdown-only).
        if (AttemptsService.MeterActive && !AttemptsService.CanStartRun)
        {
            bool adOffer = AttemptsService.AdRefillAvailable;
            TimeSpan regen = AttemptsService.NextRegenIn;
            TextMeshProUGUI blocked = CreateTmp(zone, "Blocked",
                $"OUT OF ATTEMPTS - NEXT IN {(int)regen.TotalMinutes:00}:{regen.Seconds:00}",
                18, LockedColor, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(4f, 0f), new Vector2(ui.ContentW - (adOffer ? 250f : 8f), 30f),
                new Vector2(0f, 0.5f));
            if (adOffer)
            {
                Button watch = CreateSupplyButton(zone, "WatchAd", "WATCH AD  +2", 236f,
                    new Vector2(0f, 0f), enabled: true, gold: true);
                watch.onClick.AddListener(() =>
                {
                    SfxPlayer.Play("ui-button-click");
                    RewardedAds.Show(earned =>
                    {
                        // Refresh even on a skip: a mid-ad rebuild (online meter events fire
                        // any time) renders the row WITHOUT the button - Available is false
                        // while an ad shows - so the close must always re-evaluate the row.
                        void RefreshIfAlive()
                        {
                            if (ui.Panel != null && !ui.StartPending) RefreshSuppliesSection(ui);
                        }
                        if (earned) AttemptsService.RequestAdRefill(ok => RefreshIfAlive());
                        else RefreshIfAlive();
                    });
                });
            }
            return blocked;
        }

        // TOTAL [coin]n — WALLET [coin]n, in fixed slots (amounts stay well under the slot
        // widths). Coin icon + number is the game's price language - no currency symbol.
        int total = ui.Selection.Total();
        float x = 4f;
        if (total > 0)
        {
            CreateTmp(zone, "TotalLabel", "TOTAL", 17, GoldBase, TextAnchor.MiddleLeft,
                FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(x, 0f), new Vector2(80f, 30f), new Vector2(0f, 0.5f));
            CreateCoinAmountLeft(zone, total, 17, GoldBase, x + 84f);
            x += 84f + 110f + 36f;
        }
        CreateTmp(zone, "WalletLabel", "WALLET", 17, WithAlpha(TextMuted, 0.85f), TextAnchor.MiddleLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(x, 0f), new Vector2(100f, 30f), new Vector2(0f, 0.5f));
        CreateCoinAmountLeft(zone, wallet, 17, WithAlpha(TextMuted, 0.85f), x + 104f);
        return null;
    }

    /// <summary>Left-anchored coin+amount pair: coin at <paramref name="leftX"/>, number
    /// flowing right of it.</summary>
    private static void CreateCoinAmountLeft(Transform parent, int amount, int fontSize, Color color, float leftX)
    {
        float numberX = leftX;
        Sprite coin = MenuIcon("coin");
        if (coin != null)
        {
            Image icon = CreateImage(parent, "Coin", coin, Color.white);
            icon.preserveAspect = true;
            SetRect(icon.rectTransform, new Vector2(leftX, 0f), new Vector2(26f, 26f), new Vector2(0f, 0.5f));
            numberX += 30f;
        }
        CreateTmp(parent, "Amount", CoinText(amount), fontSize, color, TextAnchor.MiddleLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(numberX, 0f), new Vector2(90f, 30f), new Vector2(0f, 0.5f));
    }

    // ---- shared card/button builders ------------------------------------------------------------

    private static RectTransform CreateSupplyCard(RectTransform section, string name, float y, float width)
    {
        RectTransform row = CreateRect(section, name,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, y), new Vector2(width, SupplyRowH));
        Image fill = row.gameObject.AddComponent<Image>();
        fill.sprite = RuntimeSprites.RoundedPanel();
        fill.type = Image.Type.Sliced;
        fill.color = new Color(0.11f, 0.10f, 0.085f, 1f);
        RuntimeUiKit.AddOutline(row, GlassBorder);
        return row;
    }

    /// <summary>A big right-anchored button (height 80, the taste-contract minimum with room to
    /// spare). Disabled = visibly dimmed but still says its price - "can't afford" must never
    /// read as "broken". Prices render as the gold coin icon + number (the game's currency has
    /// a face, not a symbol - Nick, 2026-07-20): pass price ≥ 0 to get label-left, coin+number-
    /// right; price &lt; 0 keeps a centered label.</summary>
    private static Button CreateSupplyButton(RectTransform row, string name, string label,
        float width, Vector2 rightOffset, bool enabled, bool gold, int price = -1)
    {
        Image bg = CreateImage(row, name, RuntimeSprites.RoundedPanel(),
            gold ? new Color(0.16f, 0.13f, 0.07f, 1f) : new Color(0.14f, 0.13f, 0.11f, 1f));
        bg.type = Image.Type.Sliced;
        // SetRect pivots at the anchor, so with anchor (1, 0.5) the offset is the button's
        // RIGHT edge relative to the row's right edge - callers pass e.g. (-24, 0).
        SetRect(bg.rectTransform, rightOffset, new Vector2(width, 80f), new Vector2(1f, 0.5f));
        bg.raycastTarget = true;
        RuntimeUiKit.AddOutline(bg.rectTransform,
            gold ? WithAlpha(GoldBase, enabled ? 0.9f : 0.3f) : WithAlpha(TextMuted, enabled ? 0.5f : 0.2f));

        Color textColor = enabled ? (gold ? GoldBase : TextPrimary) : WithAlpha(LockedColor, 0.8f);
        if (price < 0)
        {
            TextMeshProUGUI text = CreateTmp(bg.transform, "Label", label, 19, textColor,
                TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
            AutoSize(text, 13f, 19f);
        }
        else
        {
            TextMeshProUGUI text = CreateTmp(bg.transform, "Label", label, 19, textColor,
                TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(20f, 0f), new Vector2(width - 130f, 32f), new Vector2(0f, 0.5f));
            AutoSize(text, 13f, 19f);
            CreateCoinAmount(bg.transform, price, 19, textColor, -16f);
        }

        Button button = bg.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.interactable = enabled;
        return button;
    }

    /// <summary>Coin icon + amount as a right-anchored pair: number right-aligned at
    /// <paramref name="rightX"/>, the coin just left of it. The game-wide way to write a
    /// price - never a currency symbol.</summary>
    private static void CreateCoinAmount(Transform parent, int amount, int fontSize, Color color, float rightX)
    {
        const float numberW = 74f;
        CreateTmp(parent, "Amount", CoinText(amount), fontSize, color, TextAnchor.MiddleRight,
            FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(rightX, 0f), new Vector2(numberW, 30f), new Vector2(1f, 0.5f));
        Sprite coin = MenuIcon("coin");
        if (coin == null) return;
        Image icon = CreateImage(parent, "Coin", coin, Color.white);
        icon.preserveAspect = true;
        SetRect(icon.rectTransform, new Vector2(rightX - numberW - 4f, 0f),
            new Vector2(28f, 28f), new Vector2(1f, 0.5f));
    }

    // ---- the boost picker -------------------------------------------------------------------
    // A centered modal of neon-edged toggle cards - the same CardGradient + CardNeonRing chrome
    // as the in-run ability picker, the game's flagship surface, so pre-run and in-run choices
    // read as one system. Replaced the bottom sheet (Nick, 2026-07-29: "the list that pops up
    // from the bottom looks bad... text way too small... a bit buggy"): cards toggle IN PLACE
    // (no destroy-and-reopen overlay, which flickered for a deferred-destroy frame), the
    // equipped state is a gold ring + check badge readable at arm's length, and every card is a
    // 148px full-width target. This is the pattern the genre trains: tile = toggle, price on
    // the tile, no confirm step - DONE just closes.

    private static void OpenBoostTray(SuppliesUi ui)
    {
        if (ui.TrayOverlay != null) return;

        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Boost Picker", 5600);
        ui.TrayOverlay = overlay;
        void ClosePicker()
        {
            if (ui.TrayOverlay == overlay) ui.TrayOverlay = null;
            UnityEngine.Object.Destroy(overlay);
        }

        Image backdrop = CreateImage(overlay.transform, "Backdrop", null, new Color(0f, 0f, 0f, 0.6f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        Button backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(ClosePicker);

        // Which boosts exist for THIS level (irrelevant ones aren't shown at all, SHOP.md §3.2).
        var relevant = new List<SupplyCatalog.BoostInfo>();
        for (int i = 0; i < SupplyCatalog.Boosts.Count; i++)
        {
            if (SupplyCatalog.IsRelevant(SupplyCatalog.Boosts[i], ui.Level)) relevant.Add(SupplyCatalog.Boosts[i]);
        }

        // EXACTLY the level modal's frame: same width, same height, same center - a smaller
        // panel lets the modal underneath peek out around the edges (Nick, 2026-07-29). The
        // cards centre themselves in whatever space the fixed frame leaves.
        const float W = 880f;
        const float pad = 44f;
        float contentW = W - pad * 2f;
        const float cardH = 148f;
        const float cardGap = 16f;
        const float headH = 116f;
        const float doneH = 96f;
        float H = ModalHeightWithSupplies;

        RectTransform panel = CreateRect(overlay.transform, "Panel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(W, H));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = RuntimeSprites.RoundedPanel();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color(0.075f, 0.065f, 0.058f, 1f);
        panelImage.raycastTarget = true;
        RuntimeUiKit.AddOutline(panel, GoldOutline(0.22f));

        TextMeshProUGUI title = CreateTmp(panel, "Title", "BOOSTS", 36, TextPrimary,
            TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(pad, -34f), new Vector2(400f, 46f), new Vector2(0f, 1f));
        title.characterSpacing = 4f;
        CreateTmp(panel, "Sub", "THIS RUN ONLY - TAP TO EQUIP", 16,
            WithAlpha(TextMuted, 0.85f), TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(pad, -82f), new Vector2(600f, 22f), new Vector2(0f, 1f));

        // The slot counter tells the player where the cap lives before they hit it.
        TextMeshProUGUI slots = CreateTmp(panel, "Slots", "", 26, GoldBase, TextAnchor.UpperRight,
            FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(-pad - 76f, -38f), new Vector2(160f, 34f), new Vector2(1f, 1f));
        slots.characterSpacing = 2f;

        Color closeFill = new Color(0.03f, 0.03f, 0.04f, 0.55f);
        Image closeBg = CreateImage(panel, "Close", MenuSprites.CircleBadge(closeFill, closeFill), Color.white);
        SetRect(closeBg.rectTransform, new Vector2(-20f, -20f), new Vector2(64f, 64f), new Vector2(1f, 1f));
        closeBg.raycastTarget = true;
        CreateTmp(closeBg.transform, "X", "X", 26, TextPrimary, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button closeButton = closeBg.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeBg;
        closeButton.onClick.AddListener(ClosePicker);

        // Cards live in their own container so a toggle rebuilds ONLY the cards, in place -
        // the panel, header and backdrop never blink. The block centres vertically in the
        // fixed frame's space between the header and the DONE button, so a level with three
        // relevant boosts doesn't leave all its slack piled at the bottom.
        float cardsBlockH = relevant.Count * (cardH + cardGap) - cardGap;
        float availableH = H - headH - (40f + doneH + 24f);
        float cardsTop = -headH - Mathf.Max(0f, (availableH - cardsBlockH) * 0.5f);
        RectTransform cardsRoot = CreateRect(panel, "Cards",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(pad, cardsTop), new Vector2(contentW, cardsBlockH));

        void RebuildCards()
        {
            foreach (Transform child in cardsRoot) UnityEngine.Object.Destroy(child.gameObject);
            slots.text = $"{ui.Selection.Boosts.Count} / {SupplyCatalog.MaxBoostsPerRun}";
            int wallet = PlayerProfileStore.Coins;
            for (int i = 0; i < relevant.Count; i++)
            {
                BuildBoostCard(ui, cardsRoot, relevant[i], wallet,
                    new Vector2(0f, -i * (cardH + cardGap)), contentW, cardH, RebuildCards);
            }
        }
        RebuildCards();

        // DONE: one big gold exit. Nothing to confirm - the cards already did the work and the
        // modal's TOTAL line carries the bill; this is just "back", styled like a primary.
        Image doneBg = CreateImage(panel, "Done", MenuSprites.RoundedGradient(
            Color.Lerp(GoldBase, Color.white, 0.12f), Color.Lerp(GoldBase, Color.black, 0.22f)), Color.white);
        doneBg.type = Image.Type.Sliced;
        SetRect(doneBg.rectTransform, new Vector2(pad, 40f), new Vector2(contentW, doneH), new Vector2(0f, 0f));
        doneBg.raycastTarget = true;
        TextMeshProUGUI doneLabel = CreateTmp(doneBg.transform, "Label", "DONE", 30,
            new Color(0.10f, 0.08f, 0.03f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        doneLabel.characterSpacing = 3f;
        Button doneButton = doneBg.gameObject.AddComponent<Button>();
        doneButton.targetGraphic = doneBg;
        doneButton.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            ClosePicker();
        });
    }

    private static void BuildBoostCard(SuppliesUi ui, RectTransform cardsRoot, SupplyCatalog.BoostInfo boost,
        int wallet, Vector2 position, float width, float height, Action refresh)
    {
        bool selected = ui.Selection.Boosts.Contains(boost.Id);
        bool slotsFull = !selected && ui.Selection.Boosts.Count >= SupplyCatalog.MaxBoostsPerRun;
        bool affordable = selected || ui.Selection.Total() + boost.Price <= wallet;
        bool interactable = selected || (!slotsFull && affordable);

        RectTransform card = CreateRect(cardsRoot, $"Boost{boost.Id}",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            position, new Vector2(width, height));

        // The ability-card chrome: near-black gradient slab, all the accent in the neon edge.
        // Equipped = gold ring + soft halo; available = quiet neutral ring; locked-out = barely
        // there. Padded past the card rect so the ring's outer bloom has room (AbilityCardView).
        Color bodyTop = selected ? new Color(0.16f, 0.13f, 0.06f, 0.985f) : new Color(0.10f, 0.095f, 0.085f, 0.985f);
        Color bodyBottom = selected ? new Color(0.075f, 0.06f, 0.03f, 0.985f) : new Color(0.055f, 0.05f, 0.045f, 0.985f);
        Image body = CreateImage(card, "Body", RuntimeSprites.CardGradient(bodyTop, bodyBottom), Color.white);
        body.type = Image.Type.Sliced;
        StretchPadded(body.rectTransform, RuntimeSprites.CardSpritePad);
        body.raycastTarget = true;

        if (selected)
        {
            Image halo = CreateImage(card, "Halo", MenuSprites.GlowFrame(), WithAlpha(GoldBase, 0.20f));
            halo.type = Image.Type.Sliced;
            StretchPadded(halo.rectTransform, RuntimeSprites.CardSpritePad + 10f);
            halo.raycastTarget = false;
        }
        Image ring = CreateImage(card, "Ring", RuntimeSprites.CardNeonRing(), selected
            ? WithAlpha(Color.Lerp(GoldBase, Color.white, 0.15f), 0.95f)
            : WithAlpha(TextMuted, interactable ? 0.30f : 0.12f));
        ring.type = Image.Type.Sliced;
        StretchPadded(ring.rectTransform, RuntimeSprites.CardSpritePad);
        ring.raycastTarget = false;

        // Name + blurb as one block, vertically centred in the card (they sat high with the
        // slack piled below the blurb - Nick, 2026-07-29): both anchored to the card's middle,
        // name resting its baseline just above the midline, blurb just below it.
        float alpha = interactable ? 1f : 0.45f;
        CreateTmp(card, "Name", boost.DisplayName.ToUpperInvariant(), 25, WithAlpha(TextPrimary, alpha),
            TextAnchor.LowerLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(32f, 20f), new Vector2(width - 280f, 32f), new Vector2(0f, 0.5f));
        CreateTmp(card, "Blurb", boost.Blurb, 18, WithAlpha(TextMuted, alpha),
            TextAnchor.MiddleLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont,
            new Vector2(32f, -16f), new Vector2(width - 280f, 24f), new Vector2(0f, 0.5f));

        if (selected)
        {
            // The check badge is the selected state's anchor - colour alone is not enough at a
            // glance (and never on a colour-blind player's screen).
            Image badge = CreateImage(card, "CheckBadge", MenuSprites.CircleBadge(GoldBase, GoldBase), Color.white);
            SetCenteredAt(badge.rectTransform, new Vector2(1f, 0.5f), new Vector2(-64f, 14f), new Vector2(52f, 52f));
            badge.raycastTarget = false;
            Image check = CreateImage(badge.transform, "Check",
                MenuSprites.CheckMark(new Color(0.10f, 0.08f, 0.03f, 1f)), Color.white);
            check.preserveAspect = true;
            SetCenteredAt(check.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
            check.raycastTarget = false;
            CreateTmp(card, "Tag", "EQUIPPED", 14, GoldBase, TextAnchor.MiddleCenter,
                FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(-64f, -32f), new Vector2(140f, 20f), new Vector2(1f, 0.5f));
        }
        else
        {
            CreateCoinAmount(card, boost.Price, 22,
                affordable ? WithAlpha(TextPrimary, alpha) : WithAlpha(LockedColor, 0.8f), -32f);
            if (slotsFull)
            {
                CreateTmp(card, "Full", "SLOTS FULL", 13, WithAlpha(LockedColor, 0.85f),
                    TextAnchor.MiddleRight, FontStyle.Bold, RuntimeUiKit.TitleFont,
                    new Vector2(-32f, -34f), new Vector2(160f, 18f), new Vector2(1f, 0.5f));
            }
        }

        if (!interactable) return;

        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = body;
        button.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            if (selected) ui.Selection.Boosts.Remove(boost.Id);
            else ui.Selection.Boosts.Add(boost.Id);
            RefreshSuppliesSection(ui);   // the modal's chips + TOTAL follow live
            refresh();                    // cards rebuild in place - no overlay blink
        });
    }

    /// <summary>Stretch a child rect <paramref name="pad"/> past its parent on every side -
    /// the padded-canvas pattern the neon chrome needs for its outer bloom.</summary>
    private static void StretchPadded(RectTransform rect, float pad)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-pad, -pad);
        rect.offsetMax = new Vector2(pad, pad);
    }

    // ---- the play button truth -------------------------------------------------------------

    private static void RefreshPlayButton(SuppliesUi ui)
    {
        if (ui.PlayLabel == null) return;
        if (ui.StartPending) return;   // the in-flight start_run owns the button right now

        bool boosted = ui.Selection.Boosted;
        bool offline = AttemptsService.OnlineBlocked;
        bool canStart = !offline && AttemptsService.CanStartRun;

        // Plain "PLAY" for a clean run (labelling it CLEAN read as noise - Nick); the boosted
        // state keeps its word + gold edge, that's the honesty tag. OFFLINE outranks the
        // meter: without the server there is no grant to be had (BACKEND.md §5.1).
        ui.PlayLabel.text = offline ? "OFFLINE" : !canStart ? "OUT OF ATTEMPTS" : boosted ? "PLAY - BOOSTED" : "PLAY";
        ui.PlayLabel.fontSize = boosted || !canStart ? 27f : 36f;
        if (ui.PlayOutline != null) UnityEngine.Object.Destroy(ui.PlayOutline);
        ui.PlayOutline = boosted && canStart
            ? RuntimeUiKit.AddOutline(ui.PlayBg.rectTransform, WithAlpha(GoldBase, 0.95f)).gameObject
            : null;
        ui.PlayBg.color = canStart ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f);
        if (ui.PlayButton != null) ui.PlayButton.interactable = canStart;
    }
}
