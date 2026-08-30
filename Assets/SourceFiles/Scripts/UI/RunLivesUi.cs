using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-game lives UI shared by the pause menu and the game-over screen (Nick 2026-08-09):
/// a player deciding whether to restart must SEE what it costs and what they hold - the
/// meter lives in the menu's top bar, which is invisible mid-run, so restarts felt free
/// and running dry felt like a random bounce to the menu.
///
/// Everything here is non-premium-only by construction: premium has no meter, so every
/// entry point no-ops for it (callers don't need to check).
/// </summary>
public static class RunLivesUi
{
    /// <summary>Should any of this render at all? Premium players and meterless states
    /// (soft landing, offline fallback) see nothing.</summary>
    public static bool Applies =>
        !PremiumStore.IsPremium && AttemptsService.MeterActive;

    public static bool OutOfLives => Applies && AttemptsService.Count <= 0;

    /// <summary>One centered, self-ticking attempts meter: a flag pip per attempt slot
    /// (held = full color, spent = dimmed) plus a regen-countdown chip while below the cap.
    /// Never "LIVES 3/5" text (Nick 2026-08-30) - the summit flag is the attempts glyph,
    /// the heart stays the RUN-lives glyph (AttemptSprites). Runs on unscaled time (pause
    /// and game-over both freeze timeScale). Returns null when the row does not apply.</summary>
    public static GameObject BuildStatusRow(Transform parent, float height = 52f)
    {
        if (!Applies) return null;

        var row = new GameObject("AttemptsRow", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        // Both, because the two host panels disagree: the pause/game-over panels'
        // VerticalLayoutGroup has childControlHeight=false and reads the RECT height
        // (a fresh RectTransform defaults to 100 - the buttons-off-the-panel bug,
        // Nick 2026-08-30), while a childControlHeight=true host needs the LayoutElement.
        ((RectTransform)row.transform).sizeDelta = new Vector2(0f, height);
        LayoutElement layout = row.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        HorizontalLayoutGroup group = row.AddComponent<HorizontalLayoutGroup>();
        group.spacing = 10f;
        group.childAlignment = TextAnchor.MiddleCenter;
        group.childControlWidth = false;
        group.childControlHeight = false;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;

        Sprite flag = AttemptSprites.Flag();
        int max = AttemptsService.MaxAttempts;
        var pips = new Image[max];
        for (int i = 0; i < max; i++)
        {
            var pipObject = new GameObject($"Pip{i}", typeof(RectTransform));
            pipObject.transform.SetParent(row.transform, false);
            ((RectTransform)pipObject.transform).sizeDelta = new Vector2(38f, 38f);
            Image pip = pipObject.AddComponent<Image>();
            pip.sprite = flag;
            pip.preserveAspect = true;
            pip.raycastTarget = false;
            pips[i] = pip;
        }

        // The regen chip: a dark pill with a green "alive" dot and the mm:ss countdown.
        var chipObject = new GameObject("RegenChip", typeof(RectTransform));
        chipObject.transform.SetParent(row.transform, false);
        ((RectTransform)chipObject.transform).sizeDelta = new Vector2(122f, 40f);
        Image chip = chipObject.AddComponent<Image>();
        chip.sprite = RuntimeSprites.RoundedPanel();
        chip.type = Image.Type.Sliced;
        chip.pixelsPerUnitMultiplier = 3f;
        chip.color = new Color(1f, 1f, 1f, 0.08f);
        chip.raycastTarget = false;

        Color green = new Color(0.35f, 0.85f, 0.5f, 1f);
        Image dot = RuntimeUiKit.CreateImage(chipObject.transform, "Dot",
            MenuSprites.CircleBadge(green, green), Color.white);
        dot.raycastTarget = false;
        RectTransform dotRect = dot.rectTransform;
        dotRect.anchorMin = dotRect.anchorMax = new Vector2(0f, 0.5f);
        dotRect.pivot = new Vector2(0f, 0.5f);
        dotRect.anchoredPosition = new Vector2(16f, 0f);
        dotRect.sizeDelta = new Vector2(12f, 12f);

        TextMeshProUGUI countdown = RuntimeUiKit.CreateTmp(chipObject.transform, "Countdown",
            "", 21, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont);
        RectTransform countdownRect = countdown.rectTransform;
        countdownRect.anchorMin = Vector2.zero;
        countdownRect.anchorMax = Vector2.one;
        countdownRect.offsetMin = new Vector2(26f, 0f);
        countdownRect.offsetMax = Vector2.zero;

        Ticker ticker = row.AddComponent<Ticker>();
        ticker.Pips = pips;
        ticker.Chip = chipObject;
        ticker.Countdown = countdown;
        ticker.Refresh();
        return row;
    }

    /// <summary>The confirm-sheet cost line for a restart: explicit about the price -
    /// the BALANCE is the meter row's job (BuildStatusRow sits under this line in the
    /// confirm sheet; "you have 2 of 5 left" in prose is exactly what the icon row
    /// replaced, Nick 2026-08-30).</summary>
    public static string RestartCostText() =>
        !Applies
            ? "Your current run will be lost."
            : "Your current run will be lost.\nRestarting costs an attempt.";

    /// <summary>
    /// The out-of-lives choices, in pitch order (SHOP.md: premium first, then the ad,
    /// menu last and quiet). Buttons are appended to <paramref name="parent"/> using the
    /// in-game style kit. <paramref name="onLivesGained"/> fires after a successful ad
    /// refill so the caller can rebuild itself with restart available again.
    /// Returns how many ACTION buttons it added (0 = neither ad nor store available).
    /// </summary>
    public static int BuildOutOfLivesActions(Transform parent, Action onLivesGained)
    {
        int added = 0;

        // Premium pitch first - but only when genuinely purchasable. A dead COMING SOON
        // button in a game-over screen is friction pretending to be a pitch; until the
        // store provider ships (GOLIVE §3), the ad carries the rescue alone.
        if (PremiumStore.Available && !PremiumStore.IsPremium)
        {
            Button buy = RuntimeUiKit.CreateButton(parent,
                $"Unlimited Attempts - {PremiumStore.PriceText}", 88f, null);
            GameMenuStyle.StyleButton(buy, primary: true);
            TextMeshProUGUI buyLabel = buy.GetComponentInChildren<TextMeshProUGUI>();
            buy.onClick.AddListener(() =>
            {
                SfxPlayer.Play("ui-button-click");
                buy.interactable = false;
                if (buyLabel != null) buyLabel.text = "Contacting store...";
                PremiumStore.Purchase(result =>
                {
                    // Success flows through PremiumStore.Changed -> the caller's rebuild
                    // renders the premium state (no meter, plain Try Again).
                    if (result == PremiumStoreResult.Purchased || result == PremiumStoreResult.Restored)
                    {
                        onLivesGained?.Invoke();
                        return;
                    }
                    // The panel can be rebuilt under a slow store sheet (the results card's
                    // heal watcher destroys and re-shows it) - a destroyed Button throws on
                    // the interactable setter (review 2026-08-11).
                    if (buy == null) return;
                    if (buyLabel != null) buyLabel.text = $"Unlimited Attempts - {PremiumStore.PriceText}";
                    buy.interactable = true;
                });
            });
            added++;
        }

        if (AttemptsService.AdRefillAvailable)
        {
            Button watch = RuntimeUiKit.CreateButton(parent, "Watch Ad  +2 Attempts", 88f, null);
            GameMenuStyle.StyleButton(watch, primary: added == 0); // primary unless the buy is
            TextMeshProUGUI watchLabel = watch.GetComponentInChildren<TextMeshProUGUI>();
            watch.onClick.AddListener(() =>
            {
                SfxPlayer.Play("ui-button-click");
                watch.interactable = false;
                if (watchLabel != null) watchLabel.text = "Loading ad...";
                RewardedAds.Show(earned =>
                {
                    if (!earned)
                    {
                        // Skipped or failed: restore the offer, nothing was spent. The
                        // button may be GONE by now - a regen life can land during the ad
                        // and the caller's heal watcher rebuilds the panel; a destroyed
                        // Button throws on the interactable setter (review 2026-08-11).
                        if (watch == null) return;
                        if (watchLabel != null) watchLabel.text = "Watch Ad  +2 Attempts";
                        watch.interactable = true;
                        return;
                    }
                    if (watchLabel != null) watchLabel.text = "Claiming...";
                    AttemptsService.RequestAdRefill(ok =>
                    {
                        // Even a false verdict may be a slow SSV grant; the meter refresh
                        // will carry it. Rebuild either way - the status row tells the truth.
                        onLivesGained?.Invoke();
                    });
                });
            });
            added++;
        }

        return added;
    }

    /// <summary>Keeps the meter row honest while the screen sits open: regen ticks, ad
    /// grants land, and the countdown moves. Unscaled - these screens pause the game.</summary>
    private sealed class Ticker : MonoBehaviour
    {
        public Image[] Pips;
        public GameObject Chip;
        public TextMeshProUGUI Countdown;
        private float _next;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            Refresh();
        }

        public void Refresh()
        {
            int count = AttemptsService.Count;
            if (Pips != null)
            {
                for (int i = 0; i < Pips.Length; i++)
                {
                    if (Pips[i] != null) Pips[i].color = i < count ? Color.white : AttemptSprites.EmptyTint;
                }
            }

            bool regenRuns = count < AttemptsService.MaxAttempts;
            if (Chip != null && Chip.activeSelf != regenRuns) Chip.SetActive(regenRuns);
            if (regenRuns && Countdown != null)
            {
                TimeSpan regen = AttemptsService.NextRegenIn;
                Countdown.text = $"{(int)regen.TotalMinutes:00}:{regen.Seconds:00}";
            }
        }
    }
}
