using System;
using UnityEngine;

public static class GameEvents
{
    /// <summary>Cumulative progression (real placements only; never rewinds) - drives the
    /// difficulty ramp, ability-picker milestones, and rarity escalation.</summary>
    public static event Action<int> ScoreChanged;
    /// <summary>The LIVE count of real placed blocks still standing (+1 placed, -1 when
    /// destroyed or fallen). Drives the HUD total and the PlaceBlocks win target.</summary>
    public static event Action<int> StandingBlocksChanged;
    public static event Action<int> LivesChanged;
    /// <summary>A life was just charged (LivesChanged also fires; this one never fires for gains).</summary>
    public static event Action LifeLost;
    public static event Action<float> HeightChanged;
    public static event Action<string> NextBlockChanged;
    /// <summary>A new piece entered play, with the controller and the variant it rolled (null = normal).</summary>
    public static event Action<BlockController, BlockData> BlockSpawned;
    public static event Action<int, float> GameOver;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        ScoreChanged = null;
        StandingBlocksChanged = null;
        LivesChanged = null;
        LifeLost = null;
        HeightChanged = null;
        NextBlockChanged = null;
        BlockSpawned = null;
        GameOver = null;
    }

    public static void RaiseScoreChanged(int score) => ScoreChanged?.Invoke(score);
    public static void RaiseStandingBlocksChanged(int count) => StandingBlocksChanged?.Invoke(count);
    public static void RaiseLivesChanged(int lives) => LivesChanged?.Invoke(lives);
    public static void RaiseLifeLost() => LifeLost?.Invoke();
    public static void RaiseHeightChanged(float height) => HeightChanged?.Invoke(height);
    public static void RaiseNextBlockChanged(string blockName) => NextBlockChanged?.Invoke(blockName);
    public static void RaiseBlockSpawned(BlockController block, BlockData variant) => BlockSpawned?.Invoke(block, variant);
    public static void RaiseGameOver(int score, float maxHeight) => GameOver?.Invoke(score, maxHeight);
}
