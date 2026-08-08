using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>Outcome of a store exchange. Purchase resolves to Purchased / Cancelled / Failed;
/// Restore resolves to Restored / NothingToRestore / Failed.</summary>
public enum PremiumStoreResult
{
    Purchased,
    Restored,
    NothingToRestore,
    Cancelled,
    Failed,
}

/// <summary>
/// What the real store SDK (Unity IAP v5, decided 2026-07-30 - one non-consumable,
/// "madtowers_unlimited") implements at integration time; see GOLIVE.md §3. The provider owns
/// the platform conversation (payment sheet, receipts, store restore); the game only ever
/// hears the resolved outcome. Each callback fires exactly once.
/// </summary>
public interface IPremiumStoreProvider
{
    /// <summary>Store initialized and the product is known (price fetched). False = every
    /// buy surface stays in its COMING SOON / hidden state.</summary>
    bool IsAvailable { get; }

    /// <summary>The store's localized price ("$3.99", "€3,99") - display verbatim, never
    /// hardcode currency.</summary>
    string PriceText { get; }

    void Purchase(Action<PremiumStoreResult> done);
    void Restore(Action<PremiumStoreResult> done);
}

/// <summary>
/// Facade over the premium unlock ("MadTowers Unlimited", SHOP.md §7): unlimited attempts,
/// no ads, offline play. Mirrors the RewardedAds pattern - no provider installed (today:
/// every device build) means nothing is purchasable and buy surfaces show COMING SOON; a
/// simulated store installs itself in the editor so the whole buy → owned → restore loop is
/// playtestable before Unity IAP ships.
///
/// ENTITLEMENT MODEL (BACKEND.md §6.4): the server's attempts.premium (set by receipt
/// validation once that Edge Function exists) is the online authority; the LOCAL save flag
/// is the offline entitlement cache - it is what makes airplane-mode play work, and it is
/// refreshed from the server verdict whenever one arrives. IsPremium here is the single
/// truth every surface reads.
/// </summary>
public static class PremiumStore
{
    private static IPremiumStoreProvider _provider;
    private static bool _busy;

    /// <summary>Fires when ownership flips (purchase, restore, server sync-down) - rebuild
    /// premium-dependent UI on it.</summary>
    public static event Action Changed;

    /// <summary>Install the live provider at boot (the Unity IAP adapter, once it exists).</summary>
    public static void Install(IPremiumStoreProvider provider) => _provider = provider;

    /// <summary>Does this player own Unlimited? Local cache OR the server's last word - a
    /// fresh install that signed in gets it from the server (then caches it locally via the
    /// sync-down below), a device in airplane mode gets it from the cache.</summary>
    public static bool IsPremium => ProgressStore.IsPremium || AttemptsSync.Premium;

    /// <summary>A store is connected and answering - gates surfaces that only make sense
    /// with one (the Settings RESTORE PURCHASES row, Apple-mandated once IAP ships).</summary>
    public static bool HasStore => _provider != null && _provider.IsAvailable;

    /// <summary>A purchase could be started right now (store up, nothing in flight).</summary>
    public static bool Available => HasStore && !_busy;

    /// <summary>Localized price for pitch copy; the design price only until a store answers.</summary>
    public static string PriceText => _provider != null && _provider.IsAvailable ? _provider.PriceText : "$3.99";

    public static void Purchase(Action<PremiumStoreResult> done) =>
        RunExchange(p => p.Purchase, done);

    public static void Restore(Action<PremiumStoreResult> done) =>
        RunExchange(p => p.Restore, done);

