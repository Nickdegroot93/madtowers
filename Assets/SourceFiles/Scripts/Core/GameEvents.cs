using System;
using System.Collections.Generic;
using UnityEngine;

public static class GameEvents
{
    /// <summary>Cumulative progression (real placements only; never rewinds) - drives the
    /// difficulty ramp, ability-picker milestones, and rarity escalation.</summary>
    public static event Action<int> ScoreChanged;
    /// <summary>The LIVE count of real placed blocks still standing (+1 placed, -1 when
    /// destroyed or fallen). Drives the HUD total and the PlaceBlocks win target.</summary>
    public static event Action<int> StandingBlocksChanged;
    /// <summary>Cumulative count of real pieces that successfully joined the tower. Never
    /// rewinds and is not affected by score/status bonuses; drives block-scheduled level events.</summary>
    public static event Action<int> BlockPlaced;
    public static event Action<int> LivesChanged;
    /// <summary>A life was just charged (LivesChanged also fires; this one never fires for gains).</summary>
    public static event Action LifeLost;
    public static event Action<float> HeightChanged;
    /// <summary>The upcoming shapes' display names, front first. One entry by default;
    /// more once a queue-visibility ability (Foresight) widens the look-ahead.</summary>
    public static event Action<IReadOnlyList<string>> NextBlockChanged;
    /// <summary>A new piece entered play, with the controller and the variant it rolled (null = normal).</summary>
    public static event Action<BlockController, BlockData> BlockSpawned;
    /// <summary>A controlled piece joined the tower. The ledger owns scoring, standing count, and height.</summary>
    public static event Action<BlockController, float> BlockLanded;
    /// <summary>A placed block left the board outside the loss-zone flow. The ledger decrements it exactly once.</summary>
    public static event Action<BlockController> BlockDestroyed;
    /// <summary>The active piece just locked into the tower (one per piece-turn). Distinct from
    /// BlockSpawned because mid-turn transmutes/banks raise a spawn without a preceding lock.</summary>
    public static event Action<BlockController> BlockLocked;
    public static event Action<LevelDefinition, RunResult> LevelCompleted;
    public static event Action<GamePhase, GamePhase> PhaseChanged;
    public static event Action<bool> SpawnAvailabilityChanged;
    public static event Action<int, float> GameOver;

    // A control gesture the player just PERFORMED on a controlled piece (rotate, drag/key move,
    // soft-drop engaged, hard-drop flick, corner nudge). Exists so the first-run tutorial can
    // detect "the player did X" and advance a step; gameplay itself ignores it. Raised once per
    // performed gesture from the gated BlockController entry points - which every input path
    // (touch, mouse, keyboard, DAS repeat) funnels through. System-initiated moves (e.g. the
    // magma melt's forced plunge) deliberately do NOT raise it. See TUTORIAL.md.
    public static event Action<BlockController, PieceGestures> PieceGesturePerformed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        ScoreChanged = null;
        StandingBlocksChanged = null;
        BlockPlaced = null;
        LivesChanged = null;
        LifeLost = null;
        HeightChanged = null;
        NextBlockChanged = null;
        BlockSpawned = null;
        BlockLanded = null;
        BlockDestroyed = null;
        BlockLocked = null;
        LevelCompleted = null;
        PhaseChanged = null;
        SpawnAvailabilityChanged = null;
        GameOver = null;
        PieceGesturePerformed = null;
    }

    public static void RaiseScoreChanged(int score) => ScoreChanged?.Invoke(score);
    public static void RaiseStandingBlocksChanged(int count) => StandingBlocksChanged?.Invoke(count);
    public static void RaiseBlockPlaced(int totalPlaced) => BlockPlaced?.Invoke(totalPlaced);
    public static void RaiseLivesChanged(int lives) => LivesChanged?.Invoke(lives);
    public static void RaiseLifeLost() => LifeLost?.Invoke();
    public static void RaiseHeightChanged(float height) => HeightChanged?.Invoke(height);
    public static void RaiseNextBlockChanged(IReadOnlyList<string> blockNames) => NextBlockChanged?.Invoke(blockNames);
    public static void RaiseBlockSpawned(BlockController block, BlockData variant) => BlockSpawned?.Invoke(block, variant);
    public static void RaiseBlockLanded(BlockController block, float highestCellY) => BlockLanded?.Invoke(block, highestCellY);
    public static void RaiseBlockDestroyed(BlockController block)
    {
        BlockDestroyed?.Invoke(block);
        BlockController.WakeDynamicLandedBlocks(block);
    }
    public static void RaiseBlockLocked(BlockController block)
    {
        Action<BlockController> handlers = BlockLocked;
        if (handlers == null) return;

        foreach (Action<BlockController> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(block);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    public static void RaiseLevelCompleted(LevelDefinition level, RunResult result) => LevelCompleted?.Invoke(level, result);
    public static void RaisePhaseChanged(GamePhase previous, GamePhase current) => PhaseChanged?.Invoke(previous, current);
    public static void RaiseSpawnAvailabilityChanged(bool canSpawn) => SpawnAvailabilityChanged?.Invoke(canSpawn);
    public static void RaiseGameOver(int score, float maxHeight) => GameOver?.Invoke(score, maxHeight);

    public static void RaisePieceGesturePerformed(BlockController block, PieceGestures gesture) =>
        PieceGesturePerformed?.Invoke(block, gesture);
}
