using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The run-supplies carrier (SHOP.md §3): the level modal builds a Pending loadout, the scene
/// reload carries it (statics survive LoadScene, same mechanism as LevelSelectionState), and
/// RunSuppliesApplier consumes it exactly once at run start - charge and apply are atomic
/// there, and Pending is nulled so a Try-Again reload can never re-charge. The Active* fields
/// describe the run in progress (the boosted flag drives which leaderboard the run is on).
/// Cleared with LevelSelectionState in MainMenuRuntime.ResetForPlayMode / ReturnToMenu.
/// </summary>
public static class RunSuppliesState
{
    public sealed class Loadout
    {
        public int Lives;                                  // 0..3, purchased pips
        public readonly List<BoostId> Boosts = new();        // up to SupplyCatalog.MaxBoostsPerRun
        public int TotalPrice;                             // exactly as quoted by the modal
        public bool Boosted => Lives > 0 || Boosts.Count > 0;
    }

    /// <summary>Set by the level modal right before the launch reload; consumed (and nulled)
    /// by RunSuppliesApplier in the loaded scene.</summary>
    public static Loadout Pending;

    /// <summary>The run in progress started with purchased supplies → its score belongs to
    /// the BOOSTED board. False for clean runs, menus and Custom Game.</summary>
    public static bool ActiveRunBoosted { get; private set; }

    private static readonly List<BoostId> _activeBoosts = new();

    // Session-local loss streaks per level (SHOP.md §7.2 "help at the wall"): after 3 straight
    // losses the modal opens its supplies tray once. Deliberately not persisted - a new session
    // starts fresh, and the nudge should never feel like the game kept a file on you.
    private static readonly Dictionary<string, int> _lossStreaks = new();
    private static readonly HashSet<string> _nudgeShown = new();

    public static bool HasActiveBoost(BoostId id) => _activeBoosts.Contains(id);

    /// <summary>Consume Pending for the starting run: publish the Active* view and null the
    /// carrier. Returns the loadout to apply, or null for a clean run.</summary>
    public static Loadout ConsumePendingForRunStart()
    {
        Loadout loadout = Pending;
        Pending = null;

        _activeBoosts.Clear();
        ActiveRunBoosted = loadout != null && loadout.Boosted;
        if (loadout != null) _activeBoosts.AddRange(loadout.Boosts);
        return loadout;
    }

    public static void NoteLoss(LevelDefinition level)
    {
        string id = ProgressStore.LevelId(level);
        if (id == null) return;
        _lossStreaks.TryGetValue(id, out int streak);
        _lossStreaks[id] = streak + 1;
        if (_lossStreaks[id] < NudgeAfterLosses) _nudgeShown.Remove(id);
    }

    public static void NoteWin(LevelDefinition level)
    {
        string id = ProgressStore.LevelId(level);
        if (id == null) return;
        _lossStreaks.Remove(id);
        _nudgeShown.Remove(id);
    }

    public const int NudgeAfterLosses = 3;

    /// <summary>True exactly once per qualifying streak: the modal should open with the
    /// supplies tray already expanded (SHOP.md §7.2 - no popup, no upsell, just open).</summary>
    public static bool ShouldNudge(LevelDefinition level)
    {
        string id = ProgressStore.LevelId(level);
        if (id == null) return false;
        if (!_lossStreaks.TryGetValue(id, out int streak) || streak < NudgeAfterLosses) return false;
        if (_nudgeShown.Contains(id)) return false;
        _nudgeShown.Add(id);
        return true;
    }

    /// <summary>Drop any un-consumed loadout and the active-run view, keeping loss streaks
    /// (quit-to-menu: the §7.2 nudge must survive the trip back to the level modal).</summary>
    public static void ClearRun()
    {
        Pending = null;
        ActiveRunBoosted = false;
        _activeBoosts.Clear();
    }

    /// <summary>Full reset (play-mode entry).</summary>
    public static void ClearAll()
    {
        Pending = null;
        ActiveRunBoosted = false;
        _activeBoosts.Clear();
        _lossStreaks.Clear();
        _nudgeShown.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode() => ClearAll();
}
