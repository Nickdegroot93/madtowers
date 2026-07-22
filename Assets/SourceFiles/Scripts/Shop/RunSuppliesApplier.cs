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
    private bool _completedThisRun;
    private RunSuppliesState.Loadout _loadout;

    private void Awake()
    {
        // Menu scene (no run starting): reset the active-run view and stand down.
        if (LevelSelectionState.IsSelectionPending)
        {
            RunSuppliesState.ConsumePendingForRunStart();
            return;
        }

        LevelDefinition level = LevelSelectionState.SelectedLevel;
        _loadout = RunSuppliesState.ConsumePendingForRunStart();

        // The attempts meter charges campaign runs only (levels with a save identity);
        // Custom Game and other runtime levels are free practice. NOTE: the modal's disabled
        // Play button is the actual gate - a false return here (meter empty) is NOT blocked,
        // so any future launch path that bypasses the modal must check
        // AttemptsService.CanStartRun itself.
        if (ProgressStore.LevelId(level) != null)
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
        GameEvents.LevelCompleted += HandleLevelCompleted;
        GameEvents.GameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        GameEvents.LevelCompleted -= HandleLevelCompleted;
        GameEvents.GameOver -= HandleGameOver;
    }

    private void HandleLevelCompleted(LevelDefinition level, RunResult result)
    {
        _completedThisRun = true;
        RunSuppliesState.NoteWin(level);
        if (!_attemptSpent) return;
        _attemptSpent = false; // wins are free (loss-only meter, SHOP.md §7)
        AttemptsService.RefundForWin();
    }

    private void HandleGameOver(int score, float maxHeight)
    {
        // A game over AFTER completion (kept playing, tower fell) is not a loss: the win
        // already refunded the attempt and cleared the streak. Streak tracking is
        // independent of the meter - premium players still get the §7.2 nudge.
        if (_completedThisRun) return;
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
