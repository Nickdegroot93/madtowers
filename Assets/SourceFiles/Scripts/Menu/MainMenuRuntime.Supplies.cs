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
        public float SectionTop;            // y of the section inside the panel
        public float ContentW;
        public float Pad;
    }

    private const float SupplyRowH = 104f;      // the two card rows - full-width tap targets
    private const float SupplyRowGap = 14f;
    private const float SupplyStatusH = 88f;    // total line / out-of-attempts row
    /// <summary>Extra modal height the section needs (LevelSummary adds this to H).</summary>
    private const float SuppliesSectionHeight = 2f * SupplyRowH + 2f * SupplyRowGap + SupplyStatusH + 26f;

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
        BuildStatusRow(ui, section, wallet, -26f - 2f * (SupplyRowH + SupplyRowGap));
        RefreshPlayButton(ui);
    }

    // ---- RUN LIVES: hearts + one big +/- stepper ----------------------------------------------

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

    // ---- BOOSTS: one big row, the tray does the choosing ----------------------------------------

    private static void BuildBoostsRow(SuppliesUi ui, RectTransform section, float y)
    {
        RectTransform row = CreateSupplyCard(section, "BoostsRow", y, ui.ContentW);

        CreateTmp(row, "Label", "BOOSTS", 21, TextPrimary, TextAnchor.UpperLeft,
            FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(24f, -20f), new Vector2(300f, 28f), new Vector2(0f, 1f));

        string picked;
        if (ui.Selection.Boosts.Count == 0)
        {
            picked = "None picked.";
        }
        else
        {
            var names = new List<string>();
            for (int i = 0; i < ui.Selection.Boosts.Count; i++)
            {
                SupplyCatalog.BoostInfo info = SupplyCatalog.Info(ui.Selection.Boosts[i]);
                if (info != null) names.Add(info.DisplayName);
            }
            picked = string.Join("  +  ", names);
        }
        CreateTmp(row, "Picked", picked, 15,
            ui.Selection.Boosts.Count > 0 ? WithAlpha(GoldBase, 0.9f) : WithAlpha(TextMuted, 0.9f),
            TextAnchor.UpperLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont,
            new Vector2(24f, -52f), new Vector2(ui.ContentW - 280f, 22f), new Vector2(0f, 1f));

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

    private static void BuildStatusRow(SuppliesUi ui, RectTransform section, int wallet, float y)
    {
        RectTransform zone = CreateRect(section, "Status",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, y), new Vector2(ui.ContentW, SupplyStatusH));

        // The meter only speaks up when it actually blocks play (its home is the top bar).
        // (The Hour Pass buy that used to sit here was cut by Nick 2026-07-20 - waiting or
        // the rewarded ad, once ads ship, are the only refills.)
        if (AttemptsService.MeterActive && !AttemptsService.CanStartRun)
        {
            TimeSpan regen = AttemptsService.NextRegenIn;
            CreateTmp(zone, "Blocked", $"OUT OF ATTEMPTS - NEXT IN {(int)regen.TotalMinutes:00}:{regen.Seconds:00}",
                18, LockedColor, TextAnchor.MiddleLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(4f, 0f), new Vector2(ui.ContentW - 8f, 30f), new Vector2(0f, 0.5f));
            return;
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

    // ---- the boost tray -------------------------------------------------------------------------

    private static void OpenBoostTray(SuppliesUi ui)
    {
        if (ui.TrayOverlay != null) return;

        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Boost Tray", 5600);
        ui.TrayOverlay = overlay;
        void CloseTray()
        {
            // Only release the state slot if WE still own it: the equip-reopen pattern
            // replaces the tray, and a click on the dying tray's backdrop (alive for one
            // deferred-destroy frame) must not orphan its successor.
            if (ui.TrayOverlay == overlay) ui.TrayOverlay = null;
            UnityEngine.Object.Destroy(overlay);
        }

        Image backdrop = CreateImage(overlay.transform, "Backdrop", null, new Color(0f, 0f, 0f, 0.55f));
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;
        Button backdropButton = backdrop.gameObject.AddComponent<Button>();
        backdropButton.transition = Selectable.Transition.None;
        backdropButton.onClick.AddListener(CloseTray);

        // Which boosts exist for THIS level (irrelevant ones aren't shown at all, SHOP.md §3.2).
        var relevant = new List<SupplyCatalog.BoostInfo>();
        for (int i = 0; i < SupplyCatalog.Boosts.Count; i++)
        {
            if (SupplyCatalog.IsRelevant(SupplyCatalog.Boosts[i], ui.Level)) relevant.Add(SupplyCatalog.Boosts[i]);
        }

        const float W = 880f;
        const float rowH = 108f;
        const float headH = 92f;
        float H = headH + 28f + relevant.Count * (rowH + 12f);

        RectTransform sheet = CreateRect(overlay.transform, "Sheet",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 24f), new Vector2(W, H));
        Image sheetImage = sheet.gameObject.AddComponent<Image>();
        sheetImage.sprite = RuntimeSprites.RoundedPanel();
        sheetImage.type = Image.Type.Sliced;
        sheetImage.color = new Color(0.075f, 0.065f, 0.058f, 1f);
        sheetImage.raycastTarget = true;
        RuntimeUiKit.AddOutline(sheet, GoldOutline(0.22f));

        TextMeshProUGUI title = CreateTmp(sheet, "Title", "BOOSTS", 30, TextPrimary,
            TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(40f, -26f), new Vector2(400f, 40f), new Vector2(0f, 1f));
        title.characterSpacing = 4f;
        CreateTmp(sheet, "Sub", $"PICK UP TO {SupplyCatalog.MaxBoostsPerRun} - THIS RUN ONLY", 15,
            WithAlpha(TextMuted, 0.85f), TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(40f, -64f), new Vector2(600f, 20f), new Vector2(0f, 1f));

        Image closeBg = CreateImage(sheet, "Close", MenuSprites.CircleBadge(
            new Color(0.03f, 0.03f, 0.04f, 0.55f), new Color(0.03f, 0.03f, 0.04f, 0.55f)), Color.white);
        SetRect(closeBg.rectTransform, new Vector2(-20f, -20f), new Vector2(64f, 64f), new Vector2(1f, 1f));
        closeBg.raycastTarget = true;
        CreateTmp(closeBg.transform, "X", "X", 26, TextPrimary, TextAnchor.MiddleCenter,
            FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button closeButton = closeBg.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeBg;
        closeButton.onClick.AddListener(CloseTray);

        int wallet = PlayerProfileStore.Coins;
        for (int i = 0; i < relevant.Count; i++)
        {
            BuildBoostTrayRow(ui, sheet, relevant[i], wallet,
                new Vector2(28f, -(headH + 16f) - i * (rowH + 12f)), W - 56f, rowH,
                () => { CloseTray(); OpenBoostTray(ui); });
        }
    }

    private static void BuildBoostTrayRow(SuppliesUi ui, RectTransform sheet, SupplyCatalog.BoostInfo boost,
        int wallet, Vector2 position, float width, float height, Action reopen)
    {
        bool selected = ui.Selection.Boosts.Contains(boost.Id);
        bool slotsFull = !selected && ui.Selection.Boosts.Count >= SupplyCatalog.MaxBoostsPerRun;
        bool affordable = selected || ui.Selection.Total() + boost.Price <= wallet;
        bool interactable = selected || (!slotsFull && affordable);

        Image card = CreateImage(sheet, $"Boost{boost.Id}", RuntimeSprites.RoundedPanel(),
            selected ? new Color(0.15f, 0.12f, 0.08f, 1f) : new Color(0.10f, 0.09f, 0.08f, 1f));
        card.type = Image.Type.Sliced;
        SetRect(card.rectTransform, position, new Vector2(width, height), new Vector2(0f, 1f));
        card.raycastTarget = true;
        RuntimeUiKit.AddOutline(card.rectTransform,
            selected ? WithAlpha(GoldBase, 0.85f) : WithAlpha(TextMuted, interactable ? 0.35f : 0.15f));

        float alpha = interactable ? 1f : 0.45f;
        CreateTmp(card.transform, "Name", boost.DisplayName, 22, WithAlpha(TextPrimary, alpha),
            TextAnchor.UpperLeft, FontStyle.Bold, RuntimeUiKit.TitleFont,
            new Vector2(28f, -18f), new Vector2(width - 260f, 28f), new Vector2(0f, 1f));
        CreateTmp(card.transform, "Blurb", boost.Blurb, 17, WithAlpha(TextMuted, alpha),
            TextAnchor.UpperLeft, FontStyle.Normal, RuntimeUiKit.DefaultFont,
            new Vector2(28f, -52f), new Vector2(width - 260f, 40f), new Vector2(0f, 1f));

        if (selected)
        {
            CreateTmp(card.transform, "Tag", "EQUIPPED", 20, GoldBase, TextAnchor.MiddleRight,
                FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(width - 220f - 28f, 0f), new Vector2(220f, 30f), new Vector2(0f, 0.5f));
        }
        else
        {
            CreateCoinAmount(card.transform, boost.Price, 20, WithAlpha(TextPrimary, alpha), -28f);
        }

        if (slotsFull && !selected)
        {
            CreateTmp(card.transform, "Full", "MAX 2", 13, WithAlpha(LockedColor, 0.8f),
                TextAnchor.LowerRight, FontStyle.Bold, RuntimeUiKit.TitleFont,
                new Vector2(width - 220f - 28f, 10f), new Vector2(220f, 16f), new Vector2(0f, 0f));
        }

        if (!interactable) return;

        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = card;
        button.onClick.AddListener(() =>
        {
            SfxPlayer.Play("ui-button-click");
            if (selected) ui.Selection.Boosts.Remove(boost.Id);
            else ui.Selection.Boosts.Add(boost.Id);
            RefreshSuppliesSection(ui);
            reopen(); // rebuild the tray in place so tags/affordability update
        });
    }

    // ---- the play button truth -------------------------------------------------------------

    private static void RefreshPlayButton(SuppliesUi ui)
    {
        if (ui.PlayLabel == null) return;

        bool boosted = ui.Selection.Boosted;
        bool canStart = AttemptsService.CanStartRun;

        // Plain "PLAY" for a clean run (labelling it CLEAN read as noise - Nick); the boosted
        // state keeps its word + gold edge, that's the honesty tag.
        ui.PlayLabel.text = !canStart ? "OUT OF ATTEMPTS" : boosted ? "PLAY - BOOSTED" : "PLAY";
        ui.PlayLabel.fontSize = boosted || !canStart ? 27f : 36f;
        if (ui.PlayOutline != null) UnityEngine.Object.Destroy(ui.PlayOutline);
        ui.PlayOutline = boosted && canStart
            ? RuntimeUiKit.AddOutline(ui.PlayBg.rectTransform, WithAlpha(GoldBase, 0.95f)).gameObject
            : null;
        ui.PlayBg.color = canStart ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f);
        if (ui.PlayButton != null) ui.PlayButton.interactable = canStart;
    }
}
