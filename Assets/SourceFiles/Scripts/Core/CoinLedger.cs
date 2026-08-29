using UnityEngine;

/// <summary>
/// Run-scoped coin accounting (JUICE.md Phase 3). PlacementScout mints skill coins here;
/// earning stays open through the whole medal chase (bronze completion mid-run must not
/// mute the golden bricks the player is still being served) and closes when the ladder is
/// done - the victory card's Keep Playing earns nothing, as post-win always did. The win
/// bonus persists the moment bronze completes (crash-safe, like the old completion bank);
/// the skill balance banks to the persistent PlayerProfileStore exactly once per run - at
/// ladder completion, on game over, or on teardown (quitting mid-run keeps what was earned;
/// a crash loses at most the current run's skill coins). Lives on the GameManager host, so
/// a scene (re)load starts a fresh run ledger automatically.
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
    private bool _earningClosed;
    private bool _banked;

    /// <summary>Skill coins earned so far this run (0 when no run is live).</summary>
    public static int RunCoins => _instance != null ? _instance._runCoins : 0;

    /// <summary>Mint skill coins at a world position (the placed block). Raises
    /// GameEvents.CoinsEarned for the HUD flight.</summary>
    public static void Earn(int amount, Vector3 worldPosition)
    {
        if (_instance == null || amount <= 0 || _instance._earningClosed || _instance._banked) return;
        _instance._runCoins += amount;
        GameEvents.RaiseCoinsEarned(amount, worldPosition, _instance._runCoins);
    }

    private void OnEnable()
    {
        _instance = this;
        _runCoins = 0;
        _earningClosed = false;
        _banked = false;
        GameEvents.LevelCompleted += HandleLevelCompleted;
        GameEvents.PhaseChanged += HandlePhaseChanged;
        GameEvents.GameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        GameEvents.LevelCompleted -= HandleLevelCompleted;
        GameEvents.PhaseChanged -= HandlePhaseChanged;
        GameEvents.GameOver -= HandleGameOver;
        Bank(); // mid-run quit keeps earned coins
        if (_instance == this) _instance = null;
    }

    // Bronze completion: the once-per-level win bonus persists immediately (an app kill during
    // the silver/gold chase must not lose it), NOT via Bank - earning stays open for the chase.
    private void HandleLevelCompleted(LevelDefinition level, RunResult result)
        => PlayerProfileStore.AddCoins(WinBonusCoins);

    // The Completed phase is requested exactly once, when the medal ladder finishes: close
    // earning (the victory card's Keep Playing pays nothing, as post-win always did) and bank -
    // the totals are final, so the old bank-at-victory crash-safety is preserved.
    private void HandlePhaseChanged(GamePhase previous, GamePhase current)
    {
        if (current != GamePhase.Completed) return;
        _earningClosed = true;
        Bank();
    }

    private void HandleGameOver(int score, float maxHeight) => Bank();

    private void Bank()
    {
        if (_banked) return;
        _banked = true;
        if (_runCoins > 0) PlayerProfileStore.AddCoins(_runCoins);
    }
}
