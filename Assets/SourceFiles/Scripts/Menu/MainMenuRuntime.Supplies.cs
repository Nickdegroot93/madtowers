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
        public int Lives;                            // purchased pips, 0..(MaxLives - FreeLives)
        public int FreeLives;                        // pips the run already starts with (see FreeLives())
        public readonly List<BoostId> Boosts = new();  // ≤ SupplyCatalog.MaxBoostsPerRun

        public bool Boosted => Lives > 0 || Boosts.Count > 0;

        public int Total()
        {
            int total = SupplyCatalog.PriceForLives(Lives, FreeLives);
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
        public Color Accent;       // chapter accent - ALL supplies chrome; gold stays currency-only (Nick 2026-08-29)
        public RectTransform Panel;         // the modal panel (section parent)
        public GameObject SectionRoot;      // destroyed + rebuilt on every change
        public GameObject TrayOverlay;      // non-null while the boost tray is open
        public Image PlayBg;
        public TextMeshProUGUI PlayLabel;
        public GameObject PlayOutline;      // accent edge, boosted only
        public Button PlayButton;
        public bool StartPending;           // a start_run grant is in flight - freeze the button
        public float SectionTop;            // y of the section inside the panel
        public float ContentW;
        public float Pad;
    }

    private const float SupplyRowH = 104f;      // the two card rows - full-width tap targets
    private const float SupplyRowGap = 14f;
    private const float SupplyStatusH = 88f;    // total line / out-of-attempts row
    /// <summary>Lives the player already holds for free at run start: the config's authored
    /// StartingLives floored by the game type's granted lives - EXACTLY GameManager.
    /// ApplyConfig's seeding rule, so the row can never sell a pip the in-run cap would
    /// swallow (RunState.AddLife silently no-ops at 3: charged coins, nothing gained).</summary>
    private static int FreeLives(LevelDefinition level)
    {
        if (level == null) return 0;
        int authored = level.GameModeConfig != null ? level.GameModeConfig.StartingLives : 0;
        return Mathf.Clamp(Mathf.Max(authored, level.GrantedRunLives), 0, RunState.MaxLives);
    }

    /// <summary>Does this level's supplies section SELL run lives? Free lives pre-fill the
    /// cheap pip slots and the stepper sells only the remainder; a mode starting at the cap
    /// (the Flood grants all 3) has no remainder, so the row renders as the INCLUDED
    /// acknowledgment instead (BuildGrantedLivesRow): the player must learn the free hearts
    /// HERE, or the in-run pips read as a bug on a level that never sold any.</summary>
    private static bool SellsLives(LevelDefinition level)
        => FreeLives(level) < RunState.MaxLives;

    /// <summary>Extra modal height the section needs (LevelSummary adds this to H).
    /// Always two rows: lives (stepper or INCLUDED acknowledgment) + boosts.</summary>
    private static float SuppliesSectionHeight(LevelDefinition level)
        => 2f * (SupplyRowH + SupplyRowGap) + SupplyStatusH + 26f;

    /// <summary>The level modal's full height once supplies are on - shared with the boost
    /// picker, which must be EXACTLY this tall: a shorter overlay panel lets the modal underneath
    /// peek out above and below it, which reads as broken borders (Nick, 2026-07-29).
    /// One height for tiered and untiered levels: the progress track (2026-08-29 redesign)
    /// occupies the same vertical as the classic TARGET/BEST pair.</summary>
    private static float ModalHeightWithSupplies(LevelDefinition level)
        => 768f + SuppliesSectionHeight(level);

    private static string CoinText(int amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

    // ---- the section -------------------------------------------------------------------------

    private static SuppliesUi BuildSuppliesSection(RectTransform panel, LevelDefinition level,
        Color accent, float pad, float contentW, float sectionTop)
    {
        var ui = new SuppliesUi
        {
            Level = level,
            Accent = accent,
            Selection = new SupplySelection { FreeLives = FreeLives(level) },
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
            new Vector2(ui.Pad, ui.SectionTop), new Vector2(ui.ContentW, SuppliesSectionHeight(ui.Level)));
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

        // A mode that grants its lives for free (the Flood: all 3) gets the acknowledgment
        // row instead of the stepper - same slot, nothing to buy.
        float rowY = -26f;
        if (SellsLives(ui.Level)) BuildLivesRow(ui, section, wallet, rowY);
        else BuildGrantedLivesRow(ui, section, rowY);
        rowY -= SupplyRowH + SupplyRowGap;
        BuildBoostsRow(ui, section, rowY);
        TextMeshProUGUI countdown = BuildStatusRow(ui, section, wallet, rowY - (SupplyRowH + SupplyRowGap));
        RefreshPlayButton(ui);

        // Keeps the section honest between taps: ticks the out-of-attempts countdown once a
        // second (the old build-time snapshot froze until the next interaction) and rebuilds
        // when the online state or the server meter changes. Dies with the section.
        SuppliesLive live = section.gameObject.AddComponent<SuppliesLive>();
        live.Ui = ui;
        live.Countdown = countdown;
        // What the row just rendered with, so the 1s tick can rebuild when the ad
        // arrives later (live fill takes seconds; the provider raises no event).
        live.RecordRenderedAdOffer(AttemptsService.AdRefillAvailable);
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
        // What the row rendered with. The WATCH AD button appears only if an ad was in
        // hand at render time - but at boot the row often builds while consent + the
        // first live-fill load are still in flight (seconds on device), and nothing
        // event-driven fires when the ad lands: the provider's IsReady flip raises no
        // event. Without this, a player who launched out of attempts stared at a
        // countdown-only row for its whole life (review 2026-08-09). The top-bar "+"
        // already self-heals via its own 1s tick; this is the same medicine here.
        private bool _renderedAdOffer;

        public void RecordRenderedAdOffer(bool adOffer) => _renderedAdOffer = adOffer;

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
            if (AttemptsService.AdRefillAvailable != _renderedAdOffer)
            {
                // The ad finished loading (or was consumed) after the row rendered -
                // rebuild so WATCH AD appears/disappears with reality.
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
        int free = ui.Selection.FreeLives;   // pre-filled pips (authored/type-granted, SHOP.md §3.1)

        // Title top-left, heart pips UNDER it on the left (redesign, Nick 2026-08-29 - the old
        // centered pips read as floating; the MAX-3 blurb was cut as overkill).
        BuildLivesTitle(row);
        BuildLivesPips(row, free + ui.Selection.Lives);

        // The stepper: [-] appears once something is picked; [+] carries the NEXT pip's price.
        if (ui.Selection.Lives > 0)
        {
            Button minus = CreateSupplyButton(row, "Minus", "-", 84f,
                new Vector2(-256f, 0f), enabled: true, accented: false, ui.Accent);
            minus.onClick.AddListener(() =>
            {
                SfxPlayer.Play("ui-button-click");
                ui.Selection.Lives--;
                RefreshSuppliesSection(ui);
            });
        }

        if (free + ui.Selection.Lives < RunState.MaxLives)
        {
            // Absolute slot pricing: free lives sit in the cheap slots, purchases pay the
            // dearer remainder (a 2-free level sells only the 90-coin third pip).
            int price = SupplyCatalog.LifePipPrices[free + ui.Selection.Lives];
            bool affordable = ui.Selection.Total() + price <= wallet;
            Button plus = CreateSupplyButton(row, "Plus", "+ LIFE", 220f,
                new Vector2(-24f, 0f), affordable, accented: true, ui.Accent, price: price);
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

    /// <summary>The lives row for a level whose free lives already fill the cap (the Flood
    /// grants all 3, so there is nothing to sell): same card, same label and pip positions
    /// as the stepper row so it reads as the familiar row answered, not a different feature -
    /// full pips, a gold INCLUDED tag where the buy button sits, and the blurb says who is
    /// paying (nobody).</summary>
    private static void BuildGrantedLivesRow(SuppliesUi ui, RectTransform section, float y)
    {
        RectTransform row = CreateSupplyCard(section, "LivesRow", y, ui.ContentW);
        BuildLivesTitle(row);
        BuildLivesPips(row, ui.Selection.FreeLives);

        TextMeshProUGUI tag = CreateTmp(row, "Included", "INCLUDED", 18, ui.Accent,
            TextAnchor.MiddleRight, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(-24f, 0f), new Vector2(200f, 30f), new Vector2(1f, 0.5f));
        tag.characterSpacing = 2f;
    }

    // The lives row's shared left column: "RUN LIVES" top-left, heart pips UNDER the title
    // (left-aligned; the old center-of-row pips read as floating, and the MAX-3 blurb was cut
    // as overkill - Nick 2026-08-29). Full/empty are the two-state heart art; while the
    // dedicated empty socket asset is pending (HeartSprites), an unfilled pip is the full art
    // dimmed.
    private static void BuildLivesTitle(RectTransform row)
    {
        CreateTmp(row, "Label", "RUN LIVES", 21, TextPrimary, TextAnchor.UpperLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(24f, -16f), new Vector2(220f, 28f), new Vector2(0f, 1f));
    }

    private static void BuildLivesPips(RectTransform row, int filledCount)
    {
        for (int i = 0; i < RunState.MaxLives; i++)
        {
            bool filled = i < filledCount;
            Sprite pipSprite = filled ? HeartSprites.Full() : HeartSprites.Empty();
            Color pipColor = filled || HeartSprites.HasDedicatedEmpty
                ? Color.white : new Color(1f, 1f, 1f, 0.16f);
            Image pip = CreateImage(row, $"Pip{i}", pipSprite, pipColor);
            pip.preserveAspect = true;
            SetRect(pip.rectTransform, new Vector2(24f + i * 48f, 12f), new Vector2(40f, 40f), new Vector2(0f, 0f));
        }
    }

    // ---- BOOSTS: one big row, the picker does the choosing --------------------------------------

    private static void BuildBoostsRow(SuppliesUi ui, RectTransform section, float y)
    {
        RectTransform row = CreateSupplyCard(section, "BoostsRow", y, ui.ContentW);

        CreateTmp(row, "Label", "BOOSTS", 21, TextPrimary, TextAnchor.UpperLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(24f, -16f), new Vector2(300f, 28f), new Vector2(0f, 1f));

        // Picked boosts render as their ICONS under the title (redesign, Nick 2026-08-29) -
        // equipment looks like equipment, and the icons echo the picker cards.
        if (ui.Selection.Boosts.Count == 0)
        {
            CreateTmp(row, "Picked", "NONE PICKED", 15, WithAlpha(TextMuted, 0.9f),
                TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(24f, -54f), new Vector2(ui.ContentW - 300f, 24f), new Vector2(0f, 1f));
        }
        else
        {
            for (int i = 0; i < ui.Selection.Boosts.Count; i++)
            {
                CreateBoostIconAt(row, ui.Selection.Boosts[i], ui.Accent,
                    new Vector2(24f + i * 50f, 12f), 42f, new Vector2(0f, 0f));
            }
        }

        Button choose = CreateSupplyButton(row, "Choose",
            ui.Selection.Boosts.Count > 0 ? "CHANGE" : "CHOOSE", 220f,
            new Vector2(-24f, 0f), enabled: true, accented: ui.Selection.Boosts.Count == 0, ui.Accent);
        choose.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            OpenBoostTray(ui);
        });
    }

    // ---- boost icon art -----------------------------------------------------------------------
    // Nick's rendered icons live at Resources/Menu/boost_<enum-name-lowercase>.png (e.g.
    // boost_slowdescent.png) and are picked up with NO code change; until one lands, the
    // placeholder is a gold-ring monogram badge. Cache includes misses - a menu session
    // doesn't retry Resources.Load per rebuild (a domain reload clears it).
    private static readonly Dictionary<BoostId, Sprite> BoostIconCache = new Dictionary<BoostId, Sprite>();

    private static Sprite BoostIconArt(BoostId id)
    {
        if (!BoostIconCache.TryGetValue(id, out Sprite sprite))
        {
            sprite = Resources.Load<Sprite>("Menu/boost_" + id.ToString().ToLowerInvariant());
            BoostIconCache[id] = sprite;
        }
        return sprite;
    }

    private static string BoostMonogram(BoostId id) => id switch
    {
        BoostId.SlowDescent => "S",
        BoostId.ScarceHazards => "H",
        BoostId.QuickStudy => "Q",
        BoostId.StockedSloMo => "M",
        BoostId.StockedZap => "Z",
        BoostId.StockedVine => "V",
        BoostId.LowTide => "L",
        BoostId.VoidWard => "W",
        BoostId.PocketCache => "P",
        _ => "?",
    };

    private static void CreateBoostIconAt(Transform parent, BoostId id, Color accent, Vector2 position,
        float size, Vector2 anchor, float alpha = 1f)
    {
        Sprite art = BoostIconArt(id);
        if (art != null)
        {
            Image icon = CreateImage(parent, $"BoostIcon{id}", art, new Color(1f, 1f, 1f, alpha));
            icon.preserveAspect = true;
            SetRect(icon.rectTransform, position, new Vector2(size, size), anchor);
            return;
        }

        Image badge = CreateImage(parent, $"BoostIcon{id}", MenuSprites.CircleBadge(
            new Color(0.10f, 0.10f, 0.12f, alpha), WithAlpha(accent, 0.7f * alpha)), Color.white);
        SetRect(badge.rectTransform, position, new Vector2(size, size), anchor);
        CreateTmp(badge.transform, "Letter", BoostMonogram(id), Mathf.RoundToInt(size * 0.42f),
            WithAlpha(accent, alpha), TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
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
        // PREMIUM plays through it (offline play is what they bought, SHOP.md §7) - for them
        // the line is a warning, not a wall: the run starts, it just won't rank.
        if (AttemptsService.OnlineBlocked)
        {
            bool premium = PremiumStore.IsPremium;
            CreateTmp(zone, "Offline", premium
                    ? "OFFLINE - RUNS WON'T RANK\nON THE LEADERBOARDS"
                    : "YOU'RE OFFLINE - CONNECTION\nNEEDED FOR RANKED LEVELS",
                17, premium ? WithAlpha(TextMuted, 0.9f) : LockedColor,
                TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(4f, 0f), new Vector2(ui.ContentW - 210f, 52f), new Vector2(0f, 0.5f));
            Button retry = CreateSupplyButton(zone, "Retry", "RETRY", 170f,
                new Vector2(0f, 0f), enabled: true, accented: !premium, ui.Accent);
            retry.onClick.AddListener(() =>
            {
                SfxPlayer.Play("ui-button-click");
                OnlineService.RetryConnect();
            });
            return null;
        }

        // The meter only speaks up when it actually blocks play (its home is the top bar).
        // (The Hour Pass buy that used to sit here was cut by Nick 2026-07-20 - waiting and
        // the rewarded ad are the only refills.) The WATCH AD button is one of the two entries
        // to the game's single ad surface (the other is the top-bar meter chip's "+",
        // SHOP.md §7): opt-in, explicit "+2" copy, and only rendered when an ad can actually
        // show AND pay out (no SDK / rate-limited = the row is countdown-only).
        if (AttemptsService.MeterActive && !AttemptsService.CanStartRun)
        {
            // First time the meter ever blocks play: offer the notification permission
            // (one-time sheet; see MaybeOfferNotificationPrompt for the full rules).
            MaybeOfferNotificationPrompt();

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
                    new Vector2(0f, 0f), enabled: true, accented: true, ui.Accent);
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
            CreateTmp(zone, "TotalLabel", "TOTAL", 17, ui.Accent, TextAnchor.MiddleLeft,
                FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(x, 0f), new Vector2(80f, 30f), new Vector2(0f, 0.5f));
            CreateCoinAmountLeft(zone, total, 17, ui.Accent, x + 84f);
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
        fill.color = new Color(0.10f, 0.10f, 0.115f, 1f); // neutral - the warm brown fill read as off-palette (Nick 2026-08-29)
        RuntimeUiKit.AddOutline(row, GlassBorder);
        return row;
    }

    /// <summary>A big right-anchored button (height 80, the taste-contract minimum with room to
    /// spare). Disabled = visibly dimmed but still says its price - "can't afford" must never
    /// read as "broken". Prices render as the gold coin icon + number (the game's currency has
    /// a face, not a symbol - Nick, 2026-07-20): pass price ≥ 0 to get label-left, coin+number-
    /// right; price &lt; 0 keeps a centered label.</summary>
    private static Button CreateSupplyButton(RectTransform row, string name, string label,
        float width, Vector2 rightOffset, bool enabled, bool accented, Color accent, int price = -1)
    {
        Image bg = CreateImage(row, name, RuntimeSprites.RoundedPanel(),
            accented ? new Color(0.12f, 0.12f, 0.14f, 1f) : new Color(0.13f, 0.13f, 0.15f, 1f));
        bg.type = Image.Type.Sliced;
        // SetRect pivots at the anchor, so with anchor (1, 0.5) the offset is the button's
        // RIGHT edge relative to the row's right edge - callers pass e.g. (-24, 0).
        SetRect(bg.rectTransform, rightOffset, new Vector2(width, 80f), new Vector2(1f, 0.5f));
        bg.raycastTarget = true;
        RuntimeUiKit.AddOutline(bg.rectTransform,
            accented ? WithAlpha(accent, enabled ? 0.8f : 0.3f) : WithAlpha(TextMuted, enabled ? 0.5f : 0.2f));

        Color textColor = enabled ? (accented ? accent : TextPrimary) : WithAlpha(LockedColor, 0.8f);
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
        float H = ModalHeightWithSupplies(ui.Level);

        RectTransform panel = CreateRect(overlay.transform, "Panel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(W, H));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        GameMenuStyle.StylePanel(panel.gameObject); // the one modal-panel treatment
        panelImage.raycastTarget = true;

        TextMeshProUGUI title = CreateTmp(panel, "Title", "BOOSTS", 36, TextPrimary,
            TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(pad, -34f), new Vector2(400f, 46f), new Vector2(0f, 1f));
        title.characterSpacing = 4f;
        CreateTmp(panel, "Sub", "THIS RUN ONLY - TAP TO EQUIP", 16,
            WithAlpha(TextMuted, 0.85f), TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(pad, -82f), new Vector2(600f, 22f), new Vector2(0f, 1f));

        // The slot counter tells the player where the cap lives before they hit it.
        TextMeshProUGUI slots = CreateTmp(panel, "Slots", "", 26, ui.Accent, TextAnchor.UpperRight,
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
        // fixed frame's space when it fits; a roster that outgrows the frame SCROLLS inside a
        // masked viewport instead (the 2026-08-29 boosts took the worst case to 8 relevant
        // cards - the old 120px compression floor stopped being enough, and the 2026-08-11
        // rule stands: a card must never slide under DONE with live taps).
        float cardsBlockH = relevant.Count * (cardH + cardGap) - cardGap;
        float availableH = H - headH - (40f + doneH + 24f);
        RectTransform viewport = CreateRect(panel, "CardsViewport",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(pad, -headH), new Vector2(contentW, availableH));
        viewport.gameObject.AddComponent<RectMask2D>();
        float cardsTop = cardsBlockH < availableH ? -(availableH - cardsBlockH) * 0.5f : 0f;
        RectTransform cardsRoot = CreateRect(viewport, "Cards",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, cardsTop), new Vector2(contentW, cardsBlockH));
        if (cardsBlockH > availableH)
        {
            // A ScrollRect needs a Graphic on the viewport to catch drags; fully transparent
            // still raycasts. Buttons on the cards keep their taps (drag vs click is the
            // standard uGUI split).
            Image dragSurface = viewport.gameObject.AddComponent<Image>();
            dragSurface.color = Color.clear;
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = cardsRoot;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
        }

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
            Color.Lerp(ui.Accent, Color.white, 0.12f), Color.Lerp(ui.Accent, Color.black, 0.22f)), Color.white);
        doneBg.type = Image.Type.Sliced;
        SetRect(doneBg.rectTransform, new Vector2(pad, 40f), new Vector2(contentW, doneH), new Vector2(0f, 0f));
        doneBg.raycastTarget = true;
        TextMeshProUGUI doneLabel = CreateTmp(doneBg.transform, "Label", "DONE", 30,
            TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
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

        // Flat card with a plain hairline border (the ability-card neon ring + halo read as a
        // glow smear here - Nick 2026-08-29): neutral like the supply rows; the equipped card
        // carries the chapter accent on its border and a whisper of it in the fill.
        Color accent = ui.Accent;
        Image body = CreateImage(card, "Body", RuntimeSprites.RoundedPanel(), selected
            ? Color.Lerp(new Color(0.10f, 0.10f, 0.12f, 1f), accent, 0.10f)
            : new Color(0.10f, 0.10f, 0.115f, 1f));
        body.type = Image.Type.Sliced;
        Stretch(body.rectTransform);
        body.raycastTarget = true;
        RuntimeUiKit.AddOutline(card, selected
            ? WithAlpha(accent, 0.9f)
            : WithAlpha(GlassBorder, interactable ? 1f : 0.5f));

        // Icon left (the boost's art, monogram placeholder until it lands), then name + blurb
        // as one block, vertically centred in the card (they sat high with the slack piled
        // below the blurb - Nick, 2026-07-29).
        float alpha = interactable ? 1f : 0.45f;
        CreateBoostIconAt(card, boost.Id, ui.Accent, new Vector2(30f, 0f), 60f, new Vector2(0f, 0.5f), alpha);
        CreateTmp(card, "Name", boost.DisplayName.ToUpperInvariant(), 25, WithAlpha(TextPrimary, alpha),
            TextAnchor.LowerLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(110f, 20f), new Vector2(width - 358f, 32f), new Vector2(0f, 0.5f));
        CreateTmp(card, "Blurb", boost.Blurb, 18, WithAlpha(TextMuted, alpha),
            TextAnchor.MiddleLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont,
            new Vector2(110f, -16f), new Vector2(width - 358f, 24f), new Vector2(0f, 0.5f));

        if (selected)
        {
            // The check badge is the selected state's anchor - colour alone is not enough at a
            // glance (and never on a colour-blind player's screen).
            Image badge = CreateImage(card, "CheckBadge", MenuSprites.CircleBadge(accent, accent), Color.white);
            SetCenteredAt(badge.rectTransform, new Vector2(1f, 0.5f), new Vector2(-64f, 14f), new Vector2(52f, 52f));
            badge.raycastTarget = false;
            Image check = CreateImage(badge.transform, "Check",
                MenuSprites.CheckMark(new Color(0.06f, 0.06f, 0.08f, 1f)), Color.white);
            check.preserveAspect = true;
            SetCenteredAt(check.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
            check.raycastTarget = false;
            CreateTmp(card, "Tag", "EQUIPPED", 14, accent, TextAnchor.MiddleCenter,
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

    // ---- the play button truth -------------------------------------------------------------

    private static void RefreshPlayButton(SuppliesUi ui)
    {
        if (ui.PlayLabel == null) return;
        if (ui.StartPending) return;   // the in-flight start_run owns the button right now

        bool boosted = ui.Selection.Boosted;
        // Premium plays offline (unranked, RunGate falls back locally) - only free players
        // are walled by connectivity (BACKEND.md §5.1 / SHOP.md §7). Offline premium also
        // outranks a STALE server meter: with the link down the local run has no meter at
        // all, so a session-cached "out of attempts" verdict must not disable the button
        // the status row just promised would work (review 2026-07-30).
        bool premiumOffline = AttemptsService.OnlineBlocked && PremiumStore.IsPremium;
        bool offline = AttemptsService.OnlineBlocked && !PremiumStore.IsPremium;
        bool canStart = premiumOffline || (!offline && AttemptsService.CanStartRun);

        // Plain "PLAY" for a clean run (labelling it CLEAN read as noise - Nick); the boosted
        // state keeps its word + accent edge, that's the honesty tag (gold chrome was retired
        // from this modal, Nick 2026-08-29 - gold is currency art only). OFFLINE outranks
        // the meter: without the server there is no grant to be had (BACKEND.md §5.1).
        ui.PlayLabel.text = offline ? "OFFLINE" : !canStart ? "OUT OF ATTEMPTS" : boosted ? "PLAY - BOOSTED" : "PLAY";
        ui.PlayLabel.fontSize = boosted || !canStart ? 27f : 36f;
        if (ui.PlayOutline != null) UnityEngine.Object.Destroy(ui.PlayOutline);
        ui.PlayOutline = boosted && canStart
            ? RuntimeUiKit.AddOutline(ui.PlayBg.rectTransform, WithAlpha(Color.Lerp(ui.Accent, Color.white, 0.35f), 0.95f)).gameObject
            : null;
        ui.PlayBg.color = canStart ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f);
        if (ui.PlayButton != null) ui.PlayButton.interactable = canStart;
    }
}
