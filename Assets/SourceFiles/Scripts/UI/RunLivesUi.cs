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

    /// <summary>One centered, self-ticking status line: "LIVES 3/5 - NEXT IN 07:19".
    /// Runs on unscaled time (pause and game-over both freeze timeScale). Returns null
    /// when the row does not apply.</summary>
    public static GameObject BuildStatusRow(Transform parent, float height = 34f)
    {
        if (!Applies) return null;

        TextMeshProUGUI row = RuntimeUiKit.CreateTmp(parent, "LivesStatus", StatusText(), 24,
            new Color(1f, 1f, 1f, 0.65f), TextAnchor.MiddleCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont);
        row.characterSpacing = 3f;
        LayoutElement layout = row.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        row.gameObject.AddComponent<Ticker>().Label = row;
        return row.gameObject;
    }

    /// <summary>The confirm-sheet cost line for a restart: explicit about the price and
    /// the balance, because "your current run will be lost" alone hid the real cost.</summary>
    public static string RestartCostText() =>
        !Applies
            ? "Your current run will be lost."
            : $"Your current run will be lost.\nRestarting costs a life - you have {AttemptsService.Count} of {AttemptsService.MaxAttempts} left.";

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
                $"Unlimited Lives - {PremiumStore.PriceText}", 88f, null);
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
                    if (buyLabel != null) buyLabel.text = $"Unlimited Lives - {PremiumStore.PriceText}";
                    buy.interactable = true;
                });
            });
            added++;
        }

        if (AttemptsService.AdRefillAvailable)
        {
            Button watch = RuntimeUiKit.CreateButton(parent, "Watch Ad  +2 Lives", 88f, null);
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
                        if (watchLabel != null) watchLabel.text = "Watch Ad  +2 Lives";
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

    private static string StatusText()
    {
        int count = AttemptsService.Count;
        int max = AttemptsService.MaxAttempts;
        if (count >= max) return $"LIVES  {count}/{max}";
        TimeSpan regen = AttemptsService.NextRegenIn;
        return $"LIVES  {count}/{max}   -   NEXT IN {(int)regen.TotalMinutes:00}:{regen.Seconds:00}";
    }

    /// <summary>Keeps the status row honest while the screen sits open: regen ticks, ad
    /// grants land, and the countdown moves. Unscaled - these screens pause the game.</summary>
    private sealed class Ticker : MonoBehaviour
    {
        public TextMeshProUGUI Label;
        private float _next;

        private void Update()
        {
            if (Label == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            Label.text = StatusText();
        }
    }
}