    /// <summary>Shared purchase/restore shell: exactly-once resolution enforced HERE (store
    /// SDKs are third-party code - a throw or double-fire must not wedge _busy shut or
    /// double-grant). EITHER success outcome grants: real stores answer a buy of an
    /// already-owned non-consumable with a restore, so a Purchase exchange resolving
    /// Restored is the normal already-owned path, not an error.</summary>
    private static void RunExchange(Func<IPremiumStoreProvider, Action<Action<PremiumStoreResult>>> call,
        Action<PremiumStoreResult> done)
    {
        if (_provider == null || !_provider.IsAvailable || _busy)
        {
            done?.Invoke(PremiumStoreResult.Failed);
            return;
        }
        _busy = true;
        bool finished = false;
        void Finish(PremiumStoreResult result)
        {
            if (finished) return;
            finished = true;
            _busy = false;
            if (result == PremiumStoreResult.Purchased || result == PremiumStoreResult.Restored)
                GrantEntitlement();
            done?.Invoke(result);
        }
        try
        {
            call(_provider)(Finish);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Premium] store provider threw: {e}");
            Finish(PremiumStoreResult.Failed);
        }
    }

    private static void GrantEntitlement()
    {
        // TODO(go-live, GOLIVE.md §3): also send the store receipt to the validate_receipt
        // Edge Function so the SERVER learns premium (attempts.premium) - the meter verdict
        // and cross-device state come from there. Until that function exists, only the local
        // cache flips; online meter removal follows once the server flag is set.
        if (!ProgressStore.IsPremium) ProgressStore.SetPremium(true);
        Changed?.Invoke();
    }

    /// <summary>The server said premium (sign-in on a new device, receipt validated
    /// elsewhere): cache it into the local save so offline play works from then on.
    /// Called DIRECTLY by AttemptsSync.ApplyServer - deliberately not an event
    /// subscription: same-phase RuntimeInitializeOnLoadMethod order is undefined, so a
    /// boot-time subscribe could be wiped by AttemptsSync's own reset (which nulls its
    /// event), silently losing the entitlement on a paying user's new device.</summary>
    internal static void CacheServerVerdict()
    {
        if (!AttemptsSync.Premium || ProgressStore.IsPremium) return;
        ProgressStore.SetPremium(true);
        Changed?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        _provider = null;
        _busy = false;
        Changed = null;
#if UNITY_EDITOR
        _provider = new SimulatedPremiumStoreProvider();
#endif
    }
}

#if UNITY_EDITOR
/// <summary>
/// Editor-only stand-in for the platform store: a payment-sheet overlay with BUY / CANCEL.
/// "The store remembers your purchases" is simulated with an EditorPrefs flag OUTSIDE the
/// game save, so the restore flow is honestly testable: reset the save, RESTORE PURCHASES
/// finds the old purchase - exactly the new-phone path. Clear the flag via the checkbox on
/// the sheet to play a never-bought account again.
/// </summary>
internal sealed class SimulatedPremiumStoreProvider : IPremiumStoreProvider
{
    private const string OwnedPrefKey = "MadTowers.SimulatedStorePurchased";

    public bool IsAvailable => true;
    public string PriceText => "$3.99";

    public void Purchase(Action<PremiumStoreResult> done)
    {
        BuildSheet("SIMULATED STORE - EDITOR ONLY", done, (sheet, close) =>
        {
            if (UnityEditor.EditorPrefs.GetBool(OwnedPrefKey, false))
            {
                // Real stores refuse to sell a non-consumable twice; they restore instead.
                AddButton(sheet, close, "ALREADY OWNED - RESTORE", true, -246f,
                    () => done(PremiumStoreResult.Restored));
            }
            else
            {
                AddButton(sheet, close, "BUY - $3.99", true, -246f, () =>
                {
                    UnityEditor.EditorPrefs.SetBool(OwnedPrefKey, true);
                    done(PremiumStoreResult.Purchased);
                });
            }
            AddButton(sheet, close, "CANCEL", false, -354f, () => done(PremiumStoreResult.Cancelled));
        });
    }

    public void Restore(Action<PremiumStoreResult> done)
    {
        // Store restore has no UI of its own - it just answers from purchase history.
        done(UnityEditor.EditorPrefs.GetBool(OwnedPrefKey, false)
            ? PremiumStoreResult.Restored
            : PremiumStoreResult.NothingToRestore);
    }

