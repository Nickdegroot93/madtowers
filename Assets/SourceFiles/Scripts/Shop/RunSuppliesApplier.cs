using UnityEngine;

/// <summary>
/// Consumes the pending supplies loadout at run start (SHOP.md §3): charges the wallet,
/// grants purchased lives, applies the speed boost and spends the attempt -
/// all in Awake, atomically with the scene that will play the run. Installed by
/// GameSystemsInstaller AFTER GameManager.ApplyConfig has set the authored starting lives, so
/// purchased lives top up on top of them (RunState clamps the total at 3, all sources).
///
/// Spawn-table boosts are NOT pushed from here: the Spawner pulls ScarceHazards itself right
/// after registering ambient chances (Start-order between two components is undefined; a pull
/// can't race). Stocked consumables are granted in Start, after every system's Awake ran.
/// </summary>
public sealed class RunSuppliesApplier : MonoBehaviour
{
    private bool _attemptSpent;
    private bool _wonThisRun;
    private RunSuppliesState.Loadout _loadout;

    private void Awake()
    {
        // Menu scene (no run starting): reset the active-run view and stand down. Reaching
        // the menu also abandons any granted-but-unfinished server run: the attempt stays
        // spent (loss-only rule) and the stale run_id must not leak into a later run's
        // finish report (RunGate.BeginRun re-grants for every campaign launch, but Custom
        // Game never talks to RunGate and would otherwise inherit it).
        if (LevelSelectionState.IsSelectionPending)
        {
            RunSuppliesState.ConsumePendingForRunStart();
            RunGate.ClearActiveRun();
            return;
        }

        LevelDefinition level = LevelSelectionState.SelectedLevel;
        _loadout = RunSuppliesState.ConsumePendingForRunStart();

        // The attempts meter charges campaign runs only (levels with a save identity);
        // Custom Game and other runtime levels are free practice. ONLINE the server already
        // charged at start_run (RunGate.BeginRun, before the launch reload) - the local
        // spend exists only for the disabled-online fallback. NOTE: the modal's disabled
        // Play button plus the start_run grant are the actual gates - a false return here
        // (meter empty) is NOT blocked, so any future launch path that bypasses the modal
        // must go through RunGate.BeginRun itself.
        if (ProgressStore.LevelId(level) != null && !OnlineService.Enabled)
        {
            _attemptSpent = AttemptsService.SpendForRunStart();
        }

        if (_loadout == null) return;

        // Charge exactly what the modal quoted.
        PlayerProfileStore.AddCoins(-_loadout.TotalPrice);

        GameManager manager = GetComponent<GameManager>();
        if (manager == null) return;

        for (int i = 0; i < _loadout.Lives; i++) manager.AddLife();

        if (RunSuppliesState.HasActiveBoost(BoostId.SlowDescent))
        {
            manager.ApplyRunSupplySpeedScale(SupplyCatalog.SlowDescentSpeedScale);
        }
    }

    private void Start()
    {
        if (_loadout == null) return;

        TryGrantStocked(BoostId.StockedSloMo);
        TryGrantStocked(BoostId.StockedZap);
    }

    private void OnEnable()
    {
        GameEvents.TierEarned += HandleTierEarned;
        GameEvents.GameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        GameEvents.TierEarned -= HandleTierEarned;
        GameEvents.GameOver -= HandleGameOver;
    }

    // ANY newly earned medal rung adjudicates the run as a win - not just bronze/LevelCompleted:
    // a replay that newly silvers or golds sees the win celebration, and charging its attempt
    // while the card cheers would contradict the loss-only meter (SHOP.md §7 wins are free -
    // and ONLINE the server refunds the same way, via the finish_run(won:true) the controller
    // sends at the run's first earned rung). Idempotent per run: the refund is latched by
    // _attemptSpent, NoteWin by its own removals.
    private void HandleTierEarned(LevelDefinition level, MedalTier tier)
    {
        _wonThisRun = true;
        RunSuppliesState.NoteWin(level);
        if (!_attemptSpent) return;
        _attemptSpent = false; // wins are free (loss-only meter, SHOP.md §7)
        AttemptsService.RefundForWin();
    }

    private void HandleGameOver(int score, float maxHeight)
    {
        // A game over AFTER a rung was earned (kept playing, tower fell) is not a loss: the
        // win already refunded the attempt and cleared the streak. Streak tracking is
        // independent of the meter - premium players still get the §7.2 nudge.
        if (_wonThisRun) return;
        RunSuppliesState.NoteLoss(LevelSelectionState.SelectedLevel);
    }

    private void TryGrantStocked(BoostId id)
    {
        if (!RunSuppliesState.HasActiveBoost(id)) return;

        SupplyCatalog.BoostInfo info = SupplyCatalog.Info(id);
        ConsumableAbility consumable = SupplyCatalog.FindConsumable(info?.ConsumableAssetName);
        if (consumable == null) return;

        AbilityRuntime runtime = GetComponent<AbilityRuntime>();
        if (runtime != null) runtime.TryAddConsumable(consumable);
    }
}
