using UnityEngine;

/// <summary>
/// Run-scoped coin accounting (JUICE.md Phase 3). PlacementScout mints skill coins here;
/// the balance banks to the persistent PlayerProfileStore exactly once per run - on level
/// completion (plus the win bonus), on game over, or on teardown (quitting mid-run keeps
/// what was earned; a crash loses at most the current run). Lives on the GameManager host,
/// so a scene (re)load starts a fresh run ledger automatically.
/// </summary>
public class CoinLedger : MonoBehaviour
{
    // The whole earning table. Deliberately tiny, and RATE-BOUNDED (JUICE.md: the target is
    // a roughly consistent coin total per 100 bricks - the golden scheduler enforces it;
    // geometry-emergent earners like rows/fits/interlocks were cut for paying wildly with
    // tower shape). Routine play earns NOTHING in-run.
    public const int PerfectStackCoins = 5;
    public const int GoldenCleanCoins = 10;   // the scheduled golden brick, landed upright
    public const int GoldenPerfectCoins = 40; // golden brick landed as a perfect stack
    public const int WinBonusCoins = 25;

    private static CoinLedger _instance;

    private int _runCoins;
    private bool _banked;

    /// <summary>Skill coins earned so far this run (0 when no run is live).</summary>
    public static int RunCoins => _instance != null ? _instance._runCoins : 0;

    /// <summary>Mint skill coins at a world position (the placed block). Raises
    /// GameEvents.CoinsEarned for the HUD flight.</summary>
    public static void Earn(int amount, Vector3 worldPosition)
    {
        if (_instance == null || amount <= 0 || _instance._banked) return;
        _instance._runCoins += amount;
        GameEvents.RaiseCoinsEarned(amount, worldPosition, _instance._runCoins);
    }

    private void OnEnable()
    {
        _instance = this;
        _runCoins = 0;
        _banked = false;
        GameEvents.LevelCompleted += HandleLevelCompleted;
        GameEvents.GameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        GameEvents.LevelCompleted -= HandleLevelCompleted;
        GameEvents.GameOver -= HandleGameOver;
        Bank(0); // mid-run quit keeps earned coins
        if (_instance == this) _instance = null;
    }

    private void HandleLevelCompleted(LevelDefinition level, RunResult result) => Bank(WinBonusCoins);

    private void HandleGameOver(int score, float maxHeight) => Bank(0);

    private void Bank(int bonus)
    {
        if (_banked) return;
        _banked = true;
        int total = _runCoins + bonus;
        if (total > 0) PlayerProfileStore.AddCoins(total);
    }
}