    // ---- the fake payment sheet ---------------------------------------------------------

    private static void BuildSheet(string subtitle, Action<PremiumStoreResult> done,
        Action<RectTransform, Action> populate)
    {
        GameObject overlay = RuntimeUiKit.CreateOverlayCanvas("Simulated Store Sheet", 9100);

        // An unanswered sheet must never wedge the facade's busy latch: if anything tears
        // the overlay down (scene load, scripted reload) before a button answered, resolve
        // as Cancelled. Finish() latches, so a normal button answer makes this a no-op.
        overlay.AddComponent<ResolveOnDestroy>().Resolve = () => done(PremiumStoreResult.Cancelled);

        Image backdrop = RuntimeUiKit.CreateImage(overlay.transform, "Backdrop", null,
            new Color(0f, 0f, 0f, 0.7f));
        RuntimeUiKit.Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true;

        Image panel = RuntimeUiKit.CreateImage(overlay.transform, "Sheet",
            RuntimeSprites.RoundedPanel(), new Color(0.09f, 0.09f, 0.11f, 1f));
        panel.type = Image.Type.Sliced;
        RuntimeUiKit.SetRect(panel.rectTransform, new Vector2(0f, 0f),
            new Vector2(700f, 470f), new Vector2(0.5f, 0.5f));
        panel.raycastTarget = true;

        RuntimeUiKit.CreateTmp(panel.transform, "Title", "HAZARD HEIGHTS UNLIMITED", 34,
            new Color(0.92f, 0.97f, 1f, 1f), TextAnchor.UpperCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(0f, -44f), new Vector2(640f, 42f),
            new Vector2(0.5f, 1f));
        RuntimeUiKit.CreateTmp(panel.transform, "Sub", subtitle, 17,
            new Color(1f, 1f, 1f, 0.45f), TextAnchor.UpperCenter, FontStyle.Bold,
            RuntimeUiKit.TitleFont, new Vector2(0f, -96f), new Vector2(640f, 24f),
            new Vector2(0.5f, 1f));
        RuntimeUiKit.CreateTmp(panel.transform, "Blurb",
            "ONE-TIME PURCHASE - THE PLATFORM STORE WOULD SHOW ITS PAYMENT SHEET HERE", 15,
            new Color(1f, 1f, 1f, 0.6f), TextAnchor.UpperCenter, FontStyle.Normal,
            RuntimeUiKit.TitleFont, new Vector2(0f, -140f), new Vector2(600f, 40f),
            new Vector2(0.5f, 1f));

        populate(panel.rectTransform, () => UnityEngine.Object.Destroy(overlay));
    }

    private sealed class ResolveOnDestroy : MonoBehaviour
    {
        public Action Resolve;
        private void OnDestroy() => Resolve?.Invoke();
    }

    private static void AddButton(RectTransform sheet, Action closeSheet, string label, bool gold, float y, Action onPick)
    {
        Image bg = RuntimeUiKit.CreateImage(sheet, label, RuntimeSprites.RoundedPanel(),
            gold ? new Color(0.94f, 0.76f, 0.31f, 1f) : new Color(0.15f, 0.15f, 0.18f, 1f));
        bg.type = Image.Type.Sliced;
        RuntimeUiKit.SetRect(bg.rectTransform, new Vector2(0f, y), new Vector2(600f, 92f),
            new Vector2(0.5f, 1f));
        bg.raycastTarget = true;
        RuntimeUiKit.CreateTmp(bg.transform, "Label", label, 24,
            gold ? new Color(0.10f, 0.08f, 0.03f, 1f) : new Color(0.92f, 0.97f, 1f, 1f),
            TextAnchor.MiddleCenter, FontStyle.Bold, RuntimeUiKit.TitleFont);
        Button button = bg.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(() =>
        {
            closeSheet?.Invoke();
            onPick?.Invoke();
        });
    }
}
#endif
